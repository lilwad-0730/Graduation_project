using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ★1125 玻璃館（dark glasses）每根燭火上方的「按 S 點燃」線條畫動畫（取代原本貼在背景的那張大圖 Glass_Candles_light，
/// 也取代貼紙風的 WorldKeyPrompt 燭火提示）。
///
///   ・畫風跟玻璃館其他線條畫提示一樣：米白色線條、後面一圈暖色柔光（星空背景上才看得清楚）。
///   ・動畫一圈約 1.2 秒：按鍵靜止 → 三個小箭頭由上往下一個接一個流進按鍵 → 按鍵換成「按下去」那張（小壓扁、柔光閃一下）→ 彈回。
///   ・玩家走到這根燭火附近才淡入，離開淡出；開始點燃、已點亮、被吃掉、播過場時很快收起來；
///     重生後燭火重置成沒點亮，提示會自己再出現。
///   ・鍵鼠顯示 S 鍵帽；手把顯示 Y 圓鈕（依 InputPrompt.IsPad 即時切換）。
///
/// 不用手掛：場景載入時自動裝（只裝在玻璃館）。
/// 總開關：CandleLineKeyHint.Enabled＝false → 這個提示不出現，LineHintDirector 會改放回原本那張大圖、
///        WorldKeyPromptDirector 也會恢復貼紙燭火提示。
/// 素材：Assets/Resources/Hints/CandleKey_S_up／S_down／Y_up／Y_down／chevron.png
/// </summary>
public class CandleLineKeyHint : MonoBehaviour
{
    /// <summary>總開關。</summary>
    public static bool Enabled = true;

    public const string GlassSceneName = "dark glasses";

    /// <summary>這根燭火是不是由這個線條畫動畫負責（WorldKeyPromptDirector 用它避免重複提示）。</summary>
    public static bool Handles(CandleAuraGlow candle)
    {
        return Enabled && candle != null && candle.gameObject.scene.name == GlassSceneName;
    }

    // ── 可調參數 ──
    [Header("大小與位置")]
    [Tooltip("按鍵圖在世界裡的寬度（公尺）")]
    public float keyWidth = 1.15f;
    [Tooltip("提示底部離燭台（燭台圖片）最上緣多高（公尺）")]
    public float lift = 1.5f;
    [Tooltip("三個小箭頭的寬度，佔按鍵寬度的比例")]
    public float chevronWidthRatio = 0.42f;

    [Header("出現／消失")]
    [Tooltip("玩家左右離燭火多近才開始淡入（公尺）；到這個距離的 70% 內完全出現")]
    public float range = 6.5f;
    [Tooltip("玩家跟燭火上下差超過這麼多就不出現（不同層）")]
    public float verticalRange = 4.5f;
    [Tooltip("淡入淡出秒數")]
    public float fadeSeconds = 0.35f;
    [Tooltip("開始點燃、已點亮、被吃掉、過場時收起來的秒數（要很快）")]
    public float hideSeconds = 0.12f;

    [Header("動畫（一圈秒數）")]
    public float cycleSeconds = 1.2f;
    [Tooltip("按下去那一格出現的時間點（一圈中的第幾秒）")]
    public float pressAt = 0.78f;
    [Tooltip("按鍵維持按下去的秒數")]
    public float pressSeconds = 0.18f;
    [Tooltip("按下去時的壓扁（x 變寬、y 變矮）")]
    public Vector2 pressSquash = new Vector2(1.04f, 0.94f);
    [Tooltip("上下漂浮幅度（公尺）")]
    public float bobAmplitude = 0.05f;

    [Header("柔光（跟玻璃館 LineHintSign 同色）")]
    public Color glowColor = new Color(1f, 0.86f, 0.62f, 1f);
    [Range(0f, 1f)] public float glowIdle = 0.28f;
    [Range(0f, 1f)] public float glowPress = 0.65f;
    [Tooltip("柔光比按鍵大幾倍")]
    public float glowScale = 2.4f;

    [Header("圖層（跟 WorldKeyPrompt 同一層級，不會被玻璃地板或怪物霧蓋住）")]
    public int sortingOrder = 9400;

    private class Item
    {
        public CandleAuraGlow candle;
        public CandleCollectible collect;
        public Renderer[] rends;
        public LineKeyHintView view;   // ★1125 畫面（按鍵／箭頭／柔光／動畫）抽到共用的 LineKeyHintView，鏡箱提示也用同一份
    }

    private readonly List<Item> _items = new List<Item>();
    private readonly HashSet<CandleAuraGlow> _known = new HashSet<CandleAuraGlow>();
    private Transform _player;
    private float _nextLookup;

    // ═══════════════════════════ 啟動 ═══════════════════════════

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Install(SceneManager.GetActiveScene());
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single) return;
        Install(scene);
    }

    private static void Install(Scene scene)
    {
        if (!Enabled || !scene.IsValid() || scene.name != GlassSceneName) return;
        foreach (CandleLineKeyHint d in FindObjectsByType<CandleLineKeyHint>(FindObjectsInactive.Include))
            if (d.gameObject.scene == scene) return;
        GameObject host = new GameObject("[燭火按鍵線條畫]");
        SceneManager.MoveGameObjectToScene(host, scene);
        CandleLineKeyHint h = host.AddComponent<CandleLineKeyHint>();
        h.StartCoroutine(h.Run());
    }

    private IEnumerator Run()
    {
        // 有些物件是場景開始後才生出來的：先掃一次，之後再補掃（跟 WorldKeyPromptDirector 一樣）
        yield return new WaitForSecondsRealtime(0.6f);
        Scan();
        yield return new WaitForSecondsRealtime(4f);
        Scan();
        yield return new WaitForSecondsRealtime(10f);
        Scan();
    }

    private void Scan()
    {
        if (!Enabled) return;
        int n = 0;
        foreach (CandleAuraGlow c in FindObjectsByType<CandleAuraGlow>(FindObjectsInactive.Exclude))
        {
            if (c == null || !_known.Add(c)) continue;
            Item it = new Item();
            it.candle = c;
            it.collect = c.GetComponent<CandleCollectible>();
            it.rends = c.GetComponentsInChildren<Renderer>();
            it.view = new LineKeyHintView(transform, "[燭火提示]", "CandleKey_S", "CandleKey_Y", "【燭火按鍵提示】", Random.Range(0f, cycleSeconds));
            _items.Add(it);
            n++;
        }
        if (n > 0) Debug.Log("【燭火按鍵提示】★1125 玻璃館：" + n + " 根燭火上方裝了按 S 線條畫動畫");
    }

    // ═══════════════════════════ 每幀 ═══════════════════════════

    private Vector3 Anchor(Item it)
    {
        Transform t = it.candle.transform;
        bool has = false;
        Bounds b = new Bounds(t.position, Vector3.zero);
        foreach (Renderer r in it.rends)
        {
            // 光暈不算（沒點亮時是縮成一點、點亮後很大）；進度條平常是關著的，掃的時候就沒收進來
            if (r == null || r.gameObject.name == "CandleAuraGlow") continue;
            if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
        }
        float top = has ? b.max.y : t.position.y + 2.5f;
        float cx = has ? b.center.x : t.position.x;
        return new Vector3(cx, top + lift, t.position.z - 0.5f);
    }

    private void LateUpdate()
    {
        if (!Enabled)
        {
            foreach (Item it in _items) it.view.SetAll();
            return;
        }

        if (_player == null && Time.unscaledTime >= _nextLookup)
        {
            _nextLookup = Time.unscaledTime + 0.5f;
            PlayerMovement pm = FindAnyObjectByType<PlayerMovement>();
            if (pm != null) _player = pm.transform;
        }

        bool pad = InputPrompt.IsPad;
        bool cutscene = PlayerMovement.IsHardCutsceneLocked;
        Camera cam = Camera.main;

        for (int i = _items.Count - 1; i >= 0; i--)
        {
            Item it = _items[i];
            if (it.candle == null)
            {
                it.view.Destroy();
                _items.RemoveAt(i);
                continue;
            }
            UpdateItem(it, pad, cutscene, cam);
        }
    }

    private void UpdateItem(Item it, bool pad, bool cutscene, Camera cam)
    {
        CandleAuraGlow c = it.candle;
        Vector3 cp = c.transform.position;

        // 要不要出現：玩家近、這根燭火還能點（沒點亮、沒在點、沒被吃掉或漏掉）、沒在過場
        bool hide = !c.gameObject.activeInHierarchy || c.IsLit || c.IsLighting || cutscene
                    || (it.collect != null && (it.collect.isCollected || it.collect.missed));
        float want = 0f;
        if (!hide && _player != null)
        {
            float dx = Mathf.Abs(_player.position.x - cp.x);
            float dy = Mathf.Abs(_player.position.y - cp.y);
            if (dy <= verticalRange)
                want = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(range * 0.7f, range, dx));
        }

        if (!it.view.UpdatePresence(want, hide, fadeSeconds, hideSeconds)) return;

        // 位置：燭台上方、微微漂浮；動畫由共用的 LineKeyHintView 播
        it.view.Draw(MakeStyle(), pad, Anchor(it), cam);
    }

    private LineKeyHintView.Style MakeStyle()
    {
        LineKeyHintView.Style st = new LineKeyHintView.Style();
        st.keyWidth = keyWidth;
        st.chevronWidth = keyWidth * chevronWidthRatio;
        st.cycleSeconds = cycleSeconds;
        st.pressAt = pressAt;
        st.pressSeconds = pressSeconds;
        st.pressSquash = pressSquash;
        st.bobAmplitude = bobAmplitude;
        st.glowColor = glowColor;
        st.glowIdle = glowIdle;
        st.glowPress = glowPress;
        st.glowScale = glowScale;
        st.sortingOrder = sortingOrder;
        return st;
    }
}
