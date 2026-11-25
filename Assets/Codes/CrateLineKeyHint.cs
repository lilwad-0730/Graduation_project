using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ★1125 玻璃館（dark glasses）鏡箱（MirrorCrate）上方的「按住 Shift 抓箱子」線條畫動畫
/// （取代原本貼在背景的大圖 Glass_CratePush_light，也取代貼紙風的 WorldKeyPrompt 鏡箱提示）。
///
///   ・畫風、動畫跟燭火的 S 鍵提示（CandleLineKeyHint）一樣，畫面共用 LineKeyHintView：
///     三個小箭頭往下流進 SHIFT 鍵 → 鍵按下去（壓扁、柔光閃一下）→ 彈回，一圈約 1.2 秒。
///   ・放在箱子頂上方，箱子被推著走時跟著它；玩家走到箱子附近才淡入，離開淡出。
///   ・她抓著箱子（推／拉）、箱子在掉落或被藏起來、播過場時很快收起來；放手後（還在附近）會再出現。
///     MirrorCrate 沒有「任務完成」的狀態，所以不會永久消失。
///   ・鍵鼠顯示 SHIFT 鍵帽；手把顯示 X 圓鈕（依 InputPrompt.IsPad 即時切換）。
///
/// 不用手掛：場景載入時自動裝（只裝在玻璃館）。
/// 總開關：CrateLineKeyHint.Enabled＝false → 這個提示不出現，LineHintDirector 會改放回原本那張大圖、
///        WorldKeyPromptDirector 也會恢復貼紙鏡箱提示。
/// 素材：Assets/Resources/Hints/CrateKey_Shift_up／_down、CrateKey_X_up／_down，小箭頭共用 CandleKey_chevron.png
/// </summary>
public class CrateLineKeyHint : MonoBehaviour
{
    /// <summary>總開關。</summary>
    public static bool Enabled = true;

    public const string GlassSceneName = "dark glasses";

    /// <summary>這個鏡箱是不是由這個線條畫動畫負責（WorldKeyPromptDirector 用它避免重複提示）。</summary>
    public static bool Handles(MirrorCrate crate)
    {
        return Enabled && crate != null && crate.gameObject.scene.name == GlassSceneName;
    }

    // ── 可調參數 ──
    [Header("大小與位置")]
    [Tooltip("鍵盤 SHIFT 鍵圖在世界裡的寬度（公尺；SHIFT 鍵是寬的）")]
    public float keyWidth = 2.0f;
    [Tooltip("手把 X 圓鈕在世界裡的寬度（公尺）")]
    public float padKeyWidth = 1.15f;
    [Tooltip("三個小箭頭的寬度（公尺）")]
    public float chevronWidth = 0.5f;
    [Tooltip("提示底部離箱子頂（碰撞框最上緣）多高（公尺）")]
    public float lift = 0.9f;

    [Header("出現／消失")]
    [Tooltip("玩家左右離箱子多近才開始淡入（公尺）；到這個距離的 70% 內完全出現")]
    public float range = 7f;
    [Tooltip("玩家跟箱子上下差超過這麼多就不出現（不同層）")]
    public float verticalRange = 4.5f;
    [Tooltip("淡入淡出秒數")]
    public float fadeSeconds = 0.35f;
    [Tooltip("抓著箱子、掉落、過場時收起來的秒數（要很快）")]
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
    public float glowScale = 2.0f;

    [Header("圖層（跟燭火提示同一層級，不會被玻璃地板或怪物霧蓋住）")]
    public int sortingOrder = 9400;

    private class Item
    {
        public MirrorCrate crate;
        public Collider col;
        public LineKeyHintView view;
    }

    private readonly List<Item> _items = new List<Item>();
    private readonly HashSet<MirrorCrate> _known = new HashSet<MirrorCrate>();
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
        foreach (CrateLineKeyHint d in FindObjectsByType<CrateLineKeyHint>(FindObjectsInactive.Include))
            if (d.gameObject.scene == scene) return;
        GameObject host = new GameObject("[鏡箱按鍵線條畫]");
        SceneManager.MoveGameObjectToScene(host, scene);
        CrateLineKeyHint h = host.AddComponent<CrateLineKeyHint>();
        h.StartCoroutine(h.Run());
    }

    private IEnumerator Run()
    {
        // 有些物件是場景開始後才生出來的：先掃一次，之後再補掃（跟 CandleLineKeyHint 一樣）
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
        foreach (MirrorCrate c in FindObjectsByType<MirrorCrate>(FindObjectsInactive.Exclude))
        {
            if (c == null || !_known.Add(c)) continue;
            Item it = new Item();
            it.crate = c;
            it.col = c.GetComponent<Collider>();
            it.view = new LineKeyHintView(transform, "[鏡箱提示]", "CrateKey_Shift", "CrateKey_X", "【鏡箱按鍵提示】", Random.Range(0f, cycleSeconds));
            _items.Add(it);
            n++;
        }
        if (n > 0) Debug.Log("【鏡箱按鍵提示】★1125 玻璃館：" + n + " 個鏡箱上方裝了按 Shift 線條畫動畫");
    }

    // ═══════════════════════════ 每幀 ═══════════════════════════

    /// <summary>箱子頂上方（用目前的碰撞框，箱子被推著走時會跟著）。</summary>
    private Vector3 Anchor(Item it)
    {
        Transform t = it.crate.transform;
        if (it.col != null && it.col.enabled)
        {
            Bounds b = it.col.bounds;
            return new Vector3(b.center.x, b.max.y + lift, t.position.z - 0.5f);
        }
        return new Vector3(t.position.x, t.position.y + 1.5f + lift, t.position.z - 0.5f);
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
            if (it.crate == null)
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
        MirrorCrate c = it.crate;

        // 要不要出現：玩家近、她沒在抓箱子、箱子站得好好的（沒在掉、沒被藏起來）、沒在過場
        bool hide = !c.gameObject.activeInHierarchy || c.IsHeld || !c.CanBeHeld || cutscene;
        float want = 0f;
        if (!hide && _player != null)
        {
            Vector3 cp = c.transform.position;
            float dx = Mathf.Abs(_player.position.x - cp.x);
            float dy = Mathf.Abs(_player.position.y - cp.y);
            if (dy <= verticalRange)
                want = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(range * 0.7f, range, dx));
        }

        if (!it.view.UpdatePresence(want, hide, fadeSeconds, hideSeconds)) return;
        it.view.Draw(MakeStyle(pad), pad, Anchor(it), cam);
    }

    private LineKeyHintView.Style MakeStyle(bool pad)
    {
        LineKeyHintView.Style st = new LineKeyHintView.Style();
        st.keyWidth = pad ? padKeyWidth : keyWidth;
        st.chevronWidth = chevronWidth;
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
