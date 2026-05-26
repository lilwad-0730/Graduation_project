using UnityEngine;
using Unity.Cinemachine;

public class StairsCameraZoom : MonoBehaviour
{
    [Header("攝影機設定")]
    [Tooltip("每次踏上階梯時，鏡頭外拉（視野變大/縮小望遠）的比例 (1.05 代表放大 5%)")]
    public float zoomOutFactor = 1.05f;

    [Tooltip("最大可以疊加外拉的次數，避免無限外拉導致主角看不見 (設為 0 代表無限制)")]
    public int maxZoomLayers = 5;

    [Tooltip("離開階梯後，需要等待多久才開始偵測掉落並還原 (秒)")]
    public float restoreDelay = 0.5f;

    [Tooltip("向下掉落的速度閾值，低於此值判定為掉落 (預設 -1.5)")]
    public float fallThreshold = -1.5f;

    [Tooltip("手動設定原始鏡頭大小。如果您的鏡頭大小因 Keep 被修改保存了，可以在此處填入您的初始大小（如 6 或 60）來強制還原。設為 0 代表自動讀取。")]
    public float overrideOriginalSize = 0f;

    [Header("平滑過渡設定")]
    [Tooltip("鏡頭縮放變化的平滑速度")]
    public float zoomLerpSpeed = 5f;

    [Header("階梯偵測設定 (自動計算，亦可手動微調)")]
    [Tooltip("向下偵測階梯的長度（建議設為角色半高加上 0.2）")]
    public float detectDistance = 1.2f;

    [Tooltip("向下偵測階梯的左右寬度（BoxCast 的寬度，設為 0 則使用單一射線）")]
    public float detectWidth = 0.5f;

    [Tooltip("偵測所使用的 LayerMask (預設為全部)")]
    public LayerMask stairLayerMask = ~0;

    [Header("疊加模式設定")]
    [Tooltip("如果為 true，每次踩到不同的 Stairs 物件時疊加；如果為 false，則改為在階梯上每向上/下移動一定高度時自動疊加（適合一整塊大樓梯）。")]
    public bool stackByGameObject = true;

    [Tooltip("當 stackByGameObject 為 false 時，角色每在階梯上上升/下降多少高度 (Y 軸距離) 就疊加一次外拉")]
    public float heightStepForStack = 1.0f;

    private CinemachineCamera _vcam3;
    private CinemachineVirtualCamera _vcamLegacy;
    private Rigidbody _rigidbody;

    // 原始設定值
    private float _originalLensSize;
    private bool _isOrthographic;
    private float _targetLensSize;

    // 狀態變數
    private int _currentZoomLayers = 0;
    private GameObject _lastStairsObject;
    private float _exitStairsTime;
    private bool _onStairs = false;
    private bool _waitingToRestore = false;
    private float _stairStartHeight;
    private float _lastStairTopY;

    void Start()
    {
        _rigidbody = GetComponent<Rigidbody>();
        if (_rigidbody == null)
        {
            _rigidbody = GetComponentInParent<Rigidbody>();
        }

        // 初始記錄
        _lastStairTopY = transform.position.y;

        // 自動根據角色的 Collider 動態計算最適合的偵測距離與寬度
        Collider col = GetComponent<Collider>();
        if (col == null)
        {
            col = GetComponentInChildren<Collider>();
        }

        if (col != null)
        {
            // 偵測長度 = 半高 + 0.2f 緩衝（確保腳底接觸地面時能精確偵測到）
            detectDistance = col.bounds.extents.y + 0.2f;
            // 偵測寬度 = 角色寬度的 80%（避免邊緣懸空時失去偵測，同時防止擦邊誤判）
            detectWidth = col.bounds.extents.x * 1.6f; 
        }

        // 尋找場景中的 Cinemachine 虛擬攝影機
        FindActiveCamera();
    }

    private void FindActiveCamera()
    {
        // 優先尋找新版的 CinemachineCamera
        _vcam3 = Object.FindAnyObjectByType<CinemachineCamera>();
        
        // 備用尋找舊版的 CinemachineVirtualCamera
        if (_vcam3 == null)
        {
            _vcamLegacy = Object.FindAnyObjectByType<CinemachineVirtualCamera>();
        }

        if (_vcam3 != null || _vcamLegacy != null)
        {
            _isOrthographic = Camera.main != null ? Camera.main.orthographic : true;
            
            if (overrideOriginalSize > 0f)
            {
                _originalLensSize = overrideOriginalSize;
            }
            else
            {
                if (_isOrthographic)
                {
                    _originalLensSize = _vcam3 != null ? _vcam3.Lens.OrthographicSize : _vcamLegacy.m_Lens.OrthographicSize;
                }
                else
                {
                    _originalLensSize = _vcam3 != null ? _vcam3.Lens.FieldOfView : _vcamLegacy.m_Lens.FieldOfView;
                }
            }
            
            _targetLensSize = _originalLensSize;
            string camName = _vcam3 != null ? _vcam3.name : _vcamLegacy.name;
            string camType = _vcam3 != null ? "CinemachineCamera" : "CinemachineVirtualCamera";
            Debug.Log($"[StairsCameraZoom] 成功綁定 {camType}: {camName}，原始大小為：{_originalLensSize} (正交模式: {_isOrthographic})");
        }
        else
        {
            Debug.LogWarning("[StairsCameraZoom] 找不到 CinemachineCamera 或 CinemachineVirtualCamera！將於 Update 中持續嘗試尋找。");
        }
    }

    void Update()
    {
        // 確保攝影機存在
        if (_vcam3 == null && _vcamLegacy == null)
        {
            FindActiveCamera();
            if (_vcam3 == null && _vcamLegacy == null) return;
        }

        // 處理鏡頭平滑過渡
        SmoothUpdateCameraLens();

        // 偵測還原條件
        if (_waitingToRestore && !_onStairs)
        {
            float timePassed = Time.time - _exitStairsTime;
            bool isFalling = (_rigidbody != null && _rigidbody.linearVelocity.y < fallThreshold);
            
            // 條件 1：離開階梯時間超過 restoreDelay 且處於向下掉落狀態 (使用者原意)
            if (timePassed >= restoreDelay && isFalling)
            {
                RestoreCamera();
            }
            // 條件 2：離開太久（比如超過 1.5 倍的延遲，代表玩家已經安穩地在平地走動而非掉落），強制還原，避免鏡頭卡死
            else if (timePassed >= restoreDelay * 1.5f)
            {
                RestoreCamera();
            }
        }
    }

    void FixedUpdate()
    {
        // 在 FixedUpdate 中進行物理偵測，確保與物理引擎同步且極度精確
        DetectStairs();
    }

    private void DetectStairs()
    {
        bool hitStair = false;
        GameObject hitStairObj = null;

        Vector3 origin = transform.position;
        // 如果有 Collider，使用 bounds.center 作為偵測起點，避免錨點不均勻
        Collider col = GetComponent<Collider>();
        if (col == null)
        {
            col = GetComponentInChildren<Collider>();
        }
        
        if (col != null)
        {
            origin = col.bounds.center;
        }

        RaycastHit hit;
        if (detectWidth > 0f)
        {
            // 使用 BoxCast 進行寬度偵測，能極其穩定地偵測到台階邊緣，避免微小抖動
            Vector3 halfExtents = new Vector3(detectWidth / 2f, 0.05f, 0.2f);
            if (Physics.BoxCast(origin, halfExtents, Vector3.down, out hit, Quaternion.identity, detectDistance, stairLayerMask, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.CompareTag("Stairs"))
                {
                    hitStair = true;
                    hitStairObj = hit.collider.gameObject;
                }
            }
        }
        else
        {
            // 使用單一向下 Raycast 偵測
            if (Physics.Raycast(origin, Vector3.down, out hit, detectDistance, stairLayerMask, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.CompareTag("Stairs"))
                {
                    hitStair = true;
                    hitStairObj = hit.collider.gameObject;
                }
            }
        }

        // 處理偵測狀態轉換
        if (hitStair)
        {
            _onStairs = true;
            _waitingToRestore = false;

            // 取得被踩到的階梯頂部 Y 座標 (最穩定、不會因為跳躍而在空中變動的參考值)
            float currentStairTopY = hit.collider.bounds.max.y;

            // 精確判定是否為「向上攀爬/跳躍」：
            // 只要新的階梯頂部高於舊的階梯頂部，就是往上爬！
            bool isClimbingUp = currentStairTopY > _lastStairTopY + 0.05f;

            // 如果正在往下跳或往下走（踩到的新階梯比上一次的階梯矮明顯超過 0.2f），主動還原鏡頭
            if (currentStairTopY < _lastStairTopY - 0.2f)
            {
                RestoreCamera();
                return;
            }

            if (isClimbingUp)
            {
                if (stackByGameObject)
                {
                    // 模式 A：踏上新階梯物件時疊加
                    if (hitStairObj != _lastStairsObject)
                    {
                        _lastStairsObject = hitStairObj;
                        _lastStairTopY = currentStairTopY; // 更新階梯高度參考點
                        ApplyZoomOut();
                    }
                }
                else
                {
                    // 模式 B：依上升高度進行疊加
                    if (_lastStairsObject == null)
                    {
                        _lastStairsObject = hitStairObj;
                        _stairStartHeight = currentStairTopY;
                        _lastStairTopY = currentStairTopY; // 更新高度參考點
                        ApplyZoomOut();
                    }
                    else
                    {
                        float heightDiff = currentStairTopY - _stairStartHeight;
                        if (heightDiff > 0f)
                        {
                            int targetLayers = Mathf.FloorToInt(heightDiff / heightStepForStack) + 1;
                            if (targetLayers > _currentZoomLayers)
                            {
                                int layersToAdd = targetLayers - _currentZoomLayers;
                                for (int i = 0; i < layersToAdd; i++)
                                {
                                    ApplyZoomOut();
                                }
                                _lastStairTopY = currentStairTopY; // 更新高度參考點
                            }
                        }
                    }
                }
            }
        }
        else
        {
            // 如果先前在階梯上，現在完全懸空/離開了，觸發離開計時
            if (_onStairs)
            {
                _onStairs = false;
                _waitingToRestore = true;
                _exitStairsTime = Time.time;
                // 注意：這裡不重置 _lastStairsObject，保留至真正還原時重置，這樣在 restoreDelay 期間的快速往上跳就不會被判定為「全新階梯而重複放大」！
                Debug.Log("[StairsCameraZoom] 離開階梯，啟動還原計時器...");
            }
        }
    }

    private void SmoothUpdateCameraLens()
    {
        if (_vcam3 != null)
        {
            var lens = _vcam3.Lens;
            if (_isOrthographic)
            {
                lens.OrthographicSize = Mathf.Lerp(lens.OrthographicSize, _targetLensSize, Time.deltaTime * zoomLerpSpeed);
            }
            else
            {
                lens.FieldOfView = Mathf.Lerp(lens.FieldOfView, _targetLensSize, Time.deltaTime * zoomLerpSpeed);
            }
            _vcam3.Lens = lens;
        }
        else if (_vcamLegacy != null)
        {
            var lens = _vcamLegacy.m_Lens;
            if (_isOrthographic)
            {
                lens.OrthographicSize = Mathf.Lerp(lens.OrthographicSize, _targetLensSize, Time.deltaTime * zoomLerpSpeed);
            }
            else
            {
                lens.FieldOfView = Mathf.Lerp(lens.FieldOfView, _targetLensSize, Time.deltaTime * zoomLerpSpeed);
            }
            _vcamLegacy.m_Lens = lens;
        }
    }

    private void ApplyZoomOut()
    {
        if (maxZoomLayers > 0 && _currentZoomLayers >= maxZoomLayers)
        {
            Debug.Log("[StairsCameraZoom] 已達最大鏡頭疊加次數上限。");
            return;
        }

        _currentZoomLayers++;
        // 疊加外拉鏡頭大小（視野變大 5%）
        _targetLensSize = _originalLensSize * Mathf.Pow(zoomOutFactor, _currentZoomLayers);
        Debug.Log($"[StairsCameraZoom] 踏上新階梯！疊加層數: {_currentZoomLayers}，目標鏡頭大小: {_targetLensSize:F2}");
    }

    private void RestoreCamera()
    {
        _targetLensSize = _originalLensSize;
        _currentZoomLayers = 0;
        _lastStairsObject = null;
        _waitingToRestore = false;
        
        // 重置參考高度，若剛好踩在某物件上則使用物件高度，否則用玩家高度
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            Vector3 origin = col.bounds.center;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, detectDistance, stairLayerMask, QueryTriggerInteraction.Ignore))
            {
                _lastStairTopY = hit.collider.bounds.max.y;
            }
            else
            {
                _lastStairTopY = transform.position.y;
            }
        }
        else
        {
            _lastStairTopY = transform.position.y;
        }

        Debug.Log("[StairsCameraZoom] 還原攝影機大小至原始值！");
    }

    // 繪製 Debug 輔助線，方便開發者在 Scene 視窗中微調偵測範圍
    void OnDrawGizmosSelected()
    {
        Vector3 origin = transform.position;
        Collider col = GetComponent<Collider>();
        if (col == null)
        {
            col = GetComponentInChildren<Collider>();
        }

        if (col != null)
        {
            origin = col.bounds.center;
        }

        Gizmos.color = _onStairs ? Color.green : Color.red;
        if (detectWidth > 0f)
        {
            Gizmos.DrawWireCube(origin + Vector3.down * detectDistance, new Vector3(detectWidth, 0.1f, 0.4f));
            Gizmos.DrawLine(origin, origin + Vector3.down * detectDistance);
        }
        else
        {
            Gizmos.DrawLine(origin, origin + Vector3.down * detectDistance);
        }
    }
}
