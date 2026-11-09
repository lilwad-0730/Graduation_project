using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Input;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// ★1109q 字幕展示錄影（純編輯器工具，不改任何場景檔）。
/// 依序開啟五個關卡、進 Play，由 SubtitleShowcaseRunner 在執行期把玩家放到各段語音的位置、
/// 用真正的程式路徑觸發語音＋字幕，同時用 Unity Recorder（程式碼設定，不產生設定資產）錄成 MP4。
/// 進 Play 會重載 Domain，所以進度存在 SessionState，Play 結束後接著下一關。
/// </summary>
[InitializeOnLoad]
public static class SubtitleShowcaseRecorder
{
    static SubtitleShowcaseRunner.LevelDef[] LevelsRef { get { return SubtitleShowcaseRunner.Levels; } }
    public const string OutputDir = SubtitleShowcaseRunner.OutputDir;
    public const string LogPath = "Temp/showcase_log.txt";

    const string KActive = "SSR_active";
    const string KLevel = "SSR_level";
    const string KLast = "SSR_last";
    const string KDone = "SSR_levelDone";
    const string KRecord = "SSR_record";

    static RecorderController s_controller;

    static SubtitleShowcaseRecorder()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        SubtitleShowcaseRunner.LogHook = Log;
        SubtitleShowcaseRunner.StartRecorderHook = StartRecorder;
        SubtitleShowcaseRunner.StopRecorderHook = StopRecorder;
        SubtitleShowcaseRunner.LevelFinishedHook = LevelFinished;
        SubtitleShowcaseRunner.FocusHook = FocusEditor;
        SubtitleShowcaseRunner.DiagHook = Diag;
    }

    // 被別的視窗蓋住的 Game 視窗不會重繪，錄出來是全黑 → 開錄前把 Unity 視窗拉到最前面（只有 Windows）
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetForegroundWindow(System.IntPtr h);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool ShowWindow(System.IntPtr h, int n);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetWindowPos(System.IntPtr h, System.IntPtr after, int x, int y, int cx, int cy, uint flags);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, int flags, int extra);

    public static void FocusEditor()
    {
        try
        {
            var h = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            if (h == System.IntPtr.Zero) return;
            keybd_event(0x12, 0, 0, 0); keybd_event(0x12, 0, 2, 0);   // 按一下 Alt，Windows 才肯讓背景程式搶前景
            ShowWindow(h, 9);
            SetForegroundWindow(h);
            SetWindowPos(h, new System.IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0040);   // 錄影期間設成最上層，別的視窗蓋不住（錄完還原）
        }
        catch { }
    }

    static string Diag()
    {
        var sb = new StringBuilder();
        foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>())
            sb.Append(w.GetType().Name).Append(w.hasFocus ? "(焦點)" : "").Append(w.docked ? "[docked]" : "[float]").Append(' ');
        sb.Append(" | focused=").Append(EditorWindow.focusedWindow != null ? EditorWindow.focusedWindow.GetType().Name : "null");
        sb.Append(" | appFocus=").Append(UnityEditorInternal.InternalEditorUtility.isApplicationActive);
        return sb.ToString();
    }

    public static void ReleaseTopmost()
    {
        try
        {
            var h = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            if (h != System.IntPtr.Zero) SetWindowPos(h, new System.IntPtr(-2), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0040);
        }
        catch { }
    }

    [MenuItem("Tools/字幕展示診斷/掃描 P5 附近地面")]
    static void ScanGround()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/CottonCastle.unity", OpenSceneMode.Single);
        Physics.queriesHitBackfaces = true;
        var sb = new StringBuilder();
        for (float x = 20f; x <= 52f; x += 2f)
        {
            sb.Append("x=").Append(x).Append(": ");
            foreach (float z in new[] { -0.92f, 0f })
            {
                var hits = Physics.RaycastAll(new Vector3(x, 40f, z), Vector3.down, 80f, ~0, QueryTriggerInteraction.Ignore);
                System.Array.Sort(hits, (a, b) => b.point.y.CompareTo(a.point.y));
                sb.Append("[z=").Append(z).Append("] ");
                for (int i = 0; i < hits.Length && i < 4; i++) sb.Append(hits[i].collider.name).Append('@').Append(hits[i].point.y.ToString("F1")).Append(' ');
            }
            sb.AppendLine();
        }
        Physics.queriesHitBackfaces = false;
        File.WriteAllText("Temp/showcase_ground.txt", sb.ToString());
        EditorSceneManager.OpenScene("Assets/Scenes/underwater.unity", OpenSceneMode.Single);
    }

    // ───────────────────────── 選單 ─────────────────────────

    [MenuItem("Tools/字幕展示錄影")]
    public static void RecordAll() { Begin(0, SubtitleShowcaseRunner.Levels.Length - 1, true); }

    [MenuItem("Tools/字幕展示錄影（試跑，不錄影）/荒原")] static void Dry0() { Begin(0, 0, false); }
    [MenuItem("Tools/字幕展示錄影（試跑，不錄影）/水下")] static void Dry1() { Begin(1, 1, false); }
    [MenuItem("Tools/字幕展示錄影（試跑，不錄影）/玻璃館")] static void Dry2() { Begin(2, 2, false); }
    [MenuItem("Tools/字幕展示錄影（試跑，不錄影）/棉花堡")] static void Dry3() { Begin(3, 3, false); }
    [MenuItem("Tools/字幕展示錄影（試跑，不錄影）/廢墟")] static void Dry4() { Begin(4, 4, false); }

    [MenuItem("Tools/字幕展示錄影（單關）/荒原")] static void One0() { Begin(0, 0, true); }
    [MenuItem("Tools/字幕展示錄影（單關）/水下")] static void One1() { Begin(1, 1, true); }
    [MenuItem("Tools/字幕展示錄影（單關）/玻璃館")] static void One2() { Begin(2, 2, true); }
    [MenuItem("Tools/字幕展示錄影（單關）/棉花堡")] static void One3() { Begin(3, 3, true); }
    [MenuItem("Tools/字幕展示錄影（單關）/廢墟")] static void One4() { Begin(4, 4, true); }

    [MenuItem("Tools/字幕展示錄影（中止）")]
    public static void Abort()
    {
        SessionState.SetBool(KActive, false);
        ReleaseTopmost();
        StopRecorder();
        if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
    }

    static void Begin(int first, int last, bool record)
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[字幕展示] 請先停止 Play"); return; }
        Directory.CreateDirectory(OutputDir);
        File.WriteAllText(LogPath, "");
        SessionState.SetBool(KActive, true);
        SessionState.SetInt(KLevel, first);
        SessionState.SetInt(KLast, last);
        SessionState.SetBool(KRecord, record);
        SessionState.SetBool(KDone, false);
        OpenLevelAndPlay();
    }

    // ───────────────────────── 流程控制 ─────────────────────────

    static void OpenLevelAndPlay()
    {
        int lv = SessionState.GetInt(KLevel, 0);
        var def = SubtitleShowcaseRunner.Levels[lv];
        EditorSceneManager.OpenScene("Assets/Scenes/" + def.scene + ".unity", OpenSceneMode.Single);
        EditorUtility.audioMasterMute = false;
        FocusEditor();
        EditorApplication.ExecuteMenuItem("Window/General/Game");
        SessionState.SetBool(KDone, false);
        Log("=== 開始第 " + (lv + 1) + " 關 " + def.title + " (" + def.scene + ")");
        EditorApplication.delayCall += () => { EditorApplication.isPlaying = true; };
    }

    static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(KActive, false)) return;

        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            int lv = SessionState.GetInt(KLevel, 0);
            // 上一輪留下的 Runner（HideAndDontSave 會活過停止 Play）全部清掉，避免同時跑好幾個
            foreach (var old in Resources.FindObjectsOfTypeAll<SubtitleShowcaseRunner>())
                if (old != null) Object.DestroyImmediate(old.gameObject);
            var go = new GameObject("[字幕展示Runner]") { hideFlags = HideFlags.HideInHierarchy };
            var r = go.AddComponent<SubtitleShowcaseRunner>();
            r.levelIndex = lv;
            r.record = SessionState.GetBool(KRecord, true);
            r.Begin();
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            bool done = SessionState.GetBool(KDone, false);
            if (!done)
            {
                Log("!!! Play 結束但這關沒有正常完成，流程中止");
                SessionState.SetBool(KActive, false);
                ReleaseTopmost();
                return;
            }
            int lv = SessionState.GetInt(KLevel, 0);
            int last = SessionState.GetInt(KLast, SubtitleShowcaseRunner.Levels.Length - 1);
            if (lv >= last)
            {
                SessionState.SetBool(KActive, false);
                ReleaseTopmost();
                Log("=== 全部完成");
                // 等 SceneLockGuard 還原完場景，再開回水下（不存檔）
                EditorApplication.delayCall += () => EditorApplication.delayCall += () =>
                    EditorSceneManager.OpenScene("Assets/Scenes/underwater.unity", OpenSceneMode.Single);
                return;
            }
            SessionState.SetInt(KLevel, lv + 1);
            // SceneLockGuard 會在 delayCall 還原場景，這裡晚兩拍再開下一關
            EditorApplication.delayCall += () => EditorApplication.delayCall += OpenLevelAndPlay;
        }
    }

    /// <summary>Runner 一關跑完（成功／失敗）後呼叫。</summary>
    public static void LevelFinished(bool ok)
    {
        SessionState.SetBool(KDone, ok);
        if (!ok) SessionState.SetBool(KActive, false);
        EditorApplication.delayCall += () => { if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode(); };
    }

    // ───────────────────────── Recorder（全在程式碼，不產生資產） ─────────────────────────

    public static bool StartRecorder(string fileNoExt)
    {
        var cs = ScriptableObject.CreateInstance<RecorderControllerSettings>();
        var mov = ScriptableObject.CreateInstance<MovieRecorderSettings>();
        mov.name = "字幕展示";
        mov.Enabled = true;
        mov.OutputFormat = MovieRecorderSettings.VideoRecorderOutputFormat.MP4;
        mov.VideoBitRateMode = UnityEditor.VideoBitrateMode.High;
        mov.CaptureAudio = true;
        mov.AudioInputSettings.PreserveAudio = true;
        mov.ImageInputSettings = new GameViewInputSettings { OutputWidth = 1920, OutputHeight = 1080 };
        mov.OutputFile = fileNoExt;
        cs.AddRecorderSettings(mov);
        cs.SetRecordModeToManual();
        cs.FrameRate = 30f;
        cs.FrameRatePlayback = FrameRatePlayback.Constant;
        cs.CapFrameRate = true;
        s_controller = new RecorderController(cs);
        s_controller.PrepareRecording();
        bool ok = s_controller.StartRecording();
        Log("Recorder 開始 ok=" + ok + " → " + fileNoExt + ".mp4");
        return ok;
    }

    public static void StopRecorder()
    {
        if (s_controller != null && s_controller.IsRecording()) s_controller.StopRecording();
        s_controller = null;
    }

    public static bool IsRecording { get { return s_controller != null && s_controller.IsRecording(); } }

    public static void Log(string msg)
    {
        try { File.AppendAllText(LogPath, msg + "\n", Encoding.UTF8); } catch { }
        Debug.Log("[字幕展示] " + msg);
    }
}
