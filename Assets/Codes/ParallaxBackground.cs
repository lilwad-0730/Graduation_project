using UnityEngine;

/// <summary>
/// 背景位置硬鎖腳本 (Background Hard Lock)
/// 掛載於每個 RuinedBackground 物件。
/// Start() 時記住當前世界座標，LateUpdate() 每幀強制鎖死回去。
/// 完全不修改父子關係，不移動任何東西，相對位置永遠不變。
/// 若要開啟視差跟隨，請勾選 enableParallax 並設定 factor 數值。
/// </summary>
public class ParallaxBackground : MonoBehaviour
{
    [Header("視差設定 (預設完全靜止)")]
    [Tooltip("false = 硬鎖在初始世界座標，完全靜止不動")]
    public bool enableParallax = false;

    [Tooltip("X 軸視差比例 (enableParallax = true 時有效)")]
    public float parallaxFactorX = 0f;

    [Tooltip("Y 軸視差比例 (enableParallax = true 時有效)")]
    public float parallaxFactorY = 0f;

    private Vector3 _lockedWorldPosition;
    private Transform _camTransform;
    private Vector3 _lastCamPos;
    private bool _initialized = false;

    void Start()
    {
        Init();
    }

    void Init()
    {
        // 記住 Start 時的世界座標，不管 parent 是誰
        _lockedWorldPosition = transform.position;
        _initialized = true;

        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            _camTransform = mainCam.transform;
            _lastCamPos = _camTransform.position;
        }
    }

    void LateUpdate()
    {
        if (!_initialized) { Init(); return; }

        if (!enableParallax)
        {
            // ★ 硬鎖：強制回到初始世界座標，任何外力都無效
            transform.position = _lockedWorldPosition;
            return;
        }

        // 視差跟隨模式
        if (_camTransform == null)
        {
            Camera mainCam = Camera.main;
            if (mainCam != null)
            {
                _camTransform = mainCam.transform;
                _lastCamPos = _camTransform.position;
            }
            return;
        }

        Vector3 camDelta = _camTransform.position - _lastCamPos;
        _lockedWorldPosition.x += camDelta.x * parallaxFactorX;
        _lockedWorldPosition.y += camDelta.y * parallaxFactorY;

        transform.position = _lockedWorldPosition;
        _lastCamPos = _camTransform.position;
    }
}
