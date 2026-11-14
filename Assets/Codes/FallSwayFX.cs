using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ★1114 墜落時主角輕微晃動（純視覺）：失控、不安、不確定的感覺。
///
///  為什麼不會跟別的機制打架：
///   ・只動「外觀子物件」（animator 那個 Transform）的旋轉與位置，剛體、碰撞盒、鎖定的 X、墜落速度曲線一個都不碰，
///     所以落點、傳送接力、落地判定、墜落秒數都跟沒加之前完全一樣。
///   ・PlayerMovement 每幀會用 Slerp 從「目前的旋轉」往目標轉，若把晃動留在旋轉上它會被一點一點吃掉或累積。
///     所以做成「疊加→下一幀開始前先撤掉→重新疊加」：本腳本的 Update 排在最前面撤掉上一幀的偏移，
///     LateUpdate 排在最後面再疊上這一幀的偏移。PlayerMovement 看到的永遠是乾淨的值。
///   ・只在 PlayerMovement.IsInControlledFall 時有效；沒在墜落＝一律撤掉、什麼都不做（每幀只有一個布林判斷）。
///   ・落地前依 FallLandingK 收掉，落地時站得正；也不會在重生、傳送後殘留。
///   ・不改鏡頭：鏡頭位置由 CameraTargetXFollower 每幀完整接管，疊東西會互相覆蓋。
///  晃動由幾個頻率互不成倍數的正弦疊起來，不是規律鐘擺；振幅前 1.5 秒淡入、越掉越不安一點點。
///  所有數字都在 Inspector 可調；enable 打勾關掉就完全恢復原樣。
/// </summary>
[DefaultExecutionOrder(100)]
public class FallSwayFX : MonoBehaviour
{
    [Header("開關與強度")]
    public bool enableSway = true;
    [Tooltip("整體強度倍率：1＝預設、0＝關掉、1.5＝更大")]
    [Range(0f, 3f)] public float intensity = 1f;

    [Header("傾斜（度，繞畫面 Z 軸）")]
    [Range(0f, 25f)] public float rollDegrees = 14f;   // 墜落鏡頭拉很遠、主角在畫面上很小，太小看不出來
    [Header("橫向飄移（公尺，只是外觀，不是物理位置）")]
    [Range(0f, 1.5f)] public float driftMeters = 0.6f;

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
        if (!enableSway || intensity <= 0f || _pm == null || !_pm.IsInControlledFall) return;
        Transform v = FindVisual();
        if (v == null) return;

        float t = _pm.ControlledFallSeconds;
        float fade = fadeInSeconds <= 0.01f ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / fadeInSeconds));
        float build = 0.7f + 0.3f * Mathf.Clamp01(t / Mathf.Max(0.1f, buildUpSeconds));
        float settle = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_pm.FallLandingK / Mathf.Max(0.1f, settleStartK)));   // 接近地面→0
        float amp = intensity * fade * build * settle;
        if (amp <= 0.001f) return;

        float s = _seed;
        // 頻率互不成倍數（0.31 / 0.77 / 1.63 Hz）→ 不會變成規律的鐘擺
        float roll = (Mathf.Sin((t + s) * 6.2832f * 0.31f) * 0.6f
                    + Mathf.Sin((t + s * 1.7f) * 6.2832f * 0.77f) * 0.3f
                    + Mathf.Sin((t + s * 2.3f) * 6.2832f * 1.63f) * 0.1f) * rollDegrees * amp;
        float drift = (Mathf.Sin((t + s * 0.6f) * 6.2832f * 0.23f) * 0.7f
                     + Mathf.Sin((t + s * 1.1f) * 6.2832f * 0.59f) * 0.3f) * driftMeters * amp;

        _appliedRot = Quaternion.Euler(0f, 0f, roll);   // 世界 Z 軸疊加，跟斜坡傾斜同一套做法，左右朝向都對
        _appliedPos = new Vector3(drift, 0f, 0f);
        v.rotation = _appliedRot * v.rotation;
        v.position += _appliedPos;
        _applied = true;
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
