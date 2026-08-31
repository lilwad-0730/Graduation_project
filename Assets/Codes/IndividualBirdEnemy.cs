using UnityEngine;
using System.Collections;

public enum BirdBehavior { DirectPlayer, PlayerOffset, HomingPlayer }
public enum BirdTriggerMode { Both, DistanceDetection, TriggerZoneOrCollisionOnly }

/// <summary>
/// 個別鳥類敵人控制器：
/// 1. 每隻鳥為完全獨立的 AI 實例，各自維護狀態機 (Idle, Warning, Diving, Stuck, Bounced)。
/// 2. 支援距離感應 (DistanceDetection) 與專屬碰撞觸發 (TriggerZoneOrCollisionOnly)，一隻鳥被驚動絕不干擾其他鳥！
/// 3. 相容 living birds 的動畫控制器 (flying, worried, landing, die)。
/// 4. 待機時各自分散進行有機 3D 浮動與微盤旋，不全體盯著玩家，呈現逼真生態感。
/// </summary>
public class IndividualBirdEnemy : MonoBehaviour, IResettable
{
    [Header("獨立攻擊觸發模式 (Individual Attack Trigger)")]
    [Tooltip("此鳥的觸發機制：Both(兩者皆可，預設)、DistanceDetection(距離感應)、TriggerZoneOrCollisionOnly(僅靠碰撞/專屬區域觸發)")]
    public BirdTriggerMode triggerMode = BirdTriggerMode.Both;

    [Tooltip("是否在玩家進入範圍時自動引爆俯衝攻擊？(若關閉則只依靠碰撞/專屬TriggerZone)")]
    public bool autoDetectPlayer = true;

    [Tooltip("自動偵測玩家的攻擊距離 (米，預設 12)")]
    public float detectionRange = 12f;

    [Header("鳥類敵人類型與移動")]
    [Tooltip("此隻鳥的俯衝行為類型：DirectPlayer(直撲玩家當下位置，可閃避)、PlayerOffset(偏移攻擊)、HomingPlayer(動態追蹤)")]
    public BirdBehavior behaviorType = BirdBehavior.DirectPlayer;

    [Tooltip("俯衝攻擊的速度 (預設 9.8，降低約 18% 提供玩家更舒適的反應時間)")]
    public float diveSpeed = 9.8f;

    [Tooltip("【偏移模式限定】X 軸的偏移量 (預設 3)")]
    public float targetOffset = 3f;

    [Header("時間設定")]
    [Tooltip("發出聲音警報到開始俯衝的時間 (秒，預設 1.5)")]
    public float warningDuration = 1.5f;

    [Tooltip("撞擊地面後卡住停留的時間 (秒，預設 5)")]
    public float stuckDuration = 5f;

    [Tooltip("卡住後漸暗消失的時間 (秒，預設 1)")]
    public float fadeDuration = 1f;

    [Header("動畫控制 (對應 living birds 的真實動畫 State 名稱)")]
    [Tooltip("待機/盤旋動畫名稱 (預設 flying)")]
    public string idleAnimName = "flying";

    [Tooltip("警報/準備俯衝動畫名稱 (預設 worried)")]
    public string warningAnimName = "worried";

    [Tooltip("高速俯衝動畫名稱 (預設 flying)")]
    public string diveAnimName = "flying";

    [Tooltip("撞地卡住動畫名稱 (預設 landing)")]
    public string stuckAnimName = "landing";

    [Tooltip("被護盾彈飛/死亡動畫名稱 (預設 die)")]
    public string dieAnimName = "die";

    [Header("護盾反彈控制 (可在 Inspector 100% 精確掌控)")]
    [Tooltip("反彈向後距離 (米，預設 2.5 米)")]
    public float bounceDistance = 2.5f;

    [Tooltip("反彈拋物線弧度高度 (米，預設 1.2 米)")]
    public float bounceHeight = 1.2f;

    [Tooltip("反彈飛行總時間 (秒，預設 0.6 秒)")]
    public float bounceDuration = 0.6f;

    [Tooltip("反彈旋轉角度 (度，預設 180 度)")]
    public float bounceSpinAngle = 180f;

    [Header("3D 空中 8 字巡航與盤旋 (3D Figure-8 Hover & Patrol)")]
    [Tooltip("是否啟用空中待機時的自然漂浮與 8 字巡航？(預設開啟)")]
    public bool enableIdleHover = true;

    [Header("軌跡尺寸與頻率 (Scale & Speed)")]
    [Tooltip("水平左右滑翔巡航半徑 (米，預設 2.2，在正交鏡頭下左右滑翔清晰流暢)")]
    public float patrolRadiusX = 2.2f;

    [Tooltip("垂直上下呼吸與 8 字起伏高度 (米，預設 0.6)")]
    public float hoverAmplitudeY = 0.6f;

    [Tooltip("前後 3D 環形深度半徑 (米，預設 0.35)")]
    public float patrolRadiusZ = 0.35f;

    [Tooltip("巡航飛行速度/頻率 (預設 0.9)")]
    public float hoverFrequency = 0.9f;

    [Header("飛行形態與姿態 (Flight Morphology)")]
    [Tooltip("8 字型軌跡強度 (0 = 單純左右滑翔，0.5 = 橢圓弧線，1.0 = 完整經典 8 字 (∞) 盤旋，預設 1.0)")]
    [Range(0f, 1f)]
    public float figureEightStrength = 1.0f;

    [Tooltip("微風氣流擾動強度 (0~1，預設 0.15，保持鳥群秩序)")]
    [Range(0f, 1f)]
    public float noiseStrength = 0.15f;

    [Tooltip("迎風轉彎時的自然側傾角 (Banking Tilt，度數，預設 10.0 度)")]
    public float maxBankingAngle = 10.0f;

    [Tooltip("位置平滑過渡速度 (預設 5.0)")]
    public float hoverSmoothSpeed = 5.0f;

    [Tooltip("飛行轉向平滑速度 (預設 4.0)")]
    public float rotationSmoothSpeed = 4.0f;

    [Header("地面與環境偵測")]
    [Tooltip("地面的 Tag (預設 Floor，自動支援 Floor, Ground, Terrain)")]
    public string groundTag = "Floor";

    [Header("音效設定")]
    [Tooltip("俯衝前發出的叫聲音效 (若為空自動載入 crow1.wav)")]
    public AudioClip warningClip;
    [Tooltip("高速俯衝飛行的振翅音效 (若為空自動載入 鳥振翅1.mp3)")]
    public AudioClip flapClip;

    private AudioSource audioSource;
    private Rigidbody rb;
    private Animator animator;
    private SpriteRenderer spriteRenderer;
    private Renderer meshRenderer;
    
    private enum BirdState { Idle, Warning, Diving, Stuck, Bounced }
    private BirdState currentState = BirdState.Idle;

    private Transform playerTrans;
    private Vector3 targetPosition;
    private Vector3 diveDirection;
    private Vector3 originalPosition;
    private Quaternion originalRotation;
    private float hoverRandomOffset = 0f;
    private float postRespawnDelayTimer = 0f;
    private bool hasAttackedOrDied = false;

    private Vector3 originalScale = Vector3.one;

    // 每隻鳥生成時隨機決定一次的專屬特徵 (Runtime Individual Profile)
    private float _indivSpeedMult = 1.0f;
    private float _indivRadiusX = 1.0f;
    private float _indivAmpY = 1.0f;
    private float _indivRadiusZ = 1.0f;
    private float _indivPhase = 0f;
    private float _noiseSeedX, _noiseSeedY;
    private Vector3 _currentHoverOffset = Vector3.zero;
    private float _lastDriftX = 0f;
    private float _currentTiltZ = 0f;

    // 攝影機視角 2D 畫面投影基準軸 (Camera Basis Vectors)
    private Vector3 _camRight = Vector3.right;
    private Vector3 _camUp = Vector3.up;
    private Vector3 _camForward = Vector3.forward;

    private void Awake()
    {
        // 核心防呆：檢查父物件層級是否已有 IndividualBirdEnemy
        // 若父層已有，子層的重複組件必須銷毀，徹底杜絕雙重圓圈與隱形分身！
        IndividualBirdEnemy parentBird = transform.parent != null ? transform.parent.GetComponentInParent<IndividualBirdEnemy>() : null;
        if (parentBird != null && parentBird != this)
        {
            Debug.LogWarning($"[鳥群防呆] 偵測到子物件 '{gameObject.name}' 與父物件 '{parentBird.name}' 重複掛載 IndividualBirdEnemy！已自動銷毀子層重複組件！");
            Destroy(this);
            return;
        }

        // 同一 GameObject 上若有多個組件也清除重複項
        IndividualBirdEnemy[] sameObjBirds = GetComponents<IndividualBirdEnemy>();
        if (sameObjBirds.Length > 1 && sameObjBirds[0] != this)
        {
            Destroy(this);
            return;
        }
    }

    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        
        rb.useGravity = false;
        rb.isKinematic = true;
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY | RigidbodyConstraints.FreezePositionZ;

        EnsureComponents();

        originalPosition = transform.position;
        originalRotation = transform.rotation;
        originalScale = transform.localScale != Vector3.zero ? transform.localScale : Vector3.one;

        // 初始化攝影機基準平面與專屬漂移特徵
        UpdateCameraBasis();
        InitializeHoverProfile();

        // 預設播放待機飛行動畫 (flying)
        PlayAnim(idleAnimName);
    }

    /// <summary>
    /// 動態捕獲當前攝影機視角的畫面基準向量 (Camera.right, Camera.up, Camera.forward)
    /// </summary>
    public void UpdateCameraBasis()
    {
        Camera cam = Camera.main;
        if (cam != null)
        {
            _camRight = cam.transform.right;
            _camUp = cam.transform.up;
            _camForward = cam.transform.forward;
        }
        else
        {
            _camRight = Vector3.right;
            _camUp = Vector3.up;
            _camForward = Vector3.forward;
        }
    }

    /// <summary>
    /// 初始化每隻鳥專屬的隨機漂移參數 (生成時執行一次，確保每隻鳥個體節奏與軌跡獨一無二)
    /// </summary>
    private void InitializeHoverProfile()
    {
        UpdateCameraBasis();

        // 隨機差異收斂至小範圍 (±5% ~ ±7%)，保持鳥群秩序感與同一物種的統一協調性
        _indivSpeedMult = Random.Range(0.93f, 1.07f);
        _indivRadiusX = Random.Range(0.93f, 1.07f);
        _indivAmpY = Random.Range(0.93f, 1.07f);
        _indivRadiusZ = Random.Range(0.90f, 1.10f);

        // 隨機起點相位 (每隻鳥在 8 字軌跡上的不同出發點，杜絕同步)
        _indivPhase = Random.Range(0f, Mathf.PI * 2f);
        _noiseSeedX = Random.Range(0f, 1000f);
        _noiseSeedY = Random.Range(0f, 1000f);

        _currentHoverOffset = Vector3.zero;
        _lastDriftX = 0f;
        _currentTiltZ = 0f;
    }

    private void EnsureComponents()
    {
        if (animator == null)
        {
            animator = GetComponent<Animator>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
        }

        // 強制關閉 Root Motion，防止動畫強行覆蓋或鎖定鳥的 Transform Position
        Animator[] allAnims = GetComponentsInChildren<Animator>(true);
        foreach (var a in allAnims)
        {
            if (a != null) a.applyRootMotion = false;
        }

        if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (meshRenderer == null) meshRenderer = GetComponentInChildren<Renderer>();

        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
        }

#if UNITY_EDITOR
        if (warningClip == null)
        {
            warningClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/living birds/sounds/crow1.wav");
        }
        if (flapClip == null)
        {
            flapClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Music/荒漠/鳥振翅1.mp3");
        }
#endif

        // 多重搜尋策略：防止 Player 沒設 Tag 導致抓不到物件
        if (playerTrans == null)
        {
            GameObject playerObj = GameObject.FindWithTag("Player");
            if (playerObj != null)
            {
                playerTrans = playerObj.transform;
            }
            else
            {
                PlayerMovement pm = FindFirstObjectByType<PlayerMovement>();
                if (pm != null) playerTrans = pm.transform;
                else
                {
                    PlayerRespawnSystem sys = FindFirstObjectByType<PlayerRespawnSystem>();
                    if (sys != null) playerTrans = sys.transform;
                }
            }
        }
    }

    [Header("模型朝向微調 (若 FBX 模型載入時有角度偏移可在此修正)")]
    [Tooltip("模型旋轉偏移角度 (預設 0,0,0)")]
    public Vector3 modelRotationOffset = Vector3.zero;

    private void Update()
    {
        // 核心功能 1：3D 有機空中漂浮與自然微盤旋 (多頻率波形 + Perlin 氣流雜訊)
        if (currentState == BirdState.Idle && enableIdleHover)
        {
            UpdateOrganicIdleHover();
        }

        // 核心功能 2：獨立偵測玩家距離並觸發俯衝 (僅在此鳥設定允許距離感應時運作)
        if (postRespawnDelayTimer > 0f)
        {
            postRespawnDelayTimer -= Time.deltaTime;
            return;
        }

        if (currentState == BirdState.Idle && autoDetectPlayer && triggerMode != BirdTriggerMode.TriggerZoneOrCollisionOnly)
        {
            if (PlayerRespawnSystem.IsAnyRespawning || !PlayerRespawnSystem.IsPlayerMovingAfterRespawn || UmbrellaZone.IsPlayerUnderUmbrella)
            {
                return; // 重生過場中、玩家尚未主動開始移動、或在遮陽傘下安全避難，均不觸發攻擊
            }

            if (playerTrans == null) EnsureComponents();

            if (playerTrans != null)
            {
                // 計算 2.5D 水平距離與 3D 距離
                float xDist = Mathf.Abs(transform.position.x - playerTrans.position.x);
                float totalDist = Vector3.Distance(transform.position, playerTrans.position);

                if (xDist <= detectionRange || totalDist <= detectionRange)
                {
                    Debug.LogWarning($"【鳥群系統】玩家進入此鳥獨立偵測範圍 (水平距離 {xDist:F1}m <= {detectionRange}m)！{gameObject.name} 單獨發起俯衝攻擊！");
                    StartAttackSequence();
                }
            }
        }

        // 核心功能 3：警報姿態控制 (Idle 狀態的飛行旋轉與 Banking 已由 UpdateOrganicIdleHover 統一接管)
        if (currentState == BirdState.Warning)
        {
            if (playerTrans != null)
            {
                float dx = playerTrans.position.x - transform.position.x;
                Vector3 lookDir = dx < 0 ? Vector3.left : Vector3.right;
                Quaternion targetRot = Quaternion.LookRotation(lookDir, Vector3.up) * Quaternion.Euler(modelRotationOffset);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * 8f);
            }
        }
    }

    /// <summary>
    /// 計算 8 字 (∞) / 橢圓 / 巡航的參數化理論路徑座標 (嚴格以 Camera 視角 2D 畫面平面為基準)
    /// </summary>
    public Vector3 GetParametricHoverPosition(Vector3 center, float t)
    {
        // 確保 Camera 投影基準軸已初始化
        if (_camRight == Vector3.zero) UpdateCameraBasis();

        // 1. 畫面水平方向 (Screen X ➔ Camera.right)：主要左右巡航滑翔
        float x = Mathf.Sin(t) * (patrolRadiusX * _indivRadiusX);

        // 2. 畫面垂直方向 (Screen Y ➔ Camera.up)：橫向 8 字 (∞) Lissajous 軌跡 + 週期性滑翔/盤旋形態平滑調變
        // 形態調變週期 (約 3~5 秒一輪)：Figure-8 盤旋 ➔ 左右平滑滑翔 ➔ Figure-8 盤旋 (平滑無縫切換)
        float patternMod = (Mathf.Sin(t * 0.4f) + 1f) * 0.5f; // 0 ~ 1 緩慢平滑波動
        float currentFig8Weight = Mathf.Lerp(0.3f, 1.0f, patternMod) * figureEightStrength;

        float figureEightY = Mathf.Sin(2f * t);
        float glideY = Mathf.Sin(t * 0.5f);
        float y = Mathf.Lerp(glideY * 0.35f, figureEightY, currentFig8Weight) * (hoverAmplitudeY * _indivAmpY);

        // 3. 畫面深度方向 (Screen Z ➔ Camera.forward)：極小微幅輔助 (<= 0.04m，避免在玩家看不到的深度軸浪費運動量)
        float z = Mathf.Cos(t) * (patrolRadiusZ * _indivRadiusZ * 0.1f);

        // 4. 嚴格投射至 Camera 視角畫面平面 (中心點永遠為每隻鳥自己的 Spawn Point / originalPosition)
        return center + (_camRight * x) + (_camUp * y) + (_camForward * z);
    }

    /// <summary>
    /// 執行自然 3D 空中 8 字 (∞) 滑翔與微盤旋算法 (嚴格在 Camera 2D 畫面投影平面上優雅巡航)
    /// </summary>
    private void UpdateOrganicIdleHover()
    {
        float t = Time.time * hoverFrequency * _indivSpeedMult + _indivPhase;

        // 1. 計算純淨的 Camera-Relative Parametric Curve 目標點
        Vector3 rawTargetPos = GetParametricHoverPosition(originalPosition, t);

        // 2. 疊加微幅平滑氣流雜訊 (不破壞主要可視巡航形狀)
        if (noiseStrength > 0.01f)
        {
            float noiseX = (Mathf.PerlinNoise(_noiseSeedX, Time.time * 0.4f * _indivSpeedMult) - 0.5f) * 2f * noiseStrength * (patrolRadiusX * 0.15f);
            float noiseY = (Mathf.PerlinNoise(_noiseSeedY, Time.time * 0.4f * _indivSpeedMult) - 0.5f) * 2f * noiseStrength * (hoverAmplitudeY * 0.15f);
            rawTargetPos += (_camRight * noiseX) + (_camUp * noiseY);
        }

        // 3. 平滑更新鳥的世界座標 (嚴格以 originalPosition 為中心，絕不越界漂移)
        _currentHoverOffset = rawTargetPos - originalPosition;
        Vector3 newPos = Vector3.Lerp(transform.position, rawTargetPos, Time.deltaTime * hoverSmoothSpeed);
        transform.position = newPos;
        if (rb != null && rb.isKinematic)
        {
            rb.position = newPos;
        }

        // 4. 計算切線飛行速度向量 (Velocity Tangent) 以獲取鳥頭精確朝向
        Vector3 nextPathPoint = GetParametricHoverPosition(originalPosition, t + 0.12f);
        Vector3 flightDir = (nextPathPoint - transform.position).normalized;

        if (flightDir.sqrMagnitude > 0.001f)
        {
            // 5. 迎風自然側傾角 (Banking Tilt)：在 8 字兩端迴轉處側傾最明顯
            float turnRate = Mathf.Cos(t);
            float targetTiltZ = -turnRate * maxBankingAngle * Mathf.Sign(Vector3.Dot(flightDir, _camRight));
            _currentTiltZ = Mathf.Lerp(_currentTiltZ, targetTiltZ, Time.deltaTime * 5f);

            // 6. 綜合 LookRotation 與 模型旋轉偏移 (modelRotationOffset)
            Quaternion targetRot = Quaternion.LookRotation(flightDir, _camUp) * Quaternion.Euler(modelRotationOffset + new Vector3(0f, 0f, _currentTiltZ));
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * rotationSmoothSpeed);
        }
    }

    // 在 Scene 視窗繪製可視化感應範圍圈與 8 字盤旋軌跡 (Movement Debug)
    private void OnDrawGizmosSelected()
    {
        UpdateCameraBasis();

        // 1. 繪製攻擊偵測範圍圈 (青色)
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, detectionRange);

        // 2. 繪製 8 字型 / 盤旋預期活動軌跡 (金黃色，100% 貼合 Camera 2D 平面)
        Vector3 center = Application.isPlaying ? originalPosition : transform.position;
        Gizmos.color = new Color(1f, 0.82f, 0.2f, 0.85f);
        
        int steps = 50;
        Vector3 prevPoint = GetParametricHoverPosition(center, 0f);
        for (int i = 1; i <= steps; i++)
        {
            float tVal = (float)i / steps * Mathf.PI * 2f;
            Vector3 nextPoint = GetParametricHoverPosition(center, tVal);
            Gizmos.DrawLine(prevPoint, nextPoint);
            prevPoint = nextPoint;
        }

        // 3. 繪製中心點 (綠色小球)
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(center, 0.15f);
    }

    /// <summary>
    /// 【程式控制動畫核心方法】：同時設置 Animator Parameters (flying, worried, landing, die) 
    /// 與直接 State 播放，保證 100% 相容 living birds 的動畫控制器！
    /// </summary>
    public void PlayAnim(string animName)
    {
        if (animator == null) EnsureComponents();
        if (animator == null || string.IsNullOrEmpty(animName)) return;

        string targetName = animName.ToLower();

        Animator[] animators = GetComponentsInChildren<Animator>(true);
        foreach (var anim in animators)
        {
            if (anim == null) continue;
            anim.speed = 1f;

            // 1. 自動設置 Animator Controller 參數 (根據你在 Inspector/Animator 視窗截圖中的 Parameters)
            if (targetName.Contains("fly") || targetName.Contains("idle"))
            {
                SetAnimBoolIfExists(anim, "flying", true);
                SetAnimBoolIfExists(anim, "landing", false);
                SetAnimBoolIfExists(anim, "perched", false);
            }
            else if (targetName.Contains("worried") || targetName.Contains("warning"))
            {
                SetAnimTriggerIfExists(anim, "worried");
            }
            else if (targetName.Contains("land") || targetName.Contains("stuck") || targetName.Contains("peck"))
            {
                SetAnimBoolIfExists(anim, "landing", true);
                SetAnimBoolIfExists(anim, "flying", false);
                SetAnimTriggerIfExists(anim, "peck");
            }
            else if (targetName.Contains("die") || targetName.Contains("bounce"))
            {
                SetAnimTriggerIfExists(anim, "die");
            }

            // 2. 直接狀態強制過渡 (Double Protection)
            string stateToPlay = animName;
            if (animName == "flyStraight" || animName == "fly") stateToPlay = "flying";

            if (anim.HasState(0, Animator.StringToHash(stateToPlay)))
            {
                anim.CrossFade(stateToPlay, 0.1f);
            }
            else
            {
                anim.Play(stateToPlay, 0, 0f);
            }
        }
    }

    private void SetAnimBoolIfExists(Animator anim, string paramName, bool val)
    {
        foreach (var p in anim.parameters)
        {
            if (p.name == paramName && p.type == AnimatorControllerParameterType.Bool)
            {
                anim.SetBool(paramName, val);
                return;
            }
        }
    }

    private void SetAnimTriggerIfExists(Animator anim, string paramName)
    {
        foreach (var p in anim.parameters)
        {
            if (p.name == paramName && p.type == AnimatorControllerParameterType.Trigger)
            {
                anim.SetTrigger(paramName);
                return;
            }
        }
    }

    /// <summary>
    /// 外部全域調用：讓天空中所有鳥同步發起警報並攻擊！
    /// </summary>
    public static void TriggerAllBirdsAttack()
    {
        IndividualBirdEnemy[] birds = FindObjectsByType<IndividualBirdEnemy>(FindObjectsSortMode.None);
        foreach (var bird in birds)
        {
            bird.StartAttackSequence();
        }
        Debug.Log($"【鳥群系統】已觸發場景中 {birds.Length} 隻鳥發起同步俯衝攻擊！");
    }

    public void StartAttackSequence()
    {
        if (currentState != BirdState.Idle) return;
        StartCoroutine(AttackCoroutine());
    }

    private IEnumerator AttackCoroutine()
    {
        currentState = BirdState.Warning;
        
        // 1. 程式切換為警報動畫 (worried)
        PlayAnim(warningAnimName);

        // 2. 播放警告叫聲
        if (warningClip != null)
        {
            if (audioSource != null) audioSource.PlayOneShot(warningClip, AudioManager.SfxVolume);
            else AudioSource.PlayClipAtPoint(warningClip, transform.position, AudioManager.SfxVolume);
        }

        // 3. 警報期等待 (預設 1.2 秒)
        float realWarnTime = warningDuration > 0.05f ? warningDuration : 1.2f;
        yield return new WaitForSeconds(realWarnTime);

        // 4. 程式切換為俯衝飛行動畫 (flying) 並播放振翅音效
        PlayAnim(diveAnimName);
        if (flapClip != null)
        {
            if (audioSource != null) audioSource.PlayOneShot(flapClip, AudioManager.SfxVolume);
            else AudioSource.PlayClipAtPoint(flapClip, transform.position, AudioManager.SfxVolume);
        }
        currentState = BirdState.Diving;
        rb.isKinematic = false;

        // 鎖定初始目標位置與飛行向量 (Direct / Offset 模式在發起瞬間鎖定目標位置，玩家可閃避)
        UpdateTargetPosition();
        Vector3 initialPos = transform.position;
        initialPos.z = originalPosition.z;
        targetPosition.z = originalPosition.z;

        diveDirection = (targetPosition - initialPos).normalized;
        diveDirection.z = 0f;

        // 5. 朝目標位置發起高速俯衝攻擊，直到命中玩家、護盾或地面
        float diveTimer = 0f;
        float maxDiveDuration = 5.0f; // 充足俯衝時間
        Vector3 lastCheckPos = transform.position;
        float stagnationTimer = 0f;

        while (currentState == BirdState.Diving)
        {
            diveTimer += Time.deltaTime;
            stagnationTimer += Time.deltaTime;

            // 超時防呆：若俯衝超過 5.0 秒未撞擊，自動視為撞地插地
            if (diveTimer >= maxDiveDuration)
            {
                Debug.LogWarning($"【鳥群系統】{gameObject.name} 俯衝完成，觸發插地淡出！");
                OnHitGround();
                yield break;
            }

            // 停滯卡死防呆：只有在接近地表高度時，若卡住位移小於 0.08m 超過 0.5s 才判定撞擊 (絕不在半空中誤判定)
            if (stagnationTimer >= 0.5f)
            {
                float movedDist = Vector3.Distance(transform.position, lastCheckPos);
                if (movedDist < 0.08f && playerTrans != null && transform.position.y <= (playerTrans.position.y + 1.0f))
                {
                    Debug.LogWarning($"【鳥群系統】{gameObject.name} 俯衝觸地停滯，判定插地！");
                    OnHitGround();
                    yield break;
                }
                lastCheckPos = transform.position;
                stagnationTimer = 0f;
            }

            // 最低高度防穿防呆：若掉落到地表以下過深，自動觸發插地
            if (playerTrans != null && transform.position.y < (playerTrans.position.y - 3.5f))
            {
                Debug.LogWarning($"【鳥群系統】{gameObject.name} 俯衝低於地表高度，觸發防穿插地！");
                OnHitGround();
                yield break;
            }

            // 若為 Homing 模式，每幀動態更新追蹤目標；若是 Direct / Offset 模式，保持鎖定發起時的目標直線衝刺！
            if (behaviorType == BirdBehavior.HomingPlayer && playerTrans != null)
            {
                targetPosition = new Vector3(playerTrans.position.x, playerTrans.position.y, originalPosition.z);
                Vector3 cur = transform.position;
                cur.z = originalPosition.z;
                diveDirection = (targetPosition - cur).normalized;
                diveDirection.z = 0f;
            }

            rb.linearVelocity = diveDirection * diveSpeed;
            transform.position += diveDirection * (diveSpeed * Time.deltaTime);

            // 2D 飛行朝向：使 3D 鳥嘴/頭部 (+Z) 完全對準飛行向量 (diveDirection)，背部保持朝上 (+Y)
            if (diveDirection != Vector3.zero)
            {
                Quaternion targetRot = Quaternion.LookRotation(diveDirection, Vector3.up) * Quaternion.Euler(modelRotationOffset);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * 15f);
            }

            // 命中判定：當鳥撲擊接近目標時結算
            if (playerTrans != null)
            {
                float distToPlayer = Vector2.Distance(new Vector2(transform.position.x, transform.position.y), new Vector2(playerTrans.position.x, playerTrans.position.y));
                
                // 1. 優先檢查護盾：當護盾啟動時，只要鳥觸及護盾外圍 (2.4 米)，立刻在護盾表面彈飛！
                PlayerShield shield = playerTrans.GetComponentInChildren<PlayerShield>();
                if (shield == null) shield = playerTrans.GetComponentInParent<PlayerShield>();

                if (shield != null && shield.IsShieldActive)
                {
                    if (distToPlayer <= 2.4f)
                    {
                        Debug.LogWarning($"🛡️【鳥群系統】{gameObject.name} 撞擊玩家護盾外圍！立即觸發彈飛！");
                        BounceOff(shield);
                        yield break;
                    }
                }
                else
                {
                    // 2. 護盾未開啟：鳥衝撞到玩家本體 (1.1 米)
                    if (distToPlayer <= 1.1f)
                    {
                        if (PlayerPetrification.IsGodMode)
                        {
                            Debug.LogWarning($"🛡️【無敵模式】{gameObject.name} 撲擊命中無敵主角！鳥怪正常彈開，主角不觸發死亡重生！");
                        }
                        else
                        {
                            Debug.LogWarning($"💀【鳥群系統】{gameObject.name} 成功撲擊命中主角！觸發重生！");
                            PlayerRespawnSystem respawn = playerTrans.GetComponentInChildren<PlayerRespawnSystem>();
                            if (respawn == null) respawn = playerTrans.GetComponentInParent<PlayerRespawnSystem>();
                            if (respawn != null) respawn.TriggerRespawn();
                        }
                        BounceOff(null);
                        yield break;
                    }
                }
            }

            yield return null;
        }
    }

    /// <summary>
    /// 自動判斷物件是否為地表、地形、掩體、岩石、石柱或實體障礙物
    /// </summary>
    private bool IsGroundOrObstacleObject(GameObject obj, Collider col)
    {
        if (obj == null) return false;

        // 【最關鍵保護】：只要鳥在空中 (高於玩家腳底 0.4 米以上)，絕對不是撞擊地面，直接忽略！
        if (playerTrans != null && transform.position.y > (playerTrans.position.y + 0.4f))
        {
            return false;
        }

        // 1. 排除玩家與護盾（由專用碰撞邏輯處理）
        if (obj.CompareTag("Player") || obj.name.ToLower().Contains("player") || obj.GetComponentInParent<PlayerMovement>() != null || obj.GetComponentInParent<PlayerShield>() != null)
        {
            return false;
        }

        // 2. 排除其他鳥類敵人（絕對禁止鳥與鳥互相碰觸誤判為撞地！）
        if (obj.GetComponent<IndividualBirdEnemy>() != null || obj.GetComponentInParent<IndividualBirdEnemy>() != null || 
            obj.name.ToLower().Contains("crow") || obj.name.ToLower().Contains("bird") || obj.name.ToLower().Contains("enemy"))
        {
            return false;
        }

        // 3. 排除背景、光影、相機、無形觸發區域
        string lowerName = obj.name.ToLower();
        if (lowerName.Contains("bg") || lowerName.Contains("background") || lowerName.Contains("light") || 
            lowerName.Contains("camera") || lowerName.Contains("confiner") || lowerName.Contains("bound") || 
            lowerName.Contains("detector") || lowerName.Contains("cactus"))
        {
            return false;
        }

        if (col != null && col.isTrigger)
        {
            if (obj.GetComponent<UmbrellaZone>() != null || obj.GetComponent<BirdAttackTriggerZone>() != null ||
                obj.GetComponent<BGMZone>() != null || obj.GetComponent<AmbientSoundTrigger>() != null ||
                obj.GetComponent<CameraSwitchZone>() != null || obj.GetComponent<JumpTriggerZone>() != null ||
                obj.GetComponent<JumpBoostZone>() != null)
            {
                return false;
            }

            // 若不是掩體且為 Trigger，排除
            if (obj.GetComponent<WindShelter>() == null && obj.GetComponentInParent<WindShelter>() == null)
            {
                return false;
            }
        }

        // 4. 只有在接近地表時，碰到地面/掩體/岩石才算撞地
        if (obj.CompareTag("Floor") || obj.CompareTag("Ground") || lowerName.Contains("floor") || lowerName.Contains("ground") || lowerName.Contains("rock") || lowerName.Contains("pillar") || lowerName.Contains("shelter") || lowerName.Contains("wall"))
        {
            return true;
        }

        return false;
    }

    private Quaternion stuckRotation; // 快取俯衝到地面的精確 2D 插地角度

    private void LateUpdate()
    {
        // 1. 最高層級：強制鎖死 Z 軸位置，防止 3D 鳥模型翅膀拍打或物理碰撞導致 Z 軸漂移穿模
        Vector3 pos = transform.position;
        pos.z = originalPosition.z;
        transform.position = pos;

        // 2. 當撞擊插在地面上時，維護俯衝插地姿態，防止 FBX 動畫切換導致姿態跑掉
        if (currentState == BirdState.Stuck)
        {
            transform.rotation = stuckRotation;
        }
    }

    private void UpdateTargetPosition()
    {
        if (playerTrans == null)
        {
            targetPosition = transform.position + Vector3.down * 15f;
            return;
        }

        Vector3 playerPos = playerTrans.position;
        Vector3 groundTargetPos = new Vector3(playerPos.x, playerPos.y, originalPosition.z);

        switch (behaviorType)
        {
            case BirdBehavior.DirectPlayer:
                // 直衝發起時玩家所在的位置 (玩家可看準前兆跑開/跳躍閃避)
                targetPosition = groundTargetPos;
                break;

            case BirdBehavior.PlayerOffset:
                // 偏移預判攻擊：向玩家前方或後方偏移 targetOffset
                float xOffset = Random.value > 0.5f ? targetOffset : -targetOffset;
                targetPosition = new Vector3(playerPos.x + xOffset, groundTargetPos.y, originalPosition.z);
                break;

            case BirdBehavior.HomingPlayer:
                // 動態即時追蹤
                targetPosition = groundTargetPos;
                break;
        }
    }

    /// <summary>
    /// 當撞擊地面物件時觸發 (由 GroundCollisionNotifier、物理碰撞或地表高度判定)
    /// </summary>
    public void OnHitGround()
    {
        if (currentState != BirdState.Diving) return;

        StopAllCoroutines(); // 立即停止俯衝攜程

        // 記錄俯衝到地面的精確姿態，鎖死插地角度
        stuckRotation = transform.rotation;
        currentState = BirdState.Stuck;

        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }

        // 暫停動畫播放，防止 FBX keyframe 覆蓋插地姿態
        if (animator != null)
        {
            animator.speed = 0f;
        }

        Debug.Log($"【鳥群系統】{gameObject.name} 撞擊地表/掩體！立刻停格鎖定姿態插在表面，{stuckDuration} 秒後漸漸消失。");
        StartCoroutine(FadeAndDestroyCoroutine());
    }

    private IEnumerator FadeAndDestroyCoroutine()
    {
        // 俯衝插地/撞擊後停留 1.2 秒
        yield return new WaitForSeconds(Mathf.Min(stuckDuration, 1.2f));

        // 啟動淡出前將材質設定為透明渲染模式
        SetupMaterialsForFade();

        float elapsed = 0f;
        float realFadeDuration = fadeDuration > 0.05f ? fadeDuration : 1.0f;
        while (elapsed < realFadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / realFadeDuration);
            float alpha = 1.0f - t;
            
            // 1. 遞迴調整透明度 Alpha
            SetAlpha(alpha);
            
            // 2. 平滑縮放至 0 (雙重保障：即使 Shader 是 Opaque 也絕對能呈現絲滑縮小漸隱消失)
            transform.localScale = Vector3.Lerp(originalScale, Vector3.zero, t);
            
            yield return null;
        }

        SetAlpha(0f);
        hasAttackedOrDied = true;
        gameObject.SetActive(false);
        transform.localScale = originalScale;
    }

    /// <summary>
    /// 將所有 Renderer 的材質切換為 Transparent / Fade 模式以支援透明度漸變
    /// </summary>
    private void SetupMaterialsForFade()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            if (r == null || r is SpriteRenderer) continue;
            if (r.materials != null)
            {
                foreach (var mat in r.materials)
                {
                    if (mat == null) continue;

                    // URP Lit / Unlit Transparent 設定
                    if (mat.HasProperty("_Surface"))
                    {
                        mat.SetFloat("_Surface", 1); // 1 = Transparent
                        mat.SetFloat("_Blend", 0);   // 0 = Alpha
                        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                        mat.SetInt("_ZWrite", 0);
                        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                        mat.renderQueue = 3000;
                        mat.SetOverrideTag("RenderType", "Transparent");
                    }
                    // Built-in Standard Shader Fade 設定
                    else if (mat.HasProperty("_Mode"))
                    {
                        mat.SetFloat("_Mode", 2); // 2 = Fade
                        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                        mat.SetInt("_ZWrite", 0);
                        mat.DisableKeyword("_ALPHATEST_ON");
                        mat.EnableKeyword("_ALPHABLEND_ON");
                        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                        mat.renderQueue = 3000;
                        mat.SetOverrideTag("RenderType", "Transparent");
                    }
                }
            }
        }
    }

    /// <summary>
    /// 將材質恢復為 Opaque 不透明模式
    /// </summary>
    private void RestoreMaterialsOpaque()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            if (r == null || r is SpriteRenderer) continue;
            if (r.materials != null)
            {
                foreach (var mat in r.materials)
                {
                    if (mat == null) continue;
                    if (mat.HasProperty("_Surface"))
                    {
                        mat.SetFloat("_Surface", 0); // 0 = Opaque
                        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                        mat.SetInt("_ZWrite", 1);
                        mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                        mat.renderQueue = 2000;
                        mat.SetOverrideTag("RenderType", "Opaque");
                    }
                    else if (mat.HasProperty("_Mode"))
                    {
                        mat.SetFloat("_Mode", 0); // 0 = Opaque
                        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                        mat.SetInt("_ZWrite", 1);
                        mat.DisableKeyword("_ALPHABLEND_ON");
                        mat.renderQueue = 2000;
                        mat.SetOverrideTag("RenderType", "Opaque");
                    }
                }
            }
        }
    }

    /// <summary>
    /// 遞迴降低所有 Renderer (MeshRenderer / SkinnedMeshRenderer / SpriteRenderer) 的透明度
    /// </summary>
    private void SetAlpha(float alpha)
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            if (r == null) continue;

            if (r is SpriteRenderer sr)
            {
                Color c = sr.color;
                c.a = alpha;
                sr.color = c;
            }
            else if (r.materials != null)
            {
                foreach (var mat in r.materials)
                {
                    if (mat == null) continue;
                    if (mat.HasProperty("_Color"))
                    {
                        Color c = mat.color;
                        c.a = alpha;
                        mat.color = c;
                    }
                    if (mat.HasProperty("_BaseColor"))
                    {
                        Color c = mat.GetColor("_BaseColor");
                        c.a = alpha;
                        mat.SetColor("_BaseColor", c);
                    }
                }
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        HandleCollision(other.gameObject, other);
    }

    private void OnCollisionEnter(Collision collision)
    {
        HandleCollision(collision.gameObject, collision.collider);
    }

    private void HandleCollision(GameObject hitObj, Collider col)
    {
        if (currentState == BirdState.Bounced || currentState == BirdState.Stuck) return;

        // 0. 當處於待機狀態且玩家直接接觸此鳥時，單獨觸發攻擊
        if (currentState == BirdState.Idle && (hitObj.CompareTag("Player") || hitObj.GetComponentInParent<PlayerMovement>() != null))
        {
            if (!PlayerRespawnSystem.IsAnyRespawning && PlayerRespawnSystem.IsPlayerMovingAfterRespawn && !UmbrellaZone.IsPlayerUnderUmbrella)
            {
                Debug.Log($"【個別鳥觸發】玩家直接接觸 {gameObject.name}！該鳥單獨發起攻擊！");
                StartAttackSequence();
                return;
            }
        }

        // 1. 碰撞到玩家護盾
        PlayerShield shield = hitObj.GetComponentInParent<PlayerShield>();
        if (shield == null) shield = hitObj.GetComponent<PlayerShield>();

        if (shield != null && shield.IsShieldActive)
        {
            BounceOff(shield);
            return;
        }

        // 2. 碰撞到玩家本體：若無敵則不觸發重生，否則觸發玩家重生！
        if (hitObj.CompareTag("Player") || hitObj.name.ToLower().Contains("player") || hitObj.GetComponentInParent<PlayerMovement>() != null)
        {
            if (currentState == BirdState.Diving)
            {
                if (PlayerPetrification.IsGodMode)
                {
                    Debug.LogWarning($"🛡️【無敵模式】{gameObject.name} 碰撞無敵主角！鳥怪正常彈開，主角不觸發死亡重生！");
                }
                else
                {
                    PlayerRespawnSystem respawn = hitObj.GetComponent<PlayerRespawnSystem>();
                    if (respawn == null) respawn = hitObj.GetComponentInParent<PlayerRespawnSystem>();
                    if (respawn != null)
                    {
                        respawn.TriggerRespawn();
                    }
                }

                // 撞到玩家後彈開淡出
                BounceOff(shield);
                return;
            }
        }

        // 3. 俯衝期間碰撞到 Floor 地面、掩體、岩石等實體障礙物 ➔ 立刻以當前角度精確插在物件表面淡出！
        if (currentState == BirdState.Diving && IsGroundOrObstacleObject(hitObj, col))
        {
            OnHitGround();
        }
    }

    /// <summary>
    /// 由 PlayerShield 被動觸發擊飛
    /// </summary>
    public void OnShieldHit(PlayerShield shield)
    {
        BounceOff(shield);
    }

    /// <summary>
    /// 撞擊護盾時反彈飛開效果：播放 DIE 動畫，並依據自然物理拋物線彈飛至地面後漸隱！
    /// </summary>
    public void BounceOff(PlayerShield shield)
    {
        if (currentState == BirdState.Bounced) return;

        StopAllCoroutines(); // 立即停止俯衝與其他運動
        currentState = BirdState.Bounced;

        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }

        // 恢復動畫速度並播放 DIE 動畫
        if (animator != null) animator.speed = 1f;
        PlayAnim(dieAnimName);

        StartCoroutine(ControlledBounceCoroutine(shield));
    }

    /// <summary>
    /// 自然物理拋物線反彈協程：撞擊護盾後向上躍起拋物線墜落至地面，倒地後平滑漸漸隱形消失
    /// </summary>
    private IEnumerator ControlledBounceCoroutine(PlayerShield shield)
    {
        Vector3 startPos = transform.position;
        Vector3 shieldCenter = (shield != null ? shield.transform.position : (playerTrans != null ? playerTrans.position : startPos - Vector3.right));

        float realBounceDuration = bounceDuration > 0.05f ? bounceDuration : 0.85f;
        float realFadeDuration = fadeDuration > 0.05f ? fadeDuration : 1.0f;
        float realBounceDist = bounceDistance > 0.1f ? bounceDistance : 3.5f;

        // 反彈水平方向 (沿著撞擊相反方向向外彈出)
        float dirX = (startPos.x >= shieldCenter.x) ? 1.0f : -1.0f;
        float targetX = startPos.x + dirX * realBounceDist;

        // 計算地表 Y 座標 (往下射線偵測地面，保證落地不懸空)
        float targetY = startPos.y - 2.5f;
        RaycastHit hit;
        if (Physics.Raycast(new Vector3(targetX, startPos.y + 2f, originalPosition.z), Vector3.down, out hit, 40f))
        {
            targetY = hit.point.y + 0.2f;
        }
        else if (playerTrans != null)
        {
            targetY = playerTrans.position.y - 0.5f;
        }

        Vector3 targetPos = new Vector3(targetX, targetY, originalPosition.z);

        Quaternion startRot = transform.rotation;
        Quaternion endRot = startRot * Quaternion.Euler(0, 0, dirX * -bounceSpinAngle);

        Debug.Log($"【鳥群反彈】自然拋物線反彈啟動！起點: {startPos}, 落地目標: {targetPos}");

        float elapsed = 0f;
        while (elapsed < realBounceDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / realBounceDuration);

            // 水平與垂直線性插值 ＋ 自然向上拋物線弧度
            Vector3 currentPos = Vector3.Lerp(startPos, targetPos, t);
            float arcY = 4f * bounceHeight * Mathf.Sin(t * Mathf.PI * 0.5f) * (1f - t);
            currentPos.y += arcY;
            currentPos.z = originalPosition.z;

            transform.position = currentPos;
            transform.rotation = Quaternion.Slerp(startRot, endRot, t);

            yield return null;
        }

        // 確保完全觸地
        transform.position = targetPos;

        // 倒在地上稍微停頓
        if (animator != null)
        {
            animator.speed = 0.5f;
        }

        yield return new WaitForSeconds(0.4f);

        // 漸漸隱形消失
        SetupMaterialsForFade();

        float fadeElapsed = 0f;
        while (fadeElapsed < realFadeDuration)
        {
            fadeElapsed += Time.deltaTime;
            float t = Mathf.Clamp01(fadeElapsed / realFadeDuration);
            float alpha = 1.0f - t;
            
            SetAlpha(alpha);
            transform.localScale = Vector3.Lerp(originalScale, Vector3.zero, t);
            
            yield return null;
        }

        SetAlpha(0f);
        hasAttackedOrDied = true;
        gameObject.SetActive(false);
        transform.localScale = originalScale;
    }

    // --- IResettable 實作 ---
    public void ResetToInitialState()
    {
        StopAllCoroutines();

        // 檢查存檔點進度：
        // 若當前存檔點已經推進到這隻鳥的原點之後 (代表玩家已通過該存檔點且該鳥已死亡/攻擊過)，則該鳥永久保持死亡消失！
        Vector3 currentCheckpoint = PlayerRespawnSystem.ActiveRespawnPosition;
        if (hasAttackedOrDied && currentCheckpoint != Vector3.zero && (originalPosition.x <= currentCheckpoint.x + 1.0f))
        {
            gameObject.SetActive(false);
            return;
        }

        // 尚未通過的存檔點前方鳥敵人：完全刷新重生！
        hasAttackedOrDied = false;
        gameObject.SetActive(true);
        currentState = BirdState.Idle;
        InitializeHoverProfile();
        transform.position = originalPosition;
        transform.rotation = originalRotation;
        transform.localScale = originalScale;
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.position = originalPosition;
            rb.isKinematic = true;
        }
        RestoreMaterialsOpaque();
        SetAlpha(1.0f);

        // 徹底重置 Animator Controller，清除殘留的 die / worried 觸發器與死亡姿態
        Animator[] animators = GetComponentsInChildren<Animator>(true);
        foreach (var anim in animators)
        {
            if (anim != null)
            {
                anim.speed = 1f;
                anim.Rebind();
                anim.Update(0f);
            }
        }
        PlayAnim(idleAnimName);
        postRespawnDelayTimer = 1.5f; // 重生後給予 1.5 秒初始冷卻緩衝
    }
}
