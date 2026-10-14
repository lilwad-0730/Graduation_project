using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 深海壓迫感：依主角所在深度 (Y) 調整背景色、環境光、霧、水色濾鏡與後製效果。
/// 越深 → 越暗、越藍、越不飽和、暗角越重、顆粒與色差越明顯。
/// 掛在場景中任一物件上 (建議同一物件也放 Global Volume)，執行時才生效，離開 Play 會自動還原場景設定。
/// 編輯模式下可勾選 Preview In Editor 並拖 Preview Depth01 預覽不同深度。
/// </summary>
[ExecuteAlways]
public class DepthAtmosphere : MonoBehaviour
{
    [Header("深度範圍")]
    [Tooltip("這個 Y 以上算最淺 (深度 0)")]
    public float shallowY = -5f;
    [Tooltip("這個 Y 以下算最深 (深度 1)")]
    public float deepY = -130f;
    [Tooltip("深度變化曲線 (橫軸 0=淺 1=深，縱軸為套用的深度比例)。預設前段變化慢、後段加速變暗")]
    public AnimationCurve depthCurve = new AnimationCurve(new Keyframe(0f, 0f, 0f, 0.6f), new Keyframe(1f, 1f, 1.6f, 0f));
    [Tooltip("深度變化的平滑速度 (避免瞬移/重生時顏色跳動)")]
    public float smoothing = 2f;

    [Header("背景與環境光")]
    public Gradient backgroundColor = MakeGradient(
        new Color(0.19f, 0.30f, 0.48f), new Color(0.07f, 0.13f, 0.27f), new Color(0.01f, 0.02f, 0.05f));
    public Gradient ambientColor = MakeGradient(
        new Color(0.21f, 0.23f, 0.26f), new Color(0.10f, 0.13f, 0.19f), new Color(0.03f, 0.04f, 0.07f));

    [Header("霧 (遠處石頭融進黑暗)")]
    public bool useFog = true;
    [Tooltip("霧開始的距離 (淺 → 深)")]
    public Vector2 fogStart = new Vector2(14f, 6f);
    [Tooltip("霧完全遮住的距離 (淺 → 深)")]
    public Vector2 fogEnd = new Vector2(40f, 22f);

    [Header("水色濾鏡 (場景中名為 water 的半透明 Sprite)")]
    public SpriteRenderer waterTint;
    [Tooltip("水色濾鏡透明度 (淺 → 深)。越深越淡，避免青色把黑暗洗亮")]
    public Vector2 waterTintAlpha = new Vector2(0.176f, 0.05f);

    [Header("主角身邊的微光 (深處避免主角融進黑暗)")]
    public bool usePlayerGlow = true;
    public Color playerGlowColor = new Color(0.6f, 0.85f, 1f);
    [Tooltip("微光強度 (淺 → 深)")]
    public Vector2 playerGlowIntensity = new Vector2(0f, 2.5f);
    [Tooltip("微光照亮範圍 (淺 → 深)")]
    public Vector2 playerGlowRange = new Vector2(3f, 5f);
    [Tooltip("微光相對主角的位置 (Z 負值 = 朝鏡頭，才照得到主角正面)")]
    public Vector3 playerGlowOffset = new Vector3(0f, 0.6f, -1.5f);

    [Header("後製 (需要同物件或場景中的 Global Volume)")]
    public Volume volume;
    [Tooltip("曝光 (淺 → 深，單位 EV)")]
    public Vector2 postExposure = new Vector2(0f, -1.2f);
    [Tooltip("飽和度 (淺 → 深)")]
    public Vector2 saturation = new Vector2(-5f, -45f);
    [Tooltip("對比 (淺 → 深)")]
    public Vector2 contrast = new Vector2(5f, 20f);
    [Tooltip("色調濾鏡 (淺 → 深)")]
    public Gradient colorFilter = MakeGradient(new Color(0.92f, 1f, 1f), new Color(0.82f, 0.92f, 1f), new Color(0.7f, 0.82f, 1f));
    [Tooltip("暗角強度 (淺 → 深)")]
    public Vector2 vignetteIntensity = new Vector2(0.25f, 0.5f);
    public Color vignetteColor = new Color(0.0f, 0.02f, 0.06f);
    [Tooltip("膠片顆粒 (淺 → 深)")]
    public Vector2 filmGrain = new Vector2(0.05f, 0.4f);
    [Tooltip("色差 (淺 → 深)")]
    public Vector2 chromaticAberration = new Vector2(0f, 0.3f);
    [Tooltip("Bloom 強度 (淺 → 深)。越深越亮的光源越顯眼")]
    public Vector2 bloomIntensity = new Vector2(0.4f, 1.0f);

    [Header("編輯器預覽")]
    public bool previewInEditor = false;
    [Range(0f, 1f)] public float previewDepth01 = 0f;

    /// <summary>目前套用中的深度比例 (0=淺, 1=深)，其他效果可以參考。</summary>
    public float CurrentDepth01 { get; private set; }

    private Transform _player;
    private Camera _cam;
    private bool _initialized;
    private ColorAdjustments _colorAdj;
    private Vignette _vignette;
    private FilmGrain _grain;
    private ChromaticAberration _ca;
    private Bloom _bloom;
    private Light _glow;

    private static Gradient MakeGradient(Color a, Color b, Color c)
    {
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(a, 0f), new GradientColorKey(b, 0.5f), new GradientColorKey(c, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return g;
    }

    private void OnEnable()
    {
        _initialized = false;
    }

    private void Init()
    {
        _cam = Camera.main;
        PlayerMovement pm = FindAnyObjectByType<PlayerMovement>();
        _player = pm != null ? pm.transform : null;

        if (waterTint == null)
        {
            GameObject w = GameObject.Find("water");
            if (w != null) waterTint = w.GetComponent<SpriteRenderer>();
        }

        if (volume == null) volume = GetComponent<Volume>();
        if (volume != null)
        {
            // 執行時用 volume.profile (自動複製一份) 改值，不會改到存檔的 profile 資產；編輯器預覽則直接改 sharedProfile
            VolumeProfile profile = Application.isPlaying ? volume.profile : volume.sharedProfile;
            if (profile != null)
            {
                profile.TryGet(out _colorAdj);
                profile.TryGet(out _vignette);
                profile.TryGet(out _grain);
                profile.TryGet(out _ca);
                profile.TryGet(out _bloom);
            }
        }

        if (usePlayerGlow && _player != null && _glow == null && Application.isPlaying)
        {
            var go = new GameObject("DepthPlayerGlow");
            go.transform.SetParent(_player, false);
            _glow = go.AddComponent<Light>();
            _glow.type = LightType.Point;
            _glow.shadows = LightShadows.None;
            _glow.intensity = 0f;
        }

        if (_cam != null && Application.isPlaying)
        {
            _cam.clearFlags = CameraClearFlags.SolidColor;
            var data = _cam.GetUniversalAdditionalCameraData();
            if (data != null) data.renderPostProcessing = true;
        }

        CurrentDepth01 = TargetDepth01();
        _initialized = true;
    }

    private float TargetDepth01()
    {
        if (!Application.isPlaying) return previewDepth01;
        float y = _player != null ? _player.position.y : (_cam != null ? _cam.transform.position.y : shallowY);
        float t = Mathf.InverseLerp(shallowY, deepY, y);
        return Mathf.Clamp01(depthCurve.Evaluate(t));
    }

    private void LateUpdate()
    {
        if (!Application.isPlaying && !previewInEditor) return;
        if (!_initialized || (Application.isPlaying && _player == null)) Init();

        float target = TargetDepth01();
        CurrentDepth01 = Application.isPlaying
            ? Mathf.MoveTowards(CurrentDepth01, target, smoothing * Time.deltaTime)
            : target;
        Apply(CurrentDepth01);
    }

    private void Apply(float d)
    {
        Color bg = backgroundColor.Evaluate(d);
        if (_cam != null && Application.isPlaying) _cam.backgroundColor = new Color(bg.r, bg.g, bg.b, 1f);

        if (Application.isPlaying)
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = ambientColor.Evaluate(d);

            RenderSettings.fog = useFog;
            if (useFog)
            {
                RenderSettings.fogMode = FogMode.Linear;
                RenderSettings.fogColor = bg;
                RenderSettings.fogStartDistance = Mathf.Lerp(fogStart.x, fogStart.y, d);
                RenderSettings.fogEndDistance = Mathf.Lerp(fogEnd.x, fogEnd.y, d);
            }

            if (_glow != null)
            {
                _glow.enabled = usePlayerGlow;
                _glow.color = playerGlowColor;
                _glow.intensity = Mathf.Lerp(playerGlowIntensity.x, playerGlowIntensity.y, d);
                _glow.range = Mathf.Lerp(playerGlowRange.x, playerGlowRange.y, d);
                _glow.transform.position = _player.position + playerGlowOffset;
            }

            if (waterTint != null)
            {
                Color c = waterTint.color;
                c.a = Mathf.Lerp(waterTintAlpha.x, waterTintAlpha.y, d);
                waterTint.color = c;
            }
        }

        if (_colorAdj != null)
        {
            _colorAdj.postExposure.Override(Mathf.Lerp(postExposure.x, postExposure.y, d));
            _colorAdj.saturation.Override(Mathf.Lerp(saturation.x, saturation.y, d));
            _colorAdj.contrast.Override(Mathf.Lerp(contrast.x, contrast.y, d));
            _colorAdj.colorFilter.Override(colorFilter.Evaluate(d));
        }
        if (_vignette != null)
        {
            _vignette.intensity.Override(Mathf.Lerp(vignetteIntensity.x, vignetteIntensity.y, d));
            _vignette.color.Override(vignetteColor);
        }
        if (_grain != null) _grain.intensity.Override(Mathf.Lerp(filmGrain.x, filmGrain.y, d));
        if (_ca != null) _ca.intensity.Override(Mathf.Lerp(chromaticAberration.x, chromaticAberration.y, d));
        if (_bloom != null) _bloom.intensity.Override(Mathf.Lerp(bloomIntensity.x, bloomIntensity.y, d));
    }
}
