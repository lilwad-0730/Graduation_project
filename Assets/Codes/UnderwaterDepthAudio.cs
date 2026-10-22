using UnityEngine;

/// <summary>
/// 水下聽感：在 AudioListener（通常是 Main Camera）上加一個 Low-pass，
/// 玩家離「初始位置」的高度差越大，高頻被切得越多，聲音越悶。
/// 放在 underwater 場景任一物件上即可（場景裡的「UnderwaterDepthAudio」）。
/// 跟 PlayerRespawnSystem 的重生悶音是兩個獨立的 filter，會疊加，不互相搶。
/// </summary>
public class UnderwaterDepthAudio : MonoBehaviour
{
    /// <summary>這個系統自己建的 filter，給其他腳本辨認用（避免 GetComponent 拿錯）。</summary>
    public static AudioLowPassFilter OwnedFilter { get; private set; }

    [Header("玩家")]
    [Tooltip("留空會自動找 Tag = Player 的物件")]
    public Transform player;

    [Tooltip("打開＝只有玩家在水下物理模式（PlayerMovement.isUnderwater）時才套用；離開水面會慢慢恢復成空氣中的聲音")]
    public bool onlyWhenUnderwater = true;

    [Header("高度差 → 悶的程度")]
    [Tooltip("打開＝往上、往下都算高度差；關閉＝只算比初始位置更深的部分")]
    public bool countBothDirections = true;

    [Tooltip("高度差達到這個距離（公尺）時效果最重")]
    public float fullEffectHeight = 40f;

    [Tooltip("橫軸 0~1＝高度差比例，縱軸 0~1＝悶的程度")]
    public AnimationCurve depthCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("截止頻率 (Hz)")]
    [Tooltip("在水面上/空氣中的頻率（22000 = 幾乎不過濾）")]
    public float airCutoff = 22000f;

    [Tooltip("剛在水中、高度差 0 時的頻率（水中本來就會悶一點）")]
    public float shallowCutoff = 5000f;

    [Tooltip("高度差最大時的頻率（越低越悶）")]
    public float deepCutoff = 450f;

    [Range(1f, 3f)]
    [Tooltip("截止點附近的共振，稍微調高聲音會更「水」")]
    public float resonanceQ = 1.2f;

    [Header("平滑")]
    [Tooltip("頻率變化的平滑時間（秒）")]
    public float smoothTime = 0.35f;

    [Tooltip("進出水面時的過渡時間（秒）")]
    public float waterTransitionTime = 0.6f;

    [Header("除錯（唯讀）")]
    [SerializeField] private float debugHeightDiff;
    [SerializeField] private float debugCutoff;

    private AudioLowPassFilter _filter;
    private PlayerMovement _movement;
    private float _startY;
    private bool _hasStart;
    private float _waterBlend;      // 0 = 空氣, 1 = 水中
    private float _logCutoff;
    private float _logVelocity;

    private void OnEnable()
    {
        SetupFilter();
    }

    private void OnDisable()
    {
        if (_filter != null)
        {
            _filter.cutoffFrequency = 22000f;
            _filter.enabled = false;
        }
    }

    private void Start()
    {
        FindPlayer();
        if (player != null)
        {
            _startY = player.position.y;
            _hasStart = true;
        }

        _waterBlend = IsUnderwater() ? 1f : 0f;
        _logCutoff = Mathf.Log(TargetCutoff());
        Apply(Mathf.Exp(_logCutoff));
    }

    private void Update()
    {
        if (_filter == null) SetupFilter();
        if (_filter == null) return;

        if (player == null)
        {
            FindPlayer();
            if (player == null) return;
        }
        if (!_hasStart)
        {
            _startY = player.position.y;
            _hasStart = true;
        }

        float dt = Time.unscaledDeltaTime;
        float waterTarget = IsUnderwater() ? 1f : 0f;
        float step = waterTransitionTime > 0f ? dt / waterTransitionTime : 1f;
        _waterBlend = Mathf.MoveTowards(_waterBlend, waterTarget, step);

        // 在對數空間平滑，聽起來比較線性
        _logCutoff = Mathf.SmoothDamp(_logCutoff, Mathf.Log(TargetCutoff()), ref _logVelocity,
            smoothTime, Mathf.Infinity, dt);
        Apply(Mathf.Exp(_logCutoff));
    }

    /// <summary>需要時可重設高度基準（例如換關卡起點）。</summary>
    public void ResetReferenceHeight()
    {
        if (player != null)
        {
            _startY = player.position.y;
            _hasStart = true;
        }
    }

    private float TargetCutoff()
    {
        float diff = 0f;
        if (player != null && _hasStart)
        {
            float d = player.position.y - _startY;
            diff = countBothDirections ? Mathf.Abs(d) : Mathf.Max(0f, -d);
        }
        debugHeightDiff = diff;

        float t = fullEffectHeight > 0f ? Mathf.Clamp01(diff / fullEffectHeight) : 1f;
        t = Mathf.Clamp01(depthCurve != null ? depthCurve.Evaluate(t) : t);

        float water = LogLerp(shallowCutoff, deepCutoff, t);
        return LogLerp(airCutoff, water, _waterBlend);
    }

    private void Apply(float cutoff)
    {
        cutoff = Mathf.Clamp(cutoff, 10f, 22000f);
        debugCutoff = cutoff;
        _filter.cutoffFrequency = cutoff;
        _filter.lowpassResonanceQ = Mathf.Lerp(1f, resonanceQ, _waterBlend);
        _filter.enabled = cutoff < 21900f;
    }

    private bool IsUnderwater()
    {
        if (!onlyWhenUnderwater) return true;
        if (_movement == null && player != null)
        {
            _movement = player.GetComponent<PlayerMovement>();
            if (_movement == null) _movement = player.GetComponentInParent<PlayerMovement>();
            if (_movement == null) _movement = player.GetComponentInChildren<PlayerMovement>();
        }
        return _movement == null || _movement.isUnderwater;
    }

    private void FindPlayer()
    {
        if (player != null) return;
        GameObject go = GameObject.FindWithTag("Player");
        if (go != null) player = go.transform;
    }

    private void SetupFilter()
    {
        if (_filter != null) return;
        AudioListener listener = Object.FindFirstObjectByType<AudioListener>();
        if (listener == null) return;

        _filter = listener.gameObject.AddComponent<AudioLowPassFilter>();
        _filter.hideFlags = HideFlags.DontSave;
        _filter.cutoffFrequency = 22000f;
        OwnedFilter = _filter;
    }

    private void OnDestroy()
    {
        if (_filter != null) Destroy(_filter);
        if (OwnedFilter == _filter) OwnedFilter = null;
    }

    private static float LogLerp(float a, float b, float t)
    {
        a = Mathf.Max(a, 1f);
        b = Mathf.Max(b, 1f);
        return Mathf.Exp(Mathf.Lerp(Mathf.Log(a), Mathf.Log(b), t));
    }
}
