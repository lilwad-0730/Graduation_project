using UnityEngine;

/// <summary>
/// 風痕特效攝影機自動跟隨器 (Wind Trails Camera Follower)。
/// 使 Stylized WindTrails 特效與氣流系統始終緊跟玩家視野攝影機，
/// 確保無論玩家在沙漠關卡中向左、向右移動，風痕流線皆能在視窗中央完美吹拂！
/// </summary>
public class WindTrailsCameraFollower : MonoBehaviour
{
    [Header("跟隨目標攝影機")]
    public Transform targetCamera;

    [Header("相對攝影機偏置座標")]
    public Vector3 offset = new Vector3(0f, 0f, 9.5f);

    private void Start()
    {
        if (targetCamera == null && Camera.main != null)
        {
            targetCamera = Camera.main.transform;
        }
    }

    private void LateUpdate()
    {
        if (targetCamera == null)
        {
            if (Camera.main != null) targetCamera = Camera.main.transform;
            else return;
        }

        // 保持跟隨攝影機水平與垂直座標，確保風痕始終出現在畫面視窗中
        transform.position = targetCamera.position + offset;
    }
}
