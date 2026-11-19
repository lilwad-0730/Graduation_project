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
    [Header("★1120 墜落速度跟著旁白")]
    [Tooltip("沒播旁白時墜落速度是原本的幾倍（播旁白時維持原本速度）。1＝全程原本速度")]
    public float silentFallSpeedMultiplier = 4f;
    [Tooltip("下一句旁白開始前幾秒先減速。0＝人聲開始的那一刻才減速（變速點跟人聲對齊）")]
    public float slowDownBeforeLineSeconds = 0f;
    [Tooltip("速度切換的俐落程度：加減速度再乘這個倍數。1＝跟著倍率放大而已（切換約 1～2 秒）；5＝約 0.4 秒內切完，玩家明顯感覺到變快／變慢")]
    public float speedSwitchSharpness = 5f;
    [Tooltip("還有旁白沒講完時，剩下的通道不夠用原本速度講完就先不加速，確保旁白都在天空裡講完（不會拖到落地之後）")]
    public bool keepNarrationInSky = true;
    [Tooltip("上面那個估算多留的距離（公尺）")]
    public float narrationDistanceMargin = 6f;

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
    private float _voiceEndTime = -1f;

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
        UpdateFallSpeed();
    }

    /// <summary>★1120 播旁白（以及下一句開始前 slowDownBeforeLineSeconds 秒）時維持原本速度，其餘時間 silentFallSpeedMultiplier 倍。</summary>
    private void UpdateFallSpeed()
    {
        if (player == null) return;
        if (!player.IsInControlledFall) { player.ruinsFallSpeedMultiplier = 1f; player.ruinsFallAccelMultiplier = 1f; return; }
        // 加減速也跟著倍率放大，不然原本的加速度太小，4 倍速要好幾秒才加得上去、人聲前也來不及慢下來
        player.ruinsFallAccelMultiplier = Mathf.Max(1f, silentFallSpeedMultiplier) * Mathf.Max(1f, speedSwitchSharpness);
        // 第一句開始後到最後一句講完都算「在講」（句間的短停頓也維持原本速度，整段旁白是一個區段）
        bool speaking = Time.time < _voiceEndTime || (_nextLine > 0 && !_narrationDone);
        bool lineSoon = !_narrationDone && lines != null && lines.Length > 0 &&
                        (_nextLineTime < 0f
                            ? player.ControlledFallSeconds >= firstLineDelay - Mathf.Max(0f, slowDownBeforeLineSeconds)
                            : Time.time >= _nextLineTime - Mathf.Max(0f, slowDownBeforeLineSeconds));
        bool fast = !(speaking || lineSoon);

        // 保護：還有旁白沒講完時，剩下的通道要夠用「原本速度」把旁白講完；不夠就先不加速，
        //   不然加速把通道掉完、旁白會拖到傳送進廢墟、落地之後才講完
        if (fast && !_narrationDone && keepNarrationInSky)
        {
            float relayY = RelayTopY();
            if (!float.IsNaN(relayY))
            {
                float remain = NarrationRemainingSeconds();
                float fallSec = player.ControlledFallSeconds;
                float need = remain * player.RuinsFallBaseSpeedAt(fallSec + remain) + Mathf.Max(0f, narrationDistanceMargin);
                float left = player.transform.position.y - relayY;
                if (left < need) fast = false;
            }
        }
        player.ruinsFallSpeedMultiplier = fast ? Mathf.Max(0.1f, silentFallSpeedMultiplier) : 1f;
    }

    /// <summary>★1120 從現在到最後一句旁白講完還要幾秒（照目前的排程：句長＋句間空檔）。</summary>
    private float NarrationRemainingSeconds()
    {
        if (_narrationDone || lines == null) return Mathf.Max(0f, _voiceEndTime - Time.time);
        float now = Time.time;
        float end = Mathf.Max(now, _voiceEndTime);
        float next = _nextLineTime < 0f ? now + Mathf.Max(0f, firstLineDelay - player.ControlledFallSeconds) : _nextLineTime;
        for (int i = _nextLine; i < lines.Length; i++)
        {
            float len = lines[i] != null && lines[i].clip != null ? lines[i].clip.length : 0f;
            next = Mathf.Max(next, now);
            end = next + len;
            next = end + Mathf.Max(0f, lineGap);
        }
        return Mathf.Max(0f, end - now);
    }

    private Collider[] _relayCols;
    /// <summary>★1120 主角下方、墜落通道裡的接力傳送（白霧進廢墟）觸發區頂端高度；找不到回傳 NaN。</summary>
    private float RelayTopY()
    {
        if (_relayCols == null)
        {
            var list = new System.Collections.Generic.List<Collider>();
            foreach (SkyDiveTeleportTrigger t in FindObjectsByType<SkyDiveTeleportTrigger>(FindObjectsSortMode.None))
                if (t != null && t.keepControlledFall) { Collider c = t.GetComponent<Collider>(); if (c != null) list.Add(c); }
            _relayCols = list.ToArray();
        }
        float py = player.transform.position.y, px = player.transform.position.x, best = float.NaN;
        foreach (Collider c in _relayCols)
        {
            if (c == null || !c.enabled) continue;
            Bounds b = c.bounds;
            if (px < b.min.x || px > b.max.x || b.max.y > py) continue;
            if (float.IsNaN(best) || b.max.y > best) best = b.max.y;
        }
        return best;
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
        _voiceEndTime = Time.time + len;
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
