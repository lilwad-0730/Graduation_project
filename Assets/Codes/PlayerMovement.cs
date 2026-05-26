using UnityEngine;
using Unity.Cinemachine;

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
    public Animator animator; 
    
    private Collider playerCollider;
    private string currentAnimState = ""; 

    [Header("狼群減速狀態 (可調整)")]
    [Tooltip("幾隻狼能讓玩家完全停下？(建議設低一點才明顯)")]
    public float maxWolvesToStop = 3f; 
    
    [Header("觀察用 (不要手動改)")]
    public int attachedWolvesCount = 0; 
    public float currentSpeed;      
    [HideInInspector] public bool freezeHorizontal = false;

    [Header("攝影機與物理優化設定")]
    [Tooltip("這將確保鏡頭無條件永遠跟隨玩家")]
    public bool forceCameraFollowPlayer = true;
    
    [Header("跳躍與墜落動畫微調")]
    [Tooltip("當角色懸空且【向下掉落的速度】大於這個數值時，才播放 Falling 動畫 (設為 0 代表只要往下掉就播，負數代表掉落有一定速度才播)")]
    public float fallVelocityThreshold = -1.0f;
    
    [Tooltip("角色離開地面後，延遲幾秒才允許播放 Falling 動畫 (避免走過小顛簸時一直閃爍掉落動畫)")]
    public float fallAnimationDelay = 0.15f;
    
    private float currentAirTime = 0f;


    void Start()
    {
        rb = GetComponent<Rigidbody>();
        playerCollider = GetComponent<Collider>();
        
        // 強化物理設定，避免被狼撞飛或穿模
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        rb.constraints = RigidbodyConstraints.FreezeRotation; // 鎖定旋轉，永遠不會跌倒
        rb.mass = 10f; // 增加玩家質量，才不會被輕易推動

        // 賦予無摩擦力物理材質，避免卡在牆壁、物件邊緣
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

        // ==========================================
        // 確保所有攝影機無條件追蹤玩家！
        // ==========================================
        if (forceCameraFollowPlayer)
        {
            SetCameraFollow(this.transform);
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
        // 如果碰到 FallingBackground，強制將水平輸入歸零
        float moveInput = freezeHorizontal ? 0f : Input.GetAxis("Horizontal"); 
        if (moveInput > 0.1f) facingDirection = Vector3.right;
        if (moveInput < -0.1f) facingDirection = Vector3.left;

        // ==========================================
        // 1. 超穩定地面偵測 (使用 BoxCast 防止微小抖動)
        // ==========================================
        bool isGrounded = false;
        if (playerCollider != null)
        {
            Vector3 center = playerCollider.bounds.center;
            // 寬度稍微縮小避免誤判牆壁，厚度加長偵測底部
            Vector3 halfExtents = new Vector3(playerCollider.bounds.extents.x * 0.8f, 0.05f, playerCollider.bounds.extents.z * 0.8f);
            // 改用更長的距離確保一定能掃到地面，並排除自身的 collider
            isGrounded = Physics.BoxCast(center, halfExtents, Vector3.down, out _, Quaternion.identity, playerCollider.bounds.extents.y + 0.2f, ~0, QueryTriggerInteraction.Ignore);
        }

        if (isGrounded)
        {
            currentAirTime = 0f;
        }
        else
        {
            currentAirTime += Time.deltaTime;
        }

        // ==========================================
        // 2. 動畫強行控制 (直接程式碼接管播放)
        // ==========================================
        if (animator != null)
        {
            // 控制角色外觀模型轉向
            if (facingDirection != Vector3.zero)
            {
                animator.transform.rotation = Quaternion.LookRotation(facingDirection);
            }

            string targetAnim = "Idle";

            // 判斷是否應該播放墜落動畫 (加入了延遲時間與掉落速度的容錯閥值)
            bool isFalling = !isGrounded && (currentAirTime >= fallAnimationDelay) && (rb.linearVelocity.y < fallVelocityThreshold);

            // 判斷是否有水平速度或輸入
            bool hasHorizontalSpeed = Mathf.Abs(rb.linearVelocity.x) > 0.1f || Mathf.Abs(moveInput) > 0.1f;

            if (isFalling)
            {
                // 只有在空中待夠久，且真的有往下掉的速度時，才播 FALLING 動畫
                targetAnim = "Falling";
            }
            else
            {
                // 停在地上 (或剛跳起來還在上升時)
                if (hasHorizontalSpeed)
                {
                    targetAnim = "Run";
                }
                else
                {
                    targetAnim = "Idle";
                }
            }

            // 【強制接管播放】使用 Play 直接切換，捨棄 CrossFade 避免任何過渡卡頓或失敗
            if (currentAnimState != targetAnim)
            {
                // 檢查該狀態是否存在，避免 Console 狂刷錯誤
                if (animator.HasState(0, Animator.StringToHash(targetAnim)))
                {
                    animator.Play(targetAnim);
                    currentAnimState = targetAnim;
                    Debug.Log($"[動畫切換] 強制切換為：{targetAnim}");
                }
                else
                {
                    currentAnimState = targetAnim;
                    Debug.LogError($"[動畫嚴重錯誤] 試圖播放 '{targetAnim}'，但您的 Animator 裡面「完全沒有」這個名字的方塊！");
                }
            }
        }
        else
        {
            Debug.LogError("[動畫診斷] animator 是 NULL！請確認皮套子物件上有 Animator 組件，或在 Inspector 手動指定！");
        }

        // ==========================================
        // 3. 處理抓取與移動 (恢復原本的邏輯)
        // ==========================================
        if (Input.GetKeyDown(KeyCode.LeftShift)) TryGrabObject();
        if (Input.GetKeyUp(KeyCode.LeftShift)) ReleaseObject();

        float finalSpeed = (pulledObject != null) ? currentSpeed / 2f : currentSpeed;
        
        if (freezeHorizontal)
        {
            // 在 FallingBackground 下墜時，嚴格鎖死 X 軸速度為 0，只允許 Y 軸掉落
            rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, rb.linearVelocity.z);
        }
        else
        {
            rb.linearVelocity = new Vector3(moveInput * finalSpeed, rb.linearVelocity.y, rb.linearVelocity.z);
        }

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
        // 已經完全移除鎖定 Y 軸的邏輯。鏡頭現在會無條件 100% 跟隨玩家的真實位置。
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

    public void ReleaseObject()
    {
        if (pulledObject != null)
        {
            pulledObject.transform.SetParent(null);
            Rigidbody objRb = pulledObject.GetComponent<Rigidbody>();
            if (objRb != null)
            {
                objRb.isKinematic = false;
            }
            pulledObject = null;
        }
    }

    public Transform GetCameraTarget()
    {
        return this.transform; // 無條件回傳玩家本身
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

            // 把速度歸零後，直接施加一個向下的初始力，讓物理引擎「知道」你在掉落
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

            // 【關鍵修正】強制把「最後安全點」設為現在的位置！
            // 否則系統會發現玩家跟一開始的天空比起來掉落了幾百公尺，一啟動就立刻把玩家當作墜崖殺死！
            PlayerRespawnSystem respawnSystem = GetComponent<PlayerRespawnSystem>();
            if (respawnSystem != null)
            {
                respawnSystem.SetSafeGroundPosition(this.transform.position);
            }

            // 確保攝影機依然鎖定在玩家身上
            SetCameraFollow(this.transform);

            // 延遲 0.3 秒再重新啟動重生系統，確保相機完全到位，防範任何假死重生！
            StartCoroutine(EnableRespawnWithDelay());
        }
    }

    private void SetCameraFollow(Transform target)
    {
        // 尋找「所有」新版 CinemachineCamera 並強制修改目標 (解決多鏡頭切換不跟隨的問題)
        var vcams3 = Object.FindObjectsByType<CinemachineCamera>(FindObjectsSortMode.None);
        foreach(var vcam in vcams3)
        {
            vcam.Follow = target;
            Debug.Log($"[PlayerMovement] 已將 CinemachineCamera {vcam.name} 的 Follow 設為 {target.name}");
        }

        // 尋找「所有」舊版 CinemachineVirtualCamera
        var vcamsLegacy = Object.FindObjectsByType<CinemachineVirtualCamera>(FindObjectsSortMode.None);
        foreach(var vcam in vcamsLegacy)
        {
            vcam.Follow = target;
            Debug.Log($"[PlayerMovement] 已將 CinemachineVirtualCamera {vcam.name} 的 Follow 設為 {target.name}");
        }
    }

    private System.Collections.IEnumerator EnableRespawnWithDelay()
    {
        yield return new WaitForSeconds(0.3f);
        PlayerRespawnSystem respawnSystem = GetComponent<PlayerRespawnSystem>();
        if (respawnSystem != null)
        {
            respawnSystem.enabled = true;
            Debug.Log("【重生系統】已延遲重啟，安全防護生效。");
        }
    }
}