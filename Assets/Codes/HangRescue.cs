using System;
using System.Collections;
using System.Diagnostics;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

/// <summary>
/// ★1110 「怎樣都救得回來」的最後兩道保險（跟防崩潰總管、脫困、進度存檔是同一套，這兩層專門對付「選單壞了、整個遊戲停住」）：
///
///  第一層：緊急回主選單。任何關卡按住 Backspace——1.2 秒是原本的脫困，繼續按住到 5 秒就直接回主選單。
///          不經過任何選單按鈕；回不去會逐級加重（直接載入主選單場景 → 還是不行就重開遊戲）。
///  第二層：外部看門狗（只有打包後的 .exe 會真的動作）。開一條獨立的背景執行緒，遊戲每一幀都會跟它報平安；
///          超過 30 秒沒報（整個遊戲停住了，連 Update 都不跑）→ 自己把遊戲重開，重開後自動「繼續」，
///          從最近的安全位置接著玩。
///          不會誤判的設計：視窗在背景／縮小時不算卡死；滑鼠左鍵按著（正在拖視窗標題列）不算；
///          連續重開有次數上限（跑不到 2 分鐘就又卡住，最多連續 3 次）。
///  編輯器裡第二層只「試跑」——只寫 Log，不會真的重開或殺掉 Unity。
///
/// 關掉：HangRescue.Enabled = false（兩層一起）、HangRescue.WatchdogEnabled = false（只關第二層）。
/// </summary>
public static class HangRescue
{
    public static bool Enabled = true;
    public static bool WatchdogEnabled = true;
    /// <summary>多少秒沒有任何一幀就算整個遊戲卡死。</summary>
    public static float HangSeconds = 30f;
    /// <summary>按住 Backspace 幾秒回主選單（要大於脫困的 1.2 秒）。</summary>
    public static float MenuHoldSeconds = 5f;
    /// <summary>編輯器試跑偵測到幾次（驗收用）。</summary>
    public static int EditorDryRunCount;

    // ── 看門狗共用資料（背景執行緒讀，所以用 long／volatile）──
    private static long s_beatMs;                       // 最近一次「遊戲還活著」的時間（NowMs()）
    private static volatile bool s_focused = true;
    private static volatile bool s_quitting;
    private static volatile bool s_started;             // 收到第一次心跳才開始監看（開機載入階段不算）
    private static string s_exePath;
    private static string s_dataPath;
    private static int s_chain;                         // 目前是第幾次連續重開
    private static long s_startMs;
    private static bool s_threadStarted;
    private static int s_gen;                           // 世代：重新 Play 時舊的執行緒要自己退場

    private static long NowMs() { return Stopwatch.GetTimestamp() * 1000L / Stopwatch.Frequency; }
    private static void Beat() { Interlocked.Exchange(ref s_beatMs, NowMs()); s_started = true; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        s_gen++; s_quitting = false; s_started = false; s_focused = true; s_threadStarted = false; EditorDryRunCount = 0;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Boot()
    {
        if (!Enabled) return;
        try
        {
            s_dataPath = Application.persistentDataPath;
            s_startMs = NowMs();
            Beat();
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
                if (args[i] == "-hangrestart" && i + 1 < args.Length) int.TryParse(args[i + 1], out s_chain);
#if !UNITY_EDITOR
            try { s_exePath = Process.GetCurrentProcess().MainModule.FileName; } catch { s_exePath = null; }
#endif
            GameObject go = new GameObject("[緊急救援]");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<HangRescueRunner>();
            if (WatchdogEnabled) StartThread();
        }
        catch (Exception e) { Debug.LogWarning("【緊急救援】啟動失敗：" + e.Message); }
    }

    // ═════════════ 第二層：背景執行緒 ═════════════
    private static void StartThread()
    {
        if (s_threadStarted) return;
        s_threadStarted = true;
        int gen = s_gen;
        Thread t = new Thread(() => WatchLoop(gen)) { IsBackground = true, Name = "HangWatchdog" };
        t.Start();
    }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
    private static bool MouseDown() { try { return (GetAsyncKeyState(0x01) & 0x8000) != 0; } catch { return false; } }
#else
    private static bool MouseDown() { return false; }
#endif

    private static void WatchLoop(int gen)
    {
        while (!s_quitting && gen == s_gen)
        {
            Thread.Sleep(1000);
            try
            {
                if (!WatchdogEnabled || !s_started || !s_focused) continue;
                long stalled = NowMs() - Interlocked.Read(ref s_beatMs);
                if (stalled < (long)(HangSeconds * 1000f)) continue;
                if (MouseDown()) { Interlocked.Exchange(ref s_beatMs, NowMs() - (long)(HangSeconds * 1000f) + 5000); continue; }   // 可能正在拖視窗，再多等 5 秒

                string msg = "【緊急救援】整個遊戲已經 " + (stalled / 1000) + " 秒沒有任何一幀（Update 沒在跑）";
#if UNITY_EDITOR
                Debug.LogWarning(msg + "——正式版會在這裡自動重開並繼續（編輯器只試跑，不動作）");
                EditorDryRunCount++;
                Interlocked.Exchange(ref s_beatMs, NowMs());
                continue;
#else
                Debug.LogError(msg + "，重開遊戲");
                RelaunchProcess();
                return;
#endif
            }
            catch { /* 看門狗自己絕對不能出錯 */ }
        }
    }

    /// <summary>重開遊戲（開新的程序、再把卡住的這個砍掉）。可以從背景執行緒呼叫，不碰 Unity 的 API。</summary>
    internal static void RelaunchProcess()
    {
#if UNITY_EDITOR
        Debug.LogWarning("【緊急救援】（編輯器）略過重開遊戲");
#else
        try
        {
            long upSec = (NowMs() - s_startMs) / 1000;
            int next = upSec > 120 ? 1 : s_chain + 1;          // 這次跑超過 2 分鐘才卡住＝新的一段，次數重算
            if (!string.IsNullOrEmpty(s_exePath) && next <= 3)
            {
                ProcessStartInfo psi = new ProcessStartInfo(s_exePath, "-hangrestart " + next) { UseShellExecute = false };
                Process.Start(psi);
            }
        }
        catch { }
        try { Process.GetCurrentProcess().Kill(); } catch { }
#endif
    }

    // ═════════════ 主執行緒：心跳、焦點、重開後自動繼續、第一層 ═════════════
    public class HangRescueRunner : MonoBehaviour
    {
        private float _hold;
        private bool _leaving;
        private bool _simDone;
        private float _simAt = -1f;
        private float _autoContinueAt = -1f;

        private void Start()
        {
            if (s_chain > 0) _autoContinueAt = Time.unscaledTime + 1.5f;   // 被看門狗重開的：進主選單後自動繼續
        }

        // 打包版：焦點事件在視窗被縮小／切走、主執行緒被卡住「之前」就會送到，所以一定要用事件記（光靠每幀輪詢會漏）
        // 編輯器的焦點事件指的是 Game 視窗而不是整個編輯器，不準，編輯器只用輪詢
        private void OnApplicationFocus(bool focus)
        {
#if !UNITY_EDITOR
            s_focused = focus;
#endif
            if (focus) Beat();
        }

        private void OnApplicationPause(bool paused)
        {
#if !UNITY_EDITOR
            s_focused = !paused;
#endif
            if (!paused) Beat();
        }
        private void OnApplicationQuit() { s_quitting = true; }

        private void Update()
        {
            Beat();
#if UNITY_EDITOR
            s_focused = Application.isFocused;
#endif

            if (_autoContinueAt > 0f && Time.unscaledTime >= _autoContinueAt)
            {
                _autoContinueAt = -1f;
                try
                {
                    if (SceneManager.GetActiveScene().name == GameFlow.MenuSceneName && ProgressSave.HasSave)
                    {
                        Debug.Log("【緊急救援】遊戲是被看門狗重開的（第 " + s_chain + " 次），自動繼續上次進度");
                        ProgressSave.NoteAbnormalExit();
                        ProgressSave.Continue();
                    }
                }
                catch (Exception e) { Debug.LogWarning("【緊急救援】自動繼續失敗：" + e.Message); }
            }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
            // 只有開發版／編輯器才有的測試開關：-simulatehang 秒數 → 進入關卡後過這麼多秒，故意把整個遊戲卡死（驗收看門狗用，正式版不含這段）
            if (!_simDone && s_chain == 0 && ProgressSave.IsGameplay(SceneManager.GetActiveScene().name))
            {
                if (_simAt < 0f)
                {
                    string[] a = Environment.GetCommandLineArgs();
                    float sec = -1f;
                    for (int i = 0; i < a.Length - 1; i++) if (a[i] == "-simulatehang") float.TryParse(a[i + 1], out sec);
                    _simAt = sec > 0f ? Time.unscaledTime + sec : float.MaxValue;
                }
                if (Time.unscaledTime >= _simAt)
                {
                    _simDone = true;
                    Debug.LogWarning("【緊急救援】（測試開關）故意把主執行緒卡死，等看門狗來救");
                    Thread.Sleep(Timeout.Infinite);
                }
            }
#endif

            // ── 第一層：長按 Backspace 回主選單 ──
            if (!Enabled || _leaving) return;
            string sn = SceneManager.GetActiveScene().name;
            if (sn == GameFlow.MenuSceneName) { _hold = 0f; HideUi(); return; }

            if (Input.GetKey(KeyCode.Backspace))
            {
                _hold += Time.unscaledDeltaTime;
                float menuAt = Mathf.Max(MenuHoldSeconds, 2f);
                RescuePrompt.HoldActive = true;
                float mark = 1.2f / menuAt;                          // 1.2 秒是原本的「脫困」節點
                if (_hold < 1.2f) RescuePrompt.Show("嘗試脫困", "持續按住 Backspace", _hold / menuAt, mark, true);
                else RescuePrompt.Show("已嘗試脫困", "若仍無法行動，繼續按住 " + Mathf.Max(0f, menuAt - _hold).ToString("0.0") + " 秒返回主選單", _hold / menuAt, mark, true);
                if (_hold >= menuAt) { _hold = 0f; RescuePrompt.Show("正在返回主選單", "", 1f, -1f, true); StartCoroutine(LeaveRoutine()); }
            }
            else { _hold = 0f; HideUi(); }
        }

        /// <summary>逐級加重：走正常出口 → 直接載入主選單 → 還是不行就重開遊戲。</summary>
        private IEnumerator LeaveRoutine()
        {
            _leaving = true;
            Debug.LogWarning("【緊急救援】玩家長按 Backspace，緊急回主選單");
            ProgressSave.NoteAbnormalExit();   // 下次繼續從最近的安全位置接
            try { Time.timeScale = 1f; AudioListener.pause = false; } catch { }
            try { GameFlow.ReturnToMainMenu("緊急回主選單"); }
            catch (Exception e) { Debug.LogWarning("【緊急救援】正常出口失敗：" + e.Message); }

            float t = 0f;
            while (t < 3f && SceneManager.GetActiveScene().name != GameFlow.MenuSceneName) { t += Time.unscaledDeltaTime; yield return null; }
            if (SceneManager.GetActiveScene().name != GameFlow.MenuSceneName)
            {
                Debug.LogWarning("【緊急救援】正常出口沒反應，直接載入主選單場景");
                try { SceneManager.LoadScene(GameFlow.MenuSceneName); }
                catch (Exception e) { Debug.LogWarning("【緊急救援】直接載入失敗：" + e.Message); }
                t = 0f;
                while (t < 4f && SceneManager.GetActiveScene().name != GameFlow.MenuSceneName) { t += Time.unscaledDeltaTime; yield return null; }
            }
            if (SceneManager.GetActiveScene().name != GameFlow.MenuSceneName)
            {
                Debug.LogError("【緊急救援】還是回不了主選單，重開遊戲");
                RelaunchProcess();
            }
            _leaving = false;
            HideUi();
        }

        private void HideUi() { if (RescuePrompt.HoldActive) { RescuePrompt.Hide(true); RescuePrompt.HoldActive = false; } }
    }
}

/// <summary>★1110 每 2 秒記一次「安全站著的位置」（落地、沒被追、沒在過場、沒在掉落），給異常結束後的「繼續」用。</summary>
public class SafePositionRecorder : MonoBehaviour
{
    public static bool Enabled = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Boot()
    {
        if (!Enabled) return;
        GameObject go = new GameObject("[安全位置記錄]");
        DontDestroyOnLoad(go);
        go.AddComponent<SafePositionRecorder>();
    }

    private static System.Reflection.FieldInfo s_wChasing, s_wAttached;
    private float _next, _sceneStart;
    private PlayerMovement _pm;

    private void OnEnable() { SceneManager.sceneLoaded += OnLoaded; }
    private void OnDisable() { SceneManager.sceneLoaded -= OnLoaded; }
    private void OnLoaded(Scene s, LoadSceneMode m) { _sceneStart = Time.unscaledTime; _pm = null; }

    private void Awake()
    {
        s_wChasing = typeof(WolfEnemy).GetField("isChasing", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        s_wAttached = typeof(WolfEnemy).GetField("isAttached", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    }

    private void Update()
    {
        if (!Enabled || Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 2f;
        try { Sample(); } catch { /* 記錄位置絕對不能出錯 */ }
    }

    private void Sample()
    {
        string scene = SceneManager.GetActiveScene().name;
        if (!ProgressSave.IsGameplay(scene)) return;
        if (Time.unscaledTime - _sceneStart < 3f) return;
        if (!Mathf.Approximately(Time.timeScale, 1f)) return;
        if (_pm == null) { GameObject p = GameObject.FindWithTag("Player"); if (p != null) _pm = p.GetComponent<PlayerMovement>(); }
        if (_pm == null || !_pm.isActiveAndEnabled) return;
        if (_pm.isCutsceneFrozen || PlayerMovement.IsHardCutsceneLocked || _pm.IsInControlledFall) return;
        if (!(_pm.isGrounded || _pm.isSwimming)) return;
        if (PlayerRespawnSystem.IsAnyRespawning) return;

        // 被狼追、被狼咬、鳥在俯衝時不記（那種地方不適合當重來點）
        WolfEnemy[] wolves = FindObjectsByType<WolfEnemy>(FindObjectsSortMode.None);
        for (int i = 0; i < wolves.Length; i++)
        {
            WolfEnemy w = wolves[i];
            if (w == null || !w.isActiveAndEnabled) continue;
            if ((s_wChasing != null && (bool)s_wChasing.GetValue(w)) || (s_wAttached != null && (bool)s_wAttached.GetValue(w))) return;
        }
        IndividualBirdEnemy[] birds = FindObjectsByType<IndividualBirdEnemy>(FindObjectsSortMode.None);
        for (int i = 0; i < birds.Length; i++) if (birds[i] != null && birds[i].isActiveAndEnabled && birds[i].IsHostile) return;

        ProgressSave.OnSafeSample(scene, _pm.transform.position);
    }
}
