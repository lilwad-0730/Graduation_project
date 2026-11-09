using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ★1110 玻璃館：燭火底下那片會碎的玻璃平台，不能在她點燃到一半就碎掉。
///
/// 問題：玩家踩上「會碎的玻璃」約 1.2～2 秒就開始崩落，但點燃一根燭火要 1.5～1.9 秒（而且點的時候不能動）。
///       不熟的玩家走過去點，平台常常在點燃完成前就碎了，人掉下去、燭火卻還懸在半空，最後一根會直接卡關。
/// 做法（隊友的 BreakablePlatform／BreakableGlassFloor 各只加一行詢問）：
///   • 燭火「還沒點燃完成」時，燭火底下那片玻璃先不會因為踩上去而崩落（兩支崩落腳本開始崩落前會問我一聲，
///     被保護中就不開始倒數；那兩支各加了一行詢問，其他邏輯完全沒動）。
///   • 她點燃完成（或這根被判定漏掉）的那一刻解除保護；如果她正站在那片玻璃上，就立刻開始崩落，
///     原本「點完要趕快走」的壓力還在。最後一根點完就是結局，所以不受影響。
///   • 重生之後燭火回到沒點燃，玻璃又先保護起來。
///   • 萬一那片玻璃還是碎了（例如被怪物打碎）而燭火還沒被收，燭火就跟著慢慢往下飄、輕輕搖，
///     不會一個人懸在半空；在星空裡慢慢飄也合理。重生就回原位。
/// 只在玻璃館場景自動啟動。關掉：CandlePlatformGuard.Enabled = false。
/// </summary>
public class CandlePlatformGuard : MonoBehaviour
{
    public static bool Enabled = true;
    public static float DriftSpeed = 0.9f;        // 平台碎了之後，燭火往下飄的速度（公尺／秒）
    public static float DriftSway = 0.35f;        // 左右輕輕搖的幅度
    public static float DriftMaxDrop = 40f;       // 最多飄多遠
    /// <summary>目前被保護中的平台數（驗收／除錯用）。</summary>
    public static int HeldCount;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        SceneManager.sceneLoaded -= OnLoaded;
        SceneManager.sceneLoaded += OnLoaded;
        Ensure(SceneManager.GetActiveScene());
    }

    private static void OnLoaded(Scene s, LoadSceneMode m) { Ensure(s); }

    private static void Ensure(Scene s)
    {
        if (!Enabled || !s.IsValid()) return;
        string n = s.name.ToLowerInvariant();
        if (!n.Contains("glasses") && !n.Contains("glass")) return;
        if (FindFirstObjectByType<CandlePlatformGuard>() != null) return;
        new GameObject("[燭火平台保護]").AddComponent<CandlePlatformGuard>();
    }

    private class Entry
    {
        public CandleCollectible candle;
        public CandleAuraGlow aura;
        public BreakablePlatform bp;
        public BreakableGlassFloor gf;
        public GlassShatterFX fx;
        public Collider floorCol;
        public Vector3 candleHome;
        public bool holding;
        public float driftT;
    }

    private static readonly HashSet<Component> s_held = new HashSet<Component>();

    /// <summary>這片玻璃正被保護中（燭火還沒點燃完成）嗎？BreakablePlatform／BreakableGlassFloor 開始崩落前會問。</summary>
    public static bool IsHeld(Component c) { return Enabled && c != null && s_held.Contains(c); }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { s_held.Clear(); HeldCount = 0; }

    private readonly List<Entry> _entries = new List<Entry>();
    private float _collectAt, _nextCheck;
    private bool _collected;
    private Transform _player;
    private CandleCollectible _lastCandle;     // X 最大的那根＝最後一根
    private static FieldInfo s_gfTriggered;

    private void Awake()
    {
        if (s_gfTriggered == null) s_gfTriggered = typeof(BreakableGlassFloor).GetField("isTriggered", BindingFlags.NonPublic | BindingFlags.Instance);
    }

    private void Start() { _collectAt = Time.unscaledTime + 1.0f; }

    private void OnDestroy() { ReleaseAll(); HeldCount = 0; }

    private void Collect()
    {
        _collected = true;
        float maxX = float.NegativeInfinity;
        CandleCollectible[] cs = FindObjectsByType<CandleCollectible>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        BreakablePlatform[] bps = FindObjectsByType<BreakablePlatform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < cs.Length; i++)
        {
            CandleCollectible c = cs[i];
            if (c == null) continue;
            Vector3 cp = c.transform.position;
            if (cp.x > maxX) { maxX = cp.x; _lastCandle = c; }
            BreakablePlatform best = null; float bestScore = float.MaxValue; Collider bestCol = null;
            for (int k = 0; k < bps.Length; k++)
            {
                BreakablePlatform bp = bps[k];
                if (bp == null) continue;
                Collider col = bp.GetComponent<Collider>();
                if (col == null) continue;
                Bounds b = col.bounds;
                // 燭火在這片的左右範圍裡、而且就在它上面不遠（只看畫面上的 XY，Z 只是前後層次）
                if (cp.x < b.min.x - 0.5f || cp.x > b.max.x + 0.5f) continue;
                float above = cp.y - b.max.y;
                if (above < -1f || above > 8f) continue;
                float score = Mathf.Abs(above);
                if (score < bestScore) { bestScore = score; best = bp; bestCol = col; }
            }
            if (best == null) continue;
            Entry e = new Entry
            {
                candle = c,
                aura = c.GetComponent<CandleAuraGlow>(),
                bp = best,
                gf = best.GetComponent<BreakableGlassFloor>(),
                fx = best.GetComponent<GlassShatterFX>(),
                floorCol = bestCol,
                candleHome = cp
            };
            _entries.Add(e);
        }
        Debug.Log("【燭火平台保護】找到 " + _entries.Count + " 根燭火底下有會碎的玻璃，點燃完成前先不讓它崩落");
    }

    private void Update()
    {
        if (!Enabled) { if (_entries.Count > 0) ReleaseAll(); return; }
        if (!_collected) { if (Time.unscaledTime >= _collectAt) Collect(); return; }
        if (_player == null) { GameObject p = GameObject.FindWithTag("Player"); if (p != null) _player = p.transform; }

        bool check = Time.unscaledTime >= _nextCheck;
        if (check) _nextCheck = Time.unscaledTime + 0.15f;
        int held = 0;
        for (int i = 0; i < _entries.Count; i++)
        {
            Entry e = _entries[i];
            if (e.candle == null || e.bp == null) continue;
            if (check) Evaluate(e);
            if (e.holding) held++;
            Drift(e);
        }
        HeldCount = held;
    }

    private bool GfTriggered(Entry e)
    {
        return e.gf != null && s_gfTriggered != null && (bool)s_gfTriggered.GetValue(e.gf);
    }

    private void Evaluate(Entry e)
    {
        // 點燃完成就解除保護（原本「點完要趕快走」的壓力），但「最後一根」要留到怪物來收：最後一根點完就是結局，
        // 沒有下一片可以跳，崩了她掉下去重生、燭火又變回沒點燃，會變成永遠點不完。
        bool lit = e.aura != null && e.aura.IsLit;
        bool pending = !(e.candle.isCollected || e.candle.missed || (lit && e.candle != _lastCandle));
        bool platformIntact = e.bp.gameObject.activeInHierarchy && e.bp.CurrentState == BreakablePlatform.BreakState.Intact && !GfTriggered(e);

        if (pending && platformIntact)
        {
            // 還沒點燃、平台還好好的：保護起來（重生後也會在這裡重新保護）
            // （Unity 的碰撞事件就算腳本被 disable 也照樣送進去，所以不能用關腳本的方式；
            //   改成在兩支崩落腳本的「開始崩落」入口各加一行問我：這片被保護中就不開始。）
            s_held.Add(e.bp); if (e.gf != null) s_held.Add(e.gf); if (e.fx != null) s_held.Add(e.fx);
            e.holding = true;
        }
        else if (e.holding)
        {
            // 點燃完成（或漏掉），或平台已經不是完好的：放開，恢復原本的崩落機制
            e.holding = false;
            s_held.Remove(e.bp); if (e.gf != null) s_held.Remove(e.gf); if (e.fx != null) s_held.Remove(e.fx);
            Debug.Log("【燭火平台保護】放開 " + e.bp.name + "：燭火 " + e.candle.name + " 已收集=" + e.candle.isCollected + " 漏掉=" + e.candle.missed + " 平台完好=" + platformIntact + "（狀態 " + e.bp.CurrentState + "）");
            if (!pending && platformIntact && PlayerOnPlatform(e))
            {
                // 她正站在上面：從這一刻開始崩落（原本「點完要趕快走」的壓力）
                e.bp.StartBreakSequence();
                if (e.gf != null) e.gf.TriggerBreakSequence();
            }
        }
    }

    private bool PlayerOnPlatform(Entry e)
    {
        if (_player == null || e.floorCol == null) return false;
        Bounds b = e.floorCol.bounds;
        Vector3 p = _player.position;
        return p.x >= b.min.x - 0.5f && p.x <= b.max.x + 0.5f && Mathf.Abs(p.y - b.max.y) < 3.5f;
    }

    /// <summary>平台碎了、燭火還沒被收：燭火慢慢往下飄、輕輕搖；平台恢復（重生）就回原位。</summary>
    private void Drift(Entry e)
    {
        bool platformGone = !e.bp.gameObject.activeInHierarchy || e.bp.CurrentState == BreakablePlatform.BreakState.Falling || e.bp.CurrentState == BreakablePlatform.BreakState.Broken || GfTriggered(e);
        bool pending = !(e.candle.isCollected || e.candle.missed);
        if (platformGone && pending && e.candle.gameObject.activeInHierarchy)
        {
            e.driftT += Time.deltaTime;
            float drop = Mathf.Min(DriftMaxDrop, e.driftT * DriftSpeed);
            float sway = Mathf.Sin(e.driftT * 1.3f) * DriftSway;
            e.candle.transform.position = e.candleHome + new Vector3(sway, -drop, 0f);
        }
        else if (e.driftT > 0f)
        {
            e.driftT = 0f;
            if (e.candle != null && pending) e.candle.transform.position = e.candleHome;   // 重生：回到原位
        }
    }

    private void ReleaseAll()
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            Entry e = _entries[i];
            if (e.bp != null) s_held.Remove(e.bp);
            if (e.gf != null) s_held.Remove(e.gf);
            if (e.fx != null) s_held.Remove(e.fx);
            if (e.candle != null && e.driftT > 0f) e.candle.transform.position = e.candleHome;
            e.holding = false;
        }
        _entries.Clear();
    }
}
