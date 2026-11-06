using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// ★1106 情緒氛圍（全部執行時才建立，場景檔不動；關掉：EmotionAtmosphereFX.Enabled = false）
///   水下：緩緩下沉的「海雪」微粒（越深越明顯，很淡）＋ 頭頂水面的微光（越游越深越小、越遠）→ 「回不去了」
///   棉花堡：顏色從淡灰慢慢回到飽和（接荒原的去色）＋ 緩緩往上飄的暖光點（對比荒原往旁邊吹的風）＋ 雲朵極輕微呼吸
/// 全部只畫畫面，不碰碰撞、不碰玩家，不改既有效果。
/// </summary>
public class EmotionAtmosphereFX : MonoBehaviour
{
    public static bool Enabled = true;
    public static bool UnderwaterEnabled = true;
    public static bool CottonEnabled = true;
    /// <summary>棉花堡「灰色慢慢回到有顏色」：隊友還沒同意，先關掉，想法與程式保留。要開改成 true。</summary>
    public static bool CottonColorReturn = false;

    private enum Kind { Underwater, Cotton }
    private Kind _kind;

    private static string s_lastScene;

    // ───────────────────────── 啟動 ─────────────────────────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        SceneManager.sceneLoaded -= OnLoaded;
        SceneManager.sceneLoaded += OnLoaded;
        Apply(SceneManager.GetActiveScene(), true);
    }

    private static void OnLoaded(Scene scene, LoadSceneMode mode)
    {
        Apply(scene, false);
    }

    private static void Apply(Scene scene, bool first)
    {
        string prev = s_lastScene;
        s_lastScene = scene.name;
        if (!Enabled || !scene.IsValid()) return;
        string n = scene.name.ToLowerInvariant();
        if (FindFirstObjectByType<EmotionAtmosphereFX>() != null) return;

        if (n.Contains("underwater") && UnderwaterEnabled) Create(Kind.Underwater, false);
        else if (n.Contains("cotton") && CottonEnabled) Create(Kind.Cotton, prev != scene.name);   // 同場景重載（重生）不再重播色彩回來
    }

    private bool _playColorReturn;

    private static void Create(Kind k, bool colorReturn)
    {
        GameObject go = new GameObject("[情緒氛圍 " + k + "]");
        EmotionAtmosphereFX fx = go.AddComponent<EmotionAtmosphereFX>();
        fx._kind = k;
        fx._playColorReturn = colorReturn;
    }

    // ───────────────────────── 共用 ─────────────────────────
    private Camera _cam;
    private Transform _player;
    private ParticleSystem _ps;
    private Material _psMat;
    private Texture2D _dotTex;

    private void Start()
    {
        _cam = Camera.main;
        FindPlayer();
        if (_kind == Kind.Underwater) { BuildSnow(); BuildSurfaceGlow(); }
        else
        {
            BuildRisingMotes();
            if (_playColorReturn && CottonColorReturn) StartCoroutine(ColorReturn());
            CollectClouds();
        }
    }

    private void FindPlayer()
    {
        if (_player != null) return;
        GameObject p = GameObject.FindWithTag("Player");
        if (p != null) _player = p.transform;
    }

    private float PlayerZ() { return _player != null ? _player.position.z : 0f; }

    private Vector2 ViewSizeAtPlayer()
    {
        if (_cam == null) return new Vector2(30f, 17f);
        float h = _cam.orthographic
            ? _cam.orthographicSize * 2f
            : 2f * Mathf.Abs(PlayerZ() - _cam.transform.position.z) * Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        return new Vector2(h * _cam.aspect, h);
    }

    private static Texture2D MakeDot(int size)
    {
        Texture2D t = new Texture2D(size, size, TextureFormat.RGBA32, false);
        t.wrapMode = TextureWrapMode.Clamp;
        t.filterMode = FilterMode.Bilinear;
        float c = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float r = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                float a = Mathf.Clamp01(1f - r);
                a = a * a * (3f - 2f * a);       // 邊緣柔和的小光點
                t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        t.Apply();
        return t;
    }

    /// <summary>建立一個跟著鏡頭、粒子留在世界裡的柔光點系統（所以鏡頭移動時會有自然視差）。</summary>
    private void BuildParticles(string name, int maxCount, float rate, Vector2 life, Vector2 size, Color colA, Color colB,
                                Vector2 velY, float drift, int sorting)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(transform, false);
        _ps = go.AddComponent<ParticleSystem>();
        _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        _dotTex = MakeDot(32);
        _psMat = new Material(Shader.Find("Sprites/Default")) { name = "Mat_AtmosphereDot (runtime)" };
        _psMat.mainTexture = _dotTex;
        _psMat.color = new Color(1f, 1f, 1f, 0f);

        // 數值是以「畫面高 14 公尺」為基準寫的，這裡依實際畫面大小等比放大（棉花堡鏡頭畫面高 30，不放大根本看不到）
        float sc = Mathf.Max(0.5f, ViewSizeAtPlayer().y / 14f);
        size *= sc; velY *= sc; drift *= sc;

        ParticleSystem.MainModule main = _ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.prewarm = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = maxCount;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
        main.startColor = new ParticleSystem.MinMaxGradient(colA, colB);
        main.gravityModifier = 0f;
        main.useUnscaledTime = false;

        ParticleSystem.EmissionModule em = _ps.emission;
        em.rateOverTime = rate;

        Vector2 v = ViewSizeAtPlayer();
        ParticleSystem.ShapeModule sh = _ps.shape;
        sh.enabled = true;
        sh.shapeType = ParticleSystemShapeType.Box;
        sh.scale = new Vector3(v.x * 1.5f, v.y * 1.4f, 9f);

        ParticleSystem.VelocityOverLifetimeModule vel = _ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(-drift, drift);
        vel.y = new ParticleSystem.MinMaxCurve(velY.x, velY.y);
        vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

        ParticleSystem.NoiseModule noise = _ps.noise;
        noise.enabled = true;
        noise.strength = 0.12f * sc;
        noise.frequency = 0.18f;
        noise.scrollSpeed = 0.08f;
        noise.damping = true;

        // 慢慢浮現、慢慢消失，不會「啵」地出現
        ParticleSystem.ColorOverLifetimeModule col = _ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.18f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(g);

        ParticleSystemRenderer r = _ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.sharedMaterial = _psMat;
        r.sortingOrder = sorting;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;

        _ps.Play();
    }

    private void FollowCamera()
    {
        if (_cam == null) _cam = Camera.main;
        if (_player == null) FindPlayer();
        if (_ps == null || _cam == null) return;
        Vector3 c = _cam.transform.position;
        _ps.transform.position = new Vector3(c.x, c.y, PlayerZ());
    }

    private float _tIn;   // 進場淡入計時

    private void LateUpdate()
    {
        _tIn += Time.unscaledDeltaTime;
        FollowCamera();
        if (_kind == Kind.Underwater) UpdateUnderwater(); else UpdateCotton();
    }

    private void OnDestroy()
    {
        StopGrade();
        if (_psMat != null) Destroy(_psMat);
        if (_dotTex != null) Destroy(_dotTex);
        if (_glowTex != null) Destroy(_glowTex);
        if (_glowSprite != null) Destroy(_glowSprite);
    }

    // ───────────────────────── 水下 ─────────────────────────
    private const float SurfaceY = -5f, DeepY = -130f;
    private float _depth01 = -1f;

    private Image _glow;
    private Texture2D _glowTex;
    private Sprite _glowSprite;
    private RectTransform _glowRt;

    /// <summary>海雪：很小、很淡、緩緩往下沉的白點，只是讓水「有厚度」。</summary>
    private void BuildSnow()
    {
        BuildParticles("海雪", 130, 9f, new Vector2(11f, 17f), new Vector2(0.03f, 0.085f),
            new Color(0.78f, 0.92f, 1f, 0.55f), new Color(0.9f, 0.97f, 1f, 0.9f),
            new Vector2(-0.34f, -0.12f), 0.05f, 20);
    }

    private static Texture2D MakeGlowTex()
    {
        int w = 128, h = 64;
        Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, false);
        t.wrapMode = TextureWrapMode.Clamp;
        t.filterMode = FilterMode.Bilinear;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float nx = (x / (float)(w - 1) - 0.5f) * 2f;
                float ny = (y / (float)(h - 1) - 0.5f) * 2f;
                float r = Mathf.Sqrt(nx * nx + ny * ny);
                float core = Mathf.Pow(Mathf.Clamp01(1f - r), 2.2f);
                float rim = 0.28f * Mathf.Exp(-Mathf.Pow((r - 0.62f) / 0.1f, 2f)) * Mathf.Clamp01(1.2f - r);
                float a = Mathf.Clamp01(core + rim);
                t.SetPixel(x, y, new Color(0.78f, 0.93f, 1f, a));
            }
        t.Apply();
        return t;
    }

    /// <summary>頭頂水面的微光：她游得越深，它越小、越遠。很淡，只在畫面頂端露出一小塊。</summary>
    private void BuildSurfaceGlow()
    {
        GameObject cv = new GameObject("水面微光畫布");
        cv.transform.SetParent(transform, false);
        Canvas c = cv.AddComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = 5;          // 比所有提示 UI 都低
        GameObject g = new GameObject("微光");
        g.transform.SetParent(cv.transform, false);
        _glow = g.AddComponent<Image>();
        _glow.raycastTarget = false;
        _glowTex = MakeGlowTex();
        _glowSprite = Sprite.Create(_glowTex, new Rect(0, 0, _glowTex.width, _glowTex.height), new Vector2(0.5f, 0.5f), 100f);
        _glow.sprite = _glowSprite;
        _glow.color = new Color(1f, 1f, 1f, 0f);
        _glowRt = _glow.rectTransform;
        _glowRt.anchorMin = _glowRt.anchorMax = new Vector2(0.5f, 1f);
        _glowRt.pivot = new Vector2(0.5f, 0.5f);
    }

    private void UpdateUnderwater()
    {
        if (_player == null) { FindPlayer(); if (_player == null) return; }
        float target = Mathf.InverseLerp(SurfaceY, DeepY, _player.position.y);
        _depth01 = _depth01 < 0f ? target : Mathf.Lerp(_depth01, target, 1f - Mathf.Exp(-1.4f * Time.unscaledDeltaTime));
        float fadeIn = Mathf.SmoothStep(0f, 1f, _tIn / 3f);

        // 海雪：淺處幾乎看不見，越深越明顯（上限很低）
        if (_psMat != null)
            _psMat.color = new Color(1f, 1f, 1f, Mathf.Lerp(0.22f, 0.62f, Mathf.SmoothStep(0f, 1f, _depth01)) * fadeIn);

        // 水面微光
        if (_glow != null)
        {
            float H = Screen.height;
            float k = Mathf.SmoothStep(0f, 1f, _depth01);
            float w = Mathf.Lerp(0.62f, 0.2f, k) * H;                       // 越深越小
            float shimmer = 1f + 0.07f * Mathf.Sin(Time.unscaledTime * 0.9f) + 0.04f * Mathf.Sin(Time.unscaledTime * 2.3f + 1.7f);
            float a = Mathf.Lerp(0.05f, 0.17f, Mathf.SmoothStep(0.08f, 0.6f, _depth01)) * shimmer * fadeIn;
            _glowRt.sizeDelta = new Vector2(w * 1.8f, w * 0.9f);
            _glowRt.anchoredPosition = new Vector2(0f, w * 0.18f);          // 一半露在畫面外：像頭頂遠遠的一個開口
            _glow.color = new Color(1f, 1f, 1f, a);
        }
    }

    // ───────────────────────── 棉花堡 ─────────────────────────
    private void BuildRisingMotes()
    {
        // 一切往上飄：對比荒原往旁邊吹的風
        BuildParticles("上飄光點", 110, 8f, new Vector2(9f, 14f), new Vector2(0.14f, 0.4f),
            new Color(1f, 0.93f, 0.74f, 0.7f), new Color(1f, 0.99f, 0.92f, 1f),
            new Vector2(0.28f, 0.7f), 0.12f, 20);
    }

    private void UpdateCotton()
    {
        if (_psMat != null)
            _psMat.color = new Color(1f, 1f, 1f, 0.55f * Mathf.SmoothStep(0f, 1f, (_tIn - 1.5f) / 5f));   // 顏色開始回來之後，光點才慢慢出現
        BreatheClouds();
    }

    // 顏色回來：臨時開後處理，用 Volume 權重從 1 → 0，結束後整個移除，回到場景原樣
    private GameObject _gradeGo;
    private VolumeProfile _gradeProfile;
    private UniversalAdditionalCameraData _gradeCamData;
    private bool _gradePrevPost;

    private System.Collections.IEnumerator ColorReturn()
    {
        yield return null;
        try
        {
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) yield break;
            _gradeCamData = _cam.GetUniversalAdditionalCameraData();
            if (_gradeCamData == null) yield break;
            _gradePrevPost = _gradeCamData.renderPostProcessing;
            _gradeCamData.renderPostProcessing = true;

            _gradeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            ColorAdjustments ca = _gradeProfile.Add<ColorAdjustments>(true);
            ca.saturation.Override(-62f);
            ca.contrast.Override(-8f);
            _gradeGo = new GameObject("[棉花堡色彩回來 Volume]");
            _gradeGo.transform.SetParent(transform, false);
            Volume vol = _gradeGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 100f;
            vol.weight = 1f;
            vol.sharedProfile = _gradeProfile;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[EmotionAtmosphereFX] 色彩回來啟動失敗，略過：" + e.Message);
            StopGrade();
            yield break;
        }

        const float hold = 1.2f, dur = 8f;
        float t = 0f;
        Volume v = _gradeGo != null ? _gradeGo.GetComponent<Volume>() : null;
        while (v != null && t < hold + dur)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01((t - hold) / dur);
            v.weight = 1f - Mathf.SmoothStep(0f, 1f, k);
            yield return null;
        }
        StopGrade();
    }

    private void StopGrade()
    {
        if (_gradeCamData != null) { _gradeCamData.renderPostProcessing = _gradePrevPost; _gradeCamData = null; }
        if (_gradeGo != null) { Destroy(_gradeGo); _gradeGo = null; }
        if (_gradeProfile != null) { Destroy(_gradeProfile); _gradeProfile = null; }
    }

    // 雲朵呼吸：只動畫面上的雲，不動有碰撞的東西
    private struct CloudBreath { public Transform t; public Vector3 baseScale; public float phase, speed; }
    private CloudBreath[] _clouds;

    private void CollectClouds()
    {
        var list = new System.Collections.Generic.List<CloudBreath>();
        SpriteRenderer[] all = FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            SpriteRenderer sr = all[i];
            if (sr == null || !sr.name.ToLowerInvariant().Contains("cloudplatform")) continue;
            if (sr.GetComponentInChildren<Collider>() != null || sr.GetComponentInChildren<Collider2D>() != null) continue;
            if (sr.GetComponentInParent<Collider>() != null || sr.GetComponentInParent<Collider2D>() != null) continue;   // 台階（CloudPlatform_Step）的圖掛在有碰撞的台階底下，一律不動
            if (sr.GetComponentInParent<Rigidbody>() != null || sr.GetComponent<Animator>() != null) continue;
            Transform tr = sr.transform;
            float h = Mathf.Abs(tr.position.x * 0.37f + tr.position.y * 0.61f);
            list.Add(new CloudBreath { t = tr, baseScale = tr.localScale, phase = h * 6.283f, speed = 0.9f + (h % 1f) * 0.5f });
        }
        _clouds = list.ToArray();
    }

    private void BreatheClouds()
    {
        if (_clouds == null) return;
        float fade = Mathf.SmoothStep(0f, 1f, _tIn / 4f);
        for (int i = 0; i < _clouds.Length; i++)
        {
            CloudBreath c = _clouds[i];
            if (c.t == null) continue;
            float s = Mathf.Sin(Time.time * 0.9f * c.speed + c.phase);
            float k = 0.011f * fade;
            c.t.localScale = new Vector3(c.baseScale.x * (1f + k * s * 0.6f), c.baseScale.y * (1f + k * s), c.baseScale.z);
        }
    }
}
