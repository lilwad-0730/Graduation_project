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

    [Tooltip("【已不使用】舊版斜向高速俯衝的速度（場景裡每隻鳥存 12）。1015 起攻擊改成「水平對準＋垂直慢速下降」，改用下面兩個參數。欄位保留只是為了不動到場景序列化。")]
    public float diveSpeed = 9.8f;

    [Header("★1015 垂直慢速墜落（雷霆戰機式慢速彈幕；DesertBeatDirector 會統一覆蓋）")]
    [Tooltip("下降階段的垂直速度 (公尺/秒)，世界座標垂直向下。3＝玩家有充足時間看、時間躲")]
    public float diveDescentSpeed = 3f;
    [Tooltip("下降前水平移到落點正上方的最高速度 (公尺/秒)，會平滑加速。前搖時間內進行，距離太遠就延長到到位為止。不建議超過 10，太快會像橫越畫面")]
    public float diveAlignSpeed = 7f;

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
    
    [Header("🔴 攻擊前搖與路徑預告 (Telegraph)")]
    [Tooltip("警戒期間顯示紅色攻擊路徑：先亮路徑再俯衝。路徑鎖定「發現玩家當下」的方向，之後不再追蹤")]
    public bool showAttackTelegraph = true;

    [Tooltip("紅色路徑顏色")]
    public Color telegraphColor = new Color(1f, 0.25f, 0.2f, 0.9f);

    [Tooltip("路徑線寬")]
    public float telegraphWidth = 0.18f;

    [Tooltip("路徑越過鎖定點再延伸多遠（表示牠會衝過頭）")]
    public float telegraphOvershoot = 12f;

    [Tooltip("前搖期間讓鳥本體先轉向鎖定的攻擊方向。紅線關掉後，玩家仍可靠鳥頭/身體角度猜落點。")]
    public bool showAttackAngleCue = true;

    [Tooltip("鳥轉向攻擊角度的平滑速度。")]
    [Range(1f, 30f)] public float attackAngleCueTurnSpeed = 10f;

    private LineRenderer _telegraphLine;
    private Vector3 _lockedDiveTarget;
    private bool _hasLockedDiveTarget;

    // ★1002 Pending＝偵測到玩家但正在吹風：只記一筆「待攻擊」，風停才真正 lock-on（見 AttackCoroutine 開頭）
    private enum BirdState { Idle, Warning, Diving, Stuck, Bounced, Pending }
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

    // ── ★0905 荒原四拍 ─────────────────────────────────────────
    [Header("★0905 起風反應（閒置鳥當風向標）")]
    [Tooltip("前兆時全體抬高、吹風時往下風飄；鳥先動＝第二個起風前兆（Rain World：動物先知道）")]
    public bool reactToWind = true;
    [Tooltip("前兆時抬高多少（世界單位）")]
    public float windLiftHeight = 0.6f;
    [Tooltip("吹風時往下風飄多少（世界單位）")]
    public float windDriftDistance = 1.2f;

    [Header("★0905 示範俯衝（拍二第一隻：衝手套旁的地面、不打她）")]
    [Tooltip("設了就衝這個點，不衝玩家")]
    public Transform overrideTarget;
    [Tooltip("示範用：命中玩家不觸發死亡，只彈開")]
    public bool harmless = false;

    [Header("★1001 路線預判（攻擊玩家前進方向的預測位置）")]
    [Tooltip("開啟後，鳥不再瞄玩家「當下位置」，而是瞄「等牠飛到時玩家會在的位置」。\n" +
             "關掉＝完全回到原本的 DirectPlayer 行為。")]
    public bool enableRoutePrediction = true;

    [Tooltip("預判時間的倍率。\n" +
             "1.0 ＝ 精準攔截（預判時間就是「前搖 + 俯衝飛行時間」，鳥剛好在玩家抵達時到達）\n" +
             "> 1 ＝ 打得更前面（超前量），< 1 ＝ 打得偏後。\n" +
             "要更難就往上調，但紅色預告線會先亮 warningDuration 秒，玩家看得到就躲得掉。")]
    [Range(0f, 2f)]
    public float predictionTimeMultiplier = 1.0f;

    [Tooltip("預判時間下限 (秒)。太小就幾乎等於瞄當下位置")]
    [Range(0f, 1f)]
    public float minimumPredictionTime = 0.15f;

    [Tooltip("預判時間上限 (秒)。防止鳥離很遠時把攻擊點推到天邊")]
    [Range(0.1f, 3f)]
    public float maximumPredictionTime = 1.2f;

    [Tooltip("預判點最多可以離玩家目前位置多遠 (公尺)。這是最後一道保險，確保落點永遠在可反應範圍內")]
    [Range(0f, 20f)]
    public float predictionDistanceLimit = 7f;

    [Header("★1010 預判準度（微調用；DesertBeatDirector 會統一覆蓋）")]
    [Tooltip("預判攻擊中，打「剛好攔截點」的比例 (0～1)。其餘打寬鬆點。0.7＝七成很準、三成隨便")]
    [Range(0f, 1f)] public float predictionPreciseChance = 0.7f;
    [Tooltip("「很準」的攻擊相對攔截點再多（正）或少（負）幾公尺，沿玩家前進方向。0＝剛好；+1＝比剛好再前面 1 公尺")]
    public float predictionPreciseOffset = 0f;
    [Tooltip("「寬鬆」的攻擊相對攔截點的偏移範圍下限 (公尺)，沿玩家前進方向，負＝落在她身後")]
    public float predictionLooseMin = -5f;
    [Tooltip("「寬鬆」的攻擊相對攔截點的偏移範圍上限 (公尺)，正＝落在她前面")]
    public float predictionLooseMax = 5f;
    [Tooltip("俯衝命中玩家的距離 (公尺)。原本固定 1.1；稍微加大就是「剛好又稍微多一點打到」")]
    public float hitRadius = 1.1f;

    private int _predictCount = 0;
    private Vector3 _diveStartPos;
    private bool _dbgPrecise = true;
    private float _dbgOffset = 0f;

    [Header("★1001 命中逼退（沿玩家前進方向的反方向推）")]
    [Tooltip("命中時把玩家往後推，而不是直接觸發死亡重生。\n" +
             "關掉＝回到原本「命中即死」的行為（石化硬撐／護盾／無敵／harmless 的既有豁免不受影響）。")]
    public bool retreatInsteadOfKill = true;

    [Tooltip("逼退速度 (公尺/秒)。沿用既有的 PlayerMovement.ApplyWindPush 管道，所以斜坡與平地都已正確處理")]
    [Range(0f, 12f)]
    public float retreatPushSpeed = 6f;

    [Tooltip("逼退持續時間 (秒)。ApplyWindPush 每次只維持 0.15 秒，所以這段時間內會持續補推")]
    [Range(0f, 1f)]
    public float retreatPushDuration = 0.35f;

    [Header("★1002 攻擊類型與風停排隊")]
    [Tooltip("開啟＝每次攻擊由 BirdAttackScheduler 挑「定點」或「預判」（加權＋同種不連續太多次）。\n" +
             "權重、連續上限在 DesertBeatDirector 的「★1002 鳥攻擊排程」調。\n" +
             "關掉＝回到 1001 的行為（enableRoutePrediction 開就一律預判）。")]
    public bool useAttackScheduler = true;

    [Tooltip("勾選＝這隻鳥固定用下面指定的類型，不走排程（測試用）")]
    public bool overrideAttackType = false;
    public BirdAttackType attackTypeOverride = BirdAttackType.FixedPosition;

    [Tooltip("吹風（含 1 秒沙塵前兆）期間偵測到玩家：不攻擊、先排隊，風停才鎖定目標＋亮紅線＋俯衝。\n" +
             "排隊期間不鎖座標，免得風吹了 2 秒她早跑遠了還去打舊位置。")]
    public bool queueAttackDuringWind = true;

    [Tooltip("（舊路徑專用：示範俯衝等不走排程器的鳥）排隊超過這麼多秒就放棄，回到待機。0＝不放棄")]
    public float maxQueueWaitSeconds = 8f;

    [Tooltip("勾選＝依 behaviorType 走舊版行為（PlayerOffset＝打玩家位置左右各 targetOffset 公尺，不預判）。\n" +
             "預設關閉：所有鳥統一只有「定點／預判」兩種攻擊，玩家才學得會規則。場景裡約 8 隻鳥存的是 PlayerOffset，屬舊版殘留")]
    public bool useLegacyBehaviorType = false;

    private BirdAttackType _currentAttackType = BirdAttackType.FixedPosition;

    // ★1006 排程器需求狀態
    private bool _requestQueued = false;          // 排程器佇列裡有這隻鳥的需求
    private bool _granted = false;                // 排程器已放行，AttackCoroutine 直接從 Warning 開始
    private BirdAttackType _grantedType = BirdAttackType.FixedPosition;
    private float _grantedQueueWait = 0f;
    private float _dbgPredT = 0f;                 // 診斷：最近一次預判實際用的預判時間／領先量
    private float _dbgPredLead = 0f;
    private float _minPlayerDist = float.MaxValue, _minDistPlayerX, _minDistBirdX, _minDistTime, _diveStartTime;

    /// <summary>已偵測到玩家、需求在排程器佇列裡等待放行（盤旋中，不鎖座標、不亮紅線）。</summary>
    public bool IsPending => currentState == BirdState.Pending && _requestQueued;

    /// <summary>已放行：前搖或俯衝中。排程器用它算「同時攻擊上限」。</summary>
    public bool IsAttacking => currentState == BirdState.Warning || currentState == BirdState.Diving;

    /// <summary>排程器放行。鳥進入前搖（lock-on → 紅線 → 俯衝）。</summary>
    public void GrantAttack(BirdAttackType type, float queueWait)
    {
        if (currentState != BirdState.Pending) return;
        _requestQueued = false;
        _granted = true;
        _grantedType = type;
        _grantedQueueWait = queueWait;
        StartCoroutine(AttackCoroutine());
    }

    /// <summary>取消排隊中的需求，回到待機（沒有消耗這隻鳥）。toIdle=false 表示呼叫端會自己處理狀態（重生清佇列時）。</summary>
    public void CancelRequest(bool removeFromQueue)
    {
        if (removeFromQueue) BirdAttackScheduler.Remove(this);
        _requestQueued = false;
        if (currentState == BirdState.Pending) currentState = BirdState.Idle;
    }

    /// <summary>吹風中或沙塵前兆中（荒原的 Wind Phase）。沿用 WindGustSystem 既有狀態，不另做一套。</summary>
    /// <summary>吹風中或沙塵前兆中（Wind Phase）。排程器用它決定要不要放行；沿用 WindGustSystem 的狀態，不另做一套。</summary>
    /// <summary>★1012 最後一座掩體之後的鳥（x 大於這個值）不受風起風停限制：照常放行。掩體之前才是「躲風＋躲鳥」。由導演設定，預設無限大＝全部受限制。</summary>
    public static float WindFreeFromX = float.PositiveInfinity;
    public bool IgnoresWind { get { return originalPosition.x > WindFreeFromX; } }

    public static bool IsWindPhase()
    {
        WindGustSystem w = WindGustSystem.Instance;
        if (w == null || w.IsStoppedForever) return false;
        return w.CurrentState == WindState.Blowing || w.IsTelegraphing;
    }

    // granted＝排程器放行時指定的類型（C/P 循環）；沒指定（示範俯衝等舊路徑）就預判。單隻鳥的覆寫與「預判總開關」仍然優先
    private BirdAttackType ChooseAttackType(bool granted = false, BirdAttackType grantedType = BirdAttackType.PredictedPosition)
    {
        if (overrideTarget != null || !enableRoutePrediction) return BirdAttackType.FixedPosition;   // 示範俯衝/預判總開關關掉＝只打定點
        if (overrideAttackType) return attackTypeOverride;
        return granted ? grantedType : BirdAttackType.PredictedPosition;
    }

    // 玩家剛體快取（只抓一次，預判要讀她的實際水平速度）
    private Rigidbody _playerRb;
    private PlayerMovement _playerMove;

    /// <summary>全域壓制：Time.time 小於這個值時，所有鳥不偵測、不攻擊（鳥影掠地 6 秒／風停區永久）。換場景由 DesertBeatDirector 歸零。</summary>
    /// <summary>★1013 重生後，存檔點之前（玩家已經過了）還沒攻擊過的鳥是否還能攻擊。false＝只盤旋不攻擊（預設）。攻擊過的鳥一律永久消失。由 DesertBeatDirector 設定。</summary>
    public static bool BehindCheckpointBirdsCanAttack = false;
    private bool _attackDisabled = false;

    // ★1016 荒原鳥群強度：由 DesertBeatDirector 統一設定。
    // 增援鳥只複製一次、不再生增援，避免連鎖爆量；通過後退場只作用在待機/排隊鳥，不打斷已經在俯衝的鳥。
    public static bool EnableReinforcements = false;
    public static float ReinforcementIntensity = 1f;
    public static float ReinforcementChance = 0.35f;
    public static int ReinforcementMinCopies = 0;
    public static int ReinforcementMaxCopies = 1;
    public static float ReinforcementNearDistance = 6f;
    public static int ReinforcementNearExtraCopies = 2;
    public static float ReinforcementSpreadX = 4f;
    public static float ReinforcementHeightJitter = 1f;
    public static float ReinforcementAttackDelayMax = 0.35f;
    public static int ReinforcementActiveLimit = 24;
    public static float DespawnBehindPlayerDistance = 22f;

    private static int _activeReinforcements = 0;
    private bool _isReinforcement = false;
    private bool _registeredReinforcement = false;
    private bool _spawnedReinforcements = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetBirdIntensityStatics()
    {
        _activeReinforcements = 0;
    }

    public static float SuppressAllUntil = -1f;
    public static bool IsAllSuppressed => Time.time < SuppressAllUntil;

    private Vector3 GetWindReactionOffset()
    {
        if (!reactToWind) return Vector3.zero;
        WindGustSystem w = WindGustSystem.Instance;
        if (w == null) return Vector3.zero;
        float lift = 0f, drift = 0f;
        if (w.IsTelegraphing)
        {
            float p = w.TelegraphProgress01;
            lift = windLiftHeight * Mathf.SmoothStep(0f, 1f, p);
        }
        else if (w.IsPushActive)
        {
            float p = w.PushStrength01;
            lift = windLiftHeight * (1f - 0.5f * p);
            drift = windDriftDistance * p * w.WindDirectionX;
        }
        return (_camUp * lift) + (_camRight * drift);
    }
    // ───────────────────────────────────────────────────────────

    private void Update()
    {
        // 核心功能 1：3D 有機空中漂浮與自然微盤旋 (多頻率波形 + Perlin 氣流雜訊)
        if ((currentState == BirdState.Idle || currentState == BirdState.Pending) && enableIdleHover)
        {
            UpdateOrganicIdleHover();
        }

        TryDespawnAfterPlayerPassed();

        // 核心功能 2：獨立偵測玩家距離並觸發俯衝 (僅在此鳥設定允許距離感應時運作)
        if (postRespawnDelayTimer > 0f)
        {
            postRespawnDelayTimer -= Time.deltaTime;
            return;
        }

        if (currentState == BirdState.Idle && autoDetectPlayer && triggerMode != BirdTriggerMode.TriggerZoneOrCollisionOnly)
        {
            // ★1006 只有「永久壓制」（風停區 x≥225，SuppressAllUntil＝無限大）才不偵測。
            //   鳥影掠地的 3.5 秒是有限壓制：照常偵測、建立需求，由排程器等壓制結束才放行（不再直接略過）。
            if (IsAllSuppressed && float.IsPositiveInfinity(SuppressAllUntil)) return;
            if (_attackDisabled) return;   // ★1011 存檔點後面的裝飾鳥：只盤旋，不偵測
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
                    BirdAttackScheduler.Stat("DETECT");
                    BirdAttackScheduler.Log("DETECT", this, $"Distance={xDist:F1} Range={detectionRange:F0}");
                    StartAttackSequence();
                }
            }
        }

        // 核心功能 3：警報姿態控制 (Idle 狀態的飛行旋轉與 Banking 已由 UpdateOrganicIdleHover 統一接管)
        if (currentState == BirdState.Warning)
        {
            if (showAttackAngleCue && _hasLockedDiveTarget)
            {
                Vector3 lookDir = _lockedDiveTarget - transform.position;
                lookDir.z = 0f;
                if (lookDir.sqrMagnitude > 0.001f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(lookDir.normalized, Vector3.up) * Quaternion.Euler(modelRotationOffset);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * attackAngleCueTurnSpeed);
                }
            }
            else if (playerTrans != null)
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

        // ★0905 起風反應：前兆時抬高、吹風時往下風飄（沿用同一個 Lerp，動作自然）
        rawTargetPos += GetWindReactionOffset();

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
            //    ★crow 的 birdAnimatorController 裡叫「fly」不是「flying」：交給 AnimStateResolver 對別名，
            //      找不到就不硬播（原本 Play 不存在的 state → Animator.GotoState 警告刷屏、鳥永遠不拍翅）
            string stateToPlay = animName;
            if (animName == "flyStraight") stateToPlay = "fly";
            AnimStateResolver.CrossFadeSafe(anim, stateToPlay, 0.1f);
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
        if (_attackDisabled) return;
        if (playerTrans == null) EnsureComponents();

        TrySpawnReinforcements();

        // ★1006 一般鳥：建立「攻擊需求」進排程器佇列，進 Pending（照常盤旋）。等放行才鎖定。
        //   示範俯衝（overrideTarget）或關掉排程器的鳥走舊路徑，直接開始。
        if (useAttackScheduler && overrideTarget == null && playerTrans != null)
        {
            currentState = BirdState.Pending;
            _requestQueued = true;
            float d = Mathf.Abs(transform.position.x - playerTrans.position.x);
            // 由 BirdAttackTriggerZone／TriggerAllBirdsAttack 直接呼叫的屬於強制需求，插隊
            BirdAttackScheduler.Enqueue(this, !autoDetectPlayer || triggerMode == BirdTriggerMode.TriggerZoneOrCollisionOnly, d);
            return;
        }
        StartCoroutine(AttackCoroutine());
    }

    private void TrySpawnReinforcements()
    {
        if (!EnableReinforcements || _isReinforcement || _spawnedReinforcements) return;
        if (harmless || overrideTarget != null) return;
        if (playerTrans == null) return;

        _spawnedReinforcements = true;

        float intensity = Mathf.Max(0.1f, ReinforcementIntensity);
        float xDist = Mathf.Abs(transform.position.x - playerTrans.position.x);
        float chance = Mathf.Clamp01(ReinforcementChance * intensity);
        if (Random.value > chance) return;

        int minCopies = Mathf.Max(0, ReinforcementMinCopies);
        int maxCopies = Mathf.Max(minCopies, Mathf.RoundToInt(ReinforcementMaxCopies * intensity));
        int copies = Random.Range(minCopies, maxCopies + 1);

        if (xDist <= Mathf.Max(0.1f, ReinforcementNearDistance))
        {
            int extraMax = Mathf.Max(0, Mathf.RoundToInt(ReinforcementNearExtraCopies * intensity));
            copies += Random.Range(0, extraMax + 1);
        }

        int activeLimit = Mathf.Max(0, ReinforcementActiveLimit);
        if (activeLimit > 0) copies = Mathf.Min(copies, Mathf.Max(0, activeLimit - _activeReinforcements));
        if (copies <= 0) return;

        for (int i = 0; i < copies; i++)
        {
            Vector3 spawnPos = transform.position;
            spawnPos.x += Random.Range(-ReinforcementSpreadX, ReinforcementSpreadX);
            spawnPos.y += Random.Range(-ReinforcementHeightJitter, ReinforcementHeightJitter);

            GameObject cloneObj = Instantiate(gameObject, spawnPos, transform.rotation);
            cloneObj.name = gameObject.name + "_Reinforcement";

            IndividualBirdEnemy clone = cloneObj.GetComponent<IndividualBirdEnemy>();
            if (clone == null) continue;

            clone.PrepareAsReinforcement(spawnPos);
            clone.StartCoroutine(clone.StartReinforcementAttackAfterDelay(Random.Range(0f, Mathf.Max(0f, ReinforcementAttackDelayMax))));
        }
    }

    private void PrepareAsReinforcement(Vector3 spawnPos)
    {
        _isReinforcement = true;
        _registeredReinforcement = true;
        _activeReinforcements++;

        _spawnedReinforcements = true;
        _requestQueued = false;
        _granted = false;
        _attackDisabled = false;
        hasAttackedOrDied = false;
        currentState = BirdState.Idle;
        postRespawnDelayTimer = 0f;

        originalPosition = spawnPos;
        originalRotation = transform.rotation;
        originalScale = transform.localScale != Vector3.zero ? transform.localScale : Vector3.one;
        transform.position = spawnPos;
        transform.localScale = originalScale;

        autoDetectPlayer = false;
        overrideTarget = null;
        harmless = false;

        EnsureComponents();
        InitializeHoverProfile();
        RestoreMaterialsOpaque();
        SetAlpha(1f);
        PlayAnim(idleAnimName);
    }

    private IEnumerator StartReinforcementAttackAfterDelay(float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);
        if (this != null && isActiveAndEnabled && currentState == BirdState.Idle)
        {
            StartAttackSequence();
        }
    }

    private void TryDespawnAfterPlayerPassed()
    {
        if (DespawnBehindPlayerDistance <= 0f) return;
        if (currentState != BirdState.Idle && currentState != BirdState.Pending) return;
        if (playerTrans == null) EnsureComponents();
        if (playerTrans == null) return;

        if (playerTrans.position.x - originalPosition.x < DespawnBehindPlayerDistance) return;

        if (_requestQueued)
        {
            CancelRequest(true);
        }

        hasAttackedOrDied = true;
        gameObject.SetActive(false);
    }

    private IEnumerator AttackCoroutine()
    {
        float waited = _grantedQueueWait;

        // 舊路徑（示範俯衝等不走排程器的鳥）：吹風／鳥影壓制時等一下再開始；排程器放行的鳥（_granted）直接略過
        if (!_granted)
        {
            float legacyWait = 0f;
            while ((queueAttackDuringWind && IsWindPhase() && !IgnoresWind) || IsAllSuppressed)
            {
                currentState = BirdState.Pending;
                legacyWait += Time.deltaTime;
                if (maxQueueWaitSeconds > 0f && legacyWait > maxQueueWaitSeconds) { currentState = BirdState.Idle; yield break; }
                yield return null;
            }
            if (UmbrellaZone.IsPlayerUnderUmbrella) { currentState = BirdState.Idle; yield break; }
            waited = legacyWait;
        }

        currentState = BirdState.Warning;
        _currentAttackType = ChooseAttackType(_granted, _grantedType);
        _granted = false;
        BirdAttackScheduler.Stat("LOCK");
        // 1. 程式切換為警報動畫 (worried)
        PlayAnim(warningAnimName);

        // ★ 前搖鎖定：在「發現玩家的瞬間」鎖住攻擊方向——之後的紅色路徑與俯衝都沿這條線，不再追蹤
        UpdateTargetPosition();
        _lockedDiveTarget = targetPosition;
        _hasLockedDiveTarget = true;
        if (showAttackTelegraph) SetTelegraphVisible(true);
        if (BirdAttackScheduler.debugEnabled)
        {
            // 實際 runtime 用的是哪組數值：以這一行為準（鳥本身欄位已被導演覆寫過）
            float px = playerTrans != null ? playerTrans.position.x : 0f;
            BirdAttackScheduler.Log("LOCK", this,
                $"Mode={(_currentAttackType == BirdAttackType.FixedPosition ? "Current" : "Predicted")} QueueWait={waited:F1}s " +
                $"PlayerX={px:F1} vx={(_playerRb != null ? _playerRb.linearVelocity.x : 0f):F1} TargetX={_lockedDiveTarget.x:F1} Lead={(_lockedDiveTarget.x - px):F1} " +
                $"[warn={warningDuration:F1} descent={diveDescentSpeed:F1} align={diveAlignSpeed:F1} predT={_dbgPredT:F2} {(_currentAttackType == BirdAttackType.PredictedPosition ? (_dbgPrecise ? "準" : "寬鬆") + "偏移" + _dbgOffset.ToString("F1") + "m" : "")} maxPredT={maximumPredictionTime:F1} leadLimit={predictionDistanceLimit:F0} legacyType={(useLegacyBehaviorType ? behaviorType.ToString() : "off")}]");
            BirdAttackScheduler.Log("WARNING", this, $"Duration={warningDuration:F1}s");
        }

        // 2. 播放警告叫聲
        if (warningClip != null)
        {
            if (audioSource != null) audioSource.PlayOneShot(warningClip, AudioManager.SfxVolume);
            else AudioSource.PlayClipAtPoint(warningClip, transform.position, AudioManager.SfxVolume);
        }

        // ★1015 垂直慢速墜落（雷霆戰機式慢速彈幕）。
        //   舊版：前搖結束 → 沿鎖定點直線斜向高速俯衝（diveSpeed 12），再怎麼降速視覺上仍是「斜著撲過來」。
        //   新版：Lock-on 固定落點 → 前搖期間先水平移到落點正上方（diveAlignSpeed，有限速度、平滑加速）→
        //         垂直慢速下降（diveDescentSpeed，X 鎖死在落點上方）。
        //   整段只用 transform.position 移動；rb 保持 kinematic，不再同時寫 rb.linearVelocity
        //   （舊版兩邊都寫，位移有重複的風險）。
        float alignTargetX = _hasLockedDiveTarget ? _lockedDiveTarget.x : transform.position.x;

        // 3. 警報期（前搖）＋水平對準：前搖時間內鳥一邊「不安」一邊飄到落點正上方；
        //    距離太遠、前搖結束還沒到的話，繼續對準到到位為止（有逾時，被地形擋住就地開始下降）。
        float realWarnTime = warningDuration > 0.05f ? warningDuration : 1.2f;
        float warnT = 0f;
        float alignDist = Mathf.Abs(alignTargetX - transform.position.x);
        float alignTimeout = realWarnTime + alignDist / Mathf.Max(0.1f, diveAlignSpeed) + 1.5f;
        bool aligned = alignDist < 0.05f;
        float alignV = 0f;
        float alignStartTime = Time.time;
        string alignEndReason = aligned ? "已在落點上方" : "";
        if (!aligned) BirdAttackScheduler.Log("ALIGN-START", this, $"BirdX={transform.position.x:F1} LockX={alignTargetX:F1} Distance={alignDist:F1}m AlignSpeed={diveAlignSpeed:F1}");
        while (warnT < realWarnTime || !aligned)
        {
            warnT += Time.deltaTime;
            if (!aligned)
            {
                // 平滑加速到 diveAlignSpeed，不瞬移
                alignV = Mathf.MoveTowards(alignV, diveAlignSpeed, 14f * Time.deltaTime);
                float dxAlign = alignTargetX - transform.position.x;
                float stepAlign = Mathf.Min(Mathf.Abs(dxAlign), alignV * Time.deltaTime);
                Vector3 alignDir = new Vector3(Mathf.Sign(dxAlign), 0f, 0f);
                if (stepAlign > 0f && SweepForSurface(alignDir, stepAlign + 0.4f, out Vector3 _))
                {
                    aligned = true;   // 被地形／掩體擋住：就地開始下降，不要永遠卡在對準
                    alignEndReason = "被地形擋住（" + (_lastSweepCollider != null ? _lastSweepCollider.name : "?") + "）";
                }
                else
                {
                    Vector3 ap = transform.position;
                    ap.x += alignDir.x * stepAlign;
                    transform.position = ap;
                    if (Mathf.Abs(alignTargetX - ap.x) < 0.02f) { aligned = true; alignEndReason = "到位"; }
                }
                if (!aligned && Time.time - alignStartTime > alignTimeout) { aligned = true; alignEndReason = "逾時"; }
            }
            if (showAttackTelegraph) UpdateTelegraphLine();
            yield return null;
        }
        SetTelegraphVisible(false);   // 既有紅線：沒開（showAttackTelegraph=false）時這行只是保險；任何路徑都不會殘留
        if (alignDist >= 0.05f) BirdAttackScheduler.Log("ALIGN-DONE", this, $"結果={alignEndReason} 花了{Time.time - alignStartTime:F1}s 最終X誤差={Mathf.Abs(alignTargetX - transform.position.x):F2}m");

        // 4. 程式切換為飛行動畫並播放振翅音效
        PlayAnim(diveAnimName);
        if (flapClip != null)
        {
            if (audioSource != null) audioSource.PlayOneShot(flapClip, AudioManager.SfxVolume);
            else AudioSource.PlayClipAtPoint(flapClip, transform.position, AudioManager.SfxVolume);
        }
        currentState = BirdState.Diving;
        BirdAttackScheduler.Stat("DIVE");
        float descentStartY = transform.position.y;
        float lockY = _hasLockedDiveTarget ? _lockedDiveTarget.y : (playerTrans != null ? playerTrans.position.y : descentStartY - 4f);
        float heightToTarget = Mathf.Max(0f, descentStartY - lockY);
        if (BirdAttackScheduler.debugEnabled)
        {
            BirdAttackScheduler.Log("DIVE", this, $"DescentSpeed={diveDescentSpeed:F1}m/s 到落點高度={heightToTarget:F1}m ExpectedDescentTime={heightToTarget / Mathf.Max(0.1f, diveDescentSpeed):F2}s LockX={alignTargetX:F1}");
            _diveStartPos = transform.position;
        }
        _minPlayerDist = float.MaxValue;
        _diveStartTime = Time.time;

        // 垂直向下；X 鎖死在落點上方，下降期間完全不看玩家位置
        diveDirection = Vector3.down;
        float faceX = playerTrans != null ? Mathf.Sign(playerTrans.position.x - transform.position.x) : 1f;
        Vector3 lookDown = new Vector3(faceX * 0.35f, -1f, 0f).normalized;   // 稍微低頭朝玩家那側，避免 LookRotation 與 up 平行

        // 5. 垂直慢速下降，直到命中玩家、護盾或地面
        float diveTimer = 0f;
        // 逾時：預估下降時間（從現在高度到玩家腳下 2.5 公尺）＋ 2 秒，最少 5 秒。慢速下降不會被舊的固定 5 秒誤判。
        float expectedDescent = Mathf.Max(0f, descentStartY - (lockY - 2.5f)) / Mathf.Max(0.1f, diveDescentSpeed);
        float maxDiveDuration = Mathf.Max(5.0f, expectedDescent + 2.0f);
        Vector3 lastCheckPos = transform.position;
        float stagnationTimer = 0f;

        while (currentState == BirdState.Diving)
        {
            diveTimer += Time.deltaTime;
            stagnationTimer += Time.deltaTime;

            // 超時防呆：保險，視為撞地插地
            if (diveTimer >= maxDiveDuration)
            {
                Debug.LogWarning($"【鳥群系統】{gameObject.name} 下降逾時，觸發插地淡出！");
                OnHitGround();
                yield break;
            }

            // 停滯卡死防呆：只有在接近地表高度時，若卡住位移小於 0.08m 超過 0.5s 才判定撞擊 (絕不在半空中誤判定)
            if (stagnationTimer >= 0.5f)
            {
                float movedDist = Vector3.Distance(transform.position, lastCheckPos);
                if (movedDist < 0.08f && playerTrans != null && transform.position.y <= (playerTrans.position.y + 1.0f))
                {
                    Debug.LogWarning($"【鳥群系統】{gameObject.name} 下降觸地停滯，判定插地！");
                    OnHitGround();
                    yield break;
                }
                lastCheckPos = transform.position;
                stagnationTimer = 0f;
            }

            // ★ 落地掃描：沿下降方向掃這一幀會走過的距離，碰到地面／岩石／掩體就停在表面，就地縮小消失
            float sweepLen = diveDescentSpeed * Time.deltaTime + 0.4f;
            if (SweepForSurface(diveDirection, sweepLen, out Vector3 surfacePoint))
            {
                transform.position = surfacePoint - diveDirection * 0.12f;
                // 診斷：離鎖定落點的高度還很高（> 2.5 公尺）就被環境擋下，記成「被環境提前終止」，並印出擋住它的碰撞體
                float aboveTarget = transform.position.y - lockY;
                if (aboveTarget > 2.5f)
                {
                    BirdAttackScheduler.Stat("DIVE-ENV");
                    BirdAttackScheduler.Log("DIVE-END-ENV", this, $"Collider={(_lastSweepCollider != null ? _lastSweepCollider.name : "?")} 離落點高度={aboveTarget:F1}m");
                }
                OnHitGround();
                yield break;
            }

            // 最低高度防穿防呆：若掉落到地表以下（掃描沒抓到的邊緣情況），也立刻就地消失
            if (playerTrans != null && transform.position.y < (playerTrans.position.y - 2.0f))
            {
                Debug.LogWarning($"【鳥群系統】{gameObject.name} 下降低於地表高度，觸發防穿插地！");
                OnHitGround();
                yield break;
            }

            // 只動 Y：X 維持在鎖定落點上方（不追蹤玩家、不被物理推走）
            Vector3 dp = transform.position;
            dp.x = alignTargetX;
            dp.y -= diveDescentSpeed * Time.deltaTime;
            transform.position = dp;

            Quaternion targetRot = Quaternion.LookRotation(lookDown, Vector3.up) * Quaternion.Euler(modelRotationOffset);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * 8f);

            // 命中判定：當鳥撲擊接近目標時結算
            if (playerTrans != null)
            {
                float distToPlayer = Vector2.Distance(new Vector2(transform.position.x, transform.position.y), new Vector2(playerTrans.position.x, playerTrans.position.y));
                if (distToPlayer < _minPlayerDist) { _minPlayerDist = distToPlayer; _minDistPlayerX = playerTrans.position.x; _minDistBirdX = transform.position.x; _minDistTime = Time.time - _diveStartTime; }
                
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
                    if (distToPlayer <= hitRadius)
                    {
                        // 主動石化硬撐中：她是石頭，啄不動，直接彈飛（企劃：按住不放＝抗風抗鳥）
                        PlayerPetrification pet = playerTrans.GetComponentInParent<PlayerPetrification>();
                        if (pet == null) pet = playerTrans.GetComponentInChildren<PlayerPetrification>();
                        if (pet != null && pet.isPetrified)
                        {
                            Debug.LogWarning($"🪨【鳥群系統】{gameObject.name} 撞上石化硬撐中的主角！啄不動，彈飛！");
                            BounceOff(null);
                            yield break;
                        }

                        if (PlayerMovement.IsHardCutsceneLocked || PlayerRespawnSystem.IsAnyRespawning)
                        {
                            // 文字卡／轉場／重生進行中：她被演出凍住，不算被啄到（09-04 log：綠洲的 M3 卡播到一半被鳥啄死、黑屏重生疊在卡上）
                            BounceOff(null);
                            yield break;
                        }

                        if (harmless)
                        {
                            // ★0905 示範俯衝：碰到她也只是彈開
                            BounceOff(null);
                            yield break;
                        }

                        if (PlayerPetrification.IsGodMode)
                        {
                            Debug.LogWarning($"🛡️【無敵模式】{gameObject.name} 撲擊命中無敵主角！鳥怪正常彈開，主角不觸發死亡重生！");
                        }
                        else if (retreatInsteadOfKill)
                        {
                            // ★1001 逼退取代死亡：沿玩家前進方向的反方向推她，製造「前進有阻力」
                            //   上面所有既有豁免（石化硬撐／護盾／演出鎖定／harmless／無敵）都在這之前，
                            //   行為完全沒變；這裡只取代最後那條「命中即死」。
                            BirdAttackScheduler.Stat("HIT");
                            BirdAttackScheduler.Stat("RETREAT");
                            BirdAttackScheduler.Log("HIT", this, "");
                            BirdAttackScheduler.Log("RETREAT", this, $"push={retreatPushSpeed:F1}x{retreatPushDuration:F2}s");
                            ApplyPlayerRetreat();
                        }
                        else
                        {
                            BirdAttackScheduler.Stat("HIT");
                            BirdAttackScheduler.Log("HIT", this, "Kill→Respawn");
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
    /// ★0920 攻擊被中斷／取消時，保證紅色攻擊路徑一定被關掉。
    ///
    /// Root Cause：AttackCoroutine 的正常流程是「前搖結束 → SetTelegraphVisible(false) → 俯衝」，
    /// 而 BounceOff() / OnHitGround() 會先呼叫 StopAllCoroutines()。前搖期間被護盾撞到時，
    /// 協程就停在 while 迴圈裡，那行關閉永遠跑不到，紅線（useWorldSpace 的 LineRenderer）
    /// 會凍在半空，直到物件被 SetActive(false) 才一起消失。
    ///
    /// 這裡不新增第二個控制來源——仍然只呼叫既有的 SetTelegraphVisible()，
    /// 只是把「取消攻擊必須清掉預告」這件事收斂成一個所有中斷路徑共用的入口。
    /// 正常的 Lock-on／Warning 時長／Laser 開關時機／Lunge 時機完全沒有動。
    /// </summary>
    private void ClearAttackTelegraph()
    {
        _hasLockedDiveTarget = false;
        SetTelegraphVisible(false);
    }

    private void OnDisable()
    {
        if (_registeredReinforcement)
        {
            _registeredReinforcement = false;
            _activeReinforcements = Mathf.Max(0, _activeReinforcements - 1);
        }

        // 物件被關掉／場景卸載時，紅線不留殘影（SetTelegraphVisible(false) 不會建立任何物件，可安全呼叫）
        ClearAttackTelegraph();
    }

    // ── 紅色攻擊路徑（前搖預告）─────────────────────────
    private void SetTelegraphVisible(bool on)
    {
        if (!on)
        {
            if (_telegraphLine != null) _telegraphLine.enabled = false;
            return;
        }

        if (_telegraphLine == null)
        {
            GameObject lineObj = new GameObject("[AttackTelegraph]");
            lineObj.transform.SetParent(transform, false);
            _telegraphLine = lineObj.AddComponent<LineRenderer>();
            _telegraphLine.useWorldSpace = true;
            _telegraphLine.positionCount = 2;
            _telegraphLine.textureMode = LineTextureMode.Stretch;
            _telegraphLine.alignment = LineAlignment.View;
            _telegraphLine.numCapVertices = 4;
            Shader sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh != null) _telegraphLine.material = new Material(sh);
            _telegraphLine.sortingOrder = 35;
        }
        _telegraphLine.startWidth = telegraphWidth;
        _telegraphLine.endWidth = telegraphWidth * 0.35f;
        _telegraphLine.enabled = true;
        UpdateTelegraphLine();
    }

    private void UpdateTelegraphLine()
    {
        if (_telegraphLine == null || !_telegraphLine.enabled || !_hasLockedDiveTarget) return;

        Vector3 from = transform.position;
        Vector3 to = _lockedDiveTarget;
        from.z = originalPosition.z;
        to.z = originalPosition.z;
        Vector3 dir = to - from;
        dir.z = 0f;
        float dist = dir.magnitude;
        if (dist < 0.01f) return;
        dir /= dist;
        Vector3 end = from + dir * (dist + telegraphOvershoot);

        _telegraphLine.SetPosition(0, from);
        _telegraphLine.SetPosition(1, end);

        // 呼吸閃爍，醒目但不搶戲
        float pulse = 0.55f + 0.45f * Mathf.Sin(Time.time * 11f);
        Color head = telegraphColor; head.a = telegraphColor.a * pulse;
        Color tail = telegraphColor; tail.a = telegraphColor.a * pulse * 0.25f;
        _telegraphLine.startColor = head;
        _telegraphLine.endColor = tail;
    }

    /// <summary>沿俯衝方向掃描實體表面（地面、岩石、掩體）。略過自己、玩家、護盾、其他鳥與 Trigger。</summary>
    private Collider _lastSweepCollider;

    private bool SweepForSurface(Vector3 dir, float length, out Vector3 point)
    {
        point = Vector3.zero;
        if (dir.sqrMagnitude < 0.0001f) return false;

        RaycastHit[] hits = Physics.RaycastAll(transform.position, dir, length, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        bool found = false;
        for (int i = 0; i < hits.Length; i++)
        {
            Collider c = hits[i].collider;
            if (c == null) continue;
            Transform ct = c.transform;
            if (ct == transform || ct.IsChildOf(transform)) continue;
            if (c.GetComponentInParent<IndividualBirdEnemy>() != null) continue;
            if (c.CompareTag("Player") || c.GetComponentInParent<PlayerMovement>() != null || c.GetComponentInParent<PlayerShield>() != null) continue;
            if (hits[i].distance < best)
            {
                best = hits[i].distance;
                point = hits[i].point;
                found = true;
                _lastSweepCollider = c;
            }
        }
        return found;
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

    /// <summary>將鳥貼齊到目前卡入的最近表面上，避免插地判定觸發時位置已經穿入地面內部太深</summary>
    private void SnapToNearestSurface()
    {
        Vector3 dir = diveDirection.sqrMagnitude > 0.0001f ? diveDirection.normalized : Vector3.down;

        // 先沿著俯衝反方向找回目前卡住的那個表面
        if (Physics.Raycast(transform.position - dir * 1.5f, dir, out RaycastHit hit, 3.0f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider != null && hit.collider.transform != transform && !hit.collider.transform.IsChildOf(transform))
            {
                Vector3 snapped = hit.point - dir * 0.12f;
                snapped.z = originalPosition.z;
                transform.position = snapped;
                return;
            }
        }

        // 備援：直接往下找地面貼齊
        if (Physics.Raycast(transform.position + Vector3.up * 1.0f, Vector3.down, out RaycastHit downHit, 3.0f, ~0, QueryTriggerInteraction.Ignore))
        {
            Vector3 snapped = downHit.point;
            snapped.z = originalPosition.z;
            transform.position = snapped;
        }
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

        // ★0905 示範俯衝：指定了目標點就衝那裡（例如手套旁的地面）
        if (overrideTarget != null)
        {
            targetPosition = new Vector3(overrideTarget.position.x, overrideTarget.position.y, originalPosition.z);
            return;
        }

        Vector3 playerPos = playerTrans.position;
        Vector3 groundTargetPos = new Vector3(playerPos.x, playerPos.y, originalPosition.z);

        // ★1001 路線預判：改瞄「等牠飛到時玩家會在的位置」。
        //   這支函式只在 AttackCoroutine 的 Warning 開頭被呼叫一次（lock-on 的那一刻），
        //   之後整段俯衝都沿鎖定的直線走，所以不會每幀重算，57 隻鳥也不會變成效能負擔。
        if (enableRoutePrediction && overrideTarget == null && _currentAttackType == BirdAttackType.PredictedPosition)
        {
            groundTargetPos.x = PredictPlayerX(playerPos);
        }

        // ★1006 預設忽略 behaviorType（舊版 PlayerOffset 殘留），所有鳥統一走「定點/預判」兩種；要保留舊行為請勾 useLegacyBehaviorType
        switch (useLegacyBehaviorType ? behaviorType : BirdBehavior.DirectPlayer)
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
    /// ★1001 預測玩家會經過的 X：PlayerX + PlayerVelocityX × PredictionTime。
    ///
    /// PredictionTime 完全沿用既有 attack flow 的時間，不另外建一套：
    ///     前搖/對準 ＝ max(warningDuration, 水平距離 ÷ diveAlignSpeed)
    ///     下降      ＝ 鳥到玩家高度的垂直距離 ÷ diveDescentSpeed
    ///   PredictionTime = (前搖/對準 + 下降) × predictionTimeMultiplier，再夾在 min/max 之間。
    ///
    /// 玩家站著不動 → velocityX ≈ 0 → 預判點自動收回她目前位置，不會把攻擊點推到很遠。
    /// 玩家往右跑 → 預判點在右邊；往左跑 → 在左邊，方向自動跟著速度正負號。
    /// 最後再用 predictionDistanceLimit 夾住，保證落點永遠在可反應範圍內。
    /// </summary>
    private float PredictPlayerX(Vector3 playerPos)
    {
        if (_playerRb == null && playerTrans != null)
        {
            _playerRb = playerTrans.GetComponent<Rigidbody>();
            if (_playerRb == null) _playerRb = playerTrans.GetComponentInParent<Rigidbody>();
            _playerMove = playerTrans.GetComponent<PlayerMovement>();
            if (_playerMove == null) _playerMove = playerTrans.GetComponentInParent<PlayerMovement>();
        }

        // 玩家的水平速度：優先用剛體實際速度（已含狼減速／拖曳／坡度折算），退而用指令速度
        float playerVx = 0f;
        if (_playerRb != null) playerVx = _playerRb.linearVelocity.x;
        else if (_playerMove != null) playerVx = _playerMove.CommandedHorizontalSpeed;

        // 幾乎沒在動就不預判，直接打當下位置（避免站著不動時攻擊點亂跑）
        if (Mathf.Abs(playerVx) < 0.2f) return playerPos.x;

        float warnTime = warningDuration > 0.05f ? warningDuration : 1.2f;

        // ★1010 預判時間＝前搖 ＋ 鳥飛到「預判落點」的時間，是個不動點問題（落點越遠，鳥飛越久，時間越長，落點又更遠）。
        //   1005 只迭代 2 次，沒收斂：實測鎖定時用的預判時間 2.23 秒，鳥實際 1.9 秒就到了落點，
        //   落點比玩家真正會到的地方多 2 公尺左右，一直跑的玩家剛好在鳥到之前就過去了。
        //   現在迭代到收斂（落點每次只會修正「玩家速度／鳥速度」≈ 0.5 倍，6 次誤差 < 2%）。
        float predictionTime = minimumPredictionTime;
        float travelTimeGuess = 0f;
        float predictedX = playerPos.x;
        float sgn = Mathf.Sign(playerVx);
        for (int i = 0; i < 6; i++)
        {
            // ★1015 到達時間要照新的移動方式算：前搖期間水平對準（對準較久就延長，所以是 max）＋ 垂直下降到玩家高度。
            // 公式結構（預判時間 = 到達時間，落點 = 玩家位置 + 速度 × 預判時間）完全沒變，只是「到達時間」改吃新的移動模型。
            float alignTime = Mathf.Abs(transform.position.x - predictedX) / Mathf.Max(0.1f, diveAlignSpeed);
            float descendTime = Mathf.Max(0f, transform.position.y - playerPos.y) / Mathf.Max(0.1f, diveDescentSpeed);
            travelTimeGuess = descendTime;
            predictionTime = Mathf.Clamp((Mathf.Max(warnTime, alignTime) + travelTimeGuess) * predictionTimeMultiplier,
                                         minimumPredictionTime, maximumPredictionTime);
            predictedX = playerPos.x + Mathf.Clamp(playerVx * predictionTime, -predictionDistanceLimit, predictionDistanceLimit);
        }
        float lead = predictedX - playerPos.x;

        // ★1010 預判的「準度」：predictionPreciseChance（預設 70%）的攻擊打「剛好的攔截點」（再加 predictionPreciseOffset 公尺），
        //   其餘打「寬鬆點」：在 intercept ± predictionLooseRange 之間（沿玩家前進方向，正＝更前面）隨便落。
        //   用鳥名字＋第幾次預判做穩定雜湊，不是每次 Random：同一場同一隻鳥結果可重現。
        _predictCount++;
        float u1 = StableHash01(GetInstanceID() * 31 + _predictCount * 17 + 1);
        float u2 = StableHash01(GetInstanceID() * 131 + _predictCount * 71 + 7);
        bool precise = u1 < Mathf.Clamp01(predictionPreciseChance);
        float offset = precise
            ? predictionPreciseOffset
            : Mathf.Lerp(predictionLooseMin, predictionLooseMax, u2);
        float finalLead = Mathf.Clamp(lead + sgn * offset, -predictionDistanceLimit - Mathf.Abs(predictionLooseMax), predictionDistanceLimit + Mathf.Abs(predictionLooseMax));
        _dbgPredT = predictionTime;
        _dbgPredLead = finalLead;
        _dbgPrecise = precise;
        _dbgOffset = sgn * offset;
        return playerPos.x + finalLead;
    }

    // 穩定的 0..1 偽亂數（同樣的輸入永遠同樣的結果）
    private static float StableHash01(int seed)
    {
        float s = Mathf.Sin(seed * 12.9898f) * 43758.5453f;
        return s - Mathf.Floor(s);
    }

    /// <summary>
    /// ★1001 命中逼退：沿玩家前進方向的反方向推她。
    ///
    /// 刻意重用既有的 PlayerMovement.ApplyWindPush() 管道，不新增平行的外力系統：
    ///   ・斜坡分支與平地分支都已經正確處理 windOffset（斜坡會投影到坡面）
    ///   ・已有 IsWindPushBlocked() 的防抖保護（撞到牆就停止施力）
    ///   ・只作用在水平方向，不會破壞 slope movement／boulder push／wolf drag-down／jump／gravity
    /// ApplyWindPush 單次只維持 0.15 秒，所以這裡用一小段協程持續補推到 retreatPushDuration。
    /// </summary>
    private void ApplyPlayerRetreat()
    {
        if (_playerMove == null && playerTrans != null)
        {
            _playerMove = playerTrans.GetComponent<PlayerMovement>();
            if (_playerMove == null) _playerMove = playerTrans.GetComponentInParent<PlayerMovement>();
        }
        if (_playerMove == null || retreatPushSpeed <= 0f || retreatPushDuration <= 0f) return;

        // 推的方向＝玩家前進方向的反方向。速度太小就用她的面向，面向也沒有就用「鳥往玩家的反方向」
        float forward = 0f;
        if (_playerRb != null && Mathf.Abs(_playerRb.linearVelocity.x) > 0.2f)
            forward = Mathf.Sign(_playerRb.linearVelocity.x);
        else if (Mathf.Abs(_playerMove.FacingDirection.x) > 0.01f)
            forward = Mathf.Sign(_playerMove.FacingDirection.x);
        else
            forward = Mathf.Sign(playerTrans.position.x - transform.position.x);

        if (Mathf.Approximately(forward, 0f)) forward = 1f;

        _playerMove.StartCoroutine(RetreatPushRoutine(_playerMove, -forward * retreatPushSpeed, retreatPushDuration));
    }

    // 協程掛在玩家身上執行：鳥自己可能在這段時間內被 Destroy，掛在鳥身上會被中斷
    private static System.Collections.IEnumerator RetreatPushRoutine(PlayerMovement pm, float pushX, float duration)
    {
        float t = 0f;
        while (t < duration && pm != null)
        {
            pm.ApplyWindPush(pushX);
            t += Time.deltaTime;
            yield return null;
        }
    }

    /// <summary>
    /// 當撞擊地面物件時觸發 (由 GroundCollisionNotifier、物理碰撞或地表高度判定)
    /// </summary>
    public void OnHitGround()
    {
        if (currentState != BirdState.Diving) return;
        if (BirdAttackScheduler.debugEnabled && playerTrans != null)
        {
            Rigidbody prb = playerTrans.GetComponent<Rigidbody>();
            BirdAttackScheduler.Log("DIVE-RESULT", this, $"Mode={(_currentAttackType == BirdAttackType.FixedPosition ? "Current" : "Predicted")} 最近距離={_minPlayerDist:F1}m（第 {_minDistTime:F2}s，鳥X={_minDistBirdX:F1} 玩家X={_minDistPlayerX:F1}）鎖定落點X={_lockedDiveTarget.x:F1} 俯衝總時間={Time.time - _diveStartTime:F2}s 實際下降距離={(_diveStartPos.y - transform.position.y):F1}m 平均垂直速度={(_diveStartPos.y - transform.position.y) / Mathf.Max(0.01f, Time.time - _diveStartTime):F1}m/s 設定下降速度={diveDescentSpeed:F1} / 水平漂移={Mathf.Abs(transform.position.x - _diveStartPos.x):F2}m 現在玩家X={playerTrans.position.x:F1} 玩家vx={(prb != null ? prb.linearVelocity.x : 0f):F1}");
        }

        StopAllCoroutines(); // 立即停止俯衝攜程
        ClearAttackTelegraph();   // ★0920 任何中斷路徑都必須保證紅色路徑被關掉（見 ClearAttackTelegraph）

        // 校正插地深度：停滯/超時/直接碰撞等後備判定路徑觸發時，鳥當下位置可能已經穿入地面內部太深，
        // 統一往回貼齊到剛好卡在碰撞表面上（SweepForSurface 命中的路徑已經精準貼齊，這裡再做一次是安全的校正，不會有副作用）
        SnapToNearestSurface();

        // 記錄俯衝到地面的精確姿態，鎖死插地角度
        stuckRotation = transform.rotation;
        currentState = BirdState.Stuck;

        if (rb != null)
        {
            if (!rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }   // kinematic 時設速度只會噴警告
            rb.isKinematic = true;
        }

        // 暫停動畫播放，防止 FBX keyframe 覆蓋插地姿態
        if (animator != null)
        {
            animator.speed = 0f;
        }

        Debug.Log($"【鳥群系統】{gameObject.name} 撞擊地表/掩體！停格一瞬，就地縮小消失。");
        StartCoroutine(FadeAndDestroyCoroutine());
    }

    private IEnumerator FadeAndDestroyCoroutine()
    {
        // 撞擊後停格一段時間（讓玩家看清楚牠釘在哪），才開始漸漸縮小消失
        yield return new WaitForSeconds(Mathf.Max(stuckDuration, 0f));

        // 啟動淡出前將材質設定為透明渲染模式
        SetupMaterialsForFade();

        float elapsed = 0f;
        float realFadeDuration = Mathf.Max(fadeDuration, 0.3f);   // 依 Inspector 設定的時間漸漸縮小消失，不要瞬間消失
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
        BirdAttackScheduler.Stat("REMOVED");
        BirdAttackScheduler.Log("REMOVED", this, "");
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
            if (!IsAllSuppressed && postRespawnDelayTimer <= 0f && !PlayerRespawnSystem.IsAnyRespawning && PlayerRespawnSystem.IsPlayerMovingAfterRespawn && !UmbrellaZone.IsPlayerUnderUmbrella)
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
                if (harmless || PlayerPetrification.IsGodMode)
                {
                    if (!harmless) Debug.LogWarning($"🛡️【無敵模式】{gameObject.name} 碰撞無敵主角！鳥怪正常彈開，主角不觸發死亡重生！");
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
        ClearAttackTelegraph();   // ★0920 前搖中被護盾撞飛時，AttackCoroutine 會在這裡被砍掉，紅線必須自己關
        currentState = BirdState.Bounced;

        if (rb != null)
        {
            if (!rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }   // kinematic 時設速度只會噴警告
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
        BirdAttackScheduler.Stat("REMOVED");
        BirdAttackScheduler.Log("REMOVED", this, "");
        gameObject.SetActive(false);
        transform.localScale = originalScale;
    }

    // --- IResettable 實作 ---
    public void ResetToInitialState()
    {
        StopAllCoroutines();
        _requestQueued = false;
        _granted = false;
        BirdAttackScheduler.Remove(this);

        if (_isReinforcement)
        {
            Destroy(gameObject);
            return;
        }

        // 檢查存檔點進度：
        // 若當前存檔點已經推進到這隻鳥的原點之後 (代表玩家已通過該存檔點且該鳥已死亡/攻擊過)，則該鳥永久保持死亡消失！
        Vector3 currentCheckpoint = PlayerRespawnSystem.ActiveRespawnPosition;
        bool behindCheckpoint = currentCheckpoint != Vector3.zero && (originalPosition.x <= currentCheckpoint.x + 1.0f);
        // 存檔點之前（玩家已經過了的區段）：攻擊過的鳥永久消失，不刷新。
        if (hasAttackedOrDied && behindCheckpoint)
        {
            BirdAttackScheduler.Stat("RESET-GONE");
            gameObject.SetActive(false);
            return;
        }

        // ★1013 存檔點之前還沒攻擊過的鳥：保持原樣盤旋，但這一輪不再偵測、不從身後攻擊（BehindCheckpointBirdsCanAttack 可開）。
        _attackDisabled = behindCheckpoint && !BehindCheckpointBirdsCanAttack;
        BirdAttackScheduler.Stat(_attackDisabled ? "RESET-DECOR" : "RESET-REFRESH");

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
            if (!rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }   // kinematic 時設速度只會噴警告
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
        postRespawnDelayTimer = 4f; // 重生後給予 4 秒冷卻緩衝：剛復活時鳥群不會立刻發起襲擊
        _hasLockedDiveTarget = false;
        SetTelegraphVisible(false);
    }
}
