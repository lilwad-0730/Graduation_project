using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// ★1108 關卡進度存檔（崩潰、被關掉、當機之後可以從原本的進度繼續）。
///  存什麼：目前在哪一關、最近的存檔點座標（保留最近 5 個）、發生過的崩潰紀錄（崩潰迴圈斷路器用）。
///  沒存什麼：場景裡的機關狀態與撿到的東西（日誌、光絮）——繼續時場景是新的一輪，只是人站在存檔點。
///  存法：先寫暫存檔，再換名並留一份 .bak；讀不到主檔就讀 .bak，兩個都壞就當沒有存檔，不會當機。
///  位置：Application.persistentDataPath/progress.json（預設槽；其他槽見 SaveSlot）
/// 主選單左下角（故事按鈕上方）有「繼續」按鈕，只有存在有效存檔時才會出現。
/// 關掉：ProgressSave.Enabled = false。
/// </summary>
public static class ProgressSave
{
    public static bool Enabled = true;

    [Serializable] private class CheckpointRec { public string scene; public float x, y, z; public string timeTaipei; public List<StateEntry> states = new List<StateEntry>(); }   // ★1111 states＝踩到這個存檔點當下的世界狀態快照
    [Serializable] private class CrashRec { public string sig, scene; public int cx, cy, count; }
    [Serializable] private class SafeRec { public string scene; public float x, y, z; public double t; }
    [Serializable] private class HangRec { public string scene; public int cx, cy, count; }
    [Serializable]
    private class Data
    {
        public int version = 1;
        public bool running;                                   // ★1110 遊戲進行中＝true；正常離開／回主選單才改回 false。開機時還是 true＝上次是異常結束（卡死被重開、當機、被砍）
        public List<SafeRec> safe = new List<SafeRec>();       // ★1110 「最近安全站著的位置」，約每 2 秒一筆，留最近 12 筆
        public List<HangRec> hangs = new List<HangRec>();      // ★1110 異常結束發生過的地點（同地點反覆出事就改回存檔點）
        public string savedAtTaipei = "";
        public string scene = "";
        public float x, y, z;
        public int storyPage = -1;                             // 繪本解鎖到第幾頁；-1＝這個槽還沒存過（用預設）。非預設槽以這裡為準，預設槽仍以 PlayerPrefs 為準（這裡只是鏡像）
        public List<string> introSeen = new List<string>();    // 看過開場漫畫的關卡（非預設槽用；預設槽用 PlayerPrefs）
        public List<StateEntry> states = new List<StateEntry>();   // ★1111 最新存檔點的世界狀態快照（見 PersistentState）
        public List<CheckpointRec> history = new List<CheckpointRec>();
        public List<CrashRec> crashes = new List<CrashRec>();
    }

    private static Data _d;
    private static bool _dirty;
    private static float _lastFlush;

    private static string MainPath { get { return Path.Combine(SaveSlot.Dir, "progress.json"); } }
    private static string TmpPath { get { return MainPath + ".tmp"; } }
    private static string BakPath { get { return MainPath + ".bak"; } }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { _d = null; _dirty = false; _lastFlush = 0f; }

    // ───────────── 讀寫 ─────────────
    private static Data Load()
    {
        if (_d != null) return _d;
        _d = TryRead(MainPath) ?? TryRead(BakPath) ?? new Data();
        _prevAbnormal = _d.running;     // 開機時 running 還是 true＝上次沒有正常離開
        return _d;
    }

    // ───────────── ★1110 存檔槽切換（SaveSlot 呼叫）─────────────
    /// <summary>換槽之前：把目前槽還沒寫的進度寫掉，才不會寫到新槽去。</summary>
    internal static void OnSlotChanging() { if (_d != null && Enabled) Flush(); }   // 沒有變動就不寫（預設槽的檔案原封不動）
    /// <summary>換槽之後：丟掉記憶體裡舊槽的資料，下次用到時從新槽重新讀。</summary>
    internal static void OnSlotChanged() { _d = null; _dirty = false; _prevAbnormal = false; }

    /// <summary>這個槽記的繪本解鎖頁數；-1＝沒有紀錄。</summary>
    internal static int StoredStoryPage() { return Enabled ? Load().storyPage : -1; }
    /// <summary>繪本頁數有變，下次 Flush 寫進槽裡。</summary>
    internal static void NoteStoryChanged() { if (!Enabled) return; Load(); _dirty = true; }

    /// <summary>關卡開場漫畫看過沒：預設槽用 PlayerPrefs（跟以前一樣），其他槽記在槽裡。</summary>
    public static bool IntroSeen(string key)
    {
        if (SaveSlot.UsesLegacyPrefs || !Enabled) return PlayerPrefs.GetInt(key, 0) == 1;
        return Load().introSeen.Contains(key);
    }
    public static void MarkIntroSeen(string key)
    {
        if (SaveSlot.UsesLegacyPrefs || !Enabled) { PlayerPrefs.SetInt(key, 1); PlayerPrefs.Save(); return; }
        Data d = Load();
        if (!d.introSeen.Contains(key)) { d.introSeen.Add(key); _dirty = true; Flush(true); }
    }

    // ───────────── ★1110 異常結束與「最近安全位置」 ─────────────
    private static bool _prevAbnormal;

    /// <summary>上一次是不是異常結束（卡死被重開、當機、被砍）。下次「繼續」會從最近的安全位置接著玩，不是存檔點。</summary>
    public static bool PreviousExitAbnormal { get { Load(); return _prevAbnormal; } }

    /// <summary>手動標記：這次是被緊急手段拉回主選單的，下次繼續也從安全位置接。</summary>
    public static void NoteAbnormalExit() { Load(); _prevAbnormal = true; }

    internal static bool IsGameplay(string n) { return IsGameplayScene(n); }

    /// <summary>遊戲進行中標成 true、正常離開標回 false（立刻寫進磁碟，因為下一秒可能就被砍了）。</summary>
    internal static void MarkRunning(bool on)
    {
        if (!Enabled || TesterMode.StartScene != null) return;
        Data d = Load();
        if (d.running == on) return;
        d.running = on; _dirty = true; Flush(true);
    }

    /// <summary>記一筆「安全站著的位置」。位置幾乎沒動就只更新時間。</summary>
    internal static void OnSafeSample(string scene, Vector3 pos)
    {
        if (!Enabled || TesterMode.StartScene != null || !IsGameplayScene(scene)) return;
        Data d = Load();
        double now = (DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
        SafeRec last = d.safe.Count > 0 ? d.safe[d.safe.Count - 1] : null;
        if (last != null && last.scene == scene && Vector2.Distance(new Vector2(last.x, last.y), new Vector2(pos.x, pos.y)) < 0.3f) { last.t = now; }
        else
        {
            d.safe.Add(new SafeRec { scene = scene, x = pos.x, y = pos.y, z = pos.z, t = now });
            while (d.safe.Count > 12) d.safe.RemoveAt(0);
        }
        _dirty = true;
    }

    private static Data TryRead(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            Data d = JsonUtility.FromJson<Data>(File.ReadAllText(path, Encoding.UTF8));
            return (d != null && d.version >= 1) ? d : null;
        }
        catch (Exception e) { Debug.LogWarning("【進度存檔】讀取失敗（" + Path.GetFileName(path) + "）：" + e.Message); return null; }
    }

    /// <summary>寫入磁碟：先寫 .tmp，成功才換名；舊的留成 .bak。</summary>
    public static void Flush(bool force = false)
    {
        if (!Enabled || _d == null || (!_dirty && !force)) return;
        try
        {
            Directory.CreateDirectory(SaveSlot.Dir);
            _d.savedAtTaipei = DateTime.UtcNow.AddHours(8).ToString("yyyy-MM-dd HH:mm:ss");
            _d.storyPage = StoryProgress.UnlockedThrough;
            File.WriteAllText(TmpPath, JsonUtility.ToJson(_d, true), new UTF8Encoding(false));
            if (File.Exists(MainPath)) File.Replace(TmpPath, MainPath, BakPath);
            else File.Move(TmpPath, MainPath);
            _dirty = false;
            _lastFlush = Time.unscaledTime;
        }
        catch (Exception e) { Debug.LogWarning("【進度存檔】寫入失敗：" + e.Message); }
    }

    // ───────────── 記錄存檔點 ─────────────
    private static bool IsGameplayScene(string n)
    {
        return !string.IsNullOrEmpty(n) && n != GameFlow.MenuSceneName && n != "Book";
    }

    internal static void OnCheckpoint(Vector3 pos)
    {
        if (!Enabled) return;
        string scene = SceneManager.GetActiveScene().name;
        if (!IsGameplayScene(scene)) return;
        if (TesterMode.StartScene != null) return;   // 測試檔指定跳關時不存，避免測試污染玩家存檔
        Data d = Load();
        d.scene = scene; d.x = pos.x; d.y = pos.y; d.z = pos.z;
        List<StateEntry> snap = PersistentState.CaptureAll();   // ★1111 踩到存檔點當下，把有註冊的機關狀態拍成快照
        d.states = snap;
        CheckpointRec last = d.history.Count > 0 ? d.history[d.history.Count - 1] : null;
        if (last != null && last.scene == scene && Vector2.Distance(new Vector2(last.x, last.y), new Vector2(pos.x, pos.y)) <= 0.5f) last.states = snap;
        // 這是 2D 橫向遊戲：只看 XY 距離（Z 軸只是前後層次，同一點 Z 抖動不該算新的存檔點）
        if (last == null || last.scene != scene || Vector2.Distance(new Vector2(last.x, last.y), new Vector2(pos.x, pos.y)) > 0.5f)
        {
            d.history.Add(new CheckpointRec { scene = scene, x = pos.x, y = pos.y, z = pos.z, timeTaipei = DateTime.UtcNow.AddHours(8).ToString("MM/dd HH:mm"), states = snap });
            while (d.history.Count > 5) d.history.RemoveAt(0);
        }
        _dirty = true;
    }

    // ───────────── 崩潰迴圈紀錄 ─────────────
    private static int CheckpointKey(out int cx, out int cy)
    {
        cx = cy = 0;
        PlayerRespawnSystem rs = PlayerRespawnSystem.Instance;
        if (rs == null) return 0;
        Vector3 p = rs.ActivePosition;
        cx = Mathf.RoundToInt(p.x); cy = Mathf.RoundToInt(p.y);
        return 1;
    }

    internal static void RecordCrash(string sig)
    {
        if (!Enabled || TesterMode.StartScene != null) return;
        CheckpointKey(out int cx, out int cy);
        string scene = SceneManager.GetActiveScene().name;
        Data d = Load();
        CrashRec r = d.crashes.Find(c => c.sig == sig && c.scene == scene && c.cx == cx && c.cy == cy);
        if (r == null) { r = new CrashRec { sig = sig, scene = scene, cx = cx, cy = cy }; d.crashes.Add(r); }
        r.count++;
        while (d.crashes.Count > 30) d.crashes.RemoveAt(0);
        _dirty = true;
        Flush(true);   // 崩潰的當下就寫進磁碟，因為下一秒可能就整個掛了
    }

    internal static int CrashCountHere(string sig)
    {
        CheckpointKey(out int cx, out int cy);
        string scene = SceneManager.GetActiveScene().name;
        CrashRec r = Load().crashes.Find(c => c.sig == sig && c.scene == scene && c.cx == cx && c.cy == cy);
        return r != null ? r.count : 0;
    }

    // ───────────── 繼續 ─────────────
    /// <summary>有沒有可以繼續的存檔（關卡存在於 Build Settings）。</summary>
    public static bool HasSave
    {
        get
        {
            if (!Enabled) return false;
            Data d = Load();
            return !string.IsNullOrEmpty(d.scene) && IsGameplayScene(d.scene) && Application.CanStreamedLevelBeLoaded(d.scene);
        }
    }

    public static string SceneLabel(string scene)
    {
        string l = (scene ?? "").ToLower();
        if (l == "cottoncastle") return "棉花堡";
        if (l == "samplescene" || l.Contains("ruined")) return "廢墟";
        if (l == "desert") return "荒原";
        if (l == "underwater") return "水下";
        if (l.Contains("dark glasses")) return "玻璃館";
        return scene;
    }

    public static string Describe()
    {
        Data d = Load();
        string t = string.IsNullOrEmpty(d.savedAtTaipei) ? "" : d.savedAtTaipei.Substring(5, 11);
        return SceneLabel(d.scene) + (t.Length > 0 ? "　" + t : "");
    }

    /// <summary>主選單按「繼續」：載入存檔的那一關，人放在存檔點；若這個存檔點曾讓遊戲反覆崩潰，改放更早的存檔點並開安全模式。</summary>
    public static bool Continue()
    {
        if (!HasSave) return false;
        Data d = Load();
        Vector3 pos = new Vector3(d.x, d.y, d.z);
        bool useDefaultStart = false;
        List<StateEntry> restoreStates = d.states;   // ★1111 預設用最新存檔點的世界快照；退回更早存檔點時改用那個存檔點自己的快照

        CrashRec loop = d.crashes.Find(c => c.scene == d.scene && c.cx == Mathf.RoundToInt(d.x) && c.cy == Mathf.RoundToInt(d.y) && c.count >= 2);
        if (loop != null)
        {
            Debug.LogWarning("【崩潰迴圈斷路器】這個存檔點讓遊戲反覆出錯（" + loop.sig + "），改從更早的位置繼續，並開啟安全模式");
            GameRecoveryGuard.ActivateSafeMode("繼續時發現存檔點有反覆崩潰紀錄");
            CheckpointRec older = null;
            for (int i = d.history.Count - 1; i >= 0; i--)
            {
                CheckpointRec h = d.history[i];
                if (h.scene == d.scene && Vector2.Distance(new Vector2(h.x, h.y), new Vector2(pos.x, pos.y)) > 1f) { older = h; break; }
            }
            if (older != null) { pos = new Vector3(older.x, older.y, older.z); restoreStates = older.states; } else useDefaultStart = true;
            d.crashes.RemoveAll(c => c.scene == d.scene && c.cx == loop.cx && c.cy == loop.cy);   // 只避開這一次
            _dirty = true; Flush(true);
        }

        // ★1110 上次是異常結束（卡死被重開、當機）→ 從「最近安全站著的位置」接著玩（刻意往回退幾秒，避開出事的那一點）
        string scene = d.scene;
        string how = "存檔點";
        if (_prevAbnormal && !useDefaultStart && loop == null)
        {
            _prevAbnormal = false;
            SafeRec pick = PickSafeSample(d);
            if (pick != null)
            {
                scene = pick.scene; pos = new Vector3(pick.x, pick.y, pick.z); how = "最近安全位置（避開出事點）";
            }
        }
        _prevAbnormal = false;

        // ★1111 世界快照只在「進的是存檔那一關」時還原；從關卡起點重來（useDefaultStart）就不還原
        _pendingStates = (!useDefaultStart && scene == d.scene) ? restoreStates : null;
        _pendingScene = scene;

        GameFlow.ContinueRun();                                   // 會清掉待處理的出生點，所以要先呼叫
        if (useDefaultStart) PlayerRespawnSystem.ClearPendingSceneSpawn();
        else PlayerRespawnSystem.QueueNextSceneSpawn(pos);
        Debug.Log("【進度存檔】繼續：" + scene + " " + (useDefaultStart ? "（關卡起點）" : pos.ToString("F2") + "（" + how + "）"));
        SceneLoader.Load(scene);
        return true;
    }

    // ───────────── ★1111 繼續後還原世界快照 ─────────────
    private static List<StateEntry> _pendingStates;
    private static string _pendingScene;

    /// <summary>場景載入後由 Runner 呼叫：還有待還原的快照、而且場景對得上，就把它交出去（只交一次的話由呼叫端決定何時清掉）。</summary>
    internal static bool HasPendingStatesFor(string scene) { return _pendingStates != null && _pendingStates.Count > 0 && _pendingScene == scene; }
    internal static int ApplyPendingStates() { return PersistentState.RestoreAll(_pendingStates); }
    internal static void ClearPendingStates() { _pendingStates = null; _pendingScene = null; }

    /// <summary>
    /// 異常結束後挑哪個位置接著玩：記下這次出事的地點；同一帶（6 公尺內）出事第 2 次起不再用安全位置、改回存檔點（第 3 次起開安全模式）；
    /// 否則挑「比最後一筆早至少 8 秒」的安全位置，沒有就用最早的一筆。回傳 null＝用存檔點。
    /// </summary>
    private static SafeRec PickSafeSample(Data d)
    {
        if (d.safe.Count == 0) return null;
        SafeRec latest = d.safe[d.safe.Count - 1];
        if (string.IsNullOrEmpty(latest.scene) || !Application.CanStreamedLevelBeLoaded(latest.scene) || !IsGameplayScene(latest.scene)) return null;

        HangRec h = d.hangs.Find(c => c.scene == latest.scene && Vector2.Distance(new Vector2(c.cx, c.cy), new Vector2(latest.x, latest.y)) < 6f);
        if (h == null) { h = new HangRec { scene = latest.scene, cx = Mathf.RoundToInt(latest.x), cy = Mathf.RoundToInt(latest.y) }; d.hangs.Add(h); }
        h.count++;
        while (d.hangs.Count > 20) d.hangs.RemoveAt(0);
        _dirty = true; Flush(true);
        if (h.count >= 3) GameRecoveryGuard.ActivateSafeMode("同一帶反覆異常結束");
        if (h.count >= 2) { Debug.LogWarning("【進度存檔】這一帶已經異常結束 " + h.count + " 次，改回存檔點繼續"); return null; }

        SafeRec best = null;
        for (int i = d.safe.Count - 1; i >= 0; i--)
        {
            SafeRec s = d.safe[i];
            if (s.scene != latest.scene) break;
            best = s;
            if (latest.t - s.t >= 8.0) break;
        }
        return best;
    }
}

/// <summary>跑在背景的存檔工人：訂閱存檔點、定時寫檔、離開時寫檔、主選單長出「繼續」按鈕。</summary>
public class ProgressSaveRunner : MonoBehaviour
{
    private static bool _booted;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { _booted = false; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Boot()
    {
        if (_booted || !ProgressSave.Enabled) return;
        _booted = true;
        GameObject go = new GameObject("[進度存檔]");
        DontDestroyOnLoad(go);
        go.AddComponent<ProgressSaveRunner>();
    }

    private void OnEnable()
    {
        PlayerRespawnSystem.OnCheckpointChanged += ProgressSave.OnCheckpoint;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        PlayerRespawnSystem.OnCheckpointChanged -= ProgressSave.OnCheckpoint;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private float _next;
    private void Update()
    {
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 1.5f;
        ProgressSave.Flush();
    }

    private void OnApplicationPause(bool paused) { if (paused) ProgressSave.Flush(); }
    private void OnApplicationQuit() { ProgressSave.MarkRunning(false); ProgressSave.Flush(true); }

    /// <summary>
    /// 場景載入後等一下才還原：玩家被放到存檔點、場景自己的進場演出（例如廢墟墜落把光球傳到 P12）都會先跑，
    /// 太早還原會被它們蓋回去。分兩次（1.2 秒、3 秒）——還原本身只會「往前」，重複呼叫不會有副作用。
    /// </summary>
    private System.Collections.IEnumerator RestoreStatesRoutine(string sceneName)
    {
        float[] at = { 1.2f, 3f };
        float t = 0f;
        for (int i = 0; i < at.Length; i++)
        {
            while (t < at[i]) { t += Time.unscaledDeltaTime; yield return null; }
            if (SceneManager.GetActiveScene().name != sceneName || !ProgressSave.HasPendingStatesFor(sceneName)) yield break;
            int n = ProgressSave.ApplyPendingStates();
            Debug.Log("【世界狀態】繼續後還原快照（第 " + (i + 1) + " 次）：" + n + " 個機關");
        }
        ProgressSave.ClearPendingStates();
    }

    private void Start()
    {
        // ★1111 冷啟動（剛開遊戲、第一個場景就是主選單）收不到 sceneLoaded，「繼續」按鈕就不會長出來（實測：有存檔、重開遊戲看不到繼續）。
        //   所以啟動後第一幀自己補跑一次；OnSceneLoaded 內部是重複呼叫也安全的。
        Scene s = SceneManager.GetActiveScene();
        if (s.IsValid()) OnSceneLoaded(s, LoadSceneMode.Single);
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single) return;
        // ★1110 進入關卡＝遊戲進行中（被砍掉時這個旗標會留著＝異常結束）；回主選單＝正常離開
        if (ProgressSave.IsGameplay(scene.name)) ProgressSave.MarkRunning(true);
        else if (scene.name == GameFlow.MenuSceneName) ProgressSave.MarkRunning(false);
        ProgressSave.Flush();
        if (ProgressSave.HasPendingStatesFor(scene.name)) StartCoroutine(RestoreStatesRoutine(scene.name));
        else if (scene.name == GameFlow.MenuSceneName) ProgressSave.ClearPendingStates();
        if (scene.name == GameFlow.MenuSceneName && ProgressSave.HasSave && FindAnyObjectByType<ContinueMenuButton>() == null)
        {
            GameObject host = new GameObject("[ContinueMenuButton]");
            SceneManager.MoveGameObjectToScene(host, scene);
            host.AddComponent<ContinueMenuButton>();
        }
    }
}

/// <summary>主選單的「繼續」按鈕（程式長出來的，樣子跟「故事到目前為止」同一套，擺在它的上方）。</summary>
public class ContinueMenuButton : MonoBehaviour
{
    private void Start()
    {
        GameObject canvasGo = new GameObject("ContinueButtonCanvas");
        canvasGo.transform.SetParent(transform, false);
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;
        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();
        if (FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            GameObject es = new GameObject("EventSystem");
            es.transform.SetParent(transform, false);
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }

        GameObject btnGo = new GameObject("ContinueButton");
        btnGo.transform.SetParent(canvasGo.transform, false);
        RectTransform rt = btnGo.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.zero; rt.pivot = Vector2.zero;
        // 放在主選單那排按鈕（開始遊戲、設定…）的最上面、跟它們同一欄對齊。
        // 參考解析度 1920×1080、左下角為原點：「開始遊戲」的上緣約 y=577，標題的下緣約 y=700，所以這顆放 y=596～660 剛好卡在中間的空位。
        // （第一版放在左下角故事按鈕上方，實測截圖壓到「離開遊戲」，已改掉。）
        rt.anchoredPosition = new Vector2(160f, 596f);
        rt.sizeDelta = new Vector2(520f, 64f);
        Image bg = btnGo.AddComponent<Image>();
        bg.color = new Color(0.05f, 0.07f, 0.16f, 0.55f);
        Button btn = btnGo.AddComponent<Button>();
        ColorBlock cb = btn.colors;
        cb.highlightedColor = new Color(0.25f, 0.27f, 0.4f, 0.8f);
        cb.pressedColor = new Color(0.4f, 0.4f, 0.55f, 0.9f);
        btn.colors = cb;
        btn.onClick.AddListener(() => { ProgressSave.Continue(); });

        GameObject txtGo = new GameObject("Label");
        txtGo.transform.SetParent(btnGo.transform, false);
        RectTransform trt = txtGo.AddComponent<RectTransform>();
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(18f, 6f); trt.offsetMax = new Vector2(-18f, -6f);
        Text label = txtGo.AddComponent<Text>();
        Font f = Resources.Load<Font>("Fonts/ChenYuluoyan-2.0-Thin");
        if (f == null) f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.font = f;
        label.fontSize = 30;
        label.alignment = TextAnchor.MiddleLeft;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        label.color = new Color(0.91f, 0.88f, 0.82f, 0.95f);
        label.raycastTarget = false;
        label.text = "繼續　" + ProgressSave.Describe();
    }
}
