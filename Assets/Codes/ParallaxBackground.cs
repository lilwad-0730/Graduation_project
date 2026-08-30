using UnityEngine;

/// <summary>
/// 標準 2D 視差背景控制器 (Standard 2D Parallax Controller)
/// 採用絕對位移公式：當前位置 = 初始位置 + 攝影機總位移 * 視差係數。
/// 杜絕每幀增量漂移與開局跳轉，且在 Y 軸視差為 0 時嚴格維持原本的 Y 軸高度。
/// </summary>
[DefaultExecutionOrder(10000)]
public class ParallaxBackground : MonoBehaviour
{
    [Header("視差設定")]
    [Tooltip("是否開啟視差跟隨效果")]
    public bool enableParallax = true;

    [Tooltip("X 軸視差比例 (例如 0.35)")]
    public float parallaxFactorX = 0.35f;

    [Tooltip("Y 軸視差比例 (預設 0，不跟隨攝影機垂直移動)")]
    public float parallaxFactorY = 0f;

    private Vector3 _startPosition;
    private Transform _camTransform;
    private Vector3 _startCamPos;
    private bool _initialized = false;

    private void Start()
    {
        _startPosition = transform.position;
        _initialized = false;
    }

    private void Init()
    {
        _startPosition = transform.position;
        _initialized = true;

        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            _camTransform = mainCam.transform;
            _startCamPos = _camTransform.position;
        }
    }

    private void LateUpdate()
    {
        if (!_initialized)
        {
            Init();
            return;
        }

        if (!enableParallax) return;

        if (_camTransform == null)
        {
            Camera mainCam = Camera.main;
            if (mainCam != null)
            {
                _camTransform = mainCam.transform;
                _startCamPos = _camTransform.position;
            }
            return;
        }

        // 標準絕對視差公式：當前世界位置 = 初始世界位置 + (攝影機當前總位移 * 視差係數)
        Vector3 camTotalDelta = _camTransform.position - _startCamPos;

        Vector3 targetPos = transform.position;
        if (parallaxFactorX != 0f)
        {
            targetPos.x = _startPosition.x + (camTotalDelta.x * parallaxFactorX);
        }

        if (parallaxFactorY != 0f)
        {
            targetPos.y = _startPosition.y + (camTotalDelta.y * parallaxFactorY);
        }
        else
        {
            // 若 Y 軸視差為 0，維持物件本身原本的 Y 軸高度，絕不下沉！
            targetPos.y = _startPosition.y;
        }

        transform.position = targetPos;
    }
}

