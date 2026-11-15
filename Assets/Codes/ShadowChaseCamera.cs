using UnityEngine;
using Unity.Cinemachine;

/// <summary>
/// ★1116 玻璃館追逐鏡頭：讓玩家「看見」怪物。
///
/// Root Cause：怪物約 45 單位高，鏡頭只看得到約 27 單位，牠又跟在她身後 16～26 公尺、常在畫面邊緣外，
///   壓迫感只剩畫面一側變暗，看不到是什麼在追她。
/// 做法（只在追逐中，由 ShadowMonsterController 自動掛上）：
///   ・怪物在中距離時，鏡頭往怪物那一側偏 leanMax 公尺，把牠帶進畫面；
///   ・怪物越近鏡頭越往外拉（正交大小 + zoomMax），牠的身影壓進畫面。
///   追逐結束（被抓、通關、重生）慢慢回到原本。登場運鏡、懲罰鎖鏡頭時不碰。
/// 用「疊加」的方式改 CinemachineFollow 的 X 偏移與鏡頭大小（只加減自己加上去的量），其他腳本自己的設定不受影響。
/// </summary>
[DefaultExecutionOrder(-50)]
public class ShadowChaseCamera : MonoBehaviour
{
    public ShadowMonsterController ctrl;

    [Tooltip("鏡頭最多往怪物那邊偏幾公尺")]
    public float leanMax = 4f;
    [Tooltip("怪物離她這麼遠以外不偏（公尺）")]
    public float leanFarDistance = 34f;
    [Tooltip("怪物離她這麼近時偏到最多（公尺）")]
    public float leanNearDistance = 16f;

    [Tooltip("鏡頭最多往外拉多少（正交大小）")]
    public float zoomMax = 2.5f;
    [Tooltip("怪物離她這麼遠以外不拉（公尺）")]
    public float zoomFarDistance = 22f;
    [Tooltip("怪物離她這麼近時拉到最多（公尺）")]
    public float zoomNearDistance = 11f;

    [Tooltip("鏡頭跟上的速度（越大越快）")]
    public float smooth = 2f;

    private CinemachineCamera _vcam;
    private CinemachineFollow _follow;
    private float _lean, _zoom;                 // 想要的量（平滑後）
    private float _appliedLean, _appliedZoom;   // 已經加在鏡頭上的量

    private void LateUpdate()
    {
        if (ctrl == null) ctrl = GetComponent<ShadowMonsterController>();
        if (ctrl == null) return;

        int mode = ctrl.ChaseFramingMode;
        if (mode < 0) return;   // 登場運鏡／懲罰鎖鏡頭：別碰（它們自己存、自己還原）

        float wantLean = 0f, wantZoom = 0f;
        if (mode > 0)
        {
            float d = ctrl.ChaseDistance;
            wantLean = ctrl.MonsterSide * leanMax * Smooth01(Mathf.InverseLerp(leanFarDistance, leanNearDistance, d));
            wantZoom = zoomMax * Smooth01(Mathf.InverseLerp(zoomFarDistance, zoomNearDistance, d));
        }
        float k = 1f - Mathf.Exp(-Mathf.Max(0.1f, smooth) * Time.deltaTime);
        _lean = Mathf.Lerp(_lean, wantLean, k);
        _zoom = Mathf.Lerp(_zoom, wantZoom, k);
        if (mode == 0 && Mathf.Abs(_lean) < 0.01f && Mathf.Abs(_zoom) < 0.01f) { _lean = 0f; _zoom = 0f; }

        CinemachineCamera v = FindActiveVcam();
        if (v != _vcam)
        {
            Unapply();   // 換了鏡頭：把加在舊鏡頭上的量拿掉
            _vcam = v;
            _follow = v != null ? v.GetComponent<CinemachineFollow>() : null;
        }
        Apply();
    }

    private static float Smooth01(float t) => t * t * (3f - 2f * t);

    private CinemachineCamera FindActiveVcam()
    {
        Camera cam = Camera.main;
        CinemachineBrain brain = cam != null ? cam.GetComponent<CinemachineBrain>() : null;
        if (brain != null && brain.ActiveVirtualCamera is CinemachineCamera active) return active;
        return _vcam != null && _vcam.isActiveAndEnabled ? _vcam : null;
    }

    private void Apply()
    {
        if (_vcam == null) return;
        if (_follow != null && Mathf.Abs(_lean - _appliedLean) > 0.0001f)
        {
            Vector3 fo = _follow.FollowOffset;
            fo.x += _lean - _appliedLean;
            _follow.FollowOffset = fo;
            _appliedLean = _lean;
        }
        if (Mathf.Abs(_zoom - _appliedZoom) > 0.0001f)
        {
            LensSettings lens = _vcam.Lens;
            lens.OrthographicSize = Mathf.Max(0.1f, lens.OrthographicSize + _zoom - _appliedZoom);
            _vcam.Lens = lens;
            _appliedZoom = _zoom;
        }
    }

    private void Unapply()
    {
        if (_vcam != null)
        {
            if (_follow != null && Mathf.Abs(_appliedLean) > 0.0001f)
            {
                Vector3 fo = _follow.FollowOffset;
                fo.x -= _appliedLean;
                _follow.FollowOffset = fo;
            }
            if (Mathf.Abs(_appliedZoom) > 0.0001f)
            {
                LensSettings lens = _vcam.Lens;
                lens.OrthographicSize = Mathf.Max(0.1f, lens.OrthographicSize - _appliedZoom);
                _vcam.Lens = lens;
            }
        }
        _appliedLean = _appliedZoom = 0f;
    }

    private void OnDisable()
    {
        Unapply();
        _lean = _zoom = 0f;
    }
}
