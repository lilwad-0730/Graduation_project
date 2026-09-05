using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 起風前兆（遠處沙塵線）。
///
/// 風平的最後 WindGustSystem.telegraphSeconds 秒，上風處（畫面右側）會出現幾道
/// 逼近的沙塵線，正好在起風那一瞬間掃到玩家身上。玩家因此看得到「風要來了」，
/// 可以先找掩體、或先按住 ⬇/S 硬撐——被動石化才不會變成「不知道為什麼突然不能動」。
///
/// ★ 不影響推力時序：推力仍然與風痕同時到（WindGustSystem.windupSeconds 預設 0）。
///   這支只在「風平的最後一秒」畫東西，起風後就自己隱藏。
///
/// ★ 不動場景檔：進 desert（有 WindGustSystem 的場景）時自動生成，離開就消失，
///   所以不會跟任何人的場景存檔衝突。要整包關掉：WindTelegraphFX.Enabled = false，
///   或直接刪掉這支腳本。
/// </summary>
[DisallowMultipleComponent]
public class WindTelegraphFX : MonoBehaviour
{
    /// <summary>整組前兆的總開關（想關掉又不想刪檔時用）。</summary>
    public static bool Enabled = true;

    [Header("外觀")]
    [Tooltip("沙塵線的顏色（荒原色票 #D8B49A）。alpha 就是最濃的時候的濃度")]
    public Color lineColor = new Color(0.847f, 0.706f, 0.604f, 0.55f);
    [Tooltip("幾道線")]
    public int streakCount = 3;
    [Tooltip("排序圖層順序。看不到就往上加；壓到主角就往下減")]
    public int sortingOrder = -5;
    [Tooltip("留空＝用程式生成的柔邊沙塵線；美術要換圖直接拖進來")]
    public Sprite customSprite;

    [Header("位置與動態（畫面比例）")]
    [Tooltip("起點：畫面右緣外面。1 = 右緣")]
    public float startViewportX = 1.25f;
    [Tooltip("終點：起風瞬間掃到哪裡。0.5 = 畫面正中")]
    public float endViewportX = 0.30f;
    [Tooltip("高度：0 = 畫面底，1 = 畫面頂")]
    public float viewportY = 0.42f;
    [Tooltip("幾道線之間的高度差")]
    public float ySpread = 0.09f;
    [Tooltip("單道線的長度（畫面寬度的幾倍）")]
    public float lengthViewport = 0.55f;
    [Tooltip("單道線的厚度（畫面高度的幾倍）")]
    public float thicknessViewport = 0.012f;

    private WindGustSystem _wind;
    private Camera _cam;
    private SpriteRenderer[] _streaks;
    private Sprite _generated;
    private Texture2D _generatedTex;

    // ── 自動安裝：進到有 WindGustSystem 的場景就長出來，不用擺物件、不動場景檔 ──
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        TryInstall();
    }

    private static void OnSceneLoaded(Scene s, LoadSceneMode m) { TryInstall(); }

    private static void TryInstall()
    {
        if (!Enabled) return;
        if (WindGustSystem.Instance == null) return;                 // 不是荒原就不裝
        if (FindFirstObjectByType<WindTelegraphFX>() != null) return; // 已經有了
        var go = new GameObject("WindTelegraphFX (自動生成)");
        go.AddComponent<WindTelegraphFX>();
    }

    private void Awake()
    {
        _cam = Camera.main;
        BuildStreaks();
    }

    private void BuildStreaks()
    {
        streakCount = Mathf.Clamp(streakCount, 1, 8);
        _streaks = new SpriteRenderer[streakCount];
        Sprite spr = customSprite != null ? customSprite : GetGeneratedSprite();

        for (int i = 0; i < streakCount; i++)
        {
            var go = new GameObject("Streak_" + i);
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = spr;
            sr.color = lineColor;
            sr.sortingOrder = sortingOrder;
            sr.enabled = false;
            _streaks[i] = sr;
        }
    }

    /// <summary>程式生成一張柔邊的沙塵線：中間濃、上下與兩端都淡出。</summary>
    private Sprite GetGeneratedSprite()
    {
        if (_generated != null) return _generated;
        const int W = 256, H = 32;
        _generatedTex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        _generatedTex.hideFlags = HideFlags.HideAndDontSave;
        _generatedTex.wrapMode = TextureWrapMode.Clamp;
        var px = new Color[W * H];
        for (int y = 0; y < H; y++)
        {
            float fy = Mathf.Abs((y + 0.5f) / H - 0.5f) * 2f;   // 0 中間 → 1 邊緣
            float ay = Mathf.Clamp01(1f - fy * fy);              // 上下柔邊
            for (int x = 0; x < W; x++)
            {
                float fx = (x + 0.5f) / W;
                // 兩端淡出：頭端更尖（風是從右邊來，所以右邊比較實）
                float ax = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(fx / 0.35f))
                         * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - fx) / 0.12f));
                // 一點點顆粒感，免得像一條純色的棒子
                float grain = 0.75f + 0.25f * Mathf.PerlinNoise(fx * 24f, y * 0.35f);
                px[y * W + x] = new Color(1f, 1f, 1f, ay * ax * grain);
            }
        }
        _generatedTex.SetPixels(px);
        _generatedTex.Apply(false, false);
        _generated = Sprite.Create(_generatedTex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f);
        _generated.name = "WindTelegraph_Streak";
        _generated.hideFlags = HideFlags.HideAndDontSave;
        return _generated;
    }

    private void LateUpdate()
    {
        if (_wind == null) _wind = WindGustSystem.Instance;
        if (_cam == null || !_cam.isActiveAndEnabled) _cam = Camera.main;
        if (_wind == null || _cam == null || _streaks == null) { Hide(); return; }

        if (!Enabled || !_wind.IsTelegraphing) { Hide(); return; }

        float p = _wind.TelegraphProgress01;                 // 0 遠 → 1 起風瞬間
        float eased = p * p * (3f - 2f * p);                 // 先慢後快，像被吹過來的
        float alphaK = Mathf.Clamp01(p / 0.25f);             // 前 1/4 淡入，之後維持

        // 用玩家所在的 z 平面換算，才不會跑到背景後面或衝到鏡頭前
        float depth = Mathf.Abs(_cam.transform.position.z);
        Vector3 vLeft  = _cam.ViewportToWorldPoint(new Vector3(0f, 0.5f, depth));
        Vector3 vRight = _cam.ViewportToWorldPoint(new Vector3(1f, 0.5f, depth));
        Vector3 vBot   = _cam.ViewportToWorldPoint(new Vector3(0.5f, 0f, depth));
        Vector3 vTop   = _cam.ViewportToWorldPoint(new Vector3(0.5f, 1f, depth));
        float wWorld = Mathf.Abs(vRight.x - vLeft.x);
        float hWorld = Mathf.Abs(vTop.y - vBot.y);

        float vx = Mathf.Lerp(startViewportX, endViewportX, eased);

        for (int i = 0; i < _streaks.Length; i++)
        {
            var sr = _streaks[i];
            if (sr == null) continue;

            // 每道線錯開一點高度、長度與速度，才不會像三條平行的棒子
            float off = (i - (_streaks.Length - 1) * 0.5f);
            float lead = 1f + off * 0.10f;                   // 有的先到有的後到
            float x = Mathf.Lerp(startViewportX, endViewportX, Mathf.Clamp01(eased * lead));
            float y = viewportY + off * ySpread;

            Vector3 pos = _cam.ViewportToWorldPoint(new Vector3(x, y, depth));
            pos.z = 0f;
            sr.transform.position = pos;

            float len = wWorld * lengthViewport * (1f + off * 0.12f);
            float thick = hWorld * thicknessViewport;
            var b = sr.sprite.bounds.size;
            sr.transform.localScale = new Vector3(len / Mathf.Max(0.0001f, b.x),
                                                  thick / Mathf.Max(0.0001f, b.y), 1f);

            var c = lineColor;
            c.a = lineColor.a * alphaK * (1f - Mathf.Abs(off) * 0.18f);
            sr.color = c;
            sr.sortingOrder = sortingOrder;
            sr.enabled = true;
        }
    }

    private void Hide()
    {
        if (_streaks == null) return;
        for (int i = 0; i < _streaks.Length; i++)
            if (_streaks[i] != null) _streaks[i].enabled = false;
    }

    private void OnDestroy()
    {
        if (_generated != null) Destroy(_generated);
        if (_generatedTex != null) Destroy(_generatedTex);
    }
}
