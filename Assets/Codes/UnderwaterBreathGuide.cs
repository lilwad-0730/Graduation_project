using System.Collections;
using UnityEngine;
using Unity.Cinemachine;

/// <summary>
/// ★1013 快窒息時的「鏡頭特寫引導」。
///
/// 玩家看不到光球就不知道該往哪游。這裡算好時間，剩不夠時鏡頭自己飛過去讓她看見：
///   還剩幾秒溺斃 ≤ 游到目標要多久 ＋ 5 秒思考時間 ＋ 安全緩衝  →  引導。
/// 目標：優先光球；光球不能用（太遠、正在演出）才改拉最近的日誌。
/// 每口氣只引導一次（換氣之後才重新計算）。目標已經在畫面裡就不拉鏡頭。
/// 鏡頭特寫的這段時間氣暫停倒數、玩家定住不會沉，不會因為看提示而吃虧。
/// </summary>
public class UnderwaterBreathGuide : MonoBehaviour
{
    [Header("時間預算")]
    [Tooltip("玩家看到提示後的思考時間（秒）")]
    public float thinkSeconds = 5f;
    [Tooltip("安全緩衝（秒）：游錯一點路、繞石頭的餘裕")]
    public float safetySeconds = 3f;
    [Tooltip("估算游泳速度（公尺/秒）。實際水平約 4.2，這裡抓保守一點把繞路算進去")]
    public float assumedSwimSpeed = 3.2f;
    [Tooltip("危險程度（0~1）低於這個值不引導，避免剛換完氣就拉鏡頭")]
    [Range(0f, 1f)] public float minDanger = 0.25f;

    [Header("選目標")]
    [Tooltip("光球離玩家超過這麼遠就改引導最近的日誌（公尺）")]
    public float orbMaxGuideDistance = 60f;

    [Header("鏡頭")]
    public float panSeconds = 1.6f;
    public float holdSeconds = 1.8f;
    public float returnSeconds = 1.6f;
    [Tooltip("特寫的鏡頭大小。水下平常是 7，光球帶路的特寫是 10（比平常大一圈、視野廣）。有光球時直接用光球自己的 closeupLensSize")]
    public float closeupOrthoSize = 10f;

    [Header("重複引導")]
    [Tooltip("每次引導結束後，至少隔幾秒才會再引導（玩家還沒換氣、又快窒息時會一直重複）")]
    public float repeatCooldownSeconds = 8f;
    [Tooltip("目標離玩家這麼近（公尺）就不拉鏡頭，因為本來就在眼前")]
    public float tooCloseToPan = 6f;

    private float _nextGuideTime;   // 冷卻：這個時間之前不再引導
    private bool _running;
    private GuidanceLight _orb;
    private PlayerMovement _pm;
    private float _nextScan;

    private void Update()
    {
        if (_running) return;
        UnderwaterSuffocationEffect fx = UnderwaterSuffocationEffect.Instance;
        if (fx == null || !fx.effectEnabled) return;
        if (fx.IsHeld || PlayerRespawnSystem.IsAnyRespawning) return;

        if (_pm == null) _pm = FindFirstObjectByType<PlayerMovement>();
        if (_pm == null || _pm.IsControlLocked || PlayerCutsceneHold.IsHeld) return;
        if (Time.unscaledTime < _nextScan) return;
        _nextScan = Time.unscaledTime + 0.25f;

        Transform target = PickTarget(out float dist);
        if (target == null) return;

        float travel = dist / Mathf.Max(0.5f, assumedSwimSpeed);
        float need = travel + thinkSeconds + safetySeconds;
        float left = fx.SecondsUntilDeath;

        if (Time.time < _nextGuideTime) return;
        if (fx.Danger01 < minDanger) return;
        if (left > need) return;

        _nextGuideTime = Time.time + repeatCooldownSeconds;   // 先排冷卻；引導結束後會再重排一次
        if (dist <= tooCloseToPan)
        {
            Debug.Log($"【窒息引導】目標就在 {dist:F1} 公尺內，不拉鏡頭（剩 {left:F0} 秒）。");
            return;
        }
        Debug.Log($"【窒息引導】剩 {left:F0} 秒溺斃，游到「{target.name}」約 {travel:F0} 秒（＋思考 {thinkSeconds:F0}＋緩衝 {safetySeconds:F0}）→ 鏡頭特寫引導。");
        StartCoroutine(GuideRoutine(target));
    }

    private Transform PickTarget(out float dist)
    {
        dist = 0f;
        Vector3 p = _pm.transform.position;

        if (_orb == null) _orb = FindFirstObjectByType<GuidanceLight>();
        if (_orb != null && _orb.gameObject.activeInHierarchy && !_orb.IsBusy)
        {
            float d = Vector2.Distance(p, _orb.transform.position);
            if (d <= orbMaxGuideDistance) { dist = d; return _orb.transform; }
        }

        CollectibleNote best = null;
        float bestD = float.MaxValue;
        CollectibleNote[] notes = FindObjectsByType<CollectibleNote>(FindObjectsSortMode.None);
        for (int i = 0; i < notes.Length; i++)
        {
            CollectibleNote n = notes[i];
            if (n == null || n.IsCollected || !n.gameObject.activeInHierarchy) continue;
            if (n.GetComponent<SpriteRenderer>() == null) continue;   // 外層容器不算
            float d = Vector2.Distance(p, n.transform.position);
            if (d < bestD) { bestD = d; best = n; }
        }
        if (best == null) return null;
        dist = bestD;
        return best.transform;
    }

    private static bool IsOnScreen(Vector3 worldPos)
    {
        Camera cam = Camera.main;
        if (cam == null) return true;
        Vector3 v = cam.WorldToViewportPoint(worldPos);
        return v.z > 0f && v.x > 0.12f && v.x < 0.88f && v.y > 0.12f && v.y < 0.88f;
    }

    private IEnumerator GuideRoutine(Transform target)
    {
        _running = true;
        UnderwaterSuffocationEffect fx = UnderwaterSuffocationEffect.Instance;
        bool held = false, frozen = false;
        CinemachineCamera vcam = null;
        Transform origFollow = null, origTracking = null;
        CinemachineFollow follow = null;
        Vector3 origOffset = Vector3.zero;
        float origOrtho = 0f, origFov = 0f;
        bool useXFollower = Object.FindFirstObjectByType<CameraTargetXFollower>() != null;
        bool camTaken = false;
        try
        {
            float zoom = closeupOrthoSize;
            if (_orb != null) zoom = Mathf.Max(zoom, _orb.closeupLensSize);   // 跟光球帶路的特寫一樣大
            if (fx != null) { fx.SetHold(true); held = true; }       // 氣暫停倒數
            PlayerCutsceneHold.Acquire(_pm, false); frozen = true;   // 玩家定住、不會沉

            if (useXFollower)
            {
                CameraTargetXFollower.SetCameraOverrideSmooth(target, zoom, panSeconds);
                camTaken = true;
            }
            else
            {
                // 水下場景沒有 CameraTargetXFollower，鏡頭是 Cinemachine 直接跟玩家：
                // 暫時把跟隨目標換成光球／日誌，並把鏡頭拉近，結束後原樣還回去。
                foreach (var v in FindObjectsByType<CinemachineCamera>(FindObjectsSortMode.None))
                {
                    if (v != null && v.isActiveAndEnabled) { vcam = v; break; }
                }
                if (vcam != null)
                {
                    origFollow = vcam.Follow;
                    var ct = vcam.Target;
                    origTracking = ct.TrackingTarget;
                    ct.TrackingTarget = target;
                    vcam.Target = ct;
                    vcam.Follow = target;
                    follow = vcam.GetComponent<CinemachineFollow>();
                    if (follow != null)
                    {
                        origOffset = follow.FollowOffset;
                        follow.FollowOffset = new Vector3(origOffset.x, 0f, origOffset.z);   // 不要帶著玩家的上下偏移
                    }
                    origOrtho = vcam.Lens.OrthographicSize;
                    origFov = vcam.Lens.FieldOfView;
                    camTaken = true;
                }
            }

            // 鏡頭飛過去＋停留（Cinemachine 的阻尼會讓它平順滑過去，所以停留時間給足）
            float t = 0f, total = panSeconds + holdSeconds;
            while (t < total)
            {
                if (target == null || PlayerRespawnSystem.IsAnyRespawning) break;
                t += Time.deltaTime;
                if (vcam != null) LerpLens(vcam, zoom, origFov, Time.deltaTime / Mathf.Max(0.1f, panSeconds) * 3f);
                yield return null;
            }

            // 還給玩家
            if (useXFollower)
            {
                if (camTaken) { CameraTargetXFollower.ClearCameraOverrideSmooth(returnSeconds); camTaken = false; }
                yield return new WaitForSeconds(returnSeconds);
            }
            else if (vcam != null)
            {
                RestoreFollow(vcam, follow, origFollow, origTracking, origOffset);
                float r = 0f;
                while (r < returnSeconds)
                {
                    r += Time.deltaTime;
                    LerpLens(vcam, origOrtho, origFov, Time.deltaTime / Mathf.Max(0.1f, returnSeconds) * 3f);
                    yield return null;
                }
                SetLens(vcam, origOrtho, origFov);
                camTaken = false;
                vcam = null;
            }
        }
        finally
        {
            if (useXFollower && camTaken) CameraTargetXFollower.ClearCameraOverride();
            if (vcam != null)
            {
                RestoreFollow(vcam, follow, origFollow, origTracking, origOffset);
                SetLens(vcam, origOrtho, origFov);
            }
            if (frozen) PlayerCutsceneHold.Release();
            if (held && fx != null) fx.SetHold(false);
            _nextGuideTime = Time.time + repeatCooldownSeconds;
            _running = false;
        }
    }

    private void RestoreFollow(CinemachineCamera vcam, CinemachineFollow follow, Transform origFollow, Transform origTracking, Vector3 origOffset)
    {
        if (follow != null) follow.FollowOffset = origOffset;
        var ct = vcam.Target;
        Transform back = origTracking != null ? origTracking : (origFollow != null ? origFollow : (_pm != null ? _pm.transform : null));
        ct.TrackingTarget = back;
        vcam.Target = ct;
        vcam.Follow = origFollow != null ? origFollow : back;
    }

    private static void LerpLens(CinemachineCamera vcam, float orthoTarget, float fovTarget, float t)
    {
        t = Mathf.Clamp01(t);
        var lens = vcam.Lens;
        Camera cam = Camera.main;
        if (cam != null && cam.orthographic) lens.OrthographicSize = Mathf.Lerp(lens.OrthographicSize, Mathf.Max(0.1f, orthoTarget), t);
        else lens.FieldOfView = Mathf.Lerp(lens.FieldOfView, Mathf.Max(1f, fovTarget), t);
        vcam.Lens = lens;
    }

    private static void SetLens(CinemachineCamera vcam, float ortho, float fov)
    {
        var lens = vcam.Lens;
        lens.OrthographicSize = ortho;
        lens.FieldOfView = fov;
        vcam.Lens = lens;
    }
}
