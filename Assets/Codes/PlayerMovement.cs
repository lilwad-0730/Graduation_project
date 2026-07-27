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
    public Vector3 FacingDirection => facingDirection;

    [Header("動畫控制")]
    [Tooltip("請直接把有 PlayerAnimator Controller 的模型子物件拖曳到這裡！")]
    public Animator animator; 
    
    private Collider playerCollider;
    private string currentAnimState = ""; 
    public bool isGrounded = false;
    private bool isJumping = false;
    
    private float initialZ;
    
    [Header("最高層級防破圖與鎖死系統")]
    [HideInInspector] public bool isStrictLockingX = false;
    [HideInInspector] public float lockedXValue = 0f;
    private Vector3 _lastFramePos;

    [Header("狼群減速狀態 (可調整)")]
    [Tooltip("幾隻狼能讓玩家完全停下？(建議設低一點才明顯)")]
    public float maxWolvesToStop = 3f; 
    
    [Header("觀察用 (不要手動改)")]
    public int attachedWolvesCount = 0; 
    [HideInInspector] public float currentSpeed;      
    [HideInInspector] public bool freezeHorizontal = false;
    [HideInInspector] public bool isCutsceneFrozen = false; // 用於劇情鎖定 (例如光絮移動時)

    [Header("攝影機緩衝與防震設定 (取代原本的 Y 軸鎖死)")]
    [Tooltip("開啟此選項，會讓攝影機平滑跟隨玩家的上下跳躍 (減震效果，防止跳躍時畫面跟著狂震)")]
    public bool smoothCameraY = true;
    
    [Tooltip("Y 軸追蹤的平滑時間 (數值越大越慢跟上，0.2 ~ 0.5 最佳)")]
    public float cameraYDamping = 0.25f; 
    
    private Transform cameraTarget;
    private float _smoothYVelocity;

    [Header("跳躍與墜落動畫微調")]
    [Tooltip("當角色懸空且【向下掉落的速度】大於這個數值時，才播放 Falling 動畫 (設為 0 代表只要往下掉就播，負數代表掉落有一定速度才播)")]
    public float fallVelocityThreshold = -1.0f;
    
    [Tooltip("角色離開地面後，延遲幾秒才允許播放 Falling 動畫 (避免走過小顛簸時一直閃爍掉落動畫)")]
    public float fallAnimationDelay = 0.15f;
    
    private float currentAirTime = 0f;

    public enum WaterDetectionMode { Auto, ForceOn, ForceOff }

    [Header("水下物理與浮力設定 (Underwater Physics)")]
    [Tooltip("水中狀態偵測模式：Auto(自動偵測場景名稱包含 underwater 或進入水體 Trigger)、ForceOn(強制開啟水下物理)、ForceOff(強制關閉)")]
    public WaterDetectionMode waterDetectionMode = WaterDetectionMode.Auto;

    [Tooltip("目前是否處於水中狀態 (觀察用 / 腳本唯讀與觸發)")]
    public bool isUnderwater = false;

    [Tooltip("水中重力縮放比例 (0 ~ 1，數值越小浮力越強、掉落越慢，建議 0.25)")]
    public float underwaterGravityScale = 0.25f;

    [Tooltip("水中跳躍 / 撥水推進推力 (預設 3.5f，比陸地的跳躍 5.0 更平緩)")]
    public float underwaterJumpForce = 3.5f;

    [Tooltip("水中最大沉降速度限制 (單位/秒，設為 -2.0f 讓沉降不會太快，產生滯空浮動感)")]
    public float underwaterMaxFallSpeed = -2.0f;

    [Tooltip("水中垂直向上/向下移動的阻力衰減率 (數值越大衝速減慢越快，呈現滑順水阻)")]
    public float underwaterVerticalDrag = 2.0f;

    [Tooltip("水中水平移動速度倍率 (預設 0.85f，模擬水中行走/游泳的微水阻)")]
    public float underwaterHorizontalSpeedMultiplier = 0.85f;

    [Tooltip("是否允許在水中未著地時連按 W 向上撥水游泳")]
    public bool allowContinuousSwimming = true;

    [Tooltip("撥水游泳冷卻時間 (秒)")]
    public float swimCooldown = 0.25f;
    private float lastSwimTime = -999f;
    private bool isTriggerUnderwater = false;

    [Tooltip("水中按 W 向上游泳時的模型仰角傾斜 (預設 15 度)")]
    public float underwaterSwimUpTiltAngle = 15f;

    [Header("真實水下波義耳定律負浮力機制 (Depth Buoyancy Physics)")]
    [Tooltip("是否開啟水下深度波義耳定律負浮力機制 (越深水壓越大壓縮體積，超過中性浮力深度後加速沉降)")]
    public bool enableDepthBuoyancyPhysics = true;

    [Tooltip("中性浮力 Y 軸座標 (Neutral Buoyancy Y Level)。高於此處為正浮力，低於此處水壓壓縮空氣產生負浮力加速沉降")]
    public float neutralBuoyancyY = -5f;

    [Tooltip("水壓氣體體積壓縮率 (Depth Compression Rate)。每向下滑行 1 單位深度時增加的沉降加速度比率 (建議 0.1 ~ 0.4)")]
    public float depthCompressionRate = 0.2f;

    [Tooltip("水下深度負浮力最大額外下沉推力上限 (避免極深處掉落太快)")]
    public float maxNegativeBuoyancyExtraForce = 8.0f;

    // 動畫接軌預留標籤
    [HideInInspector] public bool isSwimming = false;


    void Start()
    {
        rb = GetComponent<Rigidbody>();
        playerCollider = GetComponent<Collider>();
        
        // 儲存初始 Z 軸位置，作為 2.5D 移動的基準線
        initialZ = transform.position.z;
        
        // 強化物理設定，鎖定旋轉與 Z 軸移動，避免被撞飛、偏移或穿模
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        rb.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionZ; 
        rb.mass = 10f; // 增加玩家質量，才不會被輕易推動

        // 賦予無摩擦力物理材質，避免卡在牆壁、物件邊緣
        if (playerCollider != null)
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
        // 攝影機防震假目標初始化
        // ==========================================
        if (smoothCameraY)
        {
            GameObject targetObj = new GameObject("PlayerCameraTarget_SmoothY");
            cameraTarget = targetObj.transform;
            cameraTarget.position = this.transform.position;
            
            // 強制所有攝影機追蹤這個「有避震器」的假目標
            SetCameraFollow(cameraTarget);
        }
        else
        {
            SetCameraFollow(this.transform);
        }

        // 如果沒有手動設定，才嘗試自動搜尋 (備用)
        if (animator == null)
            animator = GetComponentInChildren<Animator>();
        
        // 強制重置所有狀態，避免卡死
        freezeHorizontal = false; 
        isStrictLockingX = false;
        isCutsceneFrozen = false; // 確保起始未被鎖定
        attachedWolvesCount = 0;
        currentSpeed = baseSpeed;
        _lastFramePos = transform.position;
    }

    void Update()
    {
        // 刷新水下狀態 (三層判定)
        UpdateUnderwaterState();

        // ==========================================
        // 【防卡死】偵測瞬間移動 (例如：重生系統觸發)
        // ==========================================
        if (Vector3.Distance(transform.position, _lastFramePos) > 5f)
        {
            Debug.Log("【重生防卡死】偵測到玩家瞬間移動，強制解除所有 X 軸與墜落鎖定！");
            freezeHorizontal = false;
            isStrictLockingX = false;
        }
        _lastFramePos = transform.position;

        // 補強組件檢索
        if (playerCollider == null) playerCollider = GetComponent<Collider>();
        if (playerCollider == null) playerCollider = GetComponentInChildren<Collider>();
        if (playerCollider == null) playerCollider = GetComponentInParent<Collider>();

        // 1. 偵測地面狀態 (若 playerCollider 為空則啟用 Raycast Fallback)
        bool preliminaryGrounded = false;
        if (playerCollider != null)
        {
            Vector3 center = playerCollider.bounds.center;
            Vector3 halfExtents = new Vector3(playerCollider.bounds.extents.x * 0.8f, 0.05f, playerCollider.bounds.extents.z * 0.8f);
            preliminaryGrounded = Physics.BoxCast(center, halfExtents, Vector3.down, out _, Quaternion.identity, playerCollider.bounds.extents.y + 0.2f, ~0, QueryTriggerInteraction.Ignore);
        }
        else
        {
            preliminaryGrounded = Physics.Raycast(transform.position, Vector3.down, 1.5f, ~0, QueryTriggerInteraction.Ignore);
        }

        // ==========================================
        // 處理水平移動與最高層級墜落鎖定
        // ==========================================
        bool isInDropZone = freezeHorizontal;
        bool actuallyFreeze = isInDropZone && !preliminaryGrounded && rb.linearVelocity.y < 0f;
        
        // 讀取玩家輸入
        float rawInput = Input.GetAxis("Horizontal");
        if (rawInput == 0f)
        {
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) rawInput = 1f;
            else if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) rawInput = -1f;
        }

        // 【最高解鎖規則】：若玩家主動按下按鍵要移動，無條件強制解鎖所有 X 軸與掉落鎖死！
        if (Mathf.Abs(rawInput) > 0.1f && !isCutsceneFrozen)
        {
            isStrictLockingX = false;
            freezeHorizontal = false;
            actuallyFreeze = false;
        }

        float moveInput = (actuallyFreeze || isCutsceneFrozen) ? 0f : rawInput;

        if (moveInput > 0.1f && !isStrictLockingX) facingDirection = Vector3.right;
        if (moveInput < -0.1f && !isStrictLockingX) facingDirection = Vector3.left;

        // 【除錯診斷 LOG】：當玩家嘗試按下移動或跳躍鍵時，在 Console 印出目前所有的解鎖狀態與數值
        if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.W) || 
            Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.RightArrow))
        {
            PlayerPetrification petr = GetComponent<PlayerPetrification>();
            if (petr == null) petr = GetComponentInChildren<PlayerPetrification>();
            Debug.Log($"【移動診斷 LOG】玩家按下按鍵！\n" +
                      $" - 座標 Position: {transform.position}\n" +
                      $" - Rigidbody.isKinematic: {(rb != null ? rb.isKinematic.ToString() : "NULL")}\n" +
                      $" - PlayerMovement.enabled: {this.enabled}\n" +
                      $" - isGrounded: {preliminaryGrounded}\n" +
                      $" - freezeHorizontal: {freezeHorizontal}\n" +
                      $" - isCutsceneFrozen: {isCutsceneFrozen}\n" +
                      $" - isStrictLockingX: {isStrictLockingX}\n" +
                      $" - 石化狀態 isPetrified: {(petr != null ? petr.isPetrified.ToString() : "無石化組件")}\n" +
                      $" - 當前速度 currentSpeed: {currentSpeed}\n" +
                      $" - 剛體速度 velocity: {(rb != null ? rb.linearVelocity.ToString() : "NULL")}");
        }

        // ==========================================
        // 1. 超穩定地面偵測 (使用 BoxCast 防止微小抖動)
        // ==========================================
        isGrounded = preliminaryGrounded;
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
            // 如果落地且沒有明顯向上的速度，代表跳躍結束
            if (rb.linearVelocity.y <= 0.1f) isJumping = false;
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
            // 自動為未來的游泳 Animator 傳遞狀態標籤 (若 Animator Controller 有對應 Parameter 則更新)
            foreach (AnimatorControllerParameter param in animator.parameters)
            {
                if (param.name == "isUnderwater") animator.SetBool("isUnderwater", isUnderwater);
                if (param.name == "isSwimming") animator.SetBool("isSwimming", isSwimming);
            }

            // 控制角色外觀模型轉向與水下按 W 向上 15 度仰角傾斜
            if (facingDirection != Vector3.zero)
            {
                Quaternion baseRotation = Quaternion.LookRotation(facingDirection);
                bool isSwimmingUpward = isUnderwater && (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.Space) || rb.linearVelocity.y > 0.3f);

                if (isSwimmingUpward)
                {
                    // 局部 X 軸旋轉 -underwaterSwimUpTiltAngle (-15度) 會將模型的頭部/朝向平滑抬高仰角
                    Quaternion targetRotation = baseRotation * Quaternion.Euler(-underwaterSwimUpTiltAngle, 0f, 0f);
                    animator.transform.rotation = Quaternion.Slerp(animator.transform.rotation, targetRotation, Time.deltaTime * 12f);
                }
                else
                {
                    animator.transform.rotation = Quaternion.Slerp(animator.transform.rotation, baseRotation, Time.deltaTime * 12f);
                }
            }

            string targetAnim = "Idle";

            // 判斷是否應該播放墜落動畫 (加入了延遲時間與掉落速度的容錯閥值)
            bool isFalling = !isGrounded && (currentAirTime >= fallAnimationDelay) && (rb.linearVelocity.y < fallVelocityThreshold);

            // 判斷是否有水平速度或輸入
            bool hasHorizontalSpeed = Mathf.Abs(rb.linearVelocity.x) > 0.1f || Mathf.Abs(moveInput) > 0.1f;

            if (isUnderwater)
            {
                // 【水下動畫邏輯】：在水中只要有水平移動或按 W 向上游泳，播放 Swimming；原地靜止時一律播放 Treading Water
                if (hasHorizontalSpeed || isSwimming || Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.Space))
                {
                    targetAnim = "Swimming";
                }
                else
                {
                    targetAnim = "Treading Water";
                }
            }
            else
            {
                // 【陸地動畫邏輯】
                if (isFalling)
                {
                    targetAnim = "Falling";
                }
                else
                {
                    targetAnim = hasHorizontalSpeed ? "Run" : "Idle";
                }
            }

            // 【最高層級】：如果正在播 Falling 動畫，且位於掉落區，啟動絕對 X 軸鎖死
            if (isFalling && isInDropZone)
            {
                if (!isStrictLockingX)
                {
                    isStrictLockingX = true;
                    lockedXValue = transform.position.x;
                    Debug.Log($"【最高規則生效】開始絕對鎖死 X 座標於: {lockedXValue}");
                }
            }
            else if (isGrounded)
            {
                // 落地解除鎖死
                if (isStrictLockingX) Debug.Log("【最高規則解除】已落地，解除 X 軸鎖定。");
                isStrictLockingX = false;
            }

            // 【強制接管播放】使用 Play 直接切換指定 State
            if (currentAnimState != targetAnim)
            {
                animator.Play(targetAnim);
                currentAnimState = targetAnim;
                Debug.Log($"[動畫切換] 水下/陸地強制切換為：{targetAnim}");
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

        // 防呆校正：若未被狼咬或速度異常為0，強制重置為 baseSpeed
        if (attachedWolvesCount == 0 || currentSpeed <= 0f)
        {
            currentSpeed = baseSpeed;
        }

        float finalSpeed = currentSpeed * (isUnderwater ? underwaterHorizontalSpeedMultiplier : 1.0f);
        if (pulledObject != null)
        {
            // 強化型剛體搜尋：防呆，以防推拉的碰撞器 (Collider) 與剛體 (Rigidbody) 不在同一個物件層級上
            Rigidbody pulledRb = pulledObject.GetComponent<Rigidbody>();
            if (pulledRb == null) pulledRb = pulledObject.GetComponentInParent<Rigidbody>();
            if (pulledRb == null) pulledRb = pulledObject.GetComponentInChildren<Rigidbody>();

            if (pulledRb != null)
            {
                // 動態重量比率：主角自身重量為 10f。拉動物體越重，速度越慢。
                // 速度比例 = 10f / (10f + 物體質量)。
                // 例如：物體質量 10f -> 速度減半；物體質量 90f -> 速度變 1/10；物體質量 500f -> 幾乎拉不動。
                float weightFactor = 10f / (10f + pulledRb.mass);
                finalSpeed = finalSpeed * weightFactor;
            }
            else
            {
                finalSpeed = finalSpeed / 2f; // 備用方案
            }
        }
        
        if (actuallyFreeze || isStrictLockingX)
        {
            // 嚴格鎖死 X 軸速度為 0，只允許 Y 軸掉落
            rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, rb.linearVelocity.z);
        }
        else
        {
            Vector3 targetVelocity = new Vector3(moveInput * finalSpeed, rb.linearVelocity.y, rb.linearVelocity.z);
            
            // 【修復斜坡抖動與抽搐】：如果在地面且非跳躍中，計算斜坡法線並沿著斜坡移動 (水下著地時亦適用)
            if (isGrounded && !isJumping)
            {
                RaycastHit hit;
                Vector3 center = playerCollider.bounds.center;
                // 向下打射線偵測斜坡表面
                if (Physics.Raycast(center, Vector3.down, out hit, playerCollider.bounds.extents.y + 0.5f, ~0, QueryTriggerInteraction.Ignore))
                {
                    float angle = Vector3.Angle(Vector3.up, hit.normal);
                    // 只有在稍微有坡度（>0.5度），且不至於太陡（<60度）的斜坡才介入
                    if (angle > 0.5f && angle < 60f)
                    {
                        if (Mathf.Abs(moveInput) > 0.05f)
                        {
                            // 將原本的水平移動向量，投影到斜坡表面上
                            Vector3 moveDir = new Vector3(Mathf.Sign(moveInput), 0, 0);
                            Vector3 slopeDir = Vector3.ProjectOnPlane(moveDir, hit.normal).normalized;
                            
                            targetVelocity.x = slopeDir.x * finalSpeed * Mathf.Abs(moveInput);
                            targetVelocity.y = slopeDir.y * finalSpeed * Mathf.Abs(moveInput);
                        }
                        else
                        {
                            // 沒有按鍵時，消除 X 軸滑動，但保留自然物理墜落速度
                            targetVelocity.x = 0f;
                        }
                    }
                }
            }
            
            rb.linearVelocity = targetVelocity;
        }

        // 支援 W 鍵或空白鍵跳躍 / 水中撥水游泳 (必須沒有被劇情鎖定)
        if (!isCutsceneFrozen && (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.Space)))
        {
            if (isUnderwater)
            {
                // 在水中：著地時可跳躍，懸空時若允許連續游泳且冷卻時間已過，可向上撥水推進
                if (isGrounded || (allowContinuousSwimming && (Time.time - lastSwimTime >= swimCooldown)))
                {
                    isJumping = true;
                    lastSwimTime = Time.time;
                    isSwimming = true;

                    // 保留原本向上的部分速度，消除下沉速，套用柔和的水中向上推力
                    float baseUpVel = Mathf.Max(rb.linearVelocity.y, 0f);
                    rb.linearVelocity = new Vector3(rb.linearVelocity.x, baseUpVel, rb.linearVelocity.z);
                    rb.AddForce(Vector3.up * underwaterJumpForce, ForceMode.VelocityChange);
                }
            }
            else if (isGrounded)
            {
                // 陸地標準跳躍
                isJumping = true;
                rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0, rb.linearVelocity.z);
                rb.AddForce(Vector3.up * jumpForce, ForceMode.VelocityChange);
            }
        }
    }


    void LateUpdate()
    {
        // ==========================================
        // 【最高層級防破圖系統】確保玩家絕對不出界
        // ==========================================
        Vector3 finalPos = transform.position;

        // 1. 強制 X 軸墜落鎖定 (優先權最高)
        if (isStrictLockingX)
        {
            finalPos.x = lockedXValue;
        }

        // 強制鎖死 Z 軸位置，防止 3D 物理碰撞導致 Z 軸偏移
        finalPos.z = initialZ;

        // 套用最終的防破圖位置
        transform.position = finalPos;

        // ==========================================
        // 攝影機 Y 軸避震系統
        // ==========================================
        if (smoothCameraY && cameraTarget != null)
        {
            float targetX = transform.position.x;
            float targetZ = transform.position.z;
            
            // 如果處於 FallingBackground 大怒神下墜模式，為了避免鏡頭跟不上，把延遲降到極低
            float currentDamping = (freezeHorizontal && !isGrounded) ? 0.05f : cameraYDamping;

            // X 和 Z 軸死死咬住玩家 (0 延遲)
            // Y 軸使用 SmoothDamp 進行平滑過渡 (吸收跳躍時的碎震)
            float newY = Mathf.SmoothDamp(cameraTarget.position.y, transform.position.y, ref _smoothYVelocity, currentDamping);

            cameraTarget.position = new Vector3(targetX, newY, targetZ);
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

        // 【新增】：當累積狼咬達到上限（預設 3 隻），觸發主角死亡重生機制
        if (attachedWolvesCount >= (int)maxWolvesToStop)
        {
            PlayerRespawnSystem respawnSystem = GetComponent<PlayerRespawnSystem>();
            if (respawnSystem == null) respawnSystem = GetComponentInParent<PlayerRespawnSystem>();
            if (respawnSystem == null) respawnSystem = GetComponentInChildren<PlayerRespawnSystem>();
            if (respawnSystem == null) respawnSystem = FindAnyObjectByType<PlayerRespawnSystem>();
            if (respawnSystem == null) respawnSystem = FindFirstObjectByType<PlayerRespawnSystem>();
            #pragma warning disable CS0618
            if (respawnSystem == null) respawnSystem = (PlayerRespawnSystem)FindObjectOfType(typeof(PlayerRespawnSystem));
            #pragma warning restore CS0618

            if (respawnSystem != null)
            {
                Debug.Log("【狼咬致死】累積狼咬達到上限，觸發重生系統！");
                respawnSystem.TriggerRespawn();
            }
            else
            {
                Debug.LogError("【狼咬致死】找不到 PlayerRespawnSystem 組件！無法觸發重生！請確認場景中是否有任何物件掛載了 PlayerRespawnSystem 腳本。");
            }
        }
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
        return (smoothCameraY && cameraTarget != null) ? cameraTarget : this.transform;
    }

    /// <summary>
    /// 瞬間傳送玩家並重置攝影機跟隨點，防止 Y 軸追蹤延遲與碎震
    /// </summary>
    public void WarpTo(Vector3 position)
    {
        transform.position = position;
        if (cameraTarget != null)
        {
            cameraTarget.position = position;
        }

        // 【新增】：重置身上所有被狼咬住的計數，並恢復速度
        attachedWolvesCount = 0;
        CalculateSpeed();
        _smoothYVelocity = 0f; // 重置 Y 軸平滑速度快取
        
        // 瞬間解鎖玩家所有的移動/墜落鎖定與劇情鎖定，防止傳送後卡死
        isStrictLockingX = false;
        freezeHorizontal = false;
        isCutsceneFrozen = false;
        
        // 瞬間將主相機對齊，防止 Cinemachine 出現大跨度拉扯
        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            // 重置 Cinemachine 狀態
            CinemachineCamera[] vcams3 = FindObjectsByType<CinemachineCamera>(FindObjectsSortMode.None);
            foreach (var vcam in vcams3)
            {
                vcam.PreviousStateIsValid = false;
            }
            CinemachineVirtualCamera[] vcamsLegacy = FindObjectsByType<CinemachineVirtualCamera>(FindObjectsSortMode.None);
            foreach (var vcam in vcamsLegacy)
            {
                vcam.PreviousStateIsValid = false;
            }
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

            // 確保攝影機依然鎖定在玩家身上 (或避震假目標)
            SetCameraFollow((smoothCameraY && cameraTarget != null) ? cameraTarget : this.transform);

            // 延遲 0.3 秒再重新啟動重生系統，確保相機完全到位，防範任何假死重生！
            StartCoroutine(EnableRespawnWithDelay());
        }
    }

    private void SetCameraFollow(Transform target)
    {
        // 尋找「所有」新版 CinemachineCamera 並強制修改追蹤目標 (解決 Cinemachine v3 相機跟隨失效問題)
        var vcams3 = Object.FindObjectsByType<CinemachineCamera>(FindObjectsSortMode.None);
        foreach(var vcam in vcams3)
        {
            if (vcam != null && target != null)
            {
                vcam.Target.TrackingTarget = target;
                vcam.Follow = target;
                Debug.Log($"[PlayerMovement] 已將 CinemachineCamera {vcam.name} 的 TrackingTarget 設為 {target.name}");
            }
        }

        var vcamsLegacy = Object.FindObjectsByType<CinemachineVirtualCamera>(FindObjectsSortMode.None);
        foreach(var vcam in vcamsLegacy)
        {
            if (vcam != null && target != null)
            {
                vcam.Follow = target;
            }
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

    // ==========================================
    // 水下物理、浮力與真實深度波義耳負浮力運算 (FixedUpdate)
    // ==========================================
    private void FixedUpdate()
    {
        if (isUnderwater && rb != null && !rb.isKinematic)
        {
            // 1. 基礎浮力與微重力抵消 (Counter-Gravity / Buoyancy)
            float gravityMagnitude = Mathf.Abs(Physics.gravity.y);
            float baseCounterForce = gravityMagnitude * (1f - Mathf.Clamp01(underwaterGravityScale)) * rb.mass;

            // 2. 真實深度水壓氣體體積壓縮 (Boyle's Law & Negative Buoyancy)
            // 當深度 Y 越低 (低於 neutralBuoyancyY 中性浮力點)，水壓遞增壓縮氣體體積，正浮力衰減，產生向下加速沉降的負浮力
            float extraDownwardForce = 0f;
            if (enableDepthBuoyancyPhysics)
            {
                float depthBelowNeutral = neutralBuoyancyY - transform.position.y;
                if (depthBelowNeutral > 0f)
                {
                    extraDownwardForce = Mathf.Min(depthBelowNeutral * depthCompressionRate * rb.mass, maxNegativeBuoyancyExtraForce);
                }
            }

            float finalCounterForce = Mathf.Max(0f, baseCounterForce - extraDownwardForce);
            rb.AddForce(Vector3.up * finalCounterForce, ForceMode.Force);

            // 3. 水中垂直阻力 (Vertical Drag Damping)
            Vector3 vel = rb.linearVelocity;
            if (Mathf.Abs(vel.y) > 0.01f)
            {
                vel.y = Mathf.MoveTowards(vel.y, 0f, underwaterVerticalDrag * Time.fixedDeltaTime);
            }

            // 4. 水中最大沉降速度動態限制 (Terminal Sinking Speed Limit with Depth)
            float effectiveMaxFallSpeed = enableDepthBuoyancyPhysics && (transform.position.y < neutralBuoyancyY)
                ? underwaterMaxFallSpeed * (1f + (neutralBuoyancyY - transform.position.y) * 0.05f)
                : underwaterMaxFallSpeed;

            if (vel.y < effectiveMaxFallSpeed)
            {
                vel.y = effectiveMaxFallSpeed;
            }

            rb.linearVelocity = vel;
        }
    }

    // ==========================================
    // 水下狀態判定處理 (三層防護)
    // ==========================================
    public void SetTriggerUnderwater(bool state)
    {
        isTriggerUnderwater = state;
    }

    private void UpdateUnderwaterState()
    {
        if (waterDetectionMode == WaterDetectionMode.ForceOn)
        {
            isUnderwater = true;
        }
        else if (waterDetectionMode == WaterDetectionMode.ForceOff)
        {
            isUnderwater = false;
        }
        else // Auto 模式：自動比對場景名稱包含 underwater，或踩入水體 Trigger
        {
            string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name.ToLower();
            bool isUnderwaterScene = sceneName.Contains("underwater");
            isUnderwater = isUnderwaterScene || isTriggerUnderwater;
        }
    }
}