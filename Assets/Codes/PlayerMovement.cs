using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class PlayerMovement : MonoBehaviour
{
    [Header("基本移動設定")]
    public float baseSpeed = 5f;       
    public float pullRange = 2f;
    
    private Rigidbody rb;
    private GameObject pulledObject;
    private Vector3 facingDirection = Vector3.right; 

    private Animator animator;
    private Collider playerCollider;

    [Header("狼群減速狀態 (可調整)")]
    [Tooltip("幾隻狼能讓玩家完全停下？(建議設低一點才明顯)")]
    public float maxWolvesToStop = 3f; // 【修改】改成 3 隻就完全停下，效果會超明顯！
    
    [Header("觀察用 (不要手動改)")]
    public int attachedWolvesCount = 0; 
    public float currentSpeed;          
    [HideInInspector] public bool freezeHorizontal = false; // 【新增】當掉落特定背景時鎖死橫向移動

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        playerCollider = GetComponent<Collider>();
        
        // 【新增】強化物理設定，避免被狼撞飛或穿模
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        rb.constraints = RigidbodyConstraints.FreezeRotation; // 鎖定旋轉，永遠不會跌倒
        rb.mass = 10f; // 增加玩家質量，才不會被輕易推動

        animator = GetComponentInChildren<Animator>(); // 自動往下抓取子物件(皮套)身上的 Animator
        
        // 強制重置所有狀態，避免卡死
        freezeHorizontal = false; 
        attachedWolvesCount = 0;
        currentSpeed = baseSpeed;
    }

    void Update()
    {
        // 【修改】如果碰到 FallingBackground，強制將水平輸入歸零
        float moveInput = freezeHorizontal ? 0f : Input.GetAxis("Horizontal"); 
        if (moveInput > 0.1f) facingDirection = Vector3.right;
        if (moveInput < -0.1f) facingDirection = Vector3.left;

        // ==========================================
        // 角色轉向與動畫控制
        // ==========================================
        if (animator != null)
        {
            // 讓皮套模型永遠面朝玩家當前的移動方向
            animator.transform.rotation = Quaternion.LookRotation(facingDirection);
            // 將玩家按下左右鍵的數值 (0 到 1) 傳給 Animator
            animator.SetFloat("Speed", Mathf.Abs(moveInput));
        }

        if (Input.GetKeyDown(KeyCode.LeftShift)) TryGrabObject();
        if (Input.GetKeyUp(KeyCode.LeftShift)) ReleaseObject();

        float finalSpeed = (pulledObject != null) ? currentSpeed / 2f : currentSpeed;
        rb.linearVelocity = new Vector3(moveInput * finalSpeed, rb.linearVelocity.y, rb.linearVelocity.z);

        // 【修改】跳躍邏輯：改用動態射線偵測地板，無論 Pivot 在哪都能完美運作
        // 從碰撞器中心向下發射射線，距離剛好是碰撞器的一半高度再加上 0.1 的寬容值
        bool isGrounded = false;
        if (playerCollider != null)
        {
            // 改用 RaycastAll 來取得所有被射線打到的東西，並且忽略 Trigger
            RaycastHit[] hits = Physics.RaycastAll(playerCollider.bounds.center, Vector3.down, playerCollider.bounds.extents.y + 0.1f, ~0, QueryTriggerInteraction.Ignore);
            foreach (var hit in hits)
            {
                // 最關鍵的防呆：如果打到的東西「不是」玩家自己，也「不是」掛在玩家身上的皮套或狼
                if (hit.collider.transform.root != this.transform.root)
                {
                    isGrounded = true;
                    break; // 只要踩到任何真正的外在物件，就算是在地上
                }
            }
        }
        else
        {
            isGrounded = Physics.Raycast(transform.position + Vector3.up * 0.5f, Vector3.down, 0.7f, ~0, QueryTriggerInteraction.Ignore); // 備案
        }

        // 【新增】將掉落狀態傳送給 Animator
        if (animator != null)
        {
            // 當不在地面上，且速度是往下掉的，就判定為 Falling
            bool isFalling = !isGrounded && rb.linearVelocity.y < -0.1f;
            animator.SetBool("IsFalling", isFalling);
        }
        
        if (Input.GetKeyDown(KeyCode.Space) && isGrounded)
        {
            // 每次跳躍前先消除往下的掉落速度，確保跳躍高度一致
            rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0, rb.linearVelocity.z);
            // 改用 VelocityChange，無視質量 (mass = 10) 也能跳得一樣高
            rb.AddForce(Vector3.up * 5f, ForceMode.VelocityChange);
        }
    }

    // ==========================================
    // 狼群減速邏輯
    // ==========================================
    public void AddWolf()
    {
        attachedWolvesCount++;
        CalculateSpeed();
        Debug.Log($"狼咬！目前身上有 {attachedWolvesCount} 隻狼，玩家速度降為：{currentSpeed}");
    }

    public void RemoveWolf()
    {
        attachedWolvesCount--;
        if (attachedWolvesCount < 0) attachedWolvesCount = 0; 
        CalculateSpeed();
        Debug.Log($"狼鬆口！目前身上有 {attachedWolvesCount} 隻狼，玩家速度恢復為：{currentSpeed}");
    }

    private void CalculateSpeed()
    {
        float speedPenaltyPerWolf = baseSpeed / maxWolvesToStop;
        float newSpeed = baseSpeed - (speedPenaltyPerWolf * attachedWolvesCount);
        currentSpeed = Mathf.Max(0f, newSpeed); // 最低就是 0，不會倒退
    }

    // ==========================================
    // 抓取物件邏輯 (維持不變)
    // ==========================================
    void TryGrabObject()
    {
        Debug.DrawRay(transform.position, facingDirection * pullRange, Color.red, 2f);
        RaycastHit[] hits = Physics.RaycastAll(transform.position, facingDirection, pullRange);
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider.gameObject == this.gameObject) continue;
            if (hit.collider.CompareTag("Pushable"))
            {
                pulledObject = hit.collider.gameObject;
                pulledObject.transform.SetParent(this.transform);
                pulledObject.GetComponent<Rigidbody>().isKinematic = true;
                return; 
            }
        }
    }

    void ReleaseObject()
    {
        if (pulledObject != null)
        {
            pulledObject.transform.SetParent(null);
            pulledObject.GetComponent<Rigidbody>().isKinematic = false;
            pulledObject = null;
        }
    }

    // ==========================================
    // 觸發特定背景邏輯 (FallingBackground)
    // ==========================================
    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("FallingBackground"))
        {
            Debug.Log("碰觸到 FallingBackground！鎖死橫向移動，準備掉落！");
            freezeHorizontal = true;

            // 1. 關閉重生系統，避免掉落出畫面後被傳送回去
            PlayerRespawnSystem respawnSystem = GetComponent<PlayerRespawnSystem>();
            if (respawnSystem != null)
            {
                respawnSystem.enabled = false;
            }
        }
        else if (other.CompareTag("RuinedBackground"))
        {
            Debug.Log("碰觸到 RuinedBackground！解除鎖定，恢復所有機能！");
            freezeHorizontal = false;

            // 1. 重新啟動重生系統
            PlayerRespawnSystem respawnSystem = GetComponent<PlayerRespawnSystem>();
            if (respawnSystem != null)
            {
                respawnSystem.enabled = true;
            }

            // 2. 重新讓攝影機跟隨玩家
            Unity.Cinemachine.CinemachineVirtualCamera vcam = Object.FindAnyObjectByType<Unity.Cinemachine.CinemachineVirtualCamera>();
            if (vcam != null)
            {
                vcam.Follow = this.transform;
            }
        }
    }
}