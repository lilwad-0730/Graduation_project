using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ★1114 墜落時主角輕微晃動（純視覺，只動外觀子物件）。
///
///  ◆ 墜落晃動：失控、不安、不確定的感覺
///   ・以「左右搖晃」為主：繞畫面深度軸（Z）左右傾、繞垂直軸（Y）左右轉身，加上上下起伏（Y）；
///     前後（畫面橫向 X）的飄移與前後仰（繞 X 軸）刻意壓得很小。
///   ・幾個頻率互不成倍數的正弦疊起來，不是規律鐘擺；前 1.5 秒淡入，接近地面依 FallLandingK 收掉。
///
///  為什麼不會跟別的機制打架：
///   ・只動外觀子物件（animator 那個 Transform），剛體、碰撞盒、鎖定的 X、墜落速度曲線一個都不碰，
///     落點、傳送接力、落地判定、墜落秒數都不變。
///   ・PlayerMovement 每幀用 Slerp 從「目前的旋轉」往目標轉、斜坡補償也會改 localPosition.y。
///     所以做成「疊加→下一幀開始前先撤掉→重新疊加」：Update 排最前面撤掉上一幀偏移，LateUpdate 排最後面再疊。
///   ・沒在墜落＝一律撤掉、什麼都不做。重生、傳送後不殘留。
///   ・不改鏡頭：鏡頭位置由 CameraTargetXFollower 每幀完整接管，疊東西會互相覆蓋。
///   ・起身動作由 PlayerMovement 的 BeginRuinsStandUp 處理（隊友做的），這裡完全不碰。
///  所有數字都在 Inspector 可調；enableSway 取消勾選＝完全恢復原樣。
/// </summary>
[DefaultExecutionOrder(100)]
public class FallSwayFX : MonoBehaviour
{
    [Header("墜落晃動：開關與強度")]
    public bool enableSway = true;
    [Tooltip("整體強度倍率：1＝預設、0＝關掉、1.5＝更大")]
    [Range(0f, 3f)] public float intensity = 1f;

    [Header("左右搖晃（主角）")]
    [Tooltip("繞畫面深度軸左右傾斜（度）")]
    [Range(0f, 45f)] public float rollDegrees = 17f;
    [Tooltip("繞垂直軸左右轉身（度）：身體一下朝向鏡頭、一下背向鏡頭")]
    [Range(0f, 60f)] public float yawDegrees = 11f;
    [Tooltip("上下起伏（公尺）：像在氣流裡浮沉")]
    [Range(0f, 2f)] public float bobMeters = 0.2f;
    [Header("前後（刻意很小）")]
    [Tooltip("畫面橫向飄移（公尺）")]
    [Range(0f, 2f)] public float driftMeters = 0.25f;
    [Tooltip("前後仰（度，繞 X 軸）")]
    [Range(0f, 20f)] public float pitchDegrees = 4f;

    [Header("節奏")]
    [Tooltip("淡入秒數：墜落開始後幾秒內晃動從 0 慢慢長到全開")]
    public float fadeInSeconds = 1.5f;
    [Tooltip("越掉越不安：從開始到這個秒數，振幅由 70% 長到 100%")]
    public float buildUpSeconds = 6f;
    [Tooltip("離地面多近開始收掉（0~1，對應 PlayerMovement 的放慢距離比例）。0.6＝剩 60% 距離就開始收")]
    [Range(0.1f, 1f)] public float settleStartK = 0.6f;

    private PlayerMovement _pm;
    private Transform _visual;
    private bool _applied;
    private Quaternion _appliedRot = Quaternion.identity;
    private Vector3 _appliedPos;
    private float _seed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        AttachAll();   // 第一個場景收不到 sceneLoaded，補跑一次
    }

    private static void OnSceneLoaded(Scene s, LoadSceneMode m) { AttachAll(); }

    private static void AttachAll()
    {
        foreach (PlayerMovement pm in FindObjectsByType<PlayerMovement>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (pm != null && pm.GetComponent<FallSwayFX>() == null) pm.gameObject.AddComponent<FallSwayFX>();
    }

    private void Awake()
    {
        _pm = GetComponent<PlayerMovement>();
        _seed = Random.value * 100f;   // 每次墜落相位不同，但同一次內平滑連續
    }

    private Transform FindVisual()
    {
        if (_visual != null) return _visual;
        if (_pm == null || _pm.animator == null) return null;
        Transform t = _pm.animator.transform;
        if (t == transform) return null;   // 外觀就是根物件時不碰（會帶著剛體轉）
        _visual = t;
        return _visual;
    }

    /// <summary>撤掉上一幀疊上去的偏移（排在 PlayerMovement 之前）。</summary>
    private void Undo()
    {
        if (!_applied) return;
        _applied = false;
        Transform v = FindVisual();
        if (v == null) return;
        v.position -= _appliedPos;
        v.rotation = Quaternion.Inverse(_appliedRot) * v.rotation;
    }

    private void OnDisable() { Undo(); }

    private void LateUpdate()
    {
        if (_pm == null) return;
        Transform v = FindVisual();
        if (v == null) return;

        if (!(enableSway && intensity > 0f && _pm.IsInControlledFall)) return;

        float t = _pm.ControlledFallSeconds;
        float fade = fadeInSeconds <= 0.01f ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / fadeInSeconds));
        float build = 0.7f + 0.3f * Mathf.Clamp01(t / Mathf.Max(0.1f, buildUpSeconds));
        float settle = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_pm.FallLandingK / Mathf.Max(0.1f, settleStartK)));   // 接近地面→0
        float amp = intensity * MotionComfort.Scale * fade * build * settle;   // ★1115 動態強度（減少晃動）
        if (amp <= 0.001f) return;

        float s = _seed;
        // 頻率互不成倍數 → 不會變成規律的鐘擺；比第一版快，才有「慌亂」的感覺
        float roll = Wave(t, s, 0.43f, 1.07f, 2.3f) * rollDegrees * amp;                  // 左右傾
        float yaw = Wave(t, s * 1.3f + 7f, 0.37f, 0.91f, 1.9f) * yawDegrees * amp;        // 左右轉身
        float bob = Wave(t, s * 0.8f + 3f, 0.29f, 0.71f, 1.6f) * bobMeters * amp;          // 上下浮沉
        float pitch = Wave(t, s * 1.9f + 5f, 0.33f, 0.83f, 1.7f) * pitchDegrees * amp;     // 前後仰（很小）
        float drift = Wave(t, s * 0.6f + 11f, 0.31f, 0.83f, 1.5f) * driftMeters * amp;    // 橫向飄（很小）

        // 世界軸左乘疊加（跟斜坡傾斜同一套做法，左右朝向翻面時方向仍然正確）
        _appliedRot = Quaternion.Euler(pitch, yaw, roll);
        _appliedPos = new Vector3(drift, bob, 0f);
        v.rotation = _appliedRot * v.rotation;
        v.position += _appliedPos;
        _applied = true;
    }

    private static float Wave(float t, float seed, float f1, float f2, float f3)
    {
        return Mathf.Sin((t + seed) * 6.2832f * f1) * 0.6f
             + Mathf.Sin((t + seed * 1.7f) * 6.2832f * f2) * 0.3f
             + Mathf.Sin((t + seed * 2.3f) * 6.2832f * f3) * 0.1f;
    }

    /// <summary>排在最前面的小幫手：每幀 Update 之前先撤掉上一幀的偏移，PlayerMovement 才看到乾淨的值。</summary>
    [DefaultExecutionOrder(-10000)]
    private class FallSwayPre : MonoBehaviour
    {
        public FallSwayFX owner;
        private void Update() { if (owner != null) owner.Undo(); }
    }

    private void Start()
    {
        FallSwayPre pre = gameObject.AddComponent<FallSwayPre>();
        pre.owner = this;
    }
}
