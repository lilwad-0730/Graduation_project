using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ★1108 防崩潰總管（上線前的保險，每個場景自動啟動）：
///  1. 錯誤收集：攔住所有「例外」，整理成錯誤回報檔（場景、座標、存檔點、最近 60 行 Log、出錯的檔案與行號）。
///     編輯器內 → Logs/AutoFix/Reports/；打包後 → persistentDataPath/ErrorReports/。
///     編輯器的 AutoFixBridge 會看這個資料夾，自動叫 Claude 對症下藥。
///  2. 重生看門狗：重生／傳送轉場超過 15 秒還沒結束（例如中途出錯被砍斷）→ 強制收尾，玩家一定拿回控制權。
///  3. 崩潰迴圈斷路器：同一個錯誤在「同一個存檔點」出現 2 次以上 → 記進存檔；
///     下次「繼續」會改用更早的存檔點，並開啟安全模式（暫時關掉氛圍特效，只留遊戲本身）。
/// 關掉：GameRecoveryGuard.Enabled = false。
/// </summary>
public class GameRecoveryGuard : MonoBehaviour
{
    public static bool Enabled = true;
    [Tooltip("重生／傳送轉場最長允許幾秒（正常約 6 秒）")]
    public static float respawnTimeoutSeconds = 15f;

    /// <summary>安全模式：氛圍特效關閉，只留遊戲本身。</summary>
    public static bool SafeMode { get; private set; }

    private static GameRecoveryGuard _inst;
    private readonly LinkedList<string> _recent = new LinkedList<string>();
    private readonly Dictionary<string, int> _sigCount = new Dictionary<string, int>();
    private bool _inHandler;
    private int _reportsThisSession;
    private float _busyStart = -1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { _inst = null; SafeMode = false; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Boot()
    {
        if (!Enabled || _inst != null) return;
        GameObject go = new GameObject("[防崩潰總管]");
        DontDestroyOnLoad(go);
        _inst = go.AddComponent<GameRecoveryGuard>();
    }

    private void OnEnable() { Application.logMessageReceived += OnLog; }
    private void OnDisable() { Application.logMessageReceived -= OnLog; }

    // ───────────── 1. 錯誤收集 ─────────────
    private void OnLog(string condition, string stackTrace, LogType type)
    {
        if (_inHandler) return;
        try
        {
            _recent.AddLast("[" + type + "] " + Trim(condition, 240));
            while (_recent.Count > 60) _recent.RemoveFirst();
            if (type == LogType.Exception && Enabled) { _inHandler = true; HandleException(condition, stackTrace); }
        }
        catch { /* 防崩潰系統自己絕對不能再出錯 */ }
        finally { _inHandler = false; }
    }

    private static string Trim(string s, int n) { if (string.IsNullOrEmpty(s)) return ""; s = s.Replace('\n', ' ').Replace('\r', ' '); return s.Length <= n ? s : s.Substring(0, n) + "…"; }

    private static readonly Regex FrameRx = new Regex(@"\(at (Assets/[^:)]+):(\d+)\)", RegexOptions.Compiled);

    private void HandleException(string message, string stack)
    {
        string first = Trim(message, 160);
        string file = "", line = "";
        Match m = FrameRx.Match(stack ?? "");
        if (m.Success) { file = m.Groups[1].Value; line = m.Groups[2].Value; }
        string sig = first + "@" + file + ":" + line;
        _sigCount.TryGetValue(sig, out int n);
        n++;
        _sigCount[sig] = n;

        // 崩潰迴圈：同一個錯誤在同一個存檔點又發生 → 記進存檔，下次繼續時避開這裡
        ProgressSave.RecordCrash(sig);
        if (ProgressSave.CrashCountHere(sig) >= 2) ActivateSafeMode("同一個錯誤在同一個存檔點重複發生：" + first);

        if (n == 1 || n == 3)
        {
            bool autofixable = !string.IsNullOrEmpty(file) && file.StartsWith("Assets/Codes/") && !IsGuardFile(file);
            WriteReport("Exception", message, stack, file, line, sig, autofixable, n);
        }
    }

    private static readonly Regex FrameRx2 = new Regex(@"in .*?[\\/](Assets[\\/][^:]+?\.cs):(?:line )?(\d+)", RegexOptions.Compiled);

    /// <summary>
    /// 「已經接住、遊戲照常繼續」的例外也要留回報（不然 AI 永遠看不到）。
    /// 不用 Debug.LogException：那會讓編輯器的 Error Pause 把遊戲暫停、Console 也一片紅，而這些錯誤其實沒影響玩家。
    /// </summary>
    public static void ReportCaught(Exception e, string where)
    {
        if (_inst == null || e == null || !Enabled) return;
        try
        {
            string stack = e.StackTrace ?? "";
            string msg = e.GetType().Name + ": " + e.Message + (string.IsNullOrEmpty(where) ? "" : "（接住的位置：" + where + "）");
            // 統一成 Unity 的堆疊格式，這樣檔案與行號擷取同一套
            stack = FrameRx2.Replace(stack, "(at $1:$2)").Replace('\\', '/');
            _inst.HandleException(msg, stack);
        }
        catch { }
    }

    private static bool IsGuardFile(string f)
    {
        return f.EndsWith("GameRecoveryGuard.cs") || f.EndsWith("PlayerUnstuck.cs") || f.EndsWith("ProgressSave.cs") || f.EndsWith("AutoFixBridge.cs");
    }

    /// <summary>其他系統（例如脫困走到最後一階）要留下一份「需要人看」的回報。不會叫 AI 自動改。</summary>
    public static void ReportSoftLock(string kind, string detail)
    {
        if (_inst == null) return;
        try { _inst.WriteReport(kind, detail, "", "", "", kind + "@" + SceneManager.GetActiveScene().name, false, 1); }
        catch { }
    }

    [Serializable]
    private class ReportData
    {
        public string kind, signature, message, stackTrace, sourceFile, sourceLine, scene, timeTaipei, autofixable, occurrence;
        public string playerPosition, checkpoint, respawning, safeMode, appVersion;
        public string[] recentLog;
    }

    private void WriteReport(string kind, string message, string stack, string file, string line, string sig, bool autofixable, int occurrence)
    {
        if (_reportsThisSession >= 20) return;
        _reportsThisSession++;
        ReportData r = new ReportData();
        r.kind = kind; r.signature = sig; r.message = message; r.stackTrace = stack;
        r.sourceFile = file; r.sourceLine = line; r.occurrence = occurrence.ToString();
        r.autofixable = autofixable ? "true" : "false";
        r.timeTaipei = DateTime.UtcNow.AddHours(8).ToString("yyyy-MM-dd HH:mm:ss");
        r.scene = SceneManager.GetActiveScene().name;
        r.safeMode = SafeMode ? "true" : "false";
        r.appVersion = Application.version;
        try
        {
            GameObject p = GameObject.FindWithTag("Player");
            r.playerPosition = p != null ? p.transform.position.ToString("F2") : "（找不到玩家）";
        }
        catch { r.playerPosition = "（無法取得）"; }
        try
        {
            PlayerRespawnSystem rs = PlayerRespawnSystem.Instance;
            r.checkpoint = rs != null ? rs.ActivePosition.ToString("F2") : "（無）";
            r.respawning = (PlayerRespawnSystem.IsAnyRespawning ? "是" : "否");
        }
        catch { r.checkpoint = "（無法取得）"; }
        r.recentLog = new string[_recent.Count];
        _recent.CopyTo(r.recentLog, 0);

        string dir = ReportDir();
        try
        {
            Directory.CreateDirectory(dir);
            string id = DateTime.UtcNow.AddHours(8).ToString("yyyyMMdd_HHmmss") + "_" + Mathf.Abs(sig.GetHashCode()).ToString("X8");
            string tmp = Path.Combine(dir, id + ".json.tmp");
            string fin = Path.Combine(dir, id + ".json");
            File.WriteAllText(tmp, JsonUtility.ToJson(r, true), new UTF8Encoding(false));
            if (File.Exists(fin)) File.Delete(fin);
            File.Move(tmp, fin);   // 先寫暫存再改名：看資料夾的人不會讀到寫一半的檔
            Debug.Log("【防崩潰】已留下錯誤回報：" + fin);
        }
        catch (Exception e) { Debug.LogWarning("【防崩潰】寫回報失敗：" + e.Message); }
    }

    public static string ReportDir()
    {
#if UNITY_EDITOR
        return Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs", "AutoFix", "Reports");
#else
        return Path.Combine(Application.persistentDataPath, "ErrorReports");
#endif
    }

    // ───────────── 2. 重生看門狗 ─────────────
    private void Update()
    {
        if (!Enabled) return;
        PlayerRespawnSystem rs = PlayerRespawnSystem.Instance;
        bool busy = PlayerRespawnSystem.IsAnyRespawning || (rs != null && rs.IsBusy);
        if (!busy) { _busyStart = -1f; return; }
        if (_busyStart < 0f) { _busyStart = Time.unscaledTime; return; }
        if (Time.unscaledTime - _busyStart < respawnTimeoutSeconds) return;

        _busyStart = -1f;
        string why = "轉場超過 " + respawnTimeoutSeconds + " 秒還沒結束";
        Debug.LogWarning("【重生看門狗】" + why + "，強制收尾");
        ReportSoftLock("RespawnTimeout", why + "。通常是重生流程中途被例外砍斷，請看同一時間的 Exception 回報。");
        if (rs != null) rs.ForceFinishRespawn(why);
        else PlayerRespawnSystem.IsAnyRespawning = false;
    }

    // ───────────── 3. 安全模式 ─────────────
    public static void ActivateSafeMode(string reason)
    {
        if (SafeMode) return;
        SafeMode = true;
        Debug.LogWarning("【安全模式】啟動：" + reason + "　→ 暫時關掉氛圍特效，只留遊戲本身");
        EmotionAtmosphereFX.Enabled = false;
        WolfHeartbeatFX.Enabled = false;
        DeepGiantShadowFX.Enabled = false;
        SkySilhouetteFX.Enabled = false;
        // 已經在跑的也一起收掉（只收專屬的小物件；掛在別人物件上的只拿掉元件）
        RemoveHost<EmotionAtmosphereFX>();
        RemoveHost<WolfHeartbeatFX>();
        RemoveHost<DeepGiantShadowFX>();
        RemoveHost<SkySilhouetteFX>();
    }

    private static void RemoveHost<T>() where T : Component
    {
        foreach (T c in FindObjectsByType<T>(FindObjectsSortMode.None))
        {
            if (c == null) continue;
            if (c.gameObject.GetComponents<Component>().Length <= 2) Destroy(c.gameObject);
            else Destroy(c);
        }
    }
}
