using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ★1109 差點被咬慢動作：狼貼到幾乎碰到她、卻沒咬到（她甩開了、或狼被擋掉）→ 時間慢下來約 0.35 秒、邊緣暗角「咚」一下，
/// 然後回到正常。給「驚險逃過」一個喘不過氣的瞬間。鳥俯衝擦過、沒打到她，也一樣。
///
/// 安全規則（隊友有一堆腳本會「記下 timeScale → 設 0 → 再還原」，例如文字卡、暫停、荒原光球、綠洲下沉，
/// 如果它們在我慢動作中途記走 0.3，結束後就會一直卡在 0.3）：
///  • 只在 timeScale 剛好等於 1 時才開始（暫停、文字卡、別人的慢動作中都不開始）。
///  • 每一幀先確認 timeScale 還是我自己上次設的值；被別人動過就立刻放手、不再碰。
///  • 每次結束都還原到 1；重生、換場景、離開 Play 都強制還原。
///  • 看門狗：我不在演的時候，如果 timeScale 停在「我的慢動作值」超過半秒，就是被別人記走還原錯了，拉回 1。
/// 只讀狼與鳥的狀態（反射），不改它們任何行為。關掉：NearMissSlowMoFX.Enabled = false。
/// </summary>
public class NearMissSlowMoFX : MonoBehaviour
{
    public static bool Enabled = true;

    // 企劃可調
    public static float SlowScale = 0.3f;
    public static float HoldSeconds = 0.16f;       // 真實時間
    public static float RecoverSeconds = 0.22f;    // 真實時間，慢慢加速回 1
    public static float Cooldown = 10f;            // 兩次之間至少隔幾秒
    public static float CloseGap = 0.7f;           // 身體縫隙小於幾公尺算「貼到身上」（0＝已經碰到＝咬住）
    public static float EscapeGap = 0.8f;          // 縫隙拉開多少算「甩開」
    /// <summary>已經演過幾次（驗收／除錯用）。</summary>
    public static int PlayCount;

    private static NearMissSlowMoFX s_inst;
    private static float s_sceneStart;
    private static FieldInfo s_wChasing, s_wAttached, s_birdState;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        s_sceneStart = Time.unscaledTime;
        SceneManager.sceneLoaded -= OnLoaded;
        SceneManager.sceneLoaded += OnLoaded;
        PlayerRespawnSystem.OnResettablesReset -= OnRespawnReset;
        PlayerRespawnSystem.OnResettablesReset += OnRespawnReset;
        Ensure();
    }

    private static void OnLoaded(Scene s, LoadSceneMode m)
    {
        s_sceneStart = Time.unscaledTime;
        if (s_inst != null) s_inst.ForceRestore();
        Ensure();
    }

    private static void OnRespawnReset()
    {
        if (s_inst != null) s_inst.ForceRestore();
    }

    private static void Ensure()
    {
        if (!Enabled || s_inst != null) return;
        GameObject go = new GameObject("[差點被咬慢動作]");
        DontDestroyOnLoad(go);
        s_inst = go.AddComponent<NearMissSlowMoFX>();
    }

    private class Track { public bool close; public float minDx; public float closeAt; }

    private readonly Dictionary<Object, Track> _tracks = new Dictionary<Object, Track>();
    private WolfEnemy[] _wolves = new WolfEnemy[0];
    private IndividualBirdEnemy[] _birds = new IndividualBirdEnemy[0];
    private Transform _player;
    private PlayerMovement _pm;
    private float _nextScan, _lastTrigger = -999f;

    private bool _active;
    private float _t;
    private float _lastSet = -1f;       // 我最後一次設的 timeScale；-1＝我沒在動它
    private float _stuckFor;
    private HeartbeatVignette _hv;

    private void Awake()
    {
        if (s_wChasing == null)
        {
            s_wChasing = typeof(WolfEnemy).GetField("isChasing", BindingFlags.NonPublic | BindingFlags.Instance);
            s_wAttached = typeof(WolfEnemy).GetField("isAttached", BindingFlags.NonPublic | BindingFlags.Instance);
            s_birdState = typeof(IndividualBirdEnemy).GetField("currentState", BindingFlags.NonPublic | BindingFlags.Instance);
        }
    }

    private void Update()
    {
        if (!Enabled) { ForceRestore(); return; }
        float dt = Time.unscaledDeltaTime;

        if (_active) { RunEffect(dt); return; }
        Watchdog(dt);
        if (_hv != null) _hv.Intensity = Mathf.MoveTowards(_hv.Intensity, 0f, dt * 2f);

        if (Time.unscaledTime >= _nextScan)
        {
            _nextScan = Time.unscaledTime + 0.4f;
            _wolves = FindObjectsByType<WolfEnemy>(FindObjectsSortMode.None);
            _birds = FindObjectsByType<IndividualBirdEnemy>(FindObjectsSortMode.None);
            if (_player == null)
            {
                GameObject p = GameObject.FindWithTag("Player");
                if (p != null) { _player = p.transform; _pm = p.GetComponent<PlayerMovement>(); }
            }
        }
        if (_player == null) return;
        if (CheckNearMiss()) Begin();
    }

    // ───────── 偵測 ─────────
    private bool CheckNearMiss()
    {
        Vector2 pp = _player.position;
        bool miss = false;

        for (int i = 0; i < _wolves.Length; i++)
        {
            WolfEnemy w = _wolves[i];
            if (w == null || !w.isActiveAndEnabled) continue;
            bool attached = s_wAttached != null && (bool)s_wAttached.GetValue(w);
            bool chasing = s_wChasing != null && (bool)s_wChasing.GetValue(w);
            Track tr = GetTrack(w);
            if (attached) { tr.close = false; ClearAllClose(); return false; }     // 被咬到了：不是「差點」

            // 用兩個身體之間的「縫隙」算（狼身體很寬，中心距離 1.8 就已經碰到並咬住了）
            float gap = BodyGap(w);
            bool near = chasing && gap < CloseGap;
            if (near)
            {
                if (!tr.close) { tr.close = true; tr.minDx = gap; tr.closeAt = Time.unscaledTime; }
                else tr.minDx = Mathf.Min(tr.minDx, gap);
            }
            else if (tr.close)
            {
                bool escaped = gap > tr.minDx + EscapeGap || !chasing;
                bool fresh = Time.unscaledTime - tr.closeAt < 3f;
                tr.close = false;
                if (escaped && fresh) miss = true;
            }
        }

        for (int i = 0; i < _birds.Length; i++)
        {
            IndividualBirdEnemy b = _birds[i];
            if (b == null || !b.isActiveAndEnabled || s_birdState == null) continue;
            string st = s_birdState.GetValue(b).ToString();
            Track tr = GetTrack(b);
            Vector2 dv = (Vector2)b.transform.position - pp;
            bool diving = st == "Diving";
            if (diving && dv.magnitude < 2.2f)
            {
                if (!tr.close) { tr.close = true; tr.closeAt = Time.unscaledTime; }
            }
            else if (tr.close && !diving)
            {
                // 俯衝結束：只有「回到待機／排隊／預警」才算沒打到她被甩開；卡住、被彈開、她被撞飛都不算
                tr.close = false;
                bool missed = st == "Idle" || st == "Pending" || st == "Warning";
                if (missed && Time.unscaledTime - tr.closeAt < 3f) miss = true;
            }
        }
        return miss;
    }

    /// <summary>狼與她的碰撞體在畫面平面（XY）上的縫隙；碰到＝0。</summary>
    private float BodyGap(WolfEnemy w)
    {
        Collider wc = w.GetComponent<Collider>();
        if (_pCol == null) _pCol = _player.GetComponentInChildren<Collider>();
        if (wc == null || _pCol == null)
        {
            Vector2 d = (Vector2)w.transform.position - (Vector2)_player.position;
            return Mathf.Max(0f, d.magnitude - 1.8f);
        }
        Bounds a = wc.bounds, b = _pCol.bounds;
        float gx = Mathf.Max(0f, Mathf.Abs(a.center.x - b.center.x) - (a.extents.x + b.extents.x));
        float gy = Mathf.Max(0f, Mathf.Abs(a.center.y - b.center.y) - (a.extents.y + b.extents.y));
        return Mathf.Sqrt(gx * gx + gy * gy);
    }

    private Collider _pCol;

    private Track GetTrack(Object o)
    {
        Track t;
        if (!_tracks.TryGetValue(o, out t)) { t = new Track(); _tracks[o] = t; }
        return t;
    }

    private void ClearAllClose() { foreach (var kv in _tracks) kv.Value.close = false; }

    // ───────── 演出 ─────────
    private bool CanStart()
    {
        if (!Mathf.Approximately(Time.timeScale, 1f)) return false;                 // 暫停、文字卡、別人的慢動作
        if (Time.unscaledTime - _lastTrigger < Cooldown) return false;
        if (Time.unscaledTime - s_sceneStart < 3f) return false;
        PlayerRespawnSystem rs = PlayerRespawnSystem.Instance;
        if (rs != null && rs.IsRespawning) return false;
        if (PlayerMovement.IsHardCutsceneLocked) return false;
        if (_pm != null && _pm.isCutsceneFrozen) return false;
        return true;
    }

    private void Begin()
    {
        ClearAllClose();
        if (!CanStart()) return;
        _active = true;
        _t = 0f;
        _lastTrigger = Time.unscaledTime;
        PlayCount++;
        if (_hv == null)
        {
            _hv = HeartbeatVignette.Create(transform, "差點被咬暗角", 41);   // 疊層：41，緊貼在心跳暗角 40 上面、仍低於提示 9000 與文字卡 10000
            _hv.Color = new Color(0.02f, 0.01f, 0.02f, 1f);
            _hv.MaxAlpha = 0.42f;
            _hv.Pulse = 0f;
            _hv.Spread = 0.9f;
            _hv.Bps = 1.5f;
        }
        Set(SlowScale);
    }

    private void RunEffect(float dt)
    {
        // 被別人動過 timeScale（文字卡、暫停…）→ 立刻放手，不去覆蓋它
        if (_lastSet >= 0f && !Mathf.Approximately(Time.timeScale, _lastSet)) { Abandon(); return; }
        PlayerRespawnSystem rs = PlayerRespawnSystem.Instance;
        if (rs != null && rs.IsRespawning) { ForceRestore(); return; }

        _t += dt;
        if (_t < HoldSeconds)
        {
            Set(SlowScale);
            if (_hv != null) _hv.Intensity = Mathf.MoveTowards(_hv.Intensity, 0.85f, dt * 8f);
        }
        else
        {
            float k = (_t - HoldSeconds) / RecoverSeconds;
            if (k >= 1f) { ForceRestore(); return; }
            Set(Mathf.Lerp(SlowScale, 1f, k * k));
            if (_hv != null) _hv.Intensity = Mathf.MoveTowards(_hv.Intensity, 0f, dt * 2.5f);
        }
    }

    private void Set(float v)
    {
        Time.timeScale = v;
        _lastSet = v;
    }

    private void Abandon()
    {
        _active = false;
        _lastSet = -1f;
    }

    /// <summary>強制結束：只有 timeScale 還是我設的值才還原成 1；別人改過就不碰。</summary>
    private void ForceRestore()
    {
        if (_lastSet >= 0f && Mathf.Approximately(Time.timeScale, _lastSet)) Time.timeScale = 1f;
        _active = false;
        _lastSet = -1f;
        if (_hv != null) _hv.Intensity = 0f;
    }

    /// <summary>我不在演、timeScale 卻停在我的慢動作值：被別人「記下 → 還原」記走了，拉回 1。</summary>
    private void Watchdog(float dt)
    {
        if (Mathf.Abs(Time.timeScale - SlowScale) < 0.005f)
        {
            _stuckFor += dt;
            if (_stuckFor > 0.5f && PlayCount > 0) { Time.timeScale = 1f; _stuckFor = 0f; }
        }
        else _stuckFor = 0f;
    }

    private void OnDestroy()
    {
        ForceRestore();
        if (s_inst == this) s_inst = null;
    }

    private void OnApplicationQuit() { ForceRestore(); }
}
