using UnityEngine;
using Unity.Cinemachine;

[RequireComponent(typeof(Rigidbody))]
public class PlayerMovement : MonoBehaviour
{
    [Header("基本移動設定")]
    public float baseSpeed = 5f;       
    [Tooltip("抓取/拉動物件 (LeftShift) 的偵測距離 (可在 Inspector 自由調整)")]
    public float pullRange = 2.5f;
    [Tooltip("跳躍高度：數值越大跳越高（預設 5）")]
    public float jumpForce = 5f;
    
    private Rigidbody rb;
    private GameObject pulledObject;
    private Vector3 facingDirection = Vector3.right; 
    public Vector3 FacingDirection => facingDirection;

    [Header("動畫控制")]
    [Tooltip("請直接把有 PlayerAnimator Controller 的模型子物件拖曳到這裡！")]
    public Animator animator; 
    public float CurrentMoveInput { get; private set; }
    public float BaseSpeed => baseSpeed;
    
    private Collider playerCollider;
    private string currentAnimState = ""; 
    public bool isGrounded = false;
    private bool isJumping = false;
    
    private float initialZ;
    
    [Header("最高層級防破圖與鎖死系統")]
    [HideInInspector] public bool isStrictLockingX = false;
    [HideInInspector] public float lockedXValue = 0f;
    private Vector3 _lastFramePos;

    // FallingBackground 防重複觸發 flag
    private bool _fallingBGEntered = false;


    [Header("狼群減速狀態 (可調整)")]
    [Tooltip("幾隻狼能讓玩家完全停下？(建議設低一點才明顯)")]
    public float maxWolvesToStop = 3f; 
    
    [Header("觀察用 (不要手動改)")]
    public int attachedWolvesCount = 0; 
    [HideInInspector] public float currentSpeed;      
    [HideInInspector] public bool freezeHorizontal = false;
    [HideInInspector] public bool isCutsceneFrozen = false; // 用於劇情鎖定 (例如光絮移動時)

    private static float _hardLockSince = -1f;

    /// <summary>安全網：演出旗標若卡住超過這麼久，強制放開，免得玩家永遠不能動。</summary>
    public const float HardLockSafetySeconds = 45f;

    /// <summary>
    /// 全域演出鎖定狀態查詢 (包含重生、鏡牆演出、過場文字卡、黑影怪物特寫等所有相機被接管的情境)
    /// </summary>
    public static bool IsHardCutsceneLocked
    {
        get
        {
            bool locked =
                PlayerRespawnSystem.IsAnyRespawning
                || MirrorWallAbsorbCutscene.IsAnyCutsceneRunning
                || ShadowMonsterController.IsAnyRevealRunning
                || (StoryCardPlayer.Instance != null && StoryCardPlayer.Instance.IsPlaying);

            if (!locked) { _hardLockSince = -1f; return false; }

            // ★因為拿掉了「按鍵就自動解鎖」的逃生門，這裡補一個時間上限。
            //   任何演出旗標忘了關（協程被 StopCoroutine 砍掉之類），
            //   最多鎖 45 秒就放人。正常的卡片與運鏡都遠短於這個。
            if (_hardLockSince < 0f) _hardLockSince = Time.unscaledTime;
            if (Time.unscaledTime - _hardLockSince > HardLockSafetySeconds) return false;

            return true;
        }
    }

    public static bool IsAnyCutsceneActive => IsHardCutsceneLocked;

    /// <summary>
    /// 當前主角是否處於完全鎖定操作狀態
    /// </summary>
    public bool IsControlLocked =>
        isCutsceneFrozen ||
        freezeHorizontal ||
        IsHardCutsceneLocked;

    [Header("📦 推動物件機制 (Pushing Object System)")]
    [Tooltip("推動物件偵測距離 (向前檢測 Pushable Tag 物件)")]
    public float pushDetectionDistance = 0.35f;

    [Tooltip("除錯用：被風推卻推不動時，在 Console 印出診斷訊息 (確認防抖判定有沒有生效)")]
    public bool logWindBlockDebug = false;

    [HideInInspector] public bool isPushing = false;
    private float _collisionPushableTimer = 0f;

    [Header("🌪️ 風暴吸入物理牽引 (Wind Suction - 地形與斜坡適應)")]
    [HideInInspector] public bool isWindSuctionActive = false;
    [HideInInspector] public float windSuctionTargetX = 0f;
    [HideInInspector] public float windSuctionSpeed = 3.0f;

    public void StartWindSuction(float targetX, float speed)
    {
        isWindSuctionActive = true;
        windSuctionTargetX = targetX;
        windSuctionSpeed = speed;
        isCutsceneFrozen = false;
        freezeHorizontal = false;
        isStrictLockingX = false;
    }

    public void StopWindSuction()
    {
        isWindSuctionActive = false;
    }

    [Header("攝影機緩衝與防震設定 (取代原本的 Y 軸鎖死)")]
    [Tooltip("開啟此選項，會讓攝影機平滑跟隨玩家的上下跳躍 (減震效果，防止跳躍時畫面跟著狂震)")]
    public bool smoothCameraY = true;
    
    [Tooltip("Y 軸追蹤的平滑時間 (數值越大越慢跟上，0.2 ~ 0.5 最佳)")]
    public float cameraYDamping = 0.25f;

    [Tooltip("是否將鏡頭高度嚴格限制在 SkyBackground 範圍內 (設為 false 則直接跟隨主角)")]
    public bool clampSkyBackgroundHeight = false;

    [Header("🎵 陸地動作音效設定 (Land Action SFX)")]
    [Tooltip("地面跑步/走路音效 (例如 Ruined_running / 跑步，自動在移動期間持續 Loop 播放)")]
    public AudioClip footstepSFX;
    [Tooltip("起跳蹬地音效 (例如 起跳)")]
    public AudioClip jumpSFX;
    [Tooltip("普通落地音效 (例如 落地1)")]
    public AudioClip landSoftSFX;
    [Tooltip("高處墜落重著地回聲音效 (例如 落地_回聲)")]
    public AudioClip landHardSFX;
    [Tooltip("滯空時間大於幾秒時，著地改播重著地回聲音效 (秒，預設 0.75)")]
    public float hardLandAirTimeThreshold = 0.75f;

    [Header("🌊 水下游泳音效設定 (Underwater SFX)")]
    [Tooltip("水下游泳/划水音效 (例如 水下_游動_01.wav，水下移動時自動 Loop 播放)")]
    public AudioClip swimSFX;
    [Tooltip("水下單發氣泡音效 (例如 水下_氣泡單發_01, 02, 03，游泳時隨機播放)")]
    public AudioClip[] underwaterBubbleSFX;
    
    [Range(0f, 1f)] public float sfxVolume = 0.85f;

    private AudioSource _footstepSource;
    private AudioSource _swimSource;
    private float _lastAirTime = 0f;
    private bool _wasGroundedLastFrame = true;
    
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
    private HorizontalMovingPlatform activeMovingPlatform;

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

    [Header("隱形水下游泳體力與適時斷開機制 (Underwater Stamina & Limit)")]
    [Tooltip("連續向上游泳的最大隱形體力上限 (秒，預設 3.0)。控制按住 W 最多能連續向上游多久")]
    public float maxSwimStamina = 3.0f;

    [Tooltip("隱形體力恢復速率 (倍率，預設 1.5)。當放開按鍵或著地時體力恢復速度")]
    public float swimStaminaRegenRate = 1.5f;

    [Tooltip("體力耗盡力竭冷卻時間 (秒，預設 1.0)。體力扣完斷開推力後，需冷卻多久才能再次向上游泳")]
    public float swimExhaustionCooldown = 1.0f;

    [Tooltip("連續向上游泳的推進速度 (預設 4.0f)")]
    public float underwaterSwimUpSpeed = 4.0f;

    [HideInInspector] public float currentSwimStamina;
    [HideInInspector] public bool isSwimExhausted = false;
    private float exhaustionTimer = 0f;

    // 動畫接軌預留標籤
    [HideInInspector] public bool isSwimming = false;

    // 天空背景邊界快取 (避免每幀 FindObjectsByType)
    private float _nextSkyBoundsCheckTime = 0f;
    private Bounds _cachedSkyBounds = new Bounds();
    private bool _hasCachedSkyBounds = false;

    private void OnEnable()
    {
        isCutsceneFrozen = false;
        freezeHorizontal = false;
        isStrictLockingX = false;
        PlayerRespawnSystem.IsAnyRespawning = false;
        MirrorWallAbsorbCutscene.IsAnyCutsceneRunning = false;
    }

    private void OnDisable()
    {
        if (_footstepSource != null)
        {
            _footstepSource.Stop();
            _footstepSource.volume = 0f;
        }

        if (_swimSource != null)
        {
            _swimSource.Stop();
            _swimSource.volume = 0f;
        }
    }

    void Start()
    {
        // ★ 自動停用場景中殘留的 Timeline PlayableDirector，防止開局自動播放光離開音效
        var directors = Object.FindObjectsByType<UnityEngine.Playables.PlayableDirector>(FindObjectsSortMode.None);
        foreach (var pd in directors)
        {
            if (pd != null && (pd.gameObject.name == "GameObject" || (pd.playableAsset != null && pd.playableAsset.name.Contains("Timeline"))))
            {
                pd.Stop();
                pd.playOnAwake = false;
                pd.enabled = false;
            }
        }

        rb = GetComponent<Rigidbody>();
        playerCollider = GetComponent<Collider>();
        
        // 儲存初始 Z 軸位置，作為 2.5D 移動的基準線
        initialZ = transform.position.z;
        
        // 強化物理設定，鎖定旋轉與 Z 軸移動，避免被撞飛、偏移或穿模
        rb.interpolation = RigidbodyInterpolation.Interpolate; // 啟用物理內插，消除攝影機與物理刷新率不同步之畫面抖動
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        rb.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionZ; 
        rb.mass = 10f; // 增加玩家質量，才不會被輕易推動
        rb.maxDepenetrationVelocity = 2.0f; // ★ 消除斜坡/平地夾角被強力彈開反彈引發的劇烈抖動
        rb.solverIterations = 16;
        rb.solverVelocityIterations = 16;

        // 賦予無摩擦力物理材質，避免卡在牆壁、物件邊緣
        PhysicsMaterial noFriction = new PhysicsMaterial("NoFrictionMaterial");
        noFriction.dynamicFriction = 0f;
        noFriction.staticFriction = 0f;
        noFriction.frictionCombine = PhysicsMaterialCombine.Minimum;
        noFriction.bounciness = 0f;
        noFriction.bounceCombine = PhysicsMaterialCombine.Minimum;

        if (playerCollider != null)
        {
            playerCollider.material = noFriction;
        }

        // ★ 水下場景 Z 軸精確居中校準與岩石深度全角度密封 (水下岩石洞穴中心為 Z = -0.4f)
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name.Contains("underwater"))
        {
            initialZ = -0.4f;
            transform.position = new Vector3(transform.position.x, transform.position.y, -0.4f);
            UnderwaterRockColliderHelper.SealUnderwaterRockGaps();
        }

        // ==========================================
        // 攝影機鎖定主角初始化 (確保 Cinemachine 100% 跟隨主角)
        // ==========================================
        SetCameraFollow(this.transform);

        // 如果沒有手動設定，才嘗試自動搜尋 (備用)
        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        if (animator != null)
        {
            animator.applyRootMotion = false; // 強制關閉 Root Motion！防止 3D/FBX 動畫拖走或鎖死 Transform 導致玩家在水下卡住不能動！
            animator.speed = 1.0f;
        }
        
        // 強制重置所有狀態，避免卡死
        freezeHorizontal = false; 
        isStrictLockingX = false;
        isCutsceneFrozen = false; // 確保起始未被鎖定
        attachedWolvesCount = 0;
        currentSpeed = baseSpeed;
        _lastFramePos = transform.position;
        currentSwimStamina = maxSwimStamina;
        isSwimExhausted = false;

        // 【修復轉場殘留鎖定】：強制重置 Rigidbody 的 constraints 與重力狀態
        // WaterOasisTransition 轉場時會鎖定 FreezePositionX 和關閉 useGravity，
        // 場景切換後這些狀態會殘留在 Player 身上導致無法移動，這裡強制還原！
        if (rb != null)
        {
            rb.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionZ;
            rb.useGravity = true;
            rb.isKinematic = false;
            rb.linearVelocity = Vector3.zero; // 清除殘留速度
        }

        // =========================================
        // 【超強力啟動診斷】幫助排查卡住問題
        // =========================================
        Debug.Log($"========== 【PlayerMovement 啟動診斷】 ==========\n" +
                  $" 場景名稱: {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}\n" +
                  $" 座標 Position: {transform.position}\n" +
                  $" Rigidbody: {(rb != null ? "有" : "❌ NULL！")}\n" +
                  $"   - isKinematic: {(rb != null ? rb.isKinematic.ToString() : "N/A")}\n" +
                  $"   - useGravity: {(rb != null ? rb.useGravity.ToString() : "N/A")}\n" +
                  $"   - constraints: {(rb != null ? rb.constraints.ToString() : "N/A")}\n" +
                  $"   - mass: {(rb != null ? rb.mass.ToString() : "N/A")}\n" +
                  $" Animator: {(animator != null ? animator.name : "❌ NULL！動畫不會播放！")}\n" +
                  $" Collider: {(playerCollider != null ? playerCollider.GetType().Name : "❌ NULL！")}\n" +
                  $" PlayerMovement.enabled: {this.enabled}\n" +
                  $" isCutsceneFrozen: {isCutsceneFrozen}\n" +
                  $" freezeHorizontal: {freezeHorizontal}\n" +
                  $" isStrictLockingX: {isStrictLockingX}\n" +
                  $" isUnderwater: {isUnderwater}\n" +
                  $" baseSpeed: {baseSpeed} | currentSpeed: {currentSpeed}\n" +
                  $"==============================================");
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

        // 1. 偵測地面狀態與斜坡角度 (使用多點射線確保 BoxCollider 任何邊緣觸地皆能精確穩定感應)
        RaycastHit groundHit;
        float currentSlopeAngle = 0f;
        bool preliminaryGrounded = CheckGrounded(out groundHit, out currentSlopeAngle);

        if (preliminaryGrounded && groundHit.collider != null)
        {
            activeMovingPlatform = groundHit.collider.GetComponentInParent<HorizontalMovingPlatform>();
            if (activeMovingPlatform == null) activeMovingPlatform = groundHit.collider.GetComponent<HorizontalMovingPlatform>();
        }
        else
        {
            activeMovingPlatform = null;
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

        float moveInput = 0f;

        if (isWindSuctionActive)
        {
            // ★ 風暴吸入模式：位移由風暴引力牽引，但允許玩家按 A/D 自由左右轉身面向 (原地跑掙扎演出)
            float diffX = windSuctionTargetX - transform.position.x;
            if (Mathf.Abs(diffX) > 0.05f)
            {
                moveInput = Mathf.Sign(diffX);
            }
            else
            {
                moveInput = 0f;
            }

            // 玩家按鍵時優先面向玩家按鍵方向，沒按時面向吸入方向
            if (rawInput > 0.1f) facingDirection = Vector3.right;
            else if (rawInput < -0.1f) facingDirection = Vector3.left;
            else if (moveInput > 0.05f) facingDirection = Vector3.right;
            else if (moveInput < -0.05f) facingDirection = Vector3.left;
        }
        else
        {
            // 當處於重生中 (IsAnyRespawning) 或 劇情演出鎖定 (IsControlLocked) 時，嚴格禁止玩家移動
            if (IsControlLocked)
            {
                rawInput = 0f;
                moveInput = 0f;
                if (rb != null && !rb.isKinematic)
                {
                    rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);
                }
            }
            else
            {
                // 若無任何全局演出正在跑，玩家主動按鍵即視為正常操作，自動解鎖。
                bool wantsToMove = Mathf.Abs(rawInput) > 0.1f
                                   || Input.GetKeyDown(KeyCode.W)
                                   || Input.GetKeyDown(KeyCode.Space)
                                   || Input.GetKeyDown(KeyCode.UpArrow);
                if (wantsToMove)
                {
                    // 若非演出/重生狀態且玩家主動按鍵，才解除掉落鎖死
                    isStrictLockingX = false;
                    freezeHorizontal = false;
                }

                moveInput = (actuallyFreeze || isCutsceneFrozen || IsHardCutsceneLocked) ? 0f : rawInput;

                if (moveInput > 0.1f && !isStrictLockingX) facingDirection = Vector3.right;
                if (moveInput < -0.1f && !isStrictLockingX) facingDirection = Vector3.left;
            }
        }

        CurrentMoveInput = moveInput;

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
                      $" - isJumping: {isJumping}\n" +
                      $" - isUnderwater: {isUnderwater}（水下就不能跳）\n" +
                      $" - IsHardCutsceneLocked: {IsHardCutsceneLocked}"
                      + $"（重生中:{PlayerRespawnSystem.IsAnyRespawning}"
                      + $" 鏡牆演出:{MirrorWallAbsorbCutscene.IsAnyCutsceneRunning}"
                      + $" 怪物登場:{ShadowMonsterController.IsRevealRunning}"
                      + $" 文字卡:{(StoryCardPlayer.Instance != null && StoryCardPlayer.Instance.IsPlaying)}）\n" +
                      $" - 石化狀態 isPetrified: {(petr != null ? petr.isPetrified.ToString() : "無石化組件")}\n" +
                      $" - 當前速度 currentSpeed: {currentSpeed}\n" +
                      $" - 剛體速度 velocity: {(rb != null ? rb.linearVelocity.ToString() : "NULL")}");
        }

        // ==========================================
        // 1. 地面狀態、推動判定與著地/腳步音效判定
        // ==========================================
        isGrounded = preliminaryGrounded;

        // 偵測是否正在推動 Pushable 物件
        UpdatePushingDetection(moveInput);

        if (isGrounded)
        {
            // 剛從空中落地的瞬間
            if (!_wasGroundedLastFrame && _lastAirTime > 0.15f)
            {
                if (_lastAirTime >= hardLandAirTimeThreshold && landHardSFX != null)
                {
                    if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(landHardSFX, sfxVolume);
                }
                else if (landSoftSFX != null)
                {
                    if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(landSoftSFX, sfxVolume);
                }
            }

            _lastAirTime = 0f;
            currentAirTime = 0f;

            // 如果落地且沒有明顯向上的速度，代表跳躍結束
            if (rb.linearVelocity.y <= 0.1f) isJumping = false;

            // ★★★ 關鍵修正：只要玩家落地踩到任何地面，自動解除掉落鎖定，恢復自由控制！
            if (freezeHorizontal)
            {
                freezeHorizontal = false;
                Debug.Log("【掉落解鎖】玩家已著地 (isGrounded)，自動解除橫向鎖定，恢復自由控制！");
                StartCoroutine(EnableRespawnWithDelay());
            }
        }
        else
        {
            currentAirTime += Time.deltaTime;
            _lastAirTime = currentAirTime;
        }

        // ==========================================
        // 陸地跑步 / 推動物件音效 Loop 控制 (微包絡淡入淡出，消除波形硬切雜音與延遲)
        // ==========================================
        if (footstepSFX != null && !isUnderwater)
        {
            if (_footstepSource == null)
            {
                _footstepSource = gameObject.AddComponent<AudioSource>();
                _footstepSource.clip = footstepSFX;
                _footstepSource.loop = true;
                _footstepSource.playOnAwake = false;
                _footstepSource.volume = 0f;
            }
            else if (_footstepSource.clip != footstepSFX)
            {
                _footstepSource.clip = footstepSFX;
            }

            bool shouldPlayFootstep = isGrounded && !isJumping && !IsControlLocked && (Mathf.Abs(moveInput) > 0.1f || isPushing);
            float maxFootstepVol = AudioManager.ScaleSfx(sfxVolume * 0.75f);
            float targetFootstepVol = shouldPlayFootstep ? maxFootstepVol : 0f;

            // 50ms 快速微淡入淡出，保證無延遲且絕無波形截斷雜音
            _footstepSource.volume = Mathf.MoveTowards(_footstepSource.volume, targetFootstepVol, Time.deltaTime * (maxFootstepVol / 0.05f));

            if (_footstepSource.volume > 0.001f)
            {
                if (!_footstepSource.isPlaying) _footstepSource.Play();
            }
            else if (_footstepSource.isPlaying && !shouldPlayFootstep)
            {
                _footstepSource.Stop();
            }
        }
        else if (_footstepSource != null && _footstepSource.isPlaying)
        {
            _footstepSource.Stop();
            _footstepSource.volume = 0f;
        }

        // ==========================================
        // 水下游泳音效 Loop 控制 (只要在水下且有移動/上浮按鍵就播放)
        // ==========================================
        if (isUnderwater && swimSFX != null)
        {
            if (_swimSource == null)
            {
                _swimSource = gameObject.AddComponent<AudioSource>();
                _swimSource.clip = swimSFX;
                _swimSource.loop = true;
                _swimSource.playOnAwake = false;
                _swimSource.volume = sfxVolume * 0.85f;
            }
            else if (_swimSource.clip != swimSFX)
            {
                _swimSource.clip = swimSFX;
            }

            _swimSource.volume = AudioManager.ScaleSfx(sfxVolume * 0.85f);

            // 只有當玩家主動按下 WASD、方向鍵或空白鍵時才判定為正在划水移動
            bool hasActiveSwimInput = (Mathf.Abs(moveInput) > 0.1f)
                                   || Input.GetKey(KeyCode.W) 
                                   || Input.GetKey(KeyCode.S) 
                                   || Input.GetKey(KeyCode.A) 
                                   || Input.GetKey(KeyCode.D) 
                                   || Input.GetKey(KeyCode.Space) 
                                   || Input.GetKey(KeyCode.UpArrow) 
                                   || Input.GetKey(KeyCode.DownArrow) 
                                   || Input.GetKey(KeyCode.LeftArrow) 
                                   || Input.GetKey(KeyCode.RightArrow);

            bool isMovingInWater = hasActiveSwimInput && !IsControlLocked;
            if (isMovingInWater)
            {
                if (!_swimSource.isPlaying) _swimSource.Play();
            }
            else
            {
                if (_swimSource.isPlaying) _swimSource.Stop();
            }
        }
        else if (_swimSource != null && _swimSource.isPlaying)
        {
            _swimSource.Stop();
        }

        _wasGroundedLastFrame = isGrounded;

        // ==========================================
        // 2. 動畫強行控制 (直接程式碼接管播放)
        // ==========================================
        if (animator != null)
        {
            animator.applyRootMotion = false; // 每幀維持關閉 Root Motion，確保玩家速度由程式與 Physics 主導

            // 自動為未來的游泳/推動 Animator 傳遞狀態標籤 (若 Animator Controller 有對應 Parameter 則更新)
            foreach (AnimatorControllerParameter param in animator.parameters)
            {
                if (param.name == "isUnderwater") animator.SetBool("isUnderwater", isUnderwater);
                if (param.name == "isSwimming") animator.SetBool("isSwimming", isSwimming);
                if (param.name == "isPushing") animator.SetBool("isPushing", isPushing);
                if (param.name == "Pushing") animator.SetBool("Pushing", isPushing);
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
                    // 陸地移動（包含斜坡）：保持正立左右朝向，絕不往 Z 軸深處傾倒穿透背景貼圖
                    animator.transform.rotation = Quaternion.Slerp(animator.transform.rotation, baseRotation, Time.deltaTime * 12f);
                }
            }

            string targetAnim = "Idle";

            // 判斷是否應該播放墜落動畫 (加入了延遲時間與掉落速度的容錯閥值)
            bool isFalling = !isGrounded && (currentAirTime >= fallAnimationDelay) && (rb.linearVelocity.y < fallVelocityThreshold);

            // 判斷是否有水平速度或輸入
            bool hasHorizontalSpeed = Mathf.Abs(rb.linearVelocity.x) > 0.1f || Mathf.Abs(moveInput) > 0.1f;

            // 判斷鍵盤是否有按下任何移動按鍵 (A, D, W, Space, 方向鍵)
            bool hasKeyboardInput = Mathf.Abs(moveInput) > 0.05f 
                                 || Input.GetKey(KeyCode.W) 
                                 || Input.GetKey(KeyCode.Space) 
                                 || Input.GetKey(KeyCode.A) 
                                 || Input.GetKey(KeyCode.D) 
                                 || Input.GetKey(KeyCode.LeftArrow) 
                                 || Input.GetKey(KeyCode.RightArrow)
                                 || Input.GetKey(KeyCode.UpArrow);

            if (isUnderwater)
            {
                // 【水下絕對邏輯】：只要鍵盤沒有按鍵在動 ➔ 100% 播放 Treading Water；鍵盤有按鍵在動 ➔ 才播放 Swimming
                if (hasKeyboardInput)
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
                else if (isPushing)
                {
                    targetAnim = "Pushing";
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

            // 【強制接管播放】使用 Play 直接切換指定 State (加入安全保護)
            if (currentAnimState != targetAnim)
            {
                try
                {
                    animator.Play(targetAnim);
                    currentAnimState = targetAnim;
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[動畫安全保護] 找不到動畫 State '{targetAnim}'，跳過切換。Error: {e.Message}");
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

        // 防呆校正：若未被狼咬或速度異常為0，強制重置為 baseSpeed
        if (attachedWolvesCount == 0 || currentSpeed <= 0f)
        {
            currentSpeed = baseSpeed;
        }

        float finalSpeed = currentSpeed * (isUnderwater ? underwaterHorizontalSpeedMultiplier : 1.0f);
        if (isWindSuctionActive)
        {
            finalSpeed = windSuctionSpeed;
        }
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
            rb.useGravity = true;
            rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, rb.linearVelocity.z);
        }
        else
        {
            bool isOnSlope = isGrounded && !isJumping && (currentSlopeAngle > 0.5f && currentSlopeAngle < 60f);

            // 計算逆風推力偏移量
            float windOffset = 0f;
            if (_windPushTimer > 0f)
            {
                _windPushTimer -= Time.deltaTime;
                windOffset = _windPushVelocityX;
            }

            // ★ 風推卡住偵測 (不依賴射線，撞到什麼都有效)：
            //   被風推著、卻連續數幀幾乎沒有實際位移 → 一定是被石柱/牆面擋住了。
            //   此時若繼續每幀把速度灌進障礙物裡，PhysX 會不斷把玩家擠出來，那就是抖動的來源。
            //   直接把風推視為 0，玩家就穩穩貼著障礙物不動。
            if (IsWindPushBlocked(windOffset))
            {
                windOffset = 0f;
            }

            if (isOnSlope)
            {
                // ★★★ 核心修復：在斜坡上關閉 Unity PhysX 重力！
                rb.useGravity = false;

                if (Mathf.Abs(moveInput) > 0.05f)
                {
                    // 玩家主動按鍵移動：貼合斜坡移動並疊加逆風阻力
                    Vector3 moveDir = new Vector3(Mathf.Sign(moveInput), 0, 0);
                    Vector3 slopeDir = Vector3.ProjectOnPlane(moveDir, groundHit.normal).normalized;

                    Vector3 targetVelocity = new Vector3(
                        slopeDir.x * finalSpeed * Mathf.Abs(moveInput) + windOffset,
                        slopeDir.y * finalSpeed * Mathf.Abs(moveInput),
                        0f
                    );

                    if (activeMovingPlatform != null)
                    {
                        targetVelocity.x += activeMovingPlatform.Velocity.x;
                    }

                    rb.linearVelocity = targetVelocity;
                }
                else if (_externalPushTimer > 0f)
                {
                    _externalPushTimer -= Time.deltaTime;
                    Vector3 pushSlopeDir = Vector3.ProjectOnPlane(_externalPushVelocity, groundHit.normal);
                    float pushX = pushSlopeDir.x + windOffset;

                    // 斜坡上同樣要防止被外力/風推進石柱裡造成抖動
                    if (IsBlockedByObstacle(pushX))
                    {
                        rb.linearVelocity = new Vector3(0f, 0f, 0f);
                    }
                    else
                    {
                        rb.linearVelocity = new Vector3(pushX, pushSlopeDir.y, 0f);
                    }
                }
                else if (Mathf.Abs(windOffset) > 0.01f)
                {
                    // 在斜坡上受到風吹：順著斜坡向後平滑滑動
                    // ★ 若後方緊貼不可推的石柱/牆面，停止繼續施加風推速度，否則 PhysX 每幀把玩家擠出來就是抖動
                    if (IsBlockedByObstacle(windOffset))
                    {
                        rb.linearVelocity = Vector3.zero;
                    }
                    else
                    {
                        Vector3 windDir = new Vector3(Mathf.Sign(windOffset), 0, 0);
                        Vector3 windSlopeDir = Vector3.ProjectOnPlane(windDir, groundHit.normal).normalized;
                        rb.linearVelocity = windSlopeDir * Mathf.Abs(windOffset);
                    }
                }
                else
                {
                    // 靜止站在斜坡上：完全消除所有軸向速度，剛體穩固靜止於斜坡上
                    Vector3 targetVelocity = Vector3.zero;
                    if (activeMovingPlatform != null)
                    {
                        targetVelocity.x += activeMovingPlatform.Velocity.x;
                    }
                    rb.linearVelocity = targetVelocity;
                }
            }
            else
            {
                // 平地、空中或水下：開啟正常重力
                rb.useGravity = true;

                float targetX = (moveInput * finalSpeed) + windOffset;

                // 夾角推移緩衝：如果玩家在平地受到巨石撞擊且未按移動鍵，順應巨石推力平滑滑動
                if (Mathf.Abs(moveInput) <= 0.05f && _externalPushTimer > 0f)
                {
                    _externalPushTimer -= Time.deltaTime;
                    targetX = (_externalPushVelocity.x * 0.85f) + windOffset;
                }

                // ★★★ 實體障礙物/石柱防穿透夾角緩衝判定 (防止強風或推力把玩家頂進不可推動石柱牆面造成的 PhysX 每幀穿透反彈劇烈抖動)
                if (IsBlockedByObstacle(targetX))
                {
                    targetX = 0f; // 緊貼非可推石柱/障礙物時停止繼續施加擠壓速度，完全消除抖動！
                }

                Vector3 targetVelocity = new Vector3(targetX, rb.linearVelocity.y, rb.linearVelocity.z);

                if (isGrounded && !isJumping && !isUnderwater && Mathf.Abs(moveInput) <= 0.05f && _externalPushTimer <= 0f && Mathf.Abs(windOffset) <= 0.01f && Mathf.Abs(rb.linearVelocity.y) < 0.1f)
                {
                    targetVelocity.y = 0f;
                }

                if (activeMovingPlatform != null)
                {
                    targetVelocity.x += activeMovingPlatform.Velocity.x;
                }

                rb.linearVelocity = targetVelocity;
            }
        }

        // 水下隱形體力扣除與回復邏輯
        bool isPressingSwimUp = !IsControlLocked && isUnderwater && (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.UpArrow));

        if (isPressingSwimUp && !isSwimExhausted)
        {
            currentSwimStamina -= Time.deltaTime;
            isSwimming = true;

            if (currentSwimStamina <= 0f)
            {
                currentSwimStamina = 0f;
                isSwimExhausted = true;
                exhaustionTimer = swimExhaustionCooldown;
                Debug.Log("【水下體力機制】連續向上游泳體力耗盡，適時斷開向上推進！");
            }
        }
        else
        {
            if (isSwimExhausted)
            {
                exhaustionTimer -= Time.deltaTime;
                if (exhaustionTimer <= 0f)
                {
                    isSwimExhausted = false;
                    Debug.Log("【水下體力機制】力竭冷卻結束，恢復向上游泳機能！");
                }
            }

            // 放開按鍵或著地時，隱形體力逐漸自動恢復
            if (isGrounded || !isPressingSwimUp)
            {
                currentSwimStamina = Mathf.Min(maxSwimStamina, currentSwimStamina + Time.deltaTime * swimStaminaRegenRate);
            }
        }

        // 陸地跳躍 (非水下、非演出中、非重生中)
        bool _jumpPressed = Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.Space);

        // ★按了跳躍卻沒起跳時，直接指名是哪個條件擋住的
        if (_jumpPressed && (IsControlLocked || isUnderwater || !isGrounded))
        {
            // 一行寫完，Console 不用展開就看得到
            Debug.LogWarning("🚫跳不了 froz=" + (isCutsceneFrozen ? "T" : "F")
                + " hard=" + (IsHardCutsceneLocked ? "T" : "F")
                + "(生" + (PlayerRespawnSystem.IsAnyRespawning ? "T" : "F")
                + " 鏡" + (MirrorWallAbsorbCutscene.IsAnyCutsceneRunning ? "T" : "F")
                + " 怪" + (ShadowMonsterController.IsAnyRevealRunning ? "T" : "F")
                + " 卡" + ((StoryCardPlayer.Instance != null && StoryCardPlayer.Instance.IsPlaying) ? "T" : "F") + ")"
                + " 水=" + (isUnderwater ? "T" : "F")
                + " 地=" + (isGrounded ? "T" : "F")
                + " 跳中=" + (isJumping ? "T" : "F")
                + " kine=" + ((rb != null && rb.isKinematic) ? "T" : "F")
                + " y=" + (rb != null ? rb.linearVelocity.y.ToString("F1") : "-")
                + " x=" + transform.position.x.ToString("F1"));
        }

        if (!IsControlLocked && !isUnderwater && _jumpPressed && isGrounded)
        {
            // 陸地標準跳躍
            isJumping = true;
            rb.useGravity = true; // 跳躍時立即還原重力
            rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0, rb.linearVelocity.z);
            rb.AddForce(Vector3.up * jumpForce, ForceMode.VelocityChange);

            // ★ 起跳瞬間零延遲切斷腳步聲音量，完美銜接起跳音效！
            if (_footstepSource != null)
            {
                _footstepSource.volume = 0f;
                _footstepSource.Stop();
            }

            // 播放起跳音效
            if (jumpSFX != null && AudioManager.Instance != null)
            {
                AudioManager.Instance.PlaySFX(jumpSFX, sfxVolume);
            }
        }
    }


    void LateUpdate()
    {
        // ==========================================
        // 【最高層級防破圖系統】確保玩家絕對不出界
        // ==========================================
        if (isStrictLockingX)
        {
            Vector3 finalPos = transform.position;
            finalPos.x = lockedXValue;
            finalPos.z = initialZ;
            transform.position = finalPos;
        }
        else if (Mathf.Abs(transform.position.z - initialZ) > 0.01f)
        {
            Vector3 finalPos = transform.position;
            finalPos.z = initialZ;
            transform.position = finalPos;
        }

        // ==========================================
        // 攝影機 Y 軸避震系統
        // ==========================================
        if (smoothCameraY && cameraTarget != null)
        {
            GameObject customTarget = GameObject.Find("CameraFollowTarget");
            if (customTarget != null)
            {
                cameraTarget.position = customTarget.transform.position;
            }
            else
            {
                // ★ X 和 Z 軸始終跟緊玩家真實位置（包含 FallingBackground 墜落中）
                float targetX = transform.position.x;
                float targetZ = transform.position.z;

                float currentDamping;
                if (freezeHorizontal && !isGrounded)
                {
                    // FallingBackground 墜落中：Y 軸也幾乎無延遲，確保攝影機不掉隊
                    currentDamping = 0.01f;
                }
                else
                {
                    currentDamping = cameraYDamping;
                }

                // Y 軸 SmoothDamp（吸收跳躍碎震）；X/Z 無延遲死死跟著玩家
                float newY = Mathf.SmoothDamp(cameraTarget.position.y, transform.position.y, ref _smoothYVelocity, currentDamping);

                if (clampSkyBackgroundHeight)
                {
                    // 限制在 SkyBackground 高度範圍內 (不論主角上下移動，鏡頭視野絕不超出 SkyBackground 頂部與底部)
                    // 快取計算 SkyBackground 包圍盒，避免每幀執行全場景 FindObjectsByType 造成卡頓
                    if (!_hasCachedSkyBounds || Time.time >= _nextSkyBoundsCheckTime)
                    {
                        _nextSkyBoundsCheckTime = Time.time + 2.0f;
                        _cachedSkyBounds = new Bounds();
                        _hasCachedSkyBounds = false;

                        GameObject[] skyGos = GameObject.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
                        foreach (var go in skyGos)
                        {
                            if (go != null && go.name.Contains("SkyBackground") && go.activeInHierarchy)
                            {
                                SpriteRenderer sr = go.GetComponent<SpriteRenderer>();
                                Collider col = go.GetComponent<Collider>();
                                Bounds b = new Bounds();
                                if (sr != null && sr.sprite != null) b = sr.bounds;
                                else if (col != null) b = col.bounds;

                                if (b.size.sqrMagnitude > 0.1f)
                                {
                                    if (!_hasCachedSkyBounds) { _cachedSkyBounds = b; _hasCachedSkyBounds = true; }
                                    else { _cachedSkyBounds.Encapsulate(b); }
                                }
                            }
                        }
                    }

                    if (_hasCachedSkyBounds)
                    {
                        Camera mainCam = Camera.main;
                        float halfHeight = (mainCam != null && mainCam.orthographic) ? mainCam.orthographicSize : 10f;
                        float skyMinY = _cachedSkyBounds.min.y + halfHeight;
                        float skyMaxY = _cachedSkyBounds.max.y - halfHeight;
                        if (skyMinY <= skyMaxY) newY = Mathf.Clamp(newY, skyMinY, skyMaxY);
                        else newY = _cachedSkyBounds.center.y;
                    }
                }

                cameraTarget.position = new Vector3(targetX, newY, targetZ);
            }
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
    // 抓取物件邏輯 (Pull Range 可在 Inspector 自訂調整)
    // ==========================================
    void TryGrabObject()
    {
        Vector3 center = (playerCollider != null) ? playerCollider.bounds.center : transform.position;
        Vector3 extents = (playerCollider != null) ? playerCollider.bounds.extents : new Vector3(0.6f, 1.0f, 0.6f);
        Vector3 rayOrigin = center + facingDirection * (extents.x * 0.75f);

        Debug.DrawRay(rayOrigin, facingDirection * pullRange, Color.red, 2f);
        RaycastHit[] hits = Physics.SphereCastAll(rayOrigin, 0.4f, facingDirection, pullRange);
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider.gameObject == this.gameObject || hit.collider.transform.IsChildOf(transform)) continue;
            if (IsPushableObject(hit.collider.gameObject, hit.collider))
            {
                // 優先抓取帶有 Rigidbody 的根物件
                GameObject targetObj = hit.collider.gameObject;
                if (targetObj.GetComponent<Rigidbody>() == null && targetObj.GetComponentInParent<Rigidbody>() != null)
                {
                    targetObj = targetObj.GetComponentInParent<Rigidbody>().gameObject;
                }

                pulledObject = targetObj;
                pulledObject.transform.SetParent(this.transform);
                Rigidbody objRb = pulledObject.GetComponent<Rigidbody>();
                if (objRb != null)
                {
                    objRb.isKinematic = true;
                }
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

    private Vector3 _externalPushVelocity = Vector3.zero;
    private float _externalPushTimer = 0f;
    private float _windPushVelocityX = 0f;
    private float _windPushTimer = 0f;

    /// <summary>
    /// 接收來自荒漠強風的逆風推力，平滑融入玩家運動學速度，徹底消除物理 AddForce 產生的抽搐與抖動！
    /// </summary>
    public void ApplyWindPush(float pushSpeedX)
    {
        _windPushVelocityX = pushSpeedX;
        _windPushTimer = 0.15f; // 短暫維持緩衝
    }

    /// <summary>
    /// 前方是否貼著「不可推動的實體障礙物」(石柱、牆面)。
    /// 用於在被風/外力推向障礙物時停止繼續施加擠壓速度，避免 PhysX 每幀穿透反彈造成抖動。
    /// ★ 射線長度改用碰撞體實際 bounds 寬度 (原本用 GetPlayerRadius() 在膠囊/子物件縮放時會算得太短而射不到)。
    /// </summary>
    private bool IsBlockedByObstacle(float directionX)
    {
        if (Mathf.Abs(directionX) < 0.01f) return false;
        if (playerCollider == null) playerCollider = GetComponent<Collider>();
        if (playerCollider == null) return false;

        int obstacleMask = ~LayerMask.GetMask("Player", "Ignore Raycast", "UI");
        Bounds b = playerCollider.bounds;
        Vector3 centerPos = b.center;

        Vector3 dir = directionX < 0f ? Vector3.left : Vector3.right;
        float normalSignNeeded = directionX < 0f ? 1f : -1f;
        float rayLen = b.extents.x + 0.25f;

        // 多點高度取樣：石柱造型不規則，只掃兩點很容易剛好射空
        float[] heights = { -0.45f, -0.15f, 0.15f, 0.45f };

        foreach (float h in heights)
        {
            Vector3 origin = centerPos + Vector3.up * (b.extents.y * h);
            if (!Physics.Raycast(origin, dir, out RaycastHit hit, rayLen, obstacleMask, QueryTriggerInteraction.Ignore)) continue;
            if (hit.collider == null || hit.collider.isTrigger) continue;
            if (IsPushableObject(hit.collider.gameObject, hit.collider)) continue;
            if (hit.normal.x * normalSignNeeded > 0.15f) return true;
        }

        return false;
    }

    // ── 風推卡住偵測 ───────────────────────────────────────────────
    /// <summary>
    /// 重置游泳體力。重生時必須呼叫：
    /// 體力原本只在 Start() 設定一次，若玩家是在力竭狀態下溺斃，重生後仍然是力竭，
    /// 一浮上來就掉下去，很容易直接進入「重生 → 沉底 → 再溺斃」的死亡迴圈。
    /// </summary>
    public void ResetSwimStamina()
    {
        currentSwimStamina = maxSwimStamina;
        isSwimExhausted = false;
        exhaustionTimer = 0f;
        isSwimming = false;
    }

    private float _windStuckLastX;
    private float _windStuckTimer;
    private bool _windStuckInitialized;

    /// <summary>
    /// 被風推著但實際上幾乎沒有位移，代表被實體障礙物擋住了。
    /// 不依賴射線，撞到石柱、牆面、任何造型的碰撞體都有效。
    /// </summary>
    private bool IsWindPushBlocked(float windOffset)
    {
        if (Mathf.Abs(windOffset) < 0.01f)
        {
            _windStuckTimer = 0f;
            _windStuckLastX = transform.position.x;
            _windStuckInitialized = true;
            return false;
        }

        if (!_windStuckInitialized)
        {
            _windStuckLastX = transform.position.x;
            _windStuckInitialized = true;
            return false;
        }

        float movedX = Mathf.Abs(transform.position.x - _windStuckLastX);
        _windStuckLastX = transform.position.x;

        // 這一幀理論上應該被推動的距離
        float expectedX = Mathf.Abs(windOffset) * Time.deltaTime;

        if (expectedX > 0.0001f && movedX < expectedX * 0.3f)
        {
            _windStuckTimer += Time.deltaTime;
        }
        else
        {
            _windStuckTimer = 0f;
        }

        // 連續 0.06 秒推不動才判定卡住，避免正常減速的瞬間被誤判
        bool blocked = _windStuckTimer >= 0.06f;

        if (blocked && logWindBlockDebug && Time.frameCount % 30 == 0)
        {
            Debug.Log($"[風推卡住] 風速 {windOffset:F2}，本幀實際位移 {movedX:F4} (預期 {expectedX:F4})，判定被擋住，暫停施加風推。");
        }

        return blocked;
    }

    private float GetPlayerRadius()
    {
        if (playerCollider == null) playerCollider = GetComponent<Collider>();
        if (playerCollider is CapsuleCollider cc) return cc.radius * transform.localScale.x;
        if (playerCollider is BoxCollider bc) return bc.size.x * 0.5f * transform.localScale.x;
        return 0.4f;
    }

    /// <summary>
    /// 接收來自落石/巨石的外部推力，順勢沿斜坡下滑化解碰撞抖動
    /// </summary>
    public void ApplyExternalSlopePush(Vector3 pushVelocity)
    {
        _externalPushVelocity = pushVelocity;
        _externalPushTimer = 0.12f; // 維持緩衝
    }

    public Transform GetCameraTarget()
    {
        GameObject customTarget = GameObject.Find("CameraFollowTarget");
        if (customTarget != null) return customTarget.transform;
        return this.transform;
    }

    /// <summary>
    /// 瞬間傳送玩家並重置攝影機跟隨點，防止 Y 軸追蹤延遲與碎震
    /// </summary>
    public void WarpTo(Vector3 position)
    {
        transform.position = position;
        if (rb != null)
        {
            rb.position = position;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        if (cameraTarget != null)
        {
            cameraTarget.position = position;
        }

        // 重置身上所有被狼咬住的計數，並恢復速度
        attachedWolvesCount = 0;
        CalculateSpeed();
        _smoothYVelocity = 0f; // 重置 Y 軸平滑速度快取
        
        isStrictLockingX = false;
        freezeHorizontal = false;
        
        // 只有在非重生期間才解鎖劇情凍結 (防止黑屏期間可按 WASD 亂跑)
        if (!PlayerRespawnSystem.IsAnyRespawning)
        {
            isCutsceneFrozen = false;
        }
        
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
            // ★ 防止重複觸發：同一次墜落只允許進入一次
            if (_fallingBGEntered) return;
            _fallingBGEntered = true;

            Debug.Log("碰觸到 FallingBackground！鎖死橫向移動，開始順暢高速向下墜落！");
            freezeHorizontal = true;

            // ★ 核心修復：絕不把向下速度歸零！保留既有動量並確保向下的重力加速度
            float currentDownSpeed = rb.linearVelocity.y;
            float targetDownSpeed = Mathf.Min(currentDownSpeed, -8f); // 至少維持 -8f 以上的向下加速衝力，絕不減速！
            rb.linearVelocity = new Vector3(0f, targetDownSpeed, 0f);

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
            _fallingBGEntered = false; // 重置 FallingBackground 觸發 flag，供下次使用

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
        GameObject customTarget = GameObject.Find("CameraFollowTarget");
        if (customTarget != null)
        {
            target = customTarget.transform;
        }
        else if (target == null)
        {
            target = this.transform;
        }

        // 尋找「所有」新版 CinemachineCamera 並修改追蹤目標
        var vcams3 = Object.FindObjectsByType<CinemachineCamera>(FindObjectsSortMode.None);
        foreach(var vcam in vcams3)
        {
            if (vcam != null)
            {
                var t = vcam.Target;
                t.TrackingTarget = target;
                t.LookAtTarget = target;
                t.CustomLookAtTarget = true;
                vcam.Target = t;
                vcam.Follow = target;
            }
        }

        var vcamsLegacy = Object.FindObjectsByType<CinemachineVirtualCamera>(FindObjectsSortMode.None);
        foreach(var vcam in vcamsLegacy)
        {
            if (vcam != null)
            {
                vcam.Follow = target;
                vcam.Priority = 100;
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
            // 確保重力開啟
            rb.useGravity = true;

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

            Vector3 vel = rb.linearVelocity;

            // 3. 水下游泳推進與下潛控制 (W/Space 向上游，S/下方向鍵 主動下潛)
            bool isPressingSwimUp = !IsControlLocked && (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.UpArrow));
            bool isPressingSwimDown = !IsControlLocked && (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow));

            if (isPressingSwimUp && !isSwimExhausted)
            {
                // 按住 W / Space / 上方向鍵 向上游泳推進
                vel.y = Mathf.MoveTowards(vel.y, underwaterSwimUpSpeed, Time.fixedDeltaTime * 12f);
            }
            else if (isPressingSwimDown)
            {
                // 按住 S / 下方向鍵 主動向下快速下潛
                vel.y = Mathf.MoveTowards(vel.y, -underwaterSwimUpSpeed, Time.fixedDeltaTime * 12f);
            }
            else if (vel.y > 0.1f)
            {
                // 放開向上按鍵時，向上速度平滑減速 (水阻)，之後順應水中重力自然向下沉降
                vel.y = Mathf.MoveTowards(vel.y, 0f, underwaterVerticalDrag * Time.fixedDeltaTime * 4f);
            }

            // 4. 水中最大自然沉降速度動態限制 (當沒有按 S 主動下潛時，限制自然下沉速度)
            if (!isPressingSwimDown)
            {
                float effectiveMaxFallSpeed = enableDepthBuoyancyPhysics && (transform.position.y < neutralBuoyancyY)
                    ? underwaterMaxFallSpeed * (1f + (neutralBuoyancyY - transform.position.y) * 0.05f)
                    : underwaterMaxFallSpeed;

                if (vel.y < effectiveMaxFallSpeed)
                {
                    vel.y = effectiveMaxFallSpeed;
                }
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

    /// <summary>
    /// 精確地面與斜坡多點射線偵測系統 (多點掃描 BoxCollider 底部與斜坡法線)
    /// </summary>
    private bool CheckGrounded(out RaycastHit bestHit, out float slopeAngle)
    {
        bestHit = new RaycastHit();
        slopeAngle = 0f;

        if (playerCollider == null)
        {
            playerCollider = GetComponent<Collider>();
            if (playerCollider == null) playerCollider = GetComponentInChildren<Collider>();
            if (playerCollider == null) playerCollider = GetComponentInParent<Collider>();
        }

        if (playerCollider == null)
        {
            if (Physics.Raycast(transform.position, Vector3.down, out bestHit, 1.5f, ~0, QueryTriggerInteraction.Ignore))
            {
                slopeAngle = Vector3.Angle(Vector3.up, bestHit.normal);
                return true;
            }
            return false;
        }

        Vector3 center = playerCollider.bounds.center;
        float extentsY = playerCollider.bounds.extents.y;
        float extentsX = playerCollider.bounds.extents.x * 0.75f;
        float rayLength = extentsY + 0.35f;

        // 3 點向下射線 (中央、左腳邊緣、右腳邊緣)
        Vector3[] checkPoints = new Vector3[]
        {
            center,
            center + Vector3.left * extentsX,
            center + Vector3.right * extentsX
        };

        float minDistance = float.MaxValue;
        bool found = false;
        int layerMask = ~LayerMask.GetMask("Ignore Raycast");

        foreach (var origin in checkPoints)
        {
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, rayLength, layerMask, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider == playerCollider || hit.collider.transform.IsChildOf(transform)) continue;

                if (hit.distance < minDistance)
                {
                    minDistance = hit.distance;
                    bestHit = hit;
                    found = true;
                }
            }
        }

        if (!found)
        {
            Vector3 halfExtents = new Vector3(playerCollider.bounds.extents.x * 0.75f, 0.05f, playerCollider.bounds.extents.z * 0.75f);
            if (Physics.BoxCast(center, halfExtents, Vector3.down, out RaycastHit boxHit, Quaternion.identity, extentsY + 0.2f, layerMask, QueryTriggerInteraction.Ignore))
            {
                if (boxHit.collider != playerCollider && !boxHit.collider.transform.IsChildOf(transform))
                {
                    bestHit = boxHit;
                    found = true;
                }
            }
        }

        if (found)
        {
            slopeAngle = Vector3.Angle(Vector3.up, bestHit.normal);
        }

        return found;
    }

    // ==========================================
    // 推動物件 (Pushable) 判定機制
    // ==========================================
    private bool IsPushableObject(GameObject go, Collider col)
    {
        if (go == null && col == null) return false;
        if (go != null && (go == gameObject || go.transform.IsChildOf(transform))) return false;
        if (col != null && (col == playerCollider || col.transform.IsChildOf(transform))) return false;

        // 1. 直接 Tag 檢測
        if (go != null && go.CompareTag("Pushable")) return true;
        if (col != null && col.CompareTag("Pushable")) return true;

        // 2. 剛體 Tag 檢測
        if (col != null && col.attachedRigidbody != null && col.attachedRigidbody.CompareTag("Pushable")) return true;

        // 3. 向上遍歷父層階層直到 Root (確保石球或組合 Model 即使 Tag 掛在 Root 也能被辨識)
        Transform t = (go != null) ? go.transform : (col != null ? col.transform : null);
        while (t != null)
        {
            if (t.CompareTag("Pushable")) return true;
            t = t.parent;
        }

        return false;
    }

    /// <summary>
    /// 精確確認 Pushable 物件是否位於玩家「正前方」（排除站在物件頂部/腳底下方的情況）
    /// </summary>
    private bool IsPushableInFront(Collider col, Vector3 playerCenter, Vector3 playerExtents)
    {
        if (col == null) return false;
        if (!IsPushableObject(col.gameObject, col)) return false;

        float playerFeetY = playerCenter.y - playerExtents.y;
        float playerHeadY = playerCenter.y + playerExtents.y;

        // ★ 關鍵排除條件：Pushable 物件頂部必須明顯高於玩家腳底（至少 0.35m，達到小腿以上），才視為前方障礙
        // 若物件頂部在玩家腳底附近 (col.bounds.max.y <= playerFeetY + 0.35f)，代表玩家正站在該物件頂面，絕不判定為前方推動！
        if (col.bounds.max.y <= playerFeetY + 0.35f)
        {
            return false;
        }

        // 若物件底部高於玩家頭頂，代表在玩家上方懸空，也不算前方推動
        if (col.bounds.min.y >= playerHeadY)
        {
            return false;
        }

        return true;
    }

    private void UpdatePushingDetection(float input)
    {
        if (playerCollider == null)
        {
            playerCollider = GetComponent<Collider>();
            if (playerCollider == null) playerCollider = GetComponentInChildren<Collider>();
            if (playerCollider == null) playerCollider = GetComponentInParent<Collider>();
        }

        // 1. 玩家若沒有輸入推動方向鍵，立即離開 Pushing (鬆開方向鍵立即回歸 Idle/Run)
        if (Mathf.Abs(input) <= 0.05f)
        {
            isPushing = false;
            _collisionPushableTimer = 0f;
            return;
        }

        // 2. 空中跳躍/墜落、水下或鎖定狀態，不能判定為陸地 Pushing
        bool isAirborne = isJumping || (!isGrounded && rb.linearVelocity.y < -3.0f && currentAirTime > 0.25f);
        if (isUnderwater || isAirborne || IsControlLocked)
        {
            isPushing = false;
            _collisionPushableTimer = 0f;
            return;
        }

        Vector3 pushDir = (input > 0f) ? Vector3.right : Vector3.left;
        bool hasPushableInFront = false;

        // 3. 抓取物件中 (LeftShift)
        if (pulledObject != null)
        {
            hasPushableInFront = true;
        }

        Vector3 center = (playerCollider != null) ? playerCollider.bounds.center : transform.position;
        Vector3 extents = (playerCollider != null) ? playerCollider.bounds.extents : new Vector3(0.6f, 1.0f, 0.6f);

        // 4. 前方接觸面盒型範圍檢測 (精確覆蓋從腳踝到胸口的正面接觸區，且嚴格排除腳底頂部)
        if (!hasPushableInFront)
        {
            int pushMask = ~LayerMask.GetMask("Player", "Ignore Raycast", "UI");
            Vector3 boxCenter = new Vector3(center.x + pushDir.x * (extents.x + pushDetectionDistance * 0.5f), center.y, center.z);
            Vector3 boxHalfExtents = new Vector3(pushDetectionDistance * 0.5f, extents.y * 0.85f, extents.z * 0.85f);

            Collider[] hits = Physics.OverlapBox(boxCenter, boxHalfExtents, Quaternion.identity, pushMask, QueryTriggerInteraction.Ignore);
            foreach (var col in hits)
            {
                if (IsPushableInFront(col, center, extents))
                {
                    hasPushableInFront = true;
                    break;
                }
            }
        }

        // 5. 正面多層射線陣列檢測 (膝蓋、腰部、胸口前向掃描，嚴格排除腳底)
        if (!hasPushableInFront)
        {
            int pushMask = ~LayerMask.GetMask("Player", "Ignore Raycast", "UI");
            float rayStartX = center.x + pushDir.x * (extents.x * 0.8f);
            float rayLen = (extents.x * 0.2f) + pushDetectionDistance + 0.1f;

            Vector3[] rayOrigins = new Vector3[]
            {
                new Vector3(rayStartX, center.y - extents.y * 0.4f, center.z), // 小腿/膝蓋
                new Vector3(rayStartX, center.y + extents.y * 0.1f, center.z), // 腰部
                new Vector3(rayStartX, center.y + extents.y * 0.5f, center.z)  // 胸部
            };

            foreach (var origin in rayOrigins)
            {
                if (Physics.Raycast(origin, pushDir, out RaycastHit hit, rayLen, pushMask, QueryTriggerInteraction.Ignore))
                {
                    if (IsPushableInFront(hit.collider, center, extents))
                    {
                        hasPushableInFront = true;
                        break;
                    }
                }
            }
        }

        // 6. 實體碰撞接觸緩衝 (PhysX 剛體側面貼合判定)
        if (!hasPushableInFront && _collisionPushableTimer > 0f)
        {
            hasPushableInFront = true;
        }

        if (_collisionPushableTimer > 0f)
        {
            _collisionPushableTimer -= Time.deltaTime;
        }

        // 最終判定：只要玩家在地面/斜坡、正朝著前方貼近的 Pushable 物件持續輸入，就保持 Pushing！
        isPushing = hasPushableInFront;
    }

    private void CheckPushingCollision(Collision collision)
    {
        if (collision == null || collision.collider == null) return;
        if (IsPushableObject(collision.gameObject, collision.collider))
        {
            if (Mathf.Abs(CurrentMoveInput) > 0.05f)
            {
                Vector3 center = (playerCollider != null) ? playerCollider.bounds.center : transform.position;
                Vector3 extents = (playerCollider != null) ? playerCollider.bounds.extents : new Vector3(0.6f, 1.0f, 0.6f);
                float playerFeetY = center.y - extents.y;

                // ★ 排除站在頂部的碰撞：若 Pushable 物件頂面在玩家腳底附近，不判定為側面推動
                if (collision.collider.bounds.max.y <= playerFeetY + 0.35f) return;

                Vector3 pushDir = (CurrentMoveInput > 0f) ? Vector3.right : Vector3.left;

                // 檢查接觸點：必須是來自側面的水平推動接觸 (排除腳底垂直支撐接觸)
                for (int i = 0; i < collision.contactCount; i++)
                {
                    ContactPoint cp = collision.GetContact(i);

                    // ★ 接觸點不能在腳底（垂直支撐法線向上 cp.normal.y > 0.6f 排除）
                    if (cp.normal.y > 0.6f) continue;

                    float dotNormal = Vector3.Dot(cp.normal, -pushDir);
                    float relX = (cp.point.x - center.x) * pushDir.x;
                    if (dotNormal > 0.3f && relX > 0.05f)
                    {
                        _collisionPushableTimer = 0.2f;
                        break;
                    }
                }
            }
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        CheckPushingCollision(collision);
    }

    private void OnCollisionStay(Collision collision)
    {
        CheckPushingCollision(collision);

        if (!isUnderwater || collision.contactCount == 0) return;

        // 當水下在凹凸不平的岩石夾角間游動時，輔助法線平滑滑動，杜絕在夾角處卡住
        bool hasInput = Mathf.Abs(CurrentMoveInput) > 0.05f || Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.D);
        if (hasInput && rb != null && !rb.isKinematic)
        {
            Vector3 avgNormal = Vector3.zero;
            for (int i = 0; i < collision.contactCount; i++)
            {
                avgNormal += collision.GetContact(i).normal;
            }
            avgNormal = avgNormal.normalized;
            rb.AddForce(new Vector3(avgNormal.x, avgNormal.y, 0f) * 2.0f, ForceMode.Acceleration);
        }
    }
}
