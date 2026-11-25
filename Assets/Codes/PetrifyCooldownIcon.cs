using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ★1125 荒原「硬撐冷卻」頭上圖示（對應 PlayerPetrification 的 braceCooldownEnabled）。
///
/// 一顆「空心巨石輪廓」線條畫，裡面是一個 S 鍵帽在按下去的動畫，固定浮在她頭上。
///   ・什麼時候出現：碰到光球、拿到「按住 S 硬撐」（DesertOrbDirector.BraceAllowed）那一刻淡入，之後一直在，直到離開這一關（場景卸載）。
///     文字卡／黑幕／重生／過場（IsHardCutsceneLocked、isCutsceneFrozen）、暫停時淡出，結束後回來。
///   ・就緒（能量滿）：整張金黃色，S 鍵循環做「按下去」動畫，後面一圈暖色柔光。
///   ・硬撐中：維持金黃色，S 鍵一直按著（按下去那張）。
///   ・冷卻中（放開 S 之後）：整張變灰、S 鍵停在彈起那張；她邊走路能量邊充，金黃色從圖的「底部往上」一格一格填滿。
///   ・充滿：圖示小彈一下＋柔光閃一下，恢復就緒動畫。
///   ・冷卻中還按 S：圖示左右抖一下＋輕輕一聲悶響（PlayerPetrification 呼叫 NotifyDenied）。
///
/// 實作備註
///   ・金黃色填充「不用 SpriteMask」：desert.unity 裡有一個沒設範圍的全域遮罩（Oasis_WaterMask，綠洲水面），
///     VisibleInsideMask 的 Sprite 會被它影響、走到綠洲金色會整張漏出來。改成金色那層直接用「只取底部一段」的裁切 Sprite
///     （Sprite.Create 取貼圖下方 N%，同樣的像素密度、底部置中對齊，快取 48 格，不產生垃圾），結果一樣但完全不會被別的遮罩影響。
///   ・每一層都有一份暗色偏移陰影：亮沙地上金黃色線條很淡，靠這個才清楚；黃昏暗背景上陰影幾乎看不到，不礙事。
///   ・不用手掛：場景載入時自動裝（只裝在荒原）。總開關 PetrifyCooldownIcon.Enabled。
/// 素材：Assets/Resources/Hints/PetrifyBoulder_outline.png（巨石輪廓）、PetrifyKey_S_up／down.png（S 鍵，白色線條，執行時染色）
/// </summary>
public class PetrifyCooldownIcon : MonoBehaviour
{
    /// <summary>總開關。</summary>
    public static bool Enabled = true;

    private static PetrifyCooldownIcon _instance;

    /// <summary>冷卻中按 S 被拒絕：圖示抖一下＋悶響（PlayerPetrification 呼叫；沒有圖示就什麼都不做）。</summary>
    public static void NotifyDenied()
    {
        if (_instance != null) _instance.Deny();
    }

    // ── 可調參數 ──
    [Header("大小與位置")]
    [Tooltip("圖示寬度（公尺）。★1125 加大到 3.4：S 鍵帽（3.4×0.4≈1.36 公尺）跟荒原「按住 S」線條畫（Desert_Brace）裡的 S 鍵一樣大")]
    public float iconWidth = 2.38f;   // ★1125 整體縮成 0.7 倍（原 3.4）；S 鍵帽約 2.38×0.52≈1.24 公尺
    [Tooltip("圖示底部離頭頂多高（公尺）")]
    public float headLift = 0.35f;
    [Tooltip("頭頂高度額外校正（公尺，正＝往上）。★1125 預設 0.9：圖示變大變圓後抬高，頭上留一段空隙。頭頂高度預設用她站著時的碰撞框／模型較矮的那個，量得不準就調這裡")]
    public float headExtraLift = 0.9f;
    [Tooltip("S 鍵帽寬度，佔圖示寬度的比例")]
    public float keyWidthRatio = 0.52f;   // ★1125 S 鍵帽貼近石頭外框（外框內緣半徑約 0.43；裂痕尖端已削到半徑 0.375 以外，鍵帽四角碰不到）
    [Tooltip("S 鍵帽中心在巨石裡的位置（0～1，x 由左、y 由下）")]
    public Vector2 keyCenter = new Vector2(0.50f, 0.50f);
    [Tooltip("圖示上下漂浮幅度（公尺）")]
    public float bobAmplitude = 0.04f;

    [Header("顏色")]
    [Tooltip("★1125 就緒／充能的顏色：橘紅，搭荒原的紅土色調")]
    public Color readyColor = new Color(1f, 0.5f, 0.2f, 1f);
    public Color cooldownColor = new Color(0.55f, 0.55f, 0.55f, 1f);
    [Tooltip("暗色陰影（亮沙地上才看得清楚）")]
    public Color shadowColor = new Color(0.16f, 0.09f, 0.04f, 0.55f);
    [Tooltip("陰影往右下偏移，佔圖示寬度的比例")]
    public Vector2 shadowOffsetRatio = new Vector2(0.018f, -0.024f);
    public Color glowColor = new Color(1f, 0.56f, 0.28f, 1f);
    [Range(0f, 1f)] public float glowReady = 0.55f;
    [Tooltip("柔光比圖示大幾倍")]
    public float glowScale = 2.0f;

    [Header("動畫")]
    [Tooltip("就緒時 S 鍵按下動畫的一圈秒數")]
    public float cycleSeconds = 1.2f;
    public float pressAt = 0.7f;
    public float pressSeconds = 0.2f;
    public Vector2 pressSquash = new Vector2(1.05f, 0.93f);
    [Tooltip("淡入秒數（拿到光球那一刻）")]
    public float fadeInSeconds = 0.6f;
    [Tooltip("文字卡／重生／過場時淡出秒數")]
    public float fadeOutSeconds = 0.15f;
    [Tooltip("金色填充往上／往下跟上能量的速度（每秒佔全高的比例）")]
    public float fillRiseSpeed = 8f;
    public float fillDrainSpeed = 4f;
    [Tooltip("冷卻中按 S 的抖動秒數與幅度（公尺）")]
    public float shakeSeconds = 0.32f;
    public float shakeAmplitude = 0.07f;
    [Range(0f, 1f)] public float deniedClickVolume = 0.22f;

    [Header("圖層（在玩家與一般世界物件之上，在黑幕／文字卡／轉場 canvas 之下）")]
    public int sortingOrder = 9450;

    // ── 內部 ──
    private const int FillSteps = 48;

    private Transform _root;
    private SpriteRenderer _glow;
    private SpriteRenderer _shBoulder, _shKey, _greyBoulder, _greyKey, _goldBoulder, _goldKey;
    private Texture2D _texBoulder, _texUp, _texDown;
    private float _boulderH, _keyH, _keyBottomY;
    private bool _built, _loadFailed;

    private PlayerMovement _pm;
    private Collider _body;
    private Renderer[] _modelRends;
    private float _nextLookup;
    private float _headTopOffset = float.NaN;   // 頭頂離腳下 transform 的高度（站著時量一次）

    private float _presence;        // 0～1 出現度
    private float _fill;            // 目前顯示的金色高度 0～1
    private bool _wasReady = true;
    private float _flash;           // 充滿時的閃光 1→0
    private float _shakeT = -99f;
    private float _animT0;          // 就緒動畫的起點（按住結束後從頭播）
    private bool _initialised;

    private static readonly Dictionary<string, Sprite> SpriteCache = new Dictionary<string, Sprite>();
    private static Sprite _glowSprite;
    private static AudioClip _clickClip;

    // ═══════════════════════════ 啟動 ═══════════════════════════

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _instance = null;
        SpriteCache.Clear();
        _glowSprite = null;
        _clickClip = null;
    }

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

    private static bool IsDesert(Scene scene)
    {
        string n = scene.name.ToLower();
        return n.Contains("desert") || n.Contains("荒漠") || n.Contains("荒原");   // 同 PlayerPetrification.allowedSceneKeywords 的預設
    }

    private static void Install(Scene scene)
    {
        if (!Enabled || !scene.IsValid() || !IsDesert(scene)) return;
        foreach (PetrifyCooldownIcon d in FindObjectsByType<PetrifyCooldownIcon>(FindObjectsInactive.Include))
            if (d.gameObject.scene == scene) return;
        GameObject host = new GameObject("[硬撐冷卻頭上圖示]");
        SceneManager.MoveGameObjectToScene(host, scene);
        host.AddComponent<PetrifyCooldownIcon>();
    }

    private void Awake() { _instance = this; }
    private void OnDestroy() { if (_instance == this) _instance = null; }

    // ═══════════════════════════ 每幀 ═══════════════════════════

    private void LateUpdate()
    {
        if (!Enabled || _loadFailed) { Hide(); return; }

        if (_pm == null && Time.unscaledTime >= _nextLookup)
        {
            _nextLookup = Time.unscaledTime + 0.5f;
            _pm = FindAnyObjectByType<PlayerMovement>();
            _body = null; _modelRends = null; _headTopOffset = float.NaN;
        }

        // 要不要出現：拿到硬撐、冷卻機制開著、沒被演出／暫停蓋住
        bool featureOn = _pm != null && DesertOrbDirector.BraceAllowed && PlayerPetrification.CurrentCooldownActive;
        bool covered = _pm == null || PlayerMovement.IsHardCutsceneLocked || _pm.isCutsceneFrozen || Time.timeScale < 0.01f || _pm.isUnderwater;
        float want = featureOn && !covered ? 1f : 0f;
        float sec = want > _presence ? fadeInSeconds : fadeOutSeconds;
        _presence = Mathf.MoveTowards(_presence, want, Time.unscaledDeltaTime / Mathf.Max(0.02f, sec));
        if (_presence <= 0.002f) { Hide(); _initialised = false; return; }

        if (!_built) Build();
        if (!_built) return;

        // ── 狀態 ──
        bool bracing = PlayerPetrification.CurrentIsBracing;
        bool ready = PlayerPetrification.CurrentBraceReady;
        float charge = PlayerPetrification.CurrentBraceCharge01;
        float dt = Time.unscaledDeltaTime;

        if (!_initialised)
        {
            _initialised = true;
            _fill = (ready || bracing) ? 1f : charge;
            _wasReady = ready;
            _animT0 = Time.unscaledTime;
        }

        float targetFill = (ready || bracing) ? 1f : charge;
        _fill = Mathf.MoveTowards(_fill, targetFill, (targetFill > _fill ? fillRiseSpeed : fillDrainSpeed) * dt);

        if (ready && !_wasReady) { _flash = 1f; _animT0 = Time.unscaledTime; }
        if (!ready && _wasReady) _animT0 = Time.unscaledTime;
        _wasReady = ready;
        _flash = Mathf.MoveTowards(_flash, 0f, dt / 0.45f);

        // ── 位置：頭頂上方，面向鏡頭 ──
        float now = Time.unscaledTime;
        float bob = Mathf.Sin(now * 2.1f) * bobAmplitude;
        float shakeK = 0f;
        if (now - _shakeT < shakeSeconds)
        {
            float u = (now - _shakeT) / Mathf.Max(0.05f, shakeSeconds);
            shakeK = Mathf.Sin(u * Mathf.PI * 7f) * (1f - u) * shakeAmplitude;
        }
        Vector3 head = HeadPoint();
        Camera cam = Camera.main;
        Quaternion rot = cam != null ? cam.transform.rotation : Quaternion.identity;
        Vector3 right = rot * Vector3.right;
        _root.position = head + new Vector3(0f, bob, 0f) + right * shakeK;
        _root.rotation = rot;
        float pop = Mathf.SmoothStep(0f, 1f, _presence);
        float s = Mathf.Lerp(0.75f, 1f, pop) * (1f + 0.14f * Mathf.Sin(Mathf.Clamp01(1f - _flash) * Mathf.PI) * (_flash > 0f ? 1f : 0f));
        _root.localScale = Vector3.one * s;

        // ── S 鍵：就緒＝循環按、硬撐中＝按著、冷卻＝停在彈起 ──
        bool pressed;
        if (bracing) pressed = true;
        else if (ready)
        {
            float cyc = Mathf.Max(0.3f, cycleSeconds);
            float t = (now - _animT0) % cyc;
            float pu = (t - pressAt) / Mathf.Max(0.05f, pressSeconds);
            pressed = pu >= 0f && pu < 1f;
        }
        else pressed = false;
        Texture2D keyTex = pressed ? _texDown : _texUp;
        Vector3 keyScale = pressed ? new Vector3(pressSquash.x, pressSquash.y, 1f) : Vector3.one;

        // ── 畫 ──
        float a = _presence;
        float revealH = _fill * _boulderH;                       // 金色填到離圖底多高（公尺）
        float boulderFrac = _fill;
        float keyFrac = Mathf.Clamp01((revealH - _keyBottomY) / Mathf.Max(0.01f, _keyH));

        Sprite fullBoulder = GetSprite(_texBoulder, 1f, iconWidth);
        float kw = iconWidth * keyWidthRatio;
        Sprite fullKey = GetSprite(keyTex, 1f, kw);

        // 陰影：整張、暗色
        Vector3 off = new Vector3(shadowOffsetRatio.x * iconWidth, shadowOffsetRatio.y * iconWidth, 0.003f);
        SetR(_shBoulder, fullBoulder, Vector3.zero + off, Vector3.one, WithA(shadowColor, shadowColor.a * a));
        SetR(_shKey, fullKey, KeyPos() + off, keyScale, WithA(shadowColor, shadowColor.a * a));

        // 灰色（底層）：整張
        SetR(_greyBoulder, fullBoulder, Vector3.zero, Vector3.one, WithA(cooldownColor, a));
        SetR(_greyKey, fullKey, KeyPos(), keyScale, WithA(cooldownColor, a));

        // 金色（上層）：只畫底部 fill 那一段
        SetCropped(_goldBoulder, _texBoulder, boulderFrac, iconWidth, Vector3.zero, Vector3.one, WithA(readyColor, a));
        SetCropped(_goldKey, keyTex, keyFrac, kw, KeyPos(), keyScale, WithA(readyColor, a));

        // 柔光：就緒（含硬撐中）才有；充滿那一下閃亮
        float ga = a * (((ready || bracing) ? glowReady : 0f) * (0.85f + 0.15f * Mathf.Sin(now * 2.6f)) + _flash * 0.65f);
        _glow.color = WithA(glowColor, Mathf.Clamp01(ga));
        _glow.enabled = ga > 0.003f;
        _glow.transform.localScale = Vector3.one * (iconWidth * glowScale * (1f + _flash * 0.25f));
    }

    private Vector3 KeyPos()
    {
        return new Vector3((keyCenter.x - 0.5f) * iconWidth, _keyBottomY, -0.002f);
    }

    private static Color WithA(Color c, float a) { return new Color(c.r, c.g, c.b, Mathf.Clamp01(a)); }

    private static void SetR(SpriteRenderer sr, Sprite sp, Vector3 localPos, Vector3 scale, Color c)
    {
        if (sr == null) return;
        sr.sprite = sp;
        sr.transform.localPosition = localPos;
        sr.transform.localScale = scale;
        sr.color = c;
        sr.enabled = sp != null && c.a > 0.002f;
    }

    private void SetCropped(SpriteRenderer sr, Texture2D tex, float frac, float widthMeters, Vector3 localPos, Vector3 scale, Color c)
    {
        if (frac <= 0.004f) { if (sr != null) sr.enabled = false; return; }
        SetR(sr, GetSprite(tex, frac, widthMeters), localPos, scale, c);
    }

    private void Hide()
    {
        if (_root == null) return;
        foreach (SpriteRenderer sr in _root.GetComponentsInChildren<SpriteRenderer>(true)) sr.enabled = false;
    }

    // ═══════════════════════════ 頭頂位置 ═══════════════════════════

    private Vector3 HeadPoint()
    {
        Vector3 p = _pm.transform.position;
        if (_body == null)
        {
            foreach (Collider c in _pm.GetComponentsInChildren<Collider>())
                if (c != null && !c.isTrigger) { _body = c; break; }
        }
        float cx = _body != null ? _body.bounds.center.x : p.x;

        // 頭頂高度：站著（落地、沒石化、沒演出）時量一次，之後固定，跳起來／蹲下／被風吹動畫都不會讓圖示上下亂跳
        if (float.IsNaN(_headTopOffset) && _pm.isGrounded && !_pm.isCutsceneFrozen && !PlayerPetrification.CurrentIsBracing)
        {
            float top = float.NaN;
            if (_body != null && _body.bounds.size.y > 0.3f) top = _body.bounds.max.y - p.y;
            if (_modelRends == null)
            {
                List<Renderer> list = new List<Renderer>();
                foreach (Renderer r in _pm.GetComponentsInChildren<Renderer>())
                    if (r is SkinnedMeshRenderer || r is MeshRenderer) list.Add(r);
                _modelRends = list.ToArray();
            }
            if (_modelRends.Length > 0)
            {
                float mt = float.NegativeInfinity;
                foreach (Renderer r in _modelRends) if (r != null) mt = Mathf.Max(mt, r.bounds.max.y);
                if (!float.IsNegativeInfinity(mt))
                {
                    float rt = mt - p.y;
                    top = float.IsNaN(top) ? rt : Mathf.Min(top, rt);   // 兩個量法取較矮的（模型包圍盒常常偏大）
                }
            }
            if (!float.IsNaN(top) && top > 0.3f) _headTopOffset = top;
        }
        float h = float.IsNaN(_headTopOffset) ? 2.6f : _headTopOffset;
        return new Vector3(cx, p.y + h + headLift + headExtraLift, p.z - 0.5f);
    }

    // ═══════════════════════════ 建立 ═══════════════════════════

    private void Build()
    {
        _texBoulder = Resources.Load<Texture2D>("Hints/PetrifyBoulder_outline");
        _texUp = Resources.Load<Texture2D>("Hints/PetrifyKey_S_up");
        _texDown = Resources.Load<Texture2D>("Hints/PetrifyKey_S_down");
        if (_texBoulder == null || _texUp == null || _texDown == null)
        {
            _loadFailed = true;
            Debug.LogWarning("【硬撐冷卻圖示】找不到 Assets/Resources/Hints/PetrifyBoulder_outline／PetrifyKey_S_up／PetrifyKey_S_down.png，圖示不顯示");
            return;
        }
        _boulderH = iconWidth * _texBoulder.height / _texBoulder.width;
        _keyH = iconWidth * keyWidthRatio * _texUp.height / _texUp.width;
        _keyBottomY = keyCenter.y * _boulderH - _keyH * 0.5f;

        GameObject root = new GameObject("[PetrifyIcon]");
        root.transform.SetParent(transform, false);
        _root = root.transform;

        _glow = MakeR("Glow", sortingOrder - 1);
        _glow.sprite = GlowSprite();
        _glow.transform.localPosition = new Vector3(0f, _boulderH * 0.5f, 0.01f);
        _shBoulder = MakeR("ShadowBoulder", sortingOrder);
        _shKey = MakeR("ShadowKey", sortingOrder + 1);
        _greyBoulder = MakeR("GreyBoulder", sortingOrder + 2);
        _greyKey = MakeR("GreyKey", sortingOrder + 3);
        _goldBoulder = MakeR("GoldBoulder", sortingOrder + 4);
        _goldKey = MakeR("GoldKey", sortingOrder + 5);
        _built = true;
        Debug.Log("【硬撐冷卻圖示】★1125 已安裝：拿到光球後頭上出現巨石＋S 鍵圖示；冷卻中灰色、走路充能時金色由下往上填滿");
    }

    private SpriteRenderer MakeR(string name, int order)
    {
        GameObject g = new GameObject(name);
        g.transform.SetParent(_root, false);
        SpriteRenderer sr = g.AddComponent<SpriteRenderer>();
        sr.sortingOrder = order;
        sr.enabled = false;
        return sr;
    }

    // ═══════════════════════════ 素材 ═══════════════════════════

    /// <summary>底部置中的 Sprite，取貼圖最下面 frac（0～1）那一段；同樣的像素密度，所以不同 frac 的底部永遠對齊。</summary>
    private static Sprite GetSprite(Texture2D t, float frac, float widthMeters)
    {
        if (t == null) return null;
        int step = Mathf.Clamp(Mathf.CeilToInt(Mathf.Clamp01(frac) * FillSteps - 0.0001f), 1, FillSteps);
        string key = t.name + "|" + step + "|" + Mathf.RoundToInt(widthMeters * 100f);
        Sprite sp;
        if (SpriteCache.TryGetValue(key, out sp) && sp != null) return sp;
        int h = step >= FillSteps ? t.height : Mathf.Max(1, Mathf.RoundToInt(t.height * (step / (float)FillSteps)));
        float ppu = t.width / Mathf.Max(0.05f, widthMeters);
        sp = Sprite.Create(t, new Rect(0, 0, t.width, h), new Vector2(0.5f, 0f), ppu);
        SpriteCache[key] = sp;
        return sp;
    }

    private static Sprite GlowSprite()
    {
        if (_glowSprite != null) return _glowSprite;
        const int N = 128;
        Texture2D tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                a = a * a * (3f - 2f * a);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();
        _glowSprite = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), N);
        return _glowSprite;
    }

    // ═══════════════════════════ 被拒絕 ═══════════════════════════

    private void Deny()
    {
        if (_presence < 0.3f) return;   // 圖示沒出現（演出中）就不抖、不響
        _shakeT = Time.unscaledTime;
        if (_clickClip == null) _clickClip = MakeClickClip();
        if (_clickClip == null) return;
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(_clickClip, deniedClickVolume);
        else AudioSource.PlayClipAtPoint(_clickClip, Camera.main != null ? Camera.main.transform.position : Vector3.zero, AudioManager.ScaleSfx(deniedClickVolume));
    }

    /// <summary>程式產生的悶響：約 70 毫秒、低頻、快速衰減（不用音檔）。</summary>
    private static AudioClip MakeClickClip()
    {
        const int rate = 44100;
        int n = (int)(rate * 0.07f);
        float[] d = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            float env = Mathf.Exp(-t * 55f) * Mathf.Clamp01(t / 0.002f);
            d[i] = (Mathf.Sin(2f * Mathf.PI * 150f * t) * 0.8f + Mathf.Sin(2f * Mathf.PI * 310f * t) * 0.25f) * env * 0.8f;
        }
        AudioClip c = AudioClip.Create("PetrifyDeniedClick", n, 1, rate, false);
        c.SetData(d, 0);
        return c;
    }
}
