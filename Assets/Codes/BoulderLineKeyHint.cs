using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ★1125 廢墟巨石（RollingRockVisual）上方的「按住 Shift 推巨石」線條畫動畫
/// （取代原本貼在背景的大圖 Ruins_PushShift：回頭看狼右邊那張、山坡腳前面那張，也取代貼紙風的 WorldKeyPrompt 巨石提示）。
///
///   ・畫風、動畫跟玻璃館的鏡箱提示（CrateLineKeyHint）一樣，畫面共用 LineKeyHintView：
///     三個小箭頭往下流進 SHIFT 鍵 → 鍵按下去（壓扁、柔光閃一下）→ 彈回，一圈約 1.2 秒。
///   ・廢墟是「白天淺色背景＋深炭色線條」（跟玻璃館的奶白線＋星空相反）：
///     不另外生圖，直接拿鏡箱／燭火的奶白素材，執行時用 SpriteRenderer.color 染成廢墟線條畫的墨色（inkColor）；
///     柔光改成淺紙色（讓深色線在亮背景上更清楚、不是暖橘色光暈）。
///   ・放在巨石頂上方（用碰撞框，不會跟著巨石滾動旋轉）；巨石被推著走時跟著它。
///   ・出現條件：玩家左右離巨石 range 公尺內（上下不超過 verticalRange）淡入，離開淡出。
///   ・很快收起來的情況：她正在推（IsBeingPushedByPlayer）、已經按住 Shift（鍵都按了，不用再提示）、
///     巨石失敗下滾（IsRollingBack／BoulderChallengeController.IsFailing）、重生黑屏、過場／演出（IsHardCutsceneLocked、isCutsceneFrozen）。
///   ・巨石還鎖著（Rigidbody 是 kinematic：還沒被光球／拉桿放下、被收回樓頂、風暴開始後固定住）＝不出現；
///     放下之後才出現。放手後（還在附近）會再出現。
///   ・鍵鼠顯示 SHIFT 鍵帽；手把顯示 X 圓鈕（依 InputPrompt.IsPad 即時切換）。
///
/// 不用手掛：場景載入時自動裝（偵測到場景裡有 RollingRockVisual／BoulderChallengeController，或場景名含 Ruin）。
/// 總開關：BoulderLineKeyHint.Enabled＝false → 這個提示不出現，LineHintDirector 會改放回原本兩張大圖、
///        WorldKeyPromptDirector 也會恢復貼紙巨石提示。
/// 素材：Assets/Resources/Hints/CrateKey_Shift_up／_down、CrateKey_X_up／_down，小箭頭共用 CandleKey_chevron.png（都是奶白色，這裡染色）
/// </summary>
public class BoulderLineKeyHint : MonoBehaviour
{
    /// <summary>總開關。</summary>
    public static bool Enabled = true;

    /// <summary>這顆巨石是不是由這個線條畫動畫負責（WorldKeyPromptDirector 用它避免重複提示）。</summary>
    public static bool Handles(RollingRockVisual rock)
    {
        return Enabled && rock != null;
    }

    // 奶白素材的平均色（CrateKey／CandleKey 系列 PNG 實測 232,224,210），拿來換算染色
    private static readonly Color CreamArt = new Color(232f / 255f, 224f / 255f, 210f / 255f, 1f);

    // ── 可調參數 ──
    [Header("大小與位置")]
    [Tooltip("鍵盤 SHIFT 鍵圖在世界裡的寬度（公尺；巨石很大，所以比鏡箱的 2.0 稍大）")]
    public float keyWidth = 2.3f;
    [Tooltip("手把 X 圓鈕在世界裡的寬度（公尺）")]
    public float padKeyWidth = 1.3f;
    [Tooltip("三個小箭頭的寬度（公尺）")]
    public float chevronWidth = 0.55f;
    [Tooltip("提示底部離巨石頂（碰撞框最上緣）多高（公尺）")]
    public float lift = 1.0f;

    [Header("出現／消失")]
    [Tooltip("玩家左右離巨石多近才開始淡入（公尺）；到這個距離的 70% 內完全出現")]
    public float range = 9f;
    [Tooltip("玩家跟巨石中心上下差超過這麼多就不出現（巨石在坡上、她在坡下時要放寬一點）")]
    public float verticalRange = 6f;
    [Tooltip("淡入淡出秒數")]
    public float fadeSeconds = 0.35f;
    [Tooltip("開始推、按住 Shift、失敗下滾、重生、過場時收起來的秒數（要很快）")]
    public float hideSeconds = 0.12f;
    [Tooltip("打勾＝她已經按住 Shift（或手把 X）就先收起來（鍵都按了不用再提示），放開又在附近會再出現")]
    public bool hideWhileKeyHeld = true;

    [Header("動畫（一圈秒數，跟鏡箱提示一樣）")]
    public float cycleSeconds = 1.2f;
    [Tooltip("按下去那一格出現的時間點（一圈中的第幾秒）")]
    public float pressAt = 0.78f;
    [Tooltip("按鍵維持按下去的秒數")]
    public float pressSeconds = 0.18f;
    [Tooltip("按下去時的壓扁（x 變寬、y 變矮）")]
    public Vector2 pressSquash = new Vector2(1.04f, 0.94f);
    [Tooltip("上下漂浮幅度（公尺）")]
    public float bobAmplitude = 0.05f;

    [Header("線色（廢墟：淺色白天背景＋深炭色線）")]
    [Tooltip("染成的線色，跟廢墟線條畫（Ruins_PushShift）的炭黑線差不多")]
    public Color inkColor = new Color(40f / 255f, 34f / 255f, 50f / 255f, 1f);

    [Header("柔光（淺紙色：讓深色線在亮背景上更清楚；調成 0 就沒有光暈）")]
    public Color glowColor = new Color(1f, 0.97f, 0.9f, 1f);
    [Range(0f, 1f)] public float glowIdle = 0.38f;
    [Range(0f, 1f)] public float glowPress = 0.62f;
    [Tooltip("柔光比按鍵大幾倍")]
    public float glowScale = 2.0f;

    [Header("圖層（跟鏡箱／燭火提示同一層級）")]
    public int sortingOrder = 9400;

    private class Item
    {
        public RollingRockVisual rock;
        public Rigidbody rb;
        public Collider col;
        public LineKeyHintView view;
    }

    private readonly List<Item> _items = new List<Item>();
    private readonly HashSet<RollingRockVisual> _known = new HashSet<RollingRockVisual>();
    private PlayerMovement _pm;
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

    /// <summary>這個場景有巨石嗎？（巨石有可能是執行時才掛上 RollingRockVisual，所以也看挑戰控制器、場景名。）</summary>
    private static bool HasBoulder(Scene scene)
    {
        if (scene.name.IndexOf("ruin", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
        foreach (RollingRockVisual r in FindObjectsByType<RollingRockVisual>(FindObjectsInactive.Include))
            if (r != null && r.gameObject.scene == scene) return true;
        foreach (BoulderChallengeController c in FindObjectsByType<BoulderChallengeController>(FindObjectsInactive.Include))
            if (c != null && c.gameObject.scene == scene) return true;
        return false;
    }

    private static void Install(Scene scene)
    {
        if (!Enabled || !scene.IsValid() || !HasBoulder(scene)) return;
        foreach (BoulderLineKeyHint d in FindObjectsByType<BoulderLineKeyHint>(FindObjectsInactive.Include))
            if (d.gameObject.scene == scene) return;
        GameObject host = new GameObject("[巨石按鍵線條畫]");
        SceneManager.MoveGameObjectToScene(host, scene);
        BoulderLineKeyHint h = host.AddComponent<BoulderLineKeyHint>();
        h.StartCoroutine(h.Run());
    }

    private IEnumerator Run()
    {
        // 巨石有可能是場景開始後才掛上 RollingRockVisual：先掃一次，之後再補掃（跟鏡箱提示一樣）
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
        foreach (RollingRockVisual r in FindObjectsByType<RollingRockVisual>(FindObjectsInactive.Exclude))
        {
            if (r == null || !_known.Add(r)) continue;
            Item it = new Item();
            it.rock = r;
            it.rb = r.GetComponent<Rigidbody>();
            it.col = r.GetComponent<Collider>();
            it.view = new LineKeyHintView(transform, "[巨石提示]", "CrateKey_Shift", "CrateKey_X", "【巨石按鍵提示】", Random.Range(0f, cycleSeconds));
            _items.Add(it);
            n++;
        }
        if (n > 0) Debug.Log("【巨石按鍵提示】★1125 廢墟：" + n + " 顆巨石上方裝了按 Shift 線條畫動畫");
    }

    // ═══════════════════════════ 每幀 ═══════════════════════════

    /// <summary>巨石頂上方（用碰撞框：巨石的視覺會轉，碰撞框不轉，所以提示不會跟著滾）。</summary>
    private Vector3 Anchor(Item it)
    {
        Transform t = it.rock.transform;
        if (it.col != null && it.col.enabled)
        {
            Bounds b = it.col.bounds;
            return new Vector3(b.center.x, b.max.y + lift, t.position.z - 0.5f);
        }
        return new Vector3(t.position.x, t.position.y + 2.7f + lift, t.position.z - 0.5f);
    }

    private void LateUpdate()
    {
        if (!Enabled)
        {
            foreach (Item it in _items) it.view.SetAll();
            return;
        }

        if (_pm == null && Time.unscaledTime >= _nextLookup)
        {
            _nextLookup = Time.unscaledTime + 0.5f;
            _pm = FindAnyObjectByType<PlayerMovement>();
        }

        bool pad = InputPrompt.IsPad;
        bool cutscene = PlayerMovement.IsHardCutsceneLocked || (_pm != null && _pm.isCutsceneFrozen);
        Camera cam = Camera.main;

        for (int i = _items.Count - 1; i >= 0; i--)
        {
            Item it = _items[i];
            if (it.rock == null)
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
        RollingRockVisual r = it.rock;

        // 要不要出現：巨石已經放下（不是 kinematic）、沒在失敗下滾、她沒在推、沒按著 Shift、沒在重生／過場
        bool locked = it.rb == null || it.rb.isKinematic;
        bool keyHeld = hideWhileKeyHeld && _pm != null && _pm.BoulderPushKeyHeld;
        bool shiftNotNeeded = _pm != null && !_pm.requireShiftToPushBoulder;   // 不用按 Shift 也能推＝不用教
        bool hide = !r.gameObject.activeInHierarchy || !r.enabled || locked
                    || r.IsBeingPushedByPlayer || r.IsRollingBack || BoulderChallengeController.IsFailing
                    || keyHeld || shiftNotNeeded || cutscene;
        float want = 0f;
        if (!hide && _pm != null)
        {
            Vector3 rp = r.transform.position;
            Vector3 pp = _pm.transform.position;
            float dx = Mathf.Abs(pp.x - rp.x);
            float dy = Mathf.Abs(pp.y - rp.y);
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
        // 奶白素材 × 染色 ＝ 墨色（素材平均色 232,224,210 → inkColor）
        st.useTint = true;
        st.tint = new Color(
            Mathf.Clamp01(inkColor.r / CreamArt.r),
            Mathf.Clamp01(inkColor.g / CreamArt.g),
            Mathf.Clamp01(inkColor.b / CreamArt.b),
            1f);
        return st;
    }
}
