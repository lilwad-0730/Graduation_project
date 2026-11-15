using UnityEngine;

/// <summary>
/// ★1114 廢墟「天空雲層墜落」演出，讓拉長的墜落時間一直有變化、不像在拖時間：
///   ① 雲層視差：正交鏡頭沒有景深，前景雲與背景雲捲動速度一樣。每朵雲依「鏡頭與自己原本高度的差」上下偏移，
///      前景雲刷得比較快、背景雲比較慢；鏡頭經過某朵雲時它剛好在擺放的位置，通道再長也不會累積偏移。
///   ② 下方留空：墜落中鏡頭往下偏，主角在畫面上方約 1/3，看得到下面要穿過的雲。
///   ③ 鏡頭大小節奏：依墜落秒數拉近、稍微放開、再推近，最後接上光球特寫（大小 10）。只拉近不拉遠（拉遠會露出通道兩側）。
///   ④ 墜落旁白：墜落開始後依序播兩句語音＋字幕（整場只播一次）。
/// ②③ 只在「主角固定速度墜落中、而且鏡頭在墜落通道模式」時作用，結束後平滑還原。① 隨時依鏡頭高度計算，在墜落通道外歸零。
/// </summary>
public class RuinsFallCinematics : MonoBehaviour
{
    [System.Serializable]
    public class Line
    {
        public AudioClip clip;
        [Range(0f, 1f)] public float volume = 1f;
        [TextArea(2, 4)] public string subtitle;
    }

    [Header("對象（留空會自動找）")]
    public PlayerMovement player;
    [Tooltip("字幕樣式沿用這顆光球的 subtitleStyle（跟廢墟其他語音一致）")]
    public GuidanceLight subtitleStyleSource;
    [Tooltip("墜落旁白字幕的大小倍率（以光球字幕的字級為基準，只影響墜落旁白，不會改到光球的字幕）")]
    [Range(0.5f, 2f)] public float subtitleSizeMultiplier = 1.5f;

    [Header("① 雲層視差（每朵雲以自己擺放的高度為基準：鏡頭經過它時剛好在原位）")]
    [Tooltip("前景雲的父物件（底下每朵雲都算前景）")]
    public Transform frontLayer;
    [Tooltip("背景雲的父物件（底下每朵雲都算背景）")]
    public Transform backLayer;
    [Tooltip("其他前景雲群組（例如開場的白雲 clouds_blanco），規則同 frontLayer")]
    public Transform[] extraFrontLayers = new Transform[0];
    [Tooltip("其他背景雲群組，規則同 backLayer")]
    public Transform[] extraBackLayers = new Transform[0];
    [Tooltip("前景雲：在畫面上捲動速度＝鏡頭速度×(1＋這個值)，越大刷得越快")]
    [Range(0f, 0.6f)] public float frontParallax = 0.2f;
    [Tooltip("背景雲：在畫面上捲動速度＝鏡頭速度×(1－這個值)，越大越慢、越像在遠方")]
    [Range(0f, 0.6f)] public float backParallax = 0.2f;

    [Header("② 下方留空")]
    [Tooltip("鏡頭往下偏多少（鏡頭大小的倍數）。0.3＝主角在畫面上方約 1/3")]
    [Range(0f, 0.6f)] public float leadFraction = 0.3f;
    [Tooltip("下方留空淡入／淡出秒數")]
    public float leadBlendSeconds = 1.2f;

    [Header("③ 鏡頭大小節奏（橫軸＝墜落秒數，縱軸＝通道鏡頭大小的倍數，≤1）")]
    public AnimationCurve orthoScaleBySeconds = new AnimationCurve(
        new Keyframe(0f, 1f), new Keyframe(2f, 0.82f), new Keyframe(7.4f, 0.95f),
        new Keyframe(8f, 0.95f), new Keyframe(11.5f, 0.76f), new Keyframe(30f, 0.76f));

    [Header("④ 墜落旁白")]
    public Line[] lines = new Line[0];
    [Tooltip("墜落開始後幾秒播第一句")]
    public float firstLineDelay = 1f;
    [Tooltip("上一句播完隔幾秒播下一句")]
    public float lineGap = 0.6f;

    private CameraTargetXFollower _follower;
    private Camera _cam;
    private float _shaftMinX = float.MaxValue, _shaftMaxX = float.MinValue;
    private float _baseFallOffsetY;
    private float _lead;
    private bool _wasActive;

    private struct ParallaxCloud { public AutoHorizontalDrift drift; public float baseY; public float sign; }
    private readonly System.Collections.Generic.List<ParallaxCloud> _clouds = new System.Collections.Generic.List<ParallaxCloud>();

    private bool _narrationDone;
    private int _nextLine;
    private float _nextLineTime = -1f;

    private void Start()
    {
        if (player == null) player = FindFirstObjectByType<PlayerMovement>();
        if (subtitleStyleSource == null) subtitleStyleSource = FindFirstObjectByType<GuidanceLight>();
        _cam = Camera.main;

        // 墜落通道的左右範圍：鏡頭在這個範圍內才算視差，跑到廢墟（通道外）兩層歸零、維持原本擺位
        foreach (SpriteRenderer sr in FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
        {
            if (sr == null || !sr.CompareTag("FallingBackground")) continue;
            _shaftMinX = Mathf.Min(_shaftMinX, sr.bounds.min.x);
            _shaftMaxX = Mathf.Max(_shaftMaxX, sr.bounds.max.x);
        }

        CollectClouds(frontLayer, -1f);
        CollectClouds(backLayer, 1f);
        if (extraFrontLayers != null) foreach (Transform t in extraFrontLayers) CollectClouds(t, -1f);
        if (extraBackLayers != null) foreach (Transform t in extraBackLayers) CollectClouds(t, 1f);
    }

    private void CollectClouds(Transform layer, float sign)
    {
        if (layer == null) return;
        foreach (AutoHorizontalDrift d in layer.GetComponentsInChildren<AutoHorizontalDrift>(true))
            _clouds.Add(new ParallaxCloud { drift = d, baseY = d.transform.position.y, sign = sign });
    }

    private void LateUpdate()
    {
        if (_follower == null)
        {
            _follower = CameraTargetXFollower.Instance;
            if (_follower != null) _baseFallOffsetY = _follower.fallingCameraYOffset;
        }
        if (_cam == null) _cam = Camera.main;

        UpdateParallax();
        UpdateFallCamera();
        UpdateNarration();
    }

    private void UpdateParallax()
    {
        if (_cam == null) return;
        Vector3 c = _cam.transform.position;
        bool inShaft = c.x >= _shaftMinX && c.x <= _shaftMaxX;
        // 前景：偏移＝－k×(鏡頭Y－雲原本Y) → 畫面上捲動 (1＋k) 倍；背景：＋k×… → (1－k) 倍。鏡頭在雲的高度時偏移為 0。
        // 鏡頭不在墜落通道（例如在廢墟）就全部歸零，維持原本擺位。
        for (int i = 0; i < _clouds.Count; i++)
        {
            ParallaxCloud p = _clouds[i];
            if (p.drift == null) continue;
            float k = p.sign < 0f ? frontParallax : backParallax;
            p.drift.extraOffsetY = inShaft ? p.sign * k * (c.y - p.baseY) : 0f;
        }
    }

    private bool FallCameraActive =>
        player != null && player.IsInControlledFall &&
        _follower != null && _follower.currentMode == CameraTargetXFollower.CameraMode.Falling;

    private void UpdateFallCamera()
    {
        if (_follower == null) return;
        bool active = FallCameraActive;

        float ortho = _cam != null ? _cam.orthographicSize : 14f;
        float targetLead = active ? leadFraction * ortho : 0f;
        float rate = ortho * Mathf.Max(0.01f, leadFraction) / Mathf.Max(0.05f, leadBlendSeconds);
        _lead = Mathf.MoveTowards(_lead, targetLead, rate * Time.deltaTime);
        _follower.fallingCameraYOffset = _baseFallOffsetY - _lead;

        if (active)
            _follower.fallingOrthoScale = Mathf.Clamp(orthoScaleBySeconds.Evaluate(player.ControlledFallSeconds), 0.4f, 1f);
        else if (_wasActive || _follower.fallingOrthoScale != 1f)
            _follower.fallingOrthoScale = 1f;   // 墜落模式自己會用 Lerp 慢慢回到原大小
        _wasActive = active;
    }

    private void UpdateNarration()
    {
        if (_narrationDone || lines == null || lines.Length == 0 || player == null) return;

        if (_nextLineTime < 0f)
        {
            if (!player.IsInControlledFall) return;
            _nextLineTime = Time.time + Mathf.Max(0f, firstLineDelay - player.ControlledFallSeconds);
        }
        if (Time.time < _nextLineTime) return;

        Line line = lines[_nextLine];
        float len = 0f;
        if (line != null && line.clip != null)
        {
            len = line.clip.length;
            if (AudioManager.Instance != null) AudioManager.Instance.PlayVoice(line.clip, line.volume);
            else AudioSource.PlayClipAtPoint(line.clip, _cam != null ? _cam.transform.position : transform.position, line.volume);
            VoiceSubtitle.Style st = subtitleStyleSource != null && subtitleStyleSource.subtitleStyle != null
                ? subtitleStyleSource.subtitleStyle.Clone() : new VoiceSubtitle.Style();
            st.fontSize *= subtitleSizeMultiplier;
            VoiceSubtitle.Show(line.subtitle, len, st);
        }
        _nextLine++;
        _nextLineTime = Time.time + len + Mathf.Max(0f, lineGap);
        if (_nextLine >= lines.Length) _narrationDone = true;
    }

    private void OnDisable()
    {
        if (_follower == null) return;
        _follower.fallingCameraYOffset = _baseFallOffsetY;
        _follower.fallingOrthoScale = 1f;
    }
}
