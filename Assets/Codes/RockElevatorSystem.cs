using UnityEngine;
using System.Collections;

/// <summary>
/// 掛載於壓板（核心壓板）上的觸發偵測器。
/// </summary>
public class PressurePlateTrigger : MonoBehaviour
{
    public System.Action<GameObject, bool> OnRockStatusChanged;
    
    [Header("壓板偵測設定")]
    [Tooltip("能觸發壓板的物件 Tag。")]
    public string targetTag = "RollingRock";

    private int objectsOnPlateCount = 0;

    private void OnTriggerEnter(Collider other)
    {
        if (IsTarget(other.gameObject))
        {
            objectsOnPlateCount++;
            OnRockStatusChanged?.Invoke(other.gameObject, true);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (IsTarget(other.gameObject))
        {
            objectsOnPlateCount--;
            if (objectsOnPlateCount <= 0)
            {
                objectsOnPlateCount = 0;
                OnRockStatusChanged?.Invoke(other.gameObject, false);
            }
        }
    }

    private bool IsTarget(GameObject go)
    {
        if (!string.IsNullOrEmpty(targetTag) && go.CompareTag(targetTag)) return true;
        if (go.GetComponent<RollingRockVisual>() != null || go.name.ToLower().Contains("rock")) return true;
        return false;
    }

    public void ResetTrigger()
    {
        objectsOnPlateCount = 0;
    }
}

/// <summary>
/// 掛載於垂直升降電梯平台上。
/// 管理壓板啟動上升、與石同行登頂偵測以及失敗時的角色重生和關卡重置。
/// </summary>
public class RockElevatorSystem : MonoBehaviour, IResettable
{
    [Header("機關元件關聯")]
    [Tooltip("壓板觸發器。請將掛載了 PressurePlateTrigger 的物件拖曳到這裡。")]
    public PressurePlateTrigger pressurePlate;

    [Tooltip("滾動巨石物件。用來於重挑戰時恢復位置與狀態。")]
    public GameObject rollingRock;

    [Header("移動與高度設定")]
    [Tooltip("上升目標的高度偏移（從初始位置加上這個高度）。")]
    public float riseHeight = 10f;

    [Tooltip("電梯上升速度")]
    public float riseSpeed = 2f;

    [Tooltip("是否在電梯上升期間將玩家設為電梯子物件，避免玩家物理滑落。預設為 true。")]
    public bool parentPlayerOnRide = true;

    [Header("玩家偵測設定")]
    [Tooltip("【選填】電梯平台上方的玩家偵測區域（IsTrigger）。若留空，電梯會自動使用平台本身的 Trigger 事件進行判定。")]
    public Collider playerDetectionCollider;

    // 內部狀態
    private Vector3 platformInitialPos;
    private Vector3 rockInitialPos;
    private Quaternion rockInitialRot;
    private Rigidbody rockRigidbody;

    private bool isMoving = false;
    private bool hasReachedTop = false;
    private bool isPlayerOnPlatform = false;
    private float targetY;

    private Transform playerTransform;
    private PlayerRespawnSystem playerRespawn;

    private void Awake()
    {
        // 記錄初始位置以供 IResettable 重置
        platformInitialPos = transform.position;
        targetY = platformInitialPos.y + riseHeight;

        if (rollingRock != null)
        {
            rockInitialPos = rollingRock.transform.position;
            rockInitialRot = rollingRock.transform.rotation;
            rockRigidbody = rollingRock.GetComponent<Rigidbody>();
        }
    }

    private void Start()
    {
        // 尋找玩家
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            playerTransform = playerObj.transform;
            playerRespawn = playerObj.GetComponent<PlayerRespawnSystem>();
        }

        // 訂閱壓板觸發事件
        if (pressurePlate != null)
        {
            pressurePlate.OnRockStatusChanged += HandleRockStatusChanged;
        }
        else
        {
            Debug.LogWarning($"[RockElevatorSystem] '{gameObject.name}' 未指定壓板觸發器 (PressurePlateTrigger)！");
        }
    }

    private void OnDestroy()
    {
        if (pressurePlate != null)
        {
            pressurePlate.OnRockStatusChanged -= HandleRockStatusChanged;
        }
    }

    private void HandleRockStatusChanged(GameObject rock, bool isOnPlate)
    {
        if (isOnPlate && !hasReachedTop)
        {
            isMoving = true;
            Debug.Log("[RockElevatorSystem] 巨石已觸碰壓板，電梯開始上升！");
        }
    }

    private void FixedUpdate()
    {
        if (isMoving)
        {
            Vector3 currentPos = transform.position;
            if (currentPos.y < targetY)
            {
                // 平滑上升
                float nextY = Mathf.MoveTowards(currentPos.y, targetY, riseSpeed * Time.fixedDeltaTime);
                
                Rigidbody rb = GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.MovePosition(new Vector3(currentPos.x, nextY, currentPos.z));
                }
                else
                {
                    transform.position = new Vector3(currentPos.x, nextY, currentPos.z);
                }

                // 檢查玩家是否在平台上
                CheckPlayerOnPlatform();
            }
            else
            {
                // 到達頂部
                isMoving = false;
                hasReachedTop = true;
                Debug.Log("[RockElevatorSystem] 電梯已到達頂端！");

                // 解除玩家的父子關係 (如果之前有 Parent 起來的話)
                if (parentPlayerOnRide && playerTransform != null && playerTransform.parent == transform)
                {
                    playerTransform.SetParent(null);
                }

                // 最終判定：若玩家不在電梯上，則觸發重生與區域重置
                FinalCheckAndRespawn();
            }
        }
    }

    private void CheckPlayerOnPlatform()
    {
        if (playerDetectionCollider == null) return;
        if (playerTransform == null) return;

        // 檢查玩家的 Collider 是否與偵測範圍相交
        Collider playerCol = playerTransform.GetComponent<Collider>();
        if (playerCol != null)
        {
            isPlayerOnPlatform = playerDetectionCollider.bounds.Intersects(playerCol.bounds);
        }

        // 如果在上升期間且設定了 parentPlayerOnRide，將玩家設為電梯的子物件避免滑落
        if (parentPlayerOnRide)
        {
            if (isPlayerOnPlatform)
            {
                if (playerTransform.parent != transform)
                {
                    playerTransform.SetParent(transform);
                    Debug.Log("[RockElevatorSystem] 玩家已站在上升電梯上，綁定父子關係。");
                }
            }
            else
            {
                if (playerTransform.parent == transform)
                {
                    playerTransform.SetParent(null);
                    Debug.Log("[RockElevatorSystem] 玩家離開上升電梯，解除父子關係。");
                }
            }
        }
    }

    // --- 備用 Trigger 偵測方案 (當未設定獨立 playerDetectionCollider 時自動觸發) ---
    private void OnTriggerEnter(Collider other)
    {
        if (playerDetectionCollider == null && other.CompareTag("Player"))
        {
            isPlayerOnPlatform = true;
            if (parentPlayerOnRide && isMoving)
            {
                other.transform.SetParent(transform);
                Debug.Log("[RockElevatorSystem] (觸發器模式) 玩家已站在上升電梯上，綁定父子關係。");
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (playerDetectionCollider == null && other.CompareTag("Player"))
        {
            isPlayerOnPlatform = false;
            if (other.transform.parent == transform)
            {
                other.transform.SetParent(null);
                Debug.Log("[RockElevatorSystem] (觸發器模式) 玩家離開上升電梯，解除父子關係。");
            }
        }
    }

    private void FinalCheckAndRespawn()
    {
        // 如果有指定偵測範圍，再強制判定一次確保數據最新
        if (playerDetectionCollider != null)
        {
            CheckPlayerOnPlatform();
        }

        if (!isPlayerOnPlatform)
        {
            Debug.LogWarning("[RockElevatorSystem] 失敗！電梯已登頂但玩家未站在電梯上！觸發重生與關卡重置！");

            // 觸發玩家重生
            if (playerRespawn != null)
            {
                playerRespawn.TriggerRespawn();
            }

            // 重置當前 GameArea
            if (AreaManager.Instance != null && AreaManager.Instance.currentArea != null)
            {
                // 延遲一點點執行重置，確保是在漸黑轉場遮擋期間才完成關卡刷新
                StartCoroutine(DelayedResetArea(AreaManager.Instance.currentArea));
            }
            else
            {
                // 若無管理器則直接重置本機關
                ResetToInitialState();
            }
        }
        else
        {
            Debug.Log("[RockElevatorSystem] 成功！玩家與巨石順利登頂！");
        }
    }

    private IEnumerator DelayedResetArea(GameArea area)
    {
        // 稍微等待 0.5 秒，這時候正好是 Respawn 漸黑轉場最黑的時期
        yield return new WaitForSeconds(0.5f);
        area.ResetArea();
    }

    // --- IResettable 介面實作 ---
    public void ResetToInitialState()
    {
        StopAllCoroutines();

        // 1. 還原電梯狀態與位置
        isMoving = false;
        hasReachedTop = false;
        isPlayerOnPlatform = false;
        
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.position = platformInitialPos;
        }
        transform.position = platformInitialPos;

        // 解除可能殘留的玩家父子關係
        if (playerTransform != null && playerTransform.parent == transform)
        {
            playerTransform.SetParent(null);
        }

        // 2. 還原巨石狀態與位置
        if (rollingRock != null)
        {
            if (rockRigidbody != null)
            {
                rockRigidbody.linearVelocity = Vector3.zero;
                rockRigidbody.angularVelocity = Vector3.zero;
                rockRigidbody.position = rockInitialPos;
                rockRigidbody.rotation = rockInitialRot;
            }
            rollingRock.transform.position = rockInitialPos;
            rollingRock.transform.rotation = rockInitialRot;
        }

        // 3. 重置壓板狀態
        if (pressurePlate != null)
        {
            pressurePlate.ResetTrigger();
        }

        Debug.Log($"[RockElevatorSystem] 機關 '{gameObject.name}' 與巨石已成功重置為初始狀態！");
    }
}
