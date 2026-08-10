using UnityEngine;

/// <summary>
/// 2D/2.5D 視差背景滾動腳本 (Parallax Background)
/// 掛載於廢墟或遠景背景物件，根據攝影機移動產生深度的視覺差效果。
/// </summary>
public class ParallaxBackground : MonoBehaviour
{
    [Header("視差強度設定")]
    [Tooltip("X 軸視差比例 (0 = 固定在螢幕上, 1 = 跟隨世界不動, 0.3 = 輕微移動呈現遠景感, -0.2 = 反向移動製造視覺差)")]
    public float parallaxFactorX = 0.2f;

    [Tooltip("Y 軸視差比例 (建議設為小數值如 0.05，避免上下差距過大導致露空)")]
    public float parallaxFactorY = 0.05f;

    [Tooltip("是否跟隨主角/攝影機在 X 軸微幅移動？")]
    public bool enableParallax = true;

    private Transform _camTransform;
    private Vector3 _lastCamPos;

    void Start()
    {
        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            _camTransform = mainCam.transform;
            _lastCamPos = _camTransform.position;
        }
    }

    void LateUpdate()
    {
        if (!enableParallax) return;

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

        // 計算攝影機上一幀到這一幀的位移量 (Delta Position)
        Vector3 camDelta = _camTransform.position - _lastCamPos;

        // 根據視差比例過渡背景 position
        Vector3 newPos = transform.position;
        newPos.x += camDelta.x * parallaxFactorX;
        newPos.y += camDelta.y * parallaxFactorY;

        transform.position = newPos;
        _lastCamPos = _camTransform.position;
    }
}
