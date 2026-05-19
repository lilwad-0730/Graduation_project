using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class PlayerMovement : MonoBehaviour
{
    [Header("基本移動設定")]
    public float baseSpeed = 5f;       
    public float pullRange = 2f;
    [Tooltip("跳躍高度：數值越大跳越高（預設 5）")]
    public float jumpForce = 5f;
    
    private Rigidbody rb;
    private GameObject pulledObject;
    private Vector3 facingDirection = Vector3.right; 

    [Header("動畫控制")]
    [Tooltip("請直接把有 PlayerAnimator Controller 的模型子物件拖曳到這裡！")]
    public Animator animator; // 改成 public，讓你在 Inspector 手動指定！
    [Tooltip("向下掉落速度超過多少才算 Falling？(預設 -4，數字越負越難觸發掉落動畫)")]
    public float fallVelocityThreshold = -4f;
    private Collider playerCollider;

    [Header("狼群減速狀態 (可調整)")]
    [Tooltip("幾隻狼能讓玩家完全停下？(建議設低一點才明顯)")]
    public float maxWolvesToStop = 3f; // 【修改】改成 3 隻就完全停下，效果會超明顯！
    
    [Header("攝影機與物理優化設定")]
    [Tooltip("是否鎖定攝影機的 Y 軸高度")]
    public bool lockCameraY = true;
    private Transform cameraTarget;
    private float lockedYPosition;

    [Header("觀察用 (不要手動改)")]
    public int attachedWolvesCount = 0; 
    public float currentSpeed;      
    [HideInInspector] public bool freezeHorizontal = false;
    
    // 用來記錄目前正在播放的動畫，避免重複呼叫導致卡死
    private string currentAnimState = "Idle";

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        playerCollider = GetComponent<Collider>();
        
        // 【新增】強化物理設定，避免被狼撞飛或穿模
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        rb.constraints = RigidbodyConstraints.FreezeRotation; // 鎖定旋轉，永遠不會跌倒
        rb.mass = 10f; // 增加玩家質量，才不會被輕易推動

        // 【新增】賦予無摩擦力物理材質，避免卡在牆壁、物件邊緣
        if (playerCollider != null && (playerCollider.material == null || playerCollider.material.name == ""))
        {
            PhysicsMaterial noFriction = new PhysicsMaterial("NoFrictionMaterial");
            noFriction.dynamicFriction = 0f;
            noFriction.staticFriction = 0f;
            noFriction.frictionCombine = PhysicsMaterialCombine.Minimum;
            noFriction.bounciness = 0f;
            noFriction.bounceCombine = PhysicsMaterialCombine.Minimum;
            playerCollider.material = noFriction;
        }

        // 【新增】建立攝影機追蹤點
        if (lockCameraY)
        {
            GameObject targetObj = new GameObject("PlayerCameraTarget");
            cameraTarget = targetObj.transform;
            cameraTarget.position = transform.position;
            lockedYPosition = transform.position.y;

            var vcam = Object.FindAnyObjectByType<Unity.Cinemachine.CinemachineVirtualCamera>();
            if (vcam != null && vcam.Follow == this.transform)
            {
                vcam.Follow = cameraTarget;
            }
        }

        // 如果沒有手動設定，才嘗試自動搜尋 (備用)
        if (animator == null)
            animator = GetComponentInChildren<Animator>();
        
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
        // 掉落偵測與動畫
        // ==========================================

        // 1. 改進的地板判定 (改為 BoxCast 且不受 isFalling 限制，解決碰撞無法跳躍問題)
        bool isGrounded = false;
        if (playerCollider != null)
        {
            Vector3 center = playerCollider.bounds.center;
            // 寬度稍微縮小避免誤判牆壁，厚度設薄偵測底部
            Vector3 halfExtents = new Vector3(playerCollider.bounds.extents.x * 0.8f, 0.05f, playerCollider.bounds.extents.z * 0.8f);
            isGrounded = Physics.BoxCast(center, halfExtents, Vector3.down, out _, Quaternion.identity, playerCollider.bounds.extents.y + 0.1f, ~0, QueryTriggerInteraction.Ignore);
        }

        // 2. 更嚴謹的掉落偵測
        // 必須「沒有踩在地上」且「向下墜落的速度夠快」，避免一般小跳躍剛往下掉就播掉落動畫
        bool isFalling = (!isGrounded && rb.linearVelocity.y < fallVelocityThreshold);

        // 3. 動畫控制 (終極防呆版：程式完全接管，無視那些出問題的白色箭頭)
        if (animator != null)
        {
            animator.transform.rotation = Quaternion.LookRotation(facingDirection);

            // 決定現在「應該」要播什麼動畫
            string targetAnim = "Idle";
            if (isFalling)
            {
                targetAnim = "Falling";
            }
            else if (Mathf.Abs(moveInput) > 0.1f)
            {
                targetAnim = "Run";
            }

            // 只有當目標動畫跟現在不同時，才呼叫切換，這樣就不會每一幀重置導致卡死了！
            if (currentAnimState != targetAnim)
            {
                animator.CrossFade(targetAnim, 0.1f);
                currentAnimState = targetAnim;
            }
        }
        else
        {
            Debug.LogError("[診斷] animator 是 NULL！請確認皮套子物件上有 Animator 組件！");
        }
        // 4. 處理抓取與移動 (恢復原本的邏輯)
        if (Input.GetKeyDown(KeyCode.LeftShift)) TryGrabObject();
        if (Input.GetKeyUp(KeyCode.LeftShift)) ReleaseObject();

        float finalSpeed = (pulledObject != null) ? currentSpeed / 2f : currentSpeed;
        rb.linearVelocity = new Vector3(moveInput * finalSpeed, rb.linearVelocity.y, rb.linearVelocity.z);

        if (Input.GetKeyDown(KeyCode.Space) && isGrounded)
        {
            // 每次跳躍前先消除往下的掉落速度，確保跳躍高度一致
            rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0, rb.linearVelocity.z);
            // 改用 VelocityChange，無視質量 (mass = 10) 也能跳得一樣高
            rb.AddForce(Vector3.up * jumpForce, ForceMode.VelocityChange);
        }
    }

    void LateUpdate()
    {
        // 讓攝影機追蹤點跟隨玩家 X 和 Z 軸，但 Y 軸鎖死
        if (lockCameraY && cameraTarget != null)
        {
            cameraTarget.position = new Vector3(transform.position.x, lockedYPosition, transform.position.z);
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
            Debug.Log("碰觸到 FallingBackground！鎖死橫向移動，開始強制掉落！");
            freezeHorizontal = true;

            // 【關鍵修復】把速度歸零後，直接施加一個向下的初始力，讓物理引擎「知道」你在掉落
            rb.linearVelocity = Vector3.zero;
            rb.AddForce(Vector3.down * 5f, ForceMode.VelocityChange);

            // 關閉重生系統，避免掉落出畫面後被傳送回去
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

            // 重新啟動重生系統
            PlayerRespawnSystem respawnSystem = GetComponent<PlayerRespawnSystem>();
            if (respawnSystem != null)
            {
                respawnSystem.enabled = true;
            }

            // 重新讓攝影機跟隨玩家 (若有鎖定 Y 軸則跟隨假目標)
            Unity.Cinemachine.CinemachineVirtualCamera vcam = Object.FindAnyObjectByType<Unity.Cinemachine.CinemachineVirtualCamera>();
            if (vcam != null)
            {
                vcam.Follow = (lockCameraY && cameraTarget != null) ? cameraTarget : this.transform;
            }
        }
    }
}