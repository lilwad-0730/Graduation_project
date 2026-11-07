#if UNITY_EDITOR
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// ★1108 本機 AI 自動修復橋接（只在 Unity 編輯器裡運作，打包後完全不存在）。
///
/// 流程：遊戲裡出現例外 → GameRecoveryGuard 把錯誤整理成回報檔（Logs/AutoFix/Reports/*.json）
///       → 這支看到新回報 → 先把所有 .cs 拍一份快照 → 叫本機的 Claude（claude -p，唯讀＋只准改 Assets/Codes 底下的 .cs）
///       對症下藥 → 驗收：改了哪些檔、有沒有越界、改動量、編譯過不過 → 任何一項不對就自動還原。
///
/// 安全規則（全部由這支程式強制，不是靠 AI 自律）：
///   ・AI 只有 Read／Grep／Glob 與「Assets/Codes 底下的 Edit／Write」，沒有 Bash、沒有 git。
///   ・防崩潰系統自己的檔案（GameRecoveryGuard／PlayerUnstuck／ProgressSave／本檔）AI 改了會被還原。
///   ・改超過 150 行、或動到 Assets/Codes 以外的 .cs，整批還原。
///   ・編譯失敗就整批還原。
///   ・同一個錯誤一個編輯器工作階段只修一次；每 90 秒最多一次；一個工作階段最多 3 次。
///   ・正在 Play 時：修好的檔案先放著，等你按 Stop 才編譯（不會中途重載把遊戲打斷）。
///   ・隨時可以 Tools/AutoFix/還原上一次自動修復。
/// 關掉：Tools/AutoFix/啟用自動修復（取消打勾）。
/// </summary>
[InitializeOnLoad]
public static class AutoFixBridge
{
    private const string PrefEnabled = "AutoFix.Enabled";
    private const string PrefClaude = "AutoFix.ClaudePath";
    private const string SessPending = "AutoFix.PendingId";
    private const string SessCount = "AutoFix.RunCount";
    private const string SessLast = "AutoFix.LastRunTime";
    private const string SessAttempted = "AutoFix.Attempted";
    private const string SessRetryAfter = "AutoFix.RetryAfter";
    private const int MaxRunsPerSession = 3;
    private const double CooldownSeconds = 90;
    private const int MaxChangedLines = 150;
    private const int TimeoutSeconds = 600;

    private static readonly string[] Protected =
    {
        "Assets/Codes/GameRecoveryGuard.cs", "Assets/Codes/PlayerUnstuck.cs", "Assets/Codes/ProgressSave.cs", "Assets/Editor/AutoFixBridge.cs"
    };

    private static string Root { get { return Path.GetDirectoryName(Application.dataPath); } }
    private static string Base { get { return Path.Combine(Root, "Logs", "AutoFix"); } }
    private static string ReportDir { get { return Path.Combine(Base, "Reports"); } }

    private static readonly ConcurrentQueue<Action> Main = new ConcurrentQueue<Action>();
    private static bool _running;
    private static double _nextPoll;

    public static bool Enabled
    {
        get { return EditorPrefs.GetBool(PrefEnabled, true); }
        set { EditorPrefs.SetBool(PrefEnabled, value); }
    }

    // ───────────── 一鍵全自動展示（還原 bug → Play → 製造錯誤 → 等 Claude 修 → 停止編譯 → 重開 → 驗證）─────────────
    private const string DemoStep = "AutoFix.Demo.Step";
    private const string DemoStepT = "AutoFix.Demo.StepTime";
    private const double DemoStepTimeout = 780;   // 每一步最多等 13 分鐘（Claude 本身最多 10 分鐘）
    private static bool _sawScore, _sawException;

    [MenuItem("Tools/AutoFix/示範：一鍵全自動（約 5 分鐘，全程不用動手）")]
    private static void StartDemoLoop()
    {
        if (!Enabled) { Debug.LogWarning("[AutoFix] 自動修復目前是關閉的，請先到 Tools → AutoFix → 啟用自動修復 打勾。"); return; }
        if (EditorApplication.isPlaying) { Debug.LogWarning("[AutoFix] 請先按 Stop 再開始一鍵全自動。"); return; }
        Debug.Log("[AutoFix] ▶ 一鍵全自動展示開始：還原 bug → Play → 製造錯誤 → 等 Claude 修 → 自動停止編譯 → 自動重開驗證。全程不用動手，約 5 分鐘，結果會印在 Console。");
        SessionState.SetString(SessAttempted, "");
        SessionState.SetInt(SessCount, 0);
        SessionState.SetFloat(SessLast, -9999f);
        SessionState.SetFloat(SessRetryAfter, 0f);
        DemoNext("WAIT_COMPILE_1");
        AutoFixDemoMenu.RestoreBug();
    }

    private static void DemoNext(string step)
    {
        SessionState.SetString(DemoStep, step);
        SessionState.SetFloat(DemoStepT, (float)EditorApplication.timeSinceStartup);
    }

    private static bool DemoActive { get { return !string.IsNullOrEmpty(SessionState.GetString(DemoStep, "")); } }

    private static void DemoEnd(bool ok, string msg)
    {
        SessionState.SetString(DemoStep, "");
        if (EditorApplication.isPlaying) { EditorApplication.isPaused = false; EditorApplication.isPlaying = false; }
        if (ok) Debug.Log("[AutoFix] ✅ 一鍵全自動展示通過：" + msg);
        else Debug.LogError("[AutoFix] ❌ 一鍵全自動展示失敗：" + msg);
    }

    private static void OnLogForDemo(string cond, string stack, LogType type)
    {
        if (SessionState.GetString(DemoStep, "") != "VERIFY") return;
        if (cond.Contains("【示範】分數已記錄")) _sawScore = true;
        if (type == LogType.Exception) _sawException = true;
    }

    private static void DemoTick()
    {
        string s = SessionState.GetString(DemoStep, "");
        if (s == "") return;
        double now = EditorApplication.timeSinceStartup;
        double since = now - SessionState.GetFloat(DemoStepT, (float)now);
        if (since > DemoStepTimeout) { DemoEnd(false, "卡在步驟「" + s + "」超過 " + (DemoStepTimeout / 60) + " 分鐘"); return; }
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;

        switch (s)
        {
            case "WAIT_COMPILE_1":      // 還原 bug 後等編譯（檔案沒變就不會編譯，所以至少等 4 秒）
                if (since > 4 && !EditorApplication.isPlaying) DemoNext("ENTER_PLAY_1");
                break;
            case "ENTER_PLAY_1":
                if (!EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.EnterPlaymode();
                DemoNext("WAIT_PLAYING_1");
                break;
            case "WAIT_PLAYING_1":      // 進 Play 後等場景穩定 8 秒再製造錯誤
                if (EditorApplication.isPlaying && since > 8)
                {
                    AutoFixDemoMenu.Make();
                    DemoNext("WAIT_FIX");
                }
                break;
            case "WAIT_FIX":            // 等 Claude（Finish 會把步驟改成 STOP_FOR_COMPILE，或判定失敗）
                break;
            case "STOP_FOR_COMPILE":    // 修好了：停止遊戲 → 解鎖編譯
                if (EditorApplication.isPlaying) { EditorApplication.isPaused = false; EditorApplication.isPlaying = false; }
                else DemoNext("WAIT_COMPILE_2");
                break;
            case "WAIT_COMPILE_2":      // 等編譯結束、而且 AutoFix 已確認編譯結果
                if (since > 3 && string.IsNullOrEmpty(SessionState.GetString(SessPending, ""))) DemoNext("ENTER_PLAY_2");
                break;
            case "ENTER_PLAY_2":
                if (!EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.EnterPlaymode();
                DemoNext("WAIT_PLAYING_2");
                break;
            case "WAIT_PLAYING_2":
                if (EditorApplication.isPlaying && since > 8)
                {
                    _sawScore = false; _sawException = false;
                    DemoNext("VERIFY");
                    AutoFixDemoMenu.Make();
                }
                break;
            case "VERIFY":
                if (_sawException) DemoEnd(false, "修復後同一個錯誤又出現了");
                else if (_sawScore) DemoEnd(true, "Claude 修好了，重開後「分數已記錄」正常、沒有紅字。要再來一次：先用「還原示範錯誤」或直接再按一鍵全自動。");
                else if (since > 40) DemoEnd(false, "重開後 40 秒內沒看到「分數已記錄」");
                break;
        }
    }

    static AutoFixBridge()
    {
        Application.logMessageReceived += OnLogForDemo;
        EditorApplication.update += Update;
        EditorApplication.playModeStateChanged += OnPlayMode;
        CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompiled;
        CompilationPipeline.compilationFinished += OnCompilationFinished;
    }

    // ───────────── 選單 ─────────────
    [MenuItem("Tools/AutoFix/啟用自動修復")]
    private static void ToggleEnabled() { Enabled = !Enabled; Debug.Log("[AutoFix] 自動修復 " + (Enabled ? "已啟用" : "已關閉")); }
    [MenuItem("Tools/AutoFix/啟用自動修復", true)]
    private static bool ToggleValidate() { Menu.SetChecked("Tools/AutoFix/啟用自動修復", Enabled); return true; }

    [MenuItem("Tools/AutoFix/還原上一次自動修復")]
    private static void UndoLast() { RevertFromManifest(Path.Combine(Base, "last.txt"), "手動還原"); }

    [MenuItem("Tools/AutoFix/開啟回報資料夾")]
    private static void OpenReports() { Directory.CreateDirectory(ReportDir); EditorUtility.RevealInFinder(ReportDir); }

    [MenuItem("Tools/AutoFix/設定 Claude 路徑…")]
    private static void SetClaudePath()
    {
        string p = EditorUtility.OpenFilePanel("選 claude 執行檔（claude.exe）", Path.GetDirectoryName(ClaudePath()), "exe");
        if (!string.IsNullOrEmpty(p)) { EditorPrefs.SetString(PrefClaude, p); Debug.Log("[AutoFix] Claude 路徑：" + p); }
    }

    private static string ClaudePath()
    {
        string p = EditorPrefs.GetString(PrefClaude, "");
        if (!string.IsNullOrEmpty(p) && File.Exists(p)) return p;
        string def = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "claude.exe");
        return File.Exists(def) ? def : "claude";
    }

    // ───────────── 主迴圈 ─────────────
    private static void Update()
    {
        while (Main.TryDequeue(out Action a)) { try { a(); } catch (Exception e) { Debug.LogException(e); } }
        try { DemoTick(); } catch (Exception e) { Debug.LogWarning("[AutoFix] 一鍵展示出錯：" + e.Message); SessionState.SetString(DemoStep, ""); }
        if (EditorApplication.timeSinceStartup < _nextPoll) return;
        _nextPoll = EditorApplication.timeSinceStartup + 2.0;
        if (!Enabled || _running || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        try { PollReports(); } catch (Exception e) { Debug.LogWarning("[AutoFix] 掃描回報時出錯：" + e.Message); }
    }

    [Serializable]
    private class Report
    {
        public string kind, signature, message, stackTrace, sourceFile, sourceLine, scene, timeTaipei, autofixable, occurrence;
        public string playerPosition, checkpoint, respawning, safeMode;
        public string[] recentLog;
    }

    private static void PollReports()
    {
        if (!Directory.Exists(ReportDir)) return;
        if (EditorApplication.timeSinceStartup < SessionState.GetFloat(SessRetryAfter, 0f)) return;
        string[] files = Directory.GetFiles(ReportDir, "*.json").OrderBy(f => f).ToArray();
        if (files.Length == 0) return;
        string path = files[0];
        Report r;
        try { r = JsonUtility.FromJson<Report>(File.ReadAllText(path, Encoding.UTF8)); }
        catch { MoveReport(path, "Skipped"); return; }
        if (r == null) { MoveReport(path, "Skipped"); return; }

        string why = null;
        if (r.autofixable != "true") why = "不是程式例外（" + r.kind + "），不叫 AI 自動改，留給人看";
        else if (SessionState.GetInt(SessCount, 0) >= MaxRunsPerSession) why = "這個工作階段已經修了 " + MaxRunsPerSession + " 次，先停手";
        else if (SessionState.GetString(SessAttempted, "").Contains("|" + r.signature + "|")) why = "這個錯誤已經嘗試修過一次";
        else if (EditorApplication.timeSinceStartup - SessionState.GetFloat(SessLast, -9999f) < CooldownSeconds) return;   // 冷卻中：留著，等一下再處理
        if (why != null)
        {
            Debug.Log("[AutoFix] 略過回報 " + Path.GetFileName(path) + "：" + why);
            MoveReport(path, "Skipped");
            return;
        }
        MoveReport(path, "Processed", out string moved);
        StartFix(r, moved);
    }

    private static void MoveReport(string path, string sub) { MoveReport(path, sub, out _); }
    private static void MoveReport(string path, string sub, out string moved)
    {
        string dir = Path.Combine(Base, sub);
        Directory.CreateDirectory(dir);
        moved = Path.Combine(dir, Path.GetFileName(path));
        if (File.Exists(moved)) File.Delete(moved);
        File.Move(path, moved);
    }

    // ───────────── 快照 ─────────────
    private static IEnumerable<string> AllScripts()
    {
        return Directory.GetFiles(Application.dataPath, "*.cs", SearchOption.AllDirectories)
                        .Select(p => p.Replace('\\', '/'))
                        .Select(p => "Assets" + p.Substring(Application.dataPath.Replace('\\', '/').Length));
    }

    private static string Hash(string file)
    {
        using (var md5 = MD5.Create())
        using (var fs = File.OpenRead(file)) return BitConverter.ToString(md5.ComputeHash(fs));
    }

    private static string TakeSnapshot(string id)
    {
        string dir = Path.Combine(Base, "Snapshots", id);
        foreach (string rel in AllScripts())
        {
            string dst = Path.Combine(dir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            File.Copy(Path.Combine(Root, rel), dst, true);
        }
        return dir;
    }

    // ───────────── 叫 Claude ─────────────
    private static string BuildPrompt(Report r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("你是 Unity 專案「我你他」（URP、2D 像素風學生畢業專題）的資深工程師，正在本機幫忙修一個執行時例外。請全程用繁體中文、口語白話回報（使用者不是工程師）。");
        sb.AppendLine();
        sb.AppendLine("## 錯誤回報");
        sb.AppendLine("- 時間（台北）：" + r.timeTaipei + "　場景：" + r.scene);
        sb.AppendLine("- 出錯位置：" + r.sourceFile + ":" + r.sourceLine);
        sb.AppendLine("- 玩家位置：" + r.playerPosition + "　存檔點：" + r.checkpoint + "　重生中：" + r.respawning + "　安全模式：" + r.safeMode);
        sb.AppendLine("- 訊息：" + r.message);
        sb.AppendLine("- 堆疊：");
        sb.AppendLine(r.stackTrace);
        sb.AppendLine("- 出錯前最近的 Log：");
        if (r.recentLog != null) foreach (string l in r.recentLog.Skip(Math.Max(0, r.recentLog.Length - 25))) sb.AppendLine("  " + l);
        sb.AppendLine();
        sb.AppendLine("## 你要做的事");
        sb.AppendLine("1. 先讀出錯的檔案（行號附近與整個函式，必要時追到呼叫它的地方），找出「根本原因」。不要只用 try/catch 把錯誤蓋住；除非原因本來就是外部不可控（例如資源可能不存在），那種情況才可以加防護。");
        sb.AppendLine("2. 做「最小」的修改，保持原本的遊戲行為與設計。不重構、不改公開成員名稱、不順手改別的問題。");
        sb.AppendLine("3. 每處修改旁邊用一行註解說明原因，格式：// ★1108 AutoFix：原因（繁體中文）。");
        sb.AppendLine("4. 如果判斷不是單一程式檔能修的（例如場景設定、資源遺失），不要硬改，直接說明原因與建議。");
        sb.AppendLine();
        sb.AppendLine("## 嚴格限制（違反會被自動還原）");
        sb.AppendLine("- 只能修改 Assets/Codes 底下的 .cs。禁止動場景、prefab、ProjectSettings、Library，也不要新增檔案。");
        sb.AppendLine("- 禁止修改這些防護檔案：GameRecoveryGuard.cs、PlayerUnstuck.cs、ProgressSave.cs、AutoFixBridge.cs。");
        sb.AppendLine("- 總改動不要超過 150 行。你沒有 Bash 與 git，也不需要，不要嘗試執行指令。");
        sb.AppendLine("- 同事的設計意圖要尊重：不確定時寧可少改，並在回報裡說明。");
        sb.AppendLine();
        sb.AppendLine("## 最後請這樣回報（繁體中文、白話，不要超過 15 行）");
        sb.AppendLine("原因：一句話。　改了什麼：哪個檔案、做了什麼。　風險：會不會影響別的功能。　怎麼確認修好：玩家要做什麼操作。");
        return sb.ToString();
    }

    private static void StartFix(Report r, string reportPath)
    {
        _running = true;
        string id = DateTime.UtcNow.AddHours(8).ToString("yyyyMMdd_HHmmss");
        SessionState.SetInt(SessCount, SessionState.GetInt(SessCount, 0) + 1);
        SessionState.SetFloat(SessLast, (float)EditorApplication.timeSinceStartup);
        SessionState.SetString(SessAttempted, SessionState.GetString(SessAttempted, "") + "|" + r.signature + "|");

        string snap;
        try { snap = TakeSnapshot(id); }
        catch (Exception e) { Debug.LogError("[AutoFix] 拍快照失敗，取消這次修復：" + e.Message); _running = false; return; }

        Directory.CreateDirectory(Path.Combine(Base, "Results"));
        string resultPath = Path.Combine(Base, "Results", id + ".txt");
        string prompt = BuildPrompt(r);
        Debug.Log("[AutoFix] ▶ 收到錯誤：" + Trim(r.message, 100) + "　（" + r.sourceFile + ":" + r.sourceLine + "）\n  已拍快照，正在請 Claude 分析修復（最多 " + (TimeoutSeconds / 60) + " 分鐘，遊戲可以繼續玩）…");

        string claudeExe = ClaudePath();   // EditorPrefs 只能在主執行緒讀，所以先取好再丟進背景執行緒
        new Thread(() =>
        {
            string output = "", err = ""; int code = -1; bool timedOut = false;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = claudeExe,
                    Arguments = "-p --max-turns 30 --allowedTools \"Read,Grep,Glob,Edit(/Assets/Codes/**)\"",   // Edit 規則已涵蓋所有寫檔工具（真 Claude 實測：Write 規則無效會被警告）
                    WorkingDirectory = Root,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardInputEncoding = new UTF8Encoding(false),
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                using (var p = Process.Start(psi))
                {
                    var o = new StringBuilder(); var e = new StringBuilder();
                    p.OutputDataReceived += (s, a) => { if (a.Data != null) o.AppendLine(a.Data); };
                    p.ErrorDataReceived += (s, a) => { if (a.Data != null) e.AppendLine(a.Data); };
                    p.BeginOutputReadLine(); p.BeginErrorReadLine();
                    try { p.StandardInput.Write(prompt); p.StandardInput.Close(); }
                    catch (IOException) { /* 對方提早結束（不需要 stdin 或已出錯），不影響後面讀輸出 */ }
                    if (!p.WaitForExit(TimeoutSeconds * 1000)) { timedOut = true; try { p.Kill(); } catch { } }
                    else p.WaitForExit();
                    code = timedOut ? -2 : p.ExitCode; output = o.ToString(); err = e.ToString();
                }
            }
            catch (Exception ex) { err = "無法啟動 Claude：" + ex.Message; }
            Main.Enqueue(() => Finish(r, id, snap, resultPath, output, err, code, timedOut, reportPath));
        }) { IsBackground = true }.Start();
    }

    // ───────────── 驗收 ─────────────
    private static void Finish(Report r, string id, string snap, string resultPath, string output, string err, int code, bool timedOut, string reportPath)
    {
        // 驗收途中任何地方出錯，都不能讓「正在處理中」的旗標卡住（不然之後所有回報都不會再處理）
        try { FinishInner(r, id, snap, resultPath, output, err, code, timedOut, reportPath); }
        catch (Exception e) { Debug.LogError("[AutoFix] 驗收過程出錯：" + e.Message + "\n  如果懷疑改動不對，用 Tools → AutoFix → 還原上一次自動修復 就能退回。"); }
        finally
        {
            _running = false;
            // 一鍵展示：Claude 結束後還停在「等修復」＝沒有可套用的修復（沒改檔、被還原、沒登入…），直接宣告失敗，不要乾等
            if (SessionState.GetString(DemoStep, "") == "WAIT_FIX") DemoEnd(false, "Claude 沒有提出可套用的修復，原因看上面的 [AutoFix] 訊息。");
        }
    }

    private static void FinishInner(Report r, string id, string snap, string resultPath, string output, string err, int code, bool timedOut, string reportPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(resultPath));
        File.WriteAllText(resultPath, "exit=" + code + (timedOut ? "（逾時）" : "") + "\n\n--- 輸出 ---\n" + output + "\n--- 錯誤輸出 ---\n" + err, new UTF8Encoding(false));

        string all = output + err;
        bool launchFailed = err.StartsWith("無法啟動 Claude");
        if (launchFailed || all.Contains("Failed to authenticate") || all.Contains("OAuth") || all.Contains("not logged in") || all.Contains("Please run /login"))
        {
            Debug.LogWarning(launchFailed
                ? "[AutoFix] ✋ 啟動不了 Claude：" + Trim(err, 200) + "\n  可用 Tools → AutoFix → 設定 Claude 路徑… 指定 claude.exe。這次的回報檔放回待處理。"
                : "[AutoFix] ✋ 本機的 Claude 還沒登入（或登入過期），沒辦法自動修。\n  請開終端機輸入 claude，照畫面登入一次；之後同樣的錯誤會再試。\n  這次的回報檔我放回待處理，登入後會自動重試。");
            SessionState.SetString(SessAttempted, SessionState.GetString(SessAttempted, "").Replace("|" + r.signature + "|", ""));
            SessionState.SetInt(SessCount, Math.Max(0, SessionState.GetInt(SessCount, 0) - 1));
            SessionState.SetFloat(SessLast, -9999f);
            SessionState.SetFloat(SessRetryAfter, (float)EditorApplication.timeSinceStartup + 300f);   // 登入／路徑沒處理前，5 分鐘內不要再試（避免狂刷）
            try { string back = Path.Combine(ReportDir, Path.GetFileName(reportPath)); Directory.CreateDirectory(ReportDir); if (File.Exists(reportPath)) File.Move(reportPath, back); } catch { }
            _running = false;
            return;
        }

        // 找出改了哪些檔
        var changed = new List<string>(); var created = new List<string>(); var deleted = new List<string>();
        var now = new HashSet<string>(AllScripts());
        foreach (string rel in now)
        {
            string old = Path.Combine(snap, rel);
            if (!File.Exists(old)) created.Add(rel);
            else if (Hash(old) != Hash(Path.Combine(Root, rel))) changed.Add(rel);
        }
        foreach (string f in Directory.GetFiles(Path.Combine(snap, "Assets"), "*.cs", SearchOption.AllDirectories))
        {
            string rel = "Assets" + f.Replace('\\', '/').Substring(Path.Combine(snap, "Assets").Replace('\\', '/').Length);
            if (!now.Contains(rel)) deleted.Add(rel);
        }

        var violations = new List<string>();
        foreach (string rel in changed.Concat(created).Concat(deleted))
        {
            if (Protected.Contains(rel)) violations.Add(rel + "（防護檔案）");
            else if (!rel.StartsWith("Assets/Codes/")) violations.Add(rel + "（超出 Assets/Codes）");
        }
        foreach (string rel in created) if (!violations.Any(v => v.StartsWith(rel))) violations.Add(rel + "（不准新增檔案）");
        foreach (string rel in deleted) if (!violations.Any(v => v.StartsWith(rel))) violations.Add(rel + "（不准刪檔）");

        int lines = 0;
        foreach (string rel in changed) lines += DiffLines(Path.Combine(snap, rel), Path.Combine(Root, rel));

        string summary = Trim(output.Trim(), 1200);
        if (changed.Count + created.Count + deleted.Count == 0)
        {
            Debug.Log("[AutoFix] ■ Claude 沒有改任何檔案（結論如下）\n" + summary + "\n  完整輸出：" + resultPath);
            _running = false; return;
        }
        if (violations.Count > 0 || lines > MaxChangedLines)
        {
            Revert(snap, changed, created, deleted);
            Debug.LogWarning("[AutoFix] ⛔ 已整批還原：" + (violations.Count > 0 ? "動到不准動的檔案 → " + string.Join("、", violations) : "改動 " + lines + " 行，超過上限 " + MaxChangedLines) + "\n  Claude 的結論：\n" + summary + "\n  完整輸出：" + resultPath);
            AssetDatabase.Refresh();
            _running = false; return;
        }

        // 通過驗收：記錄清單（還原用）→ 套用
        string manifest = Path.Combine(Base, "Snapshots", id, "manifest.txt");
        File.WriteAllText(manifest, string.Join("\n", changed), new UTF8Encoding(false));
        File.Copy(manifest, Path.Combine(Base, "last.txt"), true);
        File.WriteAllText(Path.Combine(Base, "last_id.txt"), id);
        SessionState.SetString(SessPending, id);

        string msg = "[AutoFix] ✅ Claude 提出修復（" + string.Join("、", changed.Select(Path.GetFileName)) + "，共改 " + lines + " 行），驗收通過\n" + summary + "\n  完整輸出：" + resultPath + "\n  不滿意：Tools → AutoFix → 還原上一次自動修復";
        if (EditorApplication.isPlaying)
        {
            EditorApplication.LockReloadAssemblies();
            if (SessionState.GetString(DemoStep, "") == "WAIT_FIX")
            {
                Debug.Log(msg + "\n  ⏩ 一鍵展示：自動停止遊戲、編譯、重開驗證。");
                DemoNext("STOP_FOR_COMPILE");
            }
            else Debug.Log(msg + "\n  ⏸ 你正在 Play：修改已寫進檔案，等你按 Stop 後才會編譯生效（不會中途打斷遊戲）。");
        }
        else
        {
            Debug.Log(msg + "\n  正在編譯驗證…");
            AssetDatabase.Refresh();
        }
        _running = false;
    }

    private static int DiffLines(string a, string b)
    {
        var x = File.ReadAllLines(a); var y = File.ReadAllLines(b);
        var setA = new Dictionary<string, int>(); foreach (var l in x) { setA.TryGetValue(l, out int n); setA[l] = n + 1; }
        int added = 0;
        foreach (var l in y) { if (setA.TryGetValue(l, out int n) && n > 0) setA[l] = n - 1; else added++; }
        int removed = setA.Values.Sum();
        return Math.Max(added, removed);
    }

    private static void Revert(string snap, List<string> changed, List<string> created, List<string> deleted)
    {
        foreach (string rel in changed.Concat(deleted)) { string src = Path.Combine(snap, rel); if (File.Exists(src)) File.Copy(src, Path.Combine(Root, rel), true); }
        foreach (string rel in created) { string p = Path.Combine(Root, rel); if (File.Exists(p)) File.Delete(p); string m = p + ".meta"; if (File.Exists(m)) File.Delete(m); }
    }

    private static void RevertFromManifest(string manifestFile, string why)
    {
        try
        {
            string idFile = Path.Combine(Base, "last_id.txt");
            if (!File.Exists(manifestFile) || !File.Exists(idFile)) { Debug.Log("[AutoFix] 沒有可以還原的自動修復紀錄"); return; }
            string id = File.ReadAllText(idFile).Trim();
            string snap = Path.Combine(Base, "Snapshots", id);
            var files = File.ReadAllLines(manifestFile).Where(s => s.Length > 0).ToList();
            Revert(snap, files, new List<string>(), new List<string>());
            File.WriteAllText(manifestFile, "", new UTF8Encoding(false));
            Debug.Log("[AutoFix] ↩ 已還原 " + files.Count + " 個檔案（" + why + "）：" + string.Join("、", files.Select(Path.GetFileName)));
            AssetDatabase.Refresh();
        }
        catch (Exception e) { Debug.LogError("[AutoFix] 還原失敗：" + e.Message); }
    }

    // ───────────── 編譯結果 ─────────────
    private static bool _compileFailed;

    private static void OnAssemblyCompiled(string assembly, CompilerMessage[] msgs)
    {
        if (string.IsNullOrEmpty(SessionState.GetString(SessPending, ""))) return;
        // 只有「編譯錯誤出在被 AI 改過的檔案」才算它的錯；別的檔案的編譯錯（例如人手改壞的）不能賴給 AI 的修復
        string manifest = Path.Combine(Base, "last.txt");
        string[] touched = File.Exists(manifest) ? File.ReadAllLines(manifest).Where(s => s.Length > 0).ToArray() : new string[0];
        if (msgs.Any(m => m.type == CompilerMessageType.Error && touched.Any(t => (m.file ?? "").Replace('\\', '/').EndsWith(t))))
            _compileFailed = true;
    }

    private static void OnCompilationFinished(object o)
    {
        string pending = SessionState.GetString(SessPending, "");
        if (string.IsNullOrEmpty(pending)) return;
        SessionState.SetString(SessPending, "");
        if (_compileFailed)
        {
            _compileFailed = false;
            EditorApplication.delayCall += () => RevertFromManifest(Path.Combine(Base, "last.txt"), "修復後編譯失敗，自動還原");
        }
        else Debug.Log("[AutoFix] ✔ 修復已編譯成功並生效");
    }

    private static void OnPlayMode(PlayModeStateChange s)
    {
        if (s == PlayModeStateChange.EnteredEditMode)
        {
            try { EditorApplication.UnlockReloadAssemblies(); } catch { }
            if (!string.IsNullOrEmpty(SessionState.GetString(SessPending, ""))) AssetDatabase.Refresh();
        }
    }

    private static string Trim(string s, int n) { if (string.IsNullOrEmpty(s)) return ""; return s.Length <= n ? s : s.Substring(0, n) + "…"; }
}
#endif
