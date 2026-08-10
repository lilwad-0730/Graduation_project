using UnityEngine;

/// <summary>
/// 廢墟背景整體父容器視差控制器 (Parent Group Parallax Controller)
/// 1. 自動建立統一父物件 (Parent Group)，將所有 RuinedBackground 物件打包為子物件。
///    子圖片之間的相對位置 100% 絕對鎖死，絕不可能動到單張圖，接縫永遠完美！
/// 2. 只有當主角真正進入/觸碰到 RuinedBackground 區域時，才啟動視差滾動！
///    在天空或掉落通道時，廢墟背景靜止不動。
/// </summary>
public class ParallaxGroup : MonoBehaviour
{
    [Header("標籤設定")]
    public string targetTag = "RuinedBackground";

    [Header("視差強度 (僅在主角進入廢墟區域後啟動)")]
    [Tooltip("X 軸視差比例 (0.15 最佳，整體移動不拉扯)")]
    public float parallaxFactorX = 0.15f;
    public float parallaxFactorY = 0.02f;

    [Header("觀察用狀態")]
    [Tooltip("主角目前是否處於 RuinedBackground 區域內？")]
    public bool isPlayerInRuinedZone = false;

    private Transform _parallaxParentTransform;
    private Transform _camTransform;
    private Vector3 _lastCamPos;
    private Transform _playerTransform;

    void Start()
    {
        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            _camTransform = mainCam.transform;
            _lastCamPos = _camTransform.position;
        }

        FindPlayer();
        CreateParentAndGroupChildren();
    }

    void FindPlayer()
    {
        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj == null) playerObj = GameObject.Find("Player");
        if (playerObj != null) _playerTransform = playerObj.transform;
    }

    /// <summary>
    /// 自動建立父物件，將場景中所有 RuinedBackground 強制放入父物件中。
    /// 確保子物件相對位置 100% 絕對鎖死！
    /// </summary>
    void CreateParentAndGroupChildren()
    {
        GameObject[] bgs = GameObject.FindGameObjectsWithTag(targetTag);
        if (bgs == null || bgs.Length == 0) return;

        // 建立統一父物件
        GameObject parentObj = new GameObject("RuinedBackground_UnifiedParent");
        _parallaxParentTransform = parentObj.transform;

        // 計算所有背景的幾何中心點，將父物件設在中心
        Vector3 center = Vector3.zero;
        foreach (GameObject bg in bgs)
        {
            center += bg.transform.position;
        }
        center /= bgs.Length;
        _parallaxParentTransform.position = center;

        // 將所有子圖 SetParent 至父物件 (保持世界座標 position 不變)
        foreach (GameObject bg in bgs)
        {
            if (bg != null)
            {
                bg.transform.SetParent(_parallaxParentTransform, true);
            }
        }

        Debug.Log($"[ParallaxGroup] 成功將 {bgs.Length} 張 RuinedBackground 打包進統一父物件！子圖片相對位置已 100% 鎖死。");
    }

    void LateUpdate()
    {
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

        if (_parallaxParentTransform == null) return;
        if (_playerTransform == null) FindPlayer();

        // 判斷主角是否已經掉落進入廢墟區域範圍 (Y <= -85 且 X 進入廢墟區)
        if (_playerTransform != null)
        {
            // 廢墟區域判斷：Y 軸小於 -85 單位代表主角已掉落進入廢墟層
            isPlayerInRuinedZone = (_playerTransform.position.y <= -85f);
        }

        Vector3 camDelta = _camTransform.position - _lastCamPos;

        // ★★★ 核心邏輯 1：只有主角真正進入廢墟區域 (isPlayerInRuinedZone == true) 才啟動視差！
        if (isPlayerInRuinedZone)
        {
            // ★★★ 核心邏輯 2：只移動父物件 _parallaxParentTransform！
            // 底下所有 RuinedBackground 子圖片的相對位置一毫米都不會變，永遠是一整塊大地圖整體移動！
            Vector3 offset = new Vector3(camDelta.x * parallaxFactorX, camDelta.y * parallaxFactorY, 0f);
            _parallaxParentTransform.position += offset;
        }

        _lastCamPos = _camTransform.position;
    }
}
