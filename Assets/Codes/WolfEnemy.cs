using UnityEngine;
using System.Collections;

[RequireComponent(typeof(Rigidbody))]
public class WolfEnemy : MonoBehaviour, IResettable
{
    [Header("追蹤設定")]
    [Tooltip("背對狼逃跑時，狼的追擊速度 (快速追擊，跑速設為 6)")]
    public float fastChaseSpeed = 6f;
    [Tooltip("偵測到玩家在遠處時，狼的慢走速度 (慢步接近，設為 3)")]
    public float slowChaseSpeed = 3f;
    [Tooltip("狼被迫往後退的退後速度 (負數代表往回走，設為 -1.5)")]
    public float retreatSpeed = -1.5f;
    [Tooltip("狼從慢走切換到快跑的距離閥值")]
    public float runDistanceThreshold = 6f;
    public float aggroDistanceX = 6f; // 靠近到 x=6 開始追蹤
    public float giveUpDistanceX = 12f; // 【新增】逃遠到 x=12 放棄追蹤

    [Header("物理免疫設定")]
    [Tooltip("狼要忽略碰撞的物件 Collider 清單 (例如：把 Stone Steps 平台的 Collider 拉進來，狼就不會撞到它們)")]
    public System.Collections.Generic.List<Collider> collidersToIgnore = new System.Collections.Generic.List<Collider>();

    [Header("高度追蹤限制")]
    [Tooltip("當主角高度超過狼多少距離，且主角懸空時，狼會停止追蹤，直到主角觸地")]
    public float stopChaseHeightDifference = 3.0f;

    [Header("貼地追擊")]
    [Tooltip("追擊時禁止任何向上的物理速度（撞到台階邊緣也不會被彈上天），狼永遠沿著地面前進")]
    public bool keepOnGroundWhileChasing = true;
    [Tooltip("腳下地面偵測射線的額外長度 (超過碰撞體底部多遠內視為貼地)")]
    public float groundCheckDistance = 0.4f;
    [Tooltip("視為可行走斜坡的最大角度，超過此角度視同牆壁/台階，改用防彈起邏輯")]
    public float maxWalkableSlopeAngle = 55f;

    [Header("貼地穩定性（0910 B：解決上下跳動與互相推擠）")]
    [Tooltip("狼與狼之間不產生物理推擠。保留狼↔地面、狼↔玩家、狼↔StopAttackObject 的碰撞。\n" +
             "每次 OnEnable 重新配對，所以 spawner 生出來的新狼也吃得到")]
    public bool ignoreWolfToWolfCollision = true;

    [Tooltip("前方太靠近同伴就放慢，讓狼群排成一列而不是疊成一團。\n" +
             "★只縮小速度，不改變追擊方向——玩家在右邊狼就永遠往右，不會為了閃同伴往左跑")]
    public bool useSoftSeparation = true;

    [Tooltip("開始注意前方同伴的距離。超過這個距離完全不減速")]
    [Range(0.5f, 6f)]
    public float separationRadius = 2.0f;

    [Tooltip("希望維持的最小間距。逼近到這個距離時減速到最大幅度")]
    [Range(0.2f, 4f)]
    public float minimumWolfDistance = 1.2f;

    [Tooltip("最大減速幅度。0.5 = 最慢也還有半速（不會停下來等同伴）。\n" +
             "設 1.0 會讓後面的狼完全停住，不建議——那會變成排隊卡死")]
    [Range(0f, 0.95f)]
    public float separationStrength = 0.5f;

    [Tooltip("依出生順序給每隻狼固定的 Z 深度層（第 0 隻 Z=0、第 1 隻 Z=1…）。\n" +
             "相機是正交投影，所以 Z 只影響繪製前後順序，不會讓狼變大變小或位移。\n" +
             "Rigidbody 已鎖 FreezePositionZ，物理不會讓狼在 Z 軸漂移")]
    public bool useDepthLayering = true;

    [Tooltip("每一層之間的 Z 間距。\n" +
             "★如果狼放到比較深的層會踩空掉下去，代表地面的碰撞體在 Z 軸不夠厚，把這個值調小。\n" +
             "正交相機下 0.2 跟 1.0 的視覺結果一樣（都只是換前後順序），所以調小沒有任何損失")]
    [Range(0.05f, 2f)]
    public float wolfZSpacing = 1f;

    [Tooltip("腳下三點採樣的間距（乘上碰撞體半長）。1 = 前後腳剛好在身體兩端。\n" +
             "把整個身長當成量尺去讀坡面，交界處的法線突變會被前後腳拉平")]
    [Range(0.3f, 1.5f)]
    public float groundProbeSpread = 0.9f;

    [Tooltip("採樣球的半徑倍率（乘上碰撞體半寬）。越大越能跨過小凹凸，但太大會提早抓到旁邊的牆")]
    [Range(0.3f, 1.2f)]
    public float groundProbeRadiusScale = 0.8f;

    [Tooltip("採樣球半徑的絕對上限（公尺）。★場景裡狼的 CapsuleCollider radius 是 2.196，\n" +
             "照倍率算出來會變成 1.76 公尺的大球，掃到的根本不是腳下那塊地。這裡夾住")]
    [Range(0.1f, 2f)]
    public float groundProbeMaxRadius = 0.45f;

    [Tooltip("三點採樣間距的絕對上限（公尺）。同理，避免拿四公尺的跨距去平均地形法線")]
    [Range(0.1f, 3f)]
    public float groundProbeMaxSpread = 0.6f;

    [Tooltip("開場檢查用：碰撞半徑超過這個值就在 Console 警告（不會自動改）")]
    public float colliderSanityMaxRadius = 0.8f;
    [Tooltip("開場檢查用：碰撞體中心離身體超過這個距離就警告（不會自動改）")]
    public float colliderSanityMaxCenterOffset = 0.5f;

    [Tooltip("★隔離測試用開關：懷疑上坡卡頓是貼地造成的，就把這個關掉跑一次比較。\n" +
             "貼地只負責防止真正離地，不會改變追擊方向、不碰 X 速度、只作用於真正的地面")]
    public bool useGroundSnap = true;

    [Tooltip("【貼地死區】腳離地小於這個距離就當作已經貼著，不做修正。避免對微小誤差反覆施力")]
    [Range(0f, 0.2f)]
    public float snapMinGap = 0.02f;

    [Tooltip("【貼地上限】腳離地超過這個距離就當作「真的離地了」（跳起／被彈飛），不准黏回去。\n" +
             "太大會讓狼在空中被硬拉下來，太小則蓋不住石頭凸起造成的浮空")]
    [Range(0.05f, 1f)]
    public float snapMaxGap = 0.35f;

    [Tooltip("貼地修正的最大向下速度。這是速度不是瞬移，物理照樣能把狼推開。\n" +
             "太大會看起來像被吸住，太小蓋不過凸起造成的彈跳")]
    [Range(0.5f, 20f)]
    public float maxSnapSpeed = 6f;

    [Header("斜坡方向平滑（0910 A：解決平地↔斜坡的頓挫感）")]
    [Tooltip("移動方向每秒最多能轉幾度。★注意這只平滑「移動方向」，地面偵測本身還是即時的。\n" +
             "太小 → 進坡出坡會有延遲感、方向追不上地形\n" +
             "太大 → 等於沒平滑，石頭碎面的法線抖動會直接傳到速度上\n" +
             "360 大約 0.11 秒轉完一個 40 度的坡，抖動濾得掉、轉場也跟得上")]
    [Range(60f, 1440f)]
    public float slopeDirectionSmoothSpeed = 360f;

    [Tooltip("坡面方向的死區（度）。新讀到的坡面方向跟目前持有的差異小於這個角度就不更新。\n" +
             "★這是為了讓「平滑」跟「防彈飛鉗制」用同一個目標值——兩邊各用各的會互相抵銷，\n" +
             "  平滑才剛把方向轉上去、鉗制馬上又壓回來，那就是上坡頓挫的來源。\n" +
             "太小 → 過濾不掉石頭碎面的法線雜訊；太大 → 真的變坡了也慢半拍才跟上")]
    [Range(0f, 20f)]
    public float slopeAngleDeadband = 5f;

    [Tooltip("地面偵測短暫射空時，沿用上一個有效法線多久（秒）。\n" +
             "★只拿來算移動方向，不會讓狼被假地面黏住。\n" +
             "石頭 MeshCollider 偶爾漏接一兩幀是常態，讓方向整個彈回水平才是頓挫來源。\n" +
             "0.12 秒約等於 6 個物理步，夠蓋掉漏接，又短到狼真的跳離地面時不會被黏著")]
    [Range(0f, 0.5f)]
    public float groundMemoryTime = 0.12f;

    [Header("🔍 斜坡診斷（驗完請關掉）")]
    [Tooltip("開啟後每隔一段時間在 Console 印出狼的完整移動狀態，包含「實際每秒位移」——\n" +
             "那個數字才能證明狼真的跑多快，設定值不算數。上坡跟平地各跑一次比較就知道問題在哪一層")]
    public bool debugSlopeLog = false;
    [Tooltip("診斷訊息的間隔（秒）。0.5 大約每秒兩行，不會洗版")]
    public float debugSlopeLogInterval = 0.5f;

    [Header("追擊節奏曲線（Catch-up AI，0910）")]
    [Tooltip("開啟後用「距離 → 速度」的平滑曲線，取代原本 runDistanceThreshold 的兩段式切換。\n" +
             "關掉就回到舊行為（slowChaseSpeed / fastChaseSpeed 兩段切換）")]
    public bool useCatchUpCurve = true;

    [Tooltip("【貼身距離】小於這個距離就用 nearChaseSpeed。玩家在這個範圍內要有反應空間")]
    public float nearDistance = 4f;
    [Tooltip("【貼身速度】比玩家(5)快一點點就好，讓玩家還躲得掉、跳得開。太快會變成無法閃避")]
    public float nearChaseSpeed = 5.5f;

    [Tooltip("【中距離】到這個距離用 cruiseChaseSpeed，是最常見的追擊狀態")]
    public float cruiseDistance = 10f;
    [Tooltip("【中距離速度】穩定壓迫，明顯比玩家快但追不上得很快")]
    public float cruiseChaseSpeed = 7f;

    [Tooltip("【追趕距離】拉開到這個距離以上就用滿 maxCatchUpSpeed，不會再更快")]
    public float maxCatchUpDistance = 20f;
    [Tooltip("【追趕速度上限】★這是硬上限，再遠也不會超過。\n" +
             "玩家基礎速度是 5，這裡設 10 等於玩家的兩倍——追得回來但不是瞬移作弊")]
    public float maxCatchUpSpeed = 10f;

    [Header("物理手感（0910 大升級：改成有加速度的真實移動）")]
    [Tooltip("起步／變速的加速度 (單位/秒²)。\n" +
             "越大越接近舊版的「瞬間到達目標速度」，越小越有體重感、起步越慢。\n" +
             "40 大約 0.15 秒從靜止加速到跑速 6，跟舊版感覺接近但撞到東西會有反應")]
    public float acceleration = 40f;

    [Tooltip("煞車／減速的加速度 (單位/秒²)。通常設得比 acceleration 大，停下來比較俐落")]
    public float braking = 60f;

    [Tooltip("狼的體重。★玩家是 10，狼原本只有 1——輕了 10 倍，撞在一起時狼會被玩家撞飛，看起來很假。\n" +
             "設成跟玩家相當或更重，撞擊才合理。0 或負數＝不覆寫，沿用 Inspector 上 Rigidbody 的值")]
    public float bodyMass = 12f;

    [Tooltip("開啟後自動把 Rigidbody 設成 Interpolate（消除畫面抖動）與 Continuous Speculative（防止高速穿模）。\n" +
             "場景裡狼的 Rigidbody 現在是 None + Discrete，兩個都會讓移動看起來怪")]
    public bool autoFixRigidbodySettings = true;

    [Header("身體貼合斜坡角度")]
    [Tooltip("狼在斜坡上時，身體是否跟著斜坡傾斜 (跑上坡時與地面平行，而不是直挺挺地站著)")]
    public bool alignVisualToSlope = true;

    [Tooltip("要傾斜的視覺物件 (留空自動抓子物件的 SpriteRenderer)。\n" +
             "只轉視覺、不轉根物件，避免膠囊碰撞體在斜坡上卡住")]
    public Transform visualToAlign;

    [Tooltip("身體轉向斜坡的平滑速度 (越大轉越快)")]
    public float slopeAlignSpeed = 8f;

    [Tooltip("身體最多傾斜幾度 (避免極陡的坡讓狼看起來翻過去)")]
    public float maxVisualAlignAngle = 40f;

    private Transform player;
    private PlayerMovement playerMovement; 
    private Rigidbody rb;
    private Collider col;

    [Header("安全防護")]
    [Tooltip("狼生成或啟用時的咬人豁免時間 (秒)，防止刷出時因碰撞重疊直接咬傷主角")]
    public float spawnAttachImmunityTime = 1.0f;
    private float enableTime = -999f;

    [Header("🎵 狼群音效 (Wolf SFX)")]
    [Tooltip("發現玩家/進入追擊時的近距離狼嚎 (例如 狼嚎_近2)")]
    public AudioClip aggroHowlSFX;
    [Tooltip("狼群狂奔腳步聲音效 (例如 wolves_running)")]
    public AudioClip runSFX;
    [Range(0f, 1f)] public float soundVolume = 0.85f;

    private AudioSource _runAudioSource;

    // Update 決定「這一幀想跑多快」，FixedUpdate 才真的推動身體
    private float _targetSpeedX = 0f;
    private bool _hasTargetSpeed = false;
    private float _lastFacingX = 1f;   // 停下來時沒有目標方向，用最後一次的朝向來算坡面方向
    private Vector3 _smoothMoveDir = Vector3.zero;   // 平滑後的實際前進方向
    private Vector3 _heldTargetDir = Vector3.zero;   // 過了 deadband 的目標方向（平滑與防彈飛鉗制共用同一份）
    private bool _lastGroundedFacingWolf = false;    // 玩家最後一次「踩在地上」時的 123 判定結果
    private Vector3 _lastGoodNormal = Vector3.up;    // 最後一次有效的地面法線（射空時暫時沿用）

    // ── Aggro Lock（每隻狼自己一份，不是 static）──
    private bool _aggroLocked = false;      // 一旦鎖定就永遠記得玩家，只有 Reset 會清掉
    private Transform _targetPlayer = null; // 鎖定當下記住的目標

    // 狀態鎖
    private bool isChasing = false;
    private bool isAttached = false;
    private bool isStunned = false; // 被 StopAttackObject 打到時的硬直狀態

    private Vector3 _initialPosition;
    private Quaternion _initialRotation;
    private Transform _initialParent;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        col = GetComponent<Collider>();
        _initialPosition = transform.position;
        _initialRotation = transform.rotation;
        _initialParent = transform.parent;
    }

    private void OnEnable()
    {
        enableTime = Time.time;

        if (col == null) col = GetComponent<Collider>();
        if (!_allWolves.Contains(this)) _allWolves.Add(this);
        RefreshWolfPairIgnore();
        // 新來的這隻也要讓場上舊的那些認識牠（IgnoreCollision 是雙向設定，但清單要互相更新）
        foreach (WolfEnemy w in _allWolves) if (w != null && w != this) w.RefreshWolfPairIgnore();

        ApplyDepthLayer();
    }

    /// <summary>
    /// 依照出生順序給這隻狼一個固定的 Z 深度層。
    /// 相機是正交投影（orthographic），所以 Z 只影響繪製前後順序，
    /// 不會讓狼看起來變大變小或位移——這是純粹的圖層分離。
    /// ★ 由 OnEnable 依註冊清單的索引指派，所以 spawner 生的、場景本來就有的、
    ///   重生後重新啟用的，都會自動拿到正確的層，不用另外維護計數器。
    /// </summary>
    private void ApplyDepthLayer()
    {
        if (!useDepthLayering) return;
        if (isAttached) return;   // 咬在玩家身上時是玩家的子物件，這時候不要動牠的位置

        int index = _allWolves.IndexOf(this);
        if (index < 0) index = 0;

        _assignedZ = index * wolfZSpacing;
        Vector3 p = transform.position;
        transform.position = new Vector3(p.x, p.y, _assignedZ);
    }

    private float _assignedZ = 0f;

    private void OnDisable()
    {
        _allWolves.Remove(this);
    }

    /// <summary>
    /// 進入追擊。WolfSpawner 生成後會直接呼叫這支，Update 靠距離觸發時也走這支。
    /// ★兩個入口都會鎖定 Aggro，之後就永遠記得玩家。
    /// </summary>
    public void StartChase()
    {
        if (!isChasing && aggroHowlSFX != null && AudioManager.Instance != null)
        {
            AudioManager.Instance.PlaySFXAt(aggroHowlSFX, transform.position, soundVolume);
        }
        isChasing = true;

        // Spawner 直接呼叫進來的也要鎖，不然只有靠距離觸發的那些才有記憶
        _aggroLocked = true;
        if (_targetPlayer == null) _targetPlayer = player;
    }

    void Start()
    {
        if (rb == null) rb = GetComponent<Rigidbody>();
        if (col == null) col = GetComponent<Collider>();

        // ★0910：場景裡狼的 Rigidbody 是 Interpolate=None + Collision Detection=Discrete + Mass=1，
        //   這三個是「移動看起來很假」的直接原因：
        //   - None：物理跑 50 次/秒、畫面跑 60~144 次/秒，中間沒有內插 → 狼在畫面上一格一格跳
        //   - Discrete：跑速 6 時一個物理步就移動 0.12 單位，撞薄的地形會直接穿過去
        //   - Mass=1：玩家是 10，狼比玩家輕 10 倍，撞在一起是狼被撞飛，完全反過來
        if (autoFixRigidbodySettings && rb != null)
        {
            if (rb.interpolation != RigidbodyInterpolation.Interpolate)
                rb.interpolation = RigidbodyInterpolation.Interpolate;

            // ★0910 修正我自己上一版的選擇：本來設成 ContinuousSpeculative，
            //   但 Speculative 是「預測式接觸」，在 MeshCollider 的三角面接縫上
            //   容易產生鬼影碰撞（ghost contact）——狼跑在石頭表面會被不存在的邊緣頂一下，
            //   那正是「上下跳動」的另一個來源。
            //   狼只需要對「靜態地形」防穿模，Continuous 的掃描式就夠而且不會有鬼影。
            //   （水下玩家那邊維持 Speculative 是對的，那是無厚度面的穿模問題，情況不同。）
            if (rb.collisionDetectionMode != CollisionDetectionMode.Continuous)
                rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

            if (bodyMass > 0f && !Mathf.Approximately(rb.mass, bodyMass))
                rb.mass = bodyMass;

            // 2D 橫向捲軸約束：Z 不參與 Gameplay，旋轉一律鎖死（身體傾斜是 Visual 子物件在做）
            RigidbodyConstraints want2D = RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotation;
            if (rb.constraints != want2D) rb.constraints = want2D;
        }


        // 執行碰撞忽略設定
        if (col != null && collidersToIgnore != null)
        {
            foreach (Collider targetCol in collidersToIgnore)
            {
                if (targetCol != null)
                {
                    Physics.IgnoreCollision(col, targetCol, true);
                    Debug.Log($"【物理忽略】狼 '{gameObject.name}' 已設定忽略與 '{targetCol.gameObject.name}' 的碰撞");
                }
            }
        }
        
        GameObject pObj = GameObject.FindGameObjectWithTag("Player");
        if (pObj != null)
        {
            player = pObj.transform;
            // 抓取玩家身上的 PlayerMovement 組件
            playerMovement = pObj.GetComponent<PlayerMovement>();
        }

        WarnIfColliderLooksWrong();
    }

    /// <summary>
    /// 開場檢查碰撞體尺寸合不合理。不自動改——碰撞體大小會直接影響
    /// 狼撞地面、撞玩家、咬人的判定，那是關卡與手感的決定，不該由程式偷偷動。
    /// 但它異常的話所有東西都會怪，所以一定要在 Console 吼出來。
    /// </summary>
    private void WarnIfColliderLooksWrong()
    {
        CapsuleCollider cap = col as CapsuleCollider;
        if (cap == null) return;

        float worldScale = Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y));
        float worldRadius = cap.radius * worldScale;
        float centerOffset = new Vector2(cap.center.x, cap.center.y).magnitude * worldScale;

        if (worldRadius > colliderSanityMaxRadius || centerOffset > colliderSanityMaxCenterOffset)
        {
            Debug.LogWarning(
                $"⚠️【狼碰撞體異常】'{gameObject.name}' 的 CapsuleCollider 尺寸不合理：\n" +
                $"   radius = {cap.radius:F2}（世界尺寸 {worldRadius:F2} 公尺，直徑 {worldRadius * 2f:F2}）\n" +
                $"   height = {cap.height:F2}\n" +
                $"   center = {cap.center}（離身體中心 {centerOffset:F2} 公尺）\n" +
                $"   一隻狼的碰撞半徑合理值大約 0.3～0.6 公尺，center 應該接近 0。\n" +
                $"   目前這個尺寸會讓：地面偵測抓到不是腳下的地形、狼在距離身體好幾公尺外就撞到東西、\n" +
                $"   狼群之間怎麼排都會重疊。程式這邊已經把地面採樣的尺寸夾住了，\n" +
                $"   但碰撞判定本身還是照這個尺寸走——建議在 Inspector 把 radius 調到 0.4 左右、center 歸零。");
        }
    }

    void Update()
    {
        // 如果正在硬直、或已經咬住了、或找不到玩家，就不執行追蹤邏輯
        if (isStunned || isAttached || player == null) return;

        // 計算與玩家在 X 軸的絕對距離
        float distanceX = Mathf.Abs(player.position.x - transform.position.x);

        // ★0911 Aggro Lock：第一次進入追擊之後就記住玩家，之後不再每幀重新判斷「還要不要追」。
        //   原本這裡有兩條解除追蹤的路徑，兩條都會造成「追到一半突然放棄」：
        //     1. 玩家跳得比狼高 stopChaseHeightDifference 以上 → 直接 isChasing = false
        //        玩家一往上坡跑、或跳一下，狼就跟丟。坡地追擊根本追不動。
        //     2. distanceX > giveUpDistanceX → 放棄
        //        場景值 giveUpDistanceX 24 只比 aggroDistanceX 22 多 2，
        //        玩家在邊界來回走會讓狼反覆進出追擊狀態，狼嚎也一直重播。
        //   偵測跟記憶要分開：Player Detection 只負責「第一次觸發」，
        //   Ground Detection 只負責「怎麼沿地面走」，兩者都不該決定「還記不記得玩家」。
        if (!_aggroLocked)
        {
            if (distanceX <= aggroDistanceX)
            {
                _aggroLocked = true;
                _targetPlayer = player;
                StartChase();   // 走 StartChase() 才會發出狼嚎
            }
        }
        else
        {
            // 鎖定之後：目標永遠是當初記住的那個玩家，不再重新尋找
            if (_targetPlayer != null) player = _targetPlayer;
            if (!isChasing) isChasing = true;   // 任何原因被關掉都自動接回來
        }

        // 執行追蹤
        if (isChasing)
        {
            ChasePlayer();

            if (runSFX != null)
            {
                if (_runAudioSource == null)
                {
                    _runAudioSource = gameObject.AddComponent<AudioSource>();
                    _runAudioSource.clip = runSFX;
                    _runAudioSource.loop = true;
                    _runAudioSource.spatialBlend = 1f; // 3D 空間音效
                    _runAudioSource.minDistance = 3f;
                    _runAudioSource.maxDistance = 20f;
                    _runAudioSource.volume = AudioManager.ScaleSfx(soundVolume * 0.75f);
                }
                _runAudioSource.volume = AudioManager.ScaleSfx(soundVolume * 0.75f);
                if (!_runAudioSource.isPlaying) _runAudioSource.Play();
            }
        }
        else if (_runAudioSource != null && _runAudioSource.isPlaying)
        {
            _runAudioSource.Stop();
        }
    }

    private void FixedUpdate()
    {
        // 沿著地面前進：若腳下是可行走的斜坡，速度沿斜坡表面投影貼地移動；
        // 若不是斜坡（例如撞到台階邊緣被物理彈起），才清掉向上速度避免飛起來
        // ★ 原本限定 isChasing 才處理，導致「玩家回頭、狼往後退」那段沒有貼合地面。
        //   退後同樣是沿地面移動，這裡不再限制追擊狀態。
        if (rb == null || rb.isKinematic) return;
        if (isAttached || isStunned) { _hasTargetSpeed = false; return; }

        // ★★ 0910 第二版：整個 FixedUpdate 只寫一次 linearVelocity。
        //
        //   上一版分成兩段（先對 x 軸加速、再沿坡面重算），那是錯的，有兩個問題：
        //
        //   問題 A：一個物理步做了兩次加速，而且第一次會污染第二次的量測。
        //     第一段把 vx 加上 a·dt，第二段再沿坡面量：
        //       currentAlong = (s·cosθ + a·dt)·cosθ + s·sinθ·sinθ = s + a·dt·cosθ
        //     量出來永遠比真實速度多 a·dt·cosθ，於是速度接近目標時會被誤判成「太快了」，
        //     跑去走 braking 分支（60）而不是 acceleration（40）。上坡等於永遠在踩煞車。
        //
        //   問題 B（上坡專屬，這個才是主因）：只要斜坡分支沒跑到，
        //     就會掉進下面的 else 把向上速度清成 0。而上坡需要正的 y 速度、下坡不需要，
        //     所以這個 else 對上坡是致命的、對下坡完全無感——剛好就是「上坡慢、下坡正常」。
        //     斜坡分支沒跑到的情況比想像多：地面偵測是一條細射線，
        //     打在石頭這種凹凸不平的 MeshCollider 上很容易射空或打到奇怪的面。
        //     每射空一幀，爬坡速度就被歸零一次。
        //
        //   所以這一版：先決定前進方向（平地或坡面），沿「同一個方向」量目前速度，
        //   沿同一個方向加速，最後只寫一次。量測方向跟寫入方向一致，就不會有落差。
        Vector3 v = rb.linearVelocity;

        // ★0911：原本是「用掉就清掉」（_hasTargetSpeed = false）。
        //   那是錯的——Update 一幀跑一次，FixedUpdate 一幀可能跑 0~2 次。
        //   跑兩次的時候第二次會讀到「沒有目標」→ wanted = 0 → 狼開始煞車，
        //   幀率一低就變成「衝一下、頓一下」，而且看起來像追到一半放棄。
        //   改成目標值一直有效，直到下一個 Update 覆寫它。
        float wanted = _hasTargetSpeed ? _targetSpeedX : 0f;
        if (Mathf.Abs(wanted) > 0.01f) _lastFacingX = Mathf.Sign(wanted);
        else if (Mathf.Abs(v.x) > 0.01f) _lastFacingX = Mathf.Sign(v.x);

        // ── 決定這一步要沿哪個方向前進 ──
        bool groundFound = TryGetGroundSlope(out RaycastHit groundHit, out float slopeAngle);
        bool onWalkableSlope = keepOnGroundWhileChasing && groundFound
                            && slopeAngle > 0.5f && slopeAngle < maxWalkableSlopeAngle;

        // 短暫射空時沿用上一個有效法線「只拿來算方向」，不當成「還踩在地上」。
        // 石頭 MeshCollider 偶爾漏接一兩幀是常態，讓方向整個彈回水平才是頓挫的來源。
        // 超過 groundMemoryTime 就放掉，避免狼真的離地之後還被假地面黏住。
        Vector3 usedNormal = Vector3.up;
        bool haveNormal = false;
        if (onWalkableSlope)
        {
            usedNormal = groundHit.normal;
            haveNormal = true;
            _lastGoodNormal = groundHit.normal;
            _lastGoodGroundTime = Time.time;
        }
        else if (!groundFound && Time.time - _lastGoodGroundTime <= groundMemoryTime)
        {
            usedNormal = _lastGoodNormal;
            haveNormal = true;
        }

        // ── 目標方向：即時算，不平滑（偵測要即時）──
        Vector3 targetMoveDir = new Vector3(_lastFacingX, 0f, 0f);
        if (haveNormal)
        {
            Vector3 d = Vector3.ProjectOnPlane(targetMoveDir, usedNormal);
            if (d.sqrMagnitude > 0.0001f) targetMoveDir = d.normalized;
            else if (onWalkableSlope) onWalkableSlope = false;
        }

        // ── 實際用的方向：平滑轉過去（移動方向才平滑）──
        //   ★0910 A：原本每個物理步直接把 moveDir 跳到新的坡面方向。
        //     石頭地面是 MeshCollider，相鄰的三角面法線差很多，
        //     於是 slopeDir 一幀一個樣，速度方向跟著抖，就是那個「頓一下」的感覺。
        //   用 RotateTowards 限制「每秒最多轉幾度」：
        //     - 跟 fixedDeltaTime 綁一起，換 FPS 或改物理步長行為都一致
        //     - 是角度上限不是比例衰減，所以不會有「永遠追不到」的殘留誤差
        //     - 小碎面造成的高頻抖動會被濾掉，真正的坡度變化照樣跟得上
        // ★★0911 上坡卡頓的真正原因就在這裡，而且是我自己上一版造成的：
        //   移動用的是「平滑後」的方向（_smoothMoveDir），
        //   但下面防彈飛的鉗制用的是「即時未平滑」的 targetMoveDir。
        //   兩個值在凹凸地面上每一幀都不一樣——只要原始法線某一幀讀得比平滑值平，
        //   鉗制就把爬坡的 y 速度砍掉一次。上坡時這件事每幾幀就發生一次，
        //   等於平滑才剛把方向轉上去，鉗制馬上又壓回來，就是那個頓挫。
        //   平滑跟每幀修正互相抵銷，正是「反應延遲＋每幀修正」的雙重問題。
        //
        //   解法不是再加第三套平滑，是讓兩邊用「同一個值」：
        //   對目標方向加 deadband——跟目前持有的目標差異小於門檻就不更新，
        //   有實質差異才換。之後平滑的目標跟鉗制的基準都用這個 _heldTargetDir。
        if (_heldTargetDir.sqrMagnitude < 0.0001f) _heldTargetDir = targetMoveDir;

        bool turnedAround = Mathf.Sign(_heldTargetDir.x) != Mathf.Sign(targetMoveDir.x)
                            && Mathf.Abs(targetMoveDir.x) > 0.01f;
        if (turnedAround || Vector3.Angle(_heldTargetDir, targetMoveDir) > slopeAngleDeadband)
        {
            _heldTargetDir = targetMoveDir;   // 轉向、或坡度真的變了才更新
        }

        if (_smoothMoveDir.sqrMagnitude < 0.0001f) _smoothMoveDir = _heldTargetDir;   // 第一幀直接對齊，不要從 (0,0,0) 轉
        if (turnedAround) _smoothMoveDir = _heldTargetDir;   // 左右轉向是「換方向」不是「換坡度」，不要慢慢繞過去

        float maxRad = Mathf.Max(1f, slopeDirectionSmoothSpeed) * Mathf.Deg2Rad * Time.fixedDeltaTime;
        _smoothMoveDir = Vector3.RotateTowards(_smoothMoveDir, _heldTargetDir, maxRad, 0f).normalized;

        Vector3 moveDir = _smoothMoveDir;

        // 把這一幀算好的地面資訊交給 LateUpdate 的視覺傾角用。
        // 視覺層不再自己打射線，兩邊共用同一份資料，角度才不會各抖各的。
        _visualHasGround = haveNormal && onWalkableSlope;
        if (_visualHasGround) _visualGroundNormal = usedNormal;

        // ── 沿著同一個方向量目前速度、加速、寫回 ──
        //   平地時 moveDir=(±1,0,0)，Dot 就等於 ±v.x，跟以前完全一樣。
        //   斜坡時把 y 也算進去，因為爬坡的速度有一部分在 y 上——
        //   只量 x 的話每次都會少掉一個 cos，那正是上一版的坑。
        Vector3 measured = onWalkableSlope ? new Vector3(v.x, v.y, 0f) : new Vector3(v.x, 0f, 0f);
        float current = Vector3.Dot(measured, moveDir);
        float target = Mathf.Abs(wanted);   // moveDir 已經帶了方向，這裡只要大小

        bool speedingUp = target > current;
        float rate = speedingUp ? acceleration : braking;
        if (rate <= 0f) rate = 40f;

        float next = Mathf.MoveTowards(current, target, rate * Time.fixedDeltaTime);
        Vector3 nv = moveDir * next;

        // ★防彈飛鉗制：平滑方向會落後坡面，上坡跑到坡頂／坡度變緩的瞬間，
        //   舊的（比較陡的）方向還帶著向上分量，狼會被自己的速度甩上天。
        //   ★基準改用 _heldTargetDir（過了 deadband 的那個），不是每幀跳動的原始值——
        //     用原始值的話，法線只要抖一下鉗制就砍一次爬坡速度，
        //     那就是上坡頓挫的來源。現在鉗制跟平滑用同一個目標，不會互相抵銷。
        float maxUpY = _heldTargetDir.y * next;
        if (nv.y > maxUpY) nv.y = maxUpY;

        if (onWalkableSlope)
        {
            // ★貼地（Ground Snap）：只在「確定站在可行走地面、而且只是浮起一點點」時，
            //   加一點點向下的速度把腳壓回地面。
            //   為什麼需要：坡面速度是純切線方向，沒有任何東西把狼往地面壓。
            //   石頭表面一個小凸起把狼頂起來之後，切線速度會讓牠繼續飄，
            //   等重力拉回來已經過了好幾幀——那就是「上下跳動」的感覺。
            //   ★用速度不用 teleport：物理照樣能把狼推開，不會穿模也不會硬扯位置。
            //   ★有上下限：低於 snapMinGap 當作已經貼著不動它；
            //     高於 snapMaxGap 代表真的離地（跳起來、被彈飛），不准黏回去。
            float gap = useGroundSnap ? best_GapToGround(groundHit) : 0f;
            if (useGroundSnap && gap > snapMinGap && gap < snapMaxGap)
            {
                float snapDown = Mathf.Min(gap / Time.fixedDeltaTime, maxSnapSpeed);

                // ★★0911 這就是上坡減速的真正兇手，而且是我自己上一版寫的。
                //   原本是 nv.y -= snapDown，沿「世界 Y」往下減。
                //   問題：在坡上，世界 Y 方向對沿坡方向是有投影的——
                //       Dot((0,-1,0), moveDir) = -sin(坡度)
                //   所以每貼地一次，就順手偷走 snapDown × sin(坡度) 的沿坡速度。
                //   40 度坡 sin=0.64，貼地只要出力 2 m/s 就吃掉 1.28 m/s 的爬坡速度，
                //   而加速度一個物理步只補得回 0.8。平地 sin=0 完全沒影響，
                //   所以症狀剛好是「只有上坡變慢」。
                //
                //   正解：貼地要沿「地面法線的反方向」施加，那是垂直於坡面的，
                //   對沿坡方向的投影 Dot(-normal, moveDir) 剛好是 0——
                //   因為 moveDir 本來就是投影到坡面上的切線方向。
                //   這樣貼地只做它該做的事（把腳壓回地面），一點都不碰前進速度。
                nv += -usedNormal * snapDown;
            }
            rb.linearVelocity = new Vector3(nv.x, nv.y, v.z);
        }
        else
        {
            // 不在可行走坡面上：水平照算，垂直交還給重力。
            // 只有「確定踩在地上而且是平地」才壓掉向上速度（防止撞台階邊緣被彈飛）。
            // ★ 不能因為地面偵測射空就壓——那會把爬坡速度殺掉，就是上一版的問題 B。
            float y = v.y;
            if (groundFound && slopeAngle <= 0.5f && y > 0f) y = 0f;
            rb.linearVelocity = new Vector3(nv.x, y, v.z);
        }

        if (debugSlopeLog) LogSlopeDiagnostics(groundFound, onWalkableSlope, slopeAngle, groundHit, moveDir, current, target, next);
    }

    private float _dbgNextLog = 0f;
    private Vector3 _dbgLastPos;
    private float _dbgLastTime = -1f;

    /// <summary>
    /// 【暫時性診斷】把上坡變慢會用到的每一個數字都印出來，包含「實際每秒移動了多少距離」。
    /// 驗完就把 debugSlopeLog 關掉（或整段刪掉）。
    /// </summary>
    private void LogSlopeDiagnostics(bool groundFound, bool onSlope, float slopeAngle, RaycastHit hit,
                                     Vector3 moveDir, float current, float target, float next)
    {
        if (Time.time < _dbgNextLog) return;

        // 實際位移：這是唯一能證明「狼真的跑多快」的數字，不看設定值
        float measuredSpeed = -1f;
        if (_dbgLastTime > 0f)
        {
            float dt = Time.time - _dbgLastTime;
            if (dt > 0.0001f) measuredSpeed = Vector3.Distance(transform.position, _dbgLastPos) / dt;
        }
        _dbgLastPos = transform.position;
        _dbgLastTime = Time.time;
        _dbgNextLog = Time.time + debugSlopeLogInterval;

        Vector3 v = rb.linearVelocity;
        float distX = player != null ? Mathf.Abs(player.position.x - transform.position.x) : -1f;

        Debug.Log($"🐺【狼斜坡診斷】{gameObject.name}\n" +
                  $"  地面: {(groundFound ? $"有 (法線 {hit.normal}, 坡度 {slopeAngle:F1}°)" : "★射空★")}" +
                  $"  可行走坡面: {(onSlope ? "是" : "否（走平地分支）")}\n" +
                  $"  前進方向 moveDir: {moveDir}  (長度 {moveDir.magnitude:F3})\n" +
                  $"  沿方向速度  current: {current:F2}  →  target: {target:F2}  →  寫入 next: {next:F2}\n" +
                  $"  _targetSpeedX: {_targetSpeedX:F2}   距離玩家 X: {distX:F1}   曲線算出: {(useCatchUpCurve && distX >= 0 ? EvaluateChaseSpeed(distX).ToString("F2") : "n/a")}\n" +
                  $"  剛體速度: x={v.x:F2}  y={v.y:F2}  合速度={new Vector2(v.x, v.y).magnitude:F2}\n" +
                  $"  ★實際每秒位移: {(measuredSpeed >= 0 ? measuredSpeed.ToString("F2") : "首次取樣")}  ← 這個才是真的跑多快\n" +
                  $"  acceleration={acceleration} braking={braking} 這步用的={(target > current ? "加速" : "煞車")}");
    }

    /// <summary>
    /// 讓狼的身體跟著斜坡傾斜，跑上坡時與地面平行，而不是直挺挺地站著。
    /// 只轉視覺子物件，不轉根物件——根物件上有膠囊碰撞體，轉了會在斜坡上卡住。
    /// 左右翻面是 WolfSpriteAnimator 用 localScale / flipX 做的，跟這裡的 Z 軸旋轉不衝突。
    /// </summary>
    private void UpdateSlopeAlignment()
    {
        if (!alignVisualToSlope) return;

        if (visualToAlign == null)
        {
            SpriteRenderer sr = GetComponentInChildren<SpriteRenderer>();
            if (sr != null && sr.transform != transform) visualToAlign = sr.transform;
            if (visualToAlign == null && transform.childCount > 0) visualToAlign = transform.GetChild(0);
            if (visualToAlign == null) return;
        }

        float targetAngle = 0f;

        // ★0910：原本這裡自己再打一次 TryGetGroundSlope——等於每幀多做一組 SphereCast，
        //   而且讀到的是「這一幀的原始法線」，跟 FixedUpdate 用的那份是兩套資料。
        //   視覺傾角因此跟著原始法線抖，這就是「地面角度變化時 Visual 角度卡頓」。
        //   改成直接吃 FixedUpdate 已經算好、而且已經平滑過的那份，資料只有一個來源。
        if (!isAttached && !isStunned && _visualHasGround)
        {
            // 地面法線換算成 Z 軸傾角：平地法線是 (0,1,0) → 0 度；坡往右上升 → 正角度
            targetAngle = Mathf.Atan2(-_visualGroundNormal.x, _visualGroundNormal.y) * Mathf.Rad2Deg;
            targetAngle = Mathf.Clamp(targetAngle, -maxVisualAlignAngle, maxVisualAlignAngle);
        }
        // 離地或被咬住/硬直時 targetAngle 維持 0，身體平滑轉回直立

        Vector3 e = visualToAlign.localEulerAngles;
        float current = e.z > 180f ? e.z - 360f : e.z;

        // ★幀率無關的指數平滑。原本是 LerpAngle(a, b, Time.deltaTime * speed)，
        //   那個 t 直接乘 deltaTime，60fps 跟 144fps 的收斂速度不一樣。
        //   1 - exp(-speed * dt) 才是正確寫法，任何幀率下轉過去的時間都相同。
        float t = 1f - Mathf.Exp(-Mathf.Max(0.01f, slopeAlignSpeed) * Time.deltaTime);
        float next = Mathf.LerpAngle(current, targetAngle, t);
        visualToAlign.localEulerAngles = new Vector3(e.x, e.y, next);
    }

    // 給 LateUpdate 的視覺傾角用：由 FixedUpdate 寫入，只有一個資料來源
    private Vector3 _visualGroundNormal = Vector3.up;
    private bool _visualHasGround = false;

    // ── 狼與狼不互推 ──
    private static readonly System.Collections.Generic.List<WolfEnemy> _allWolves = new System.Collections.Generic.List<WolfEnemy>();

    /// <summary>
    /// 讓這隻狼跟場上其他所有狼互相忽略碰撞。
    /// 為什麼寫在程式而不是用 Layer Collision Matrix：狼目前在 Default 層，
    /// 要用 Matrix 得先開一個新 Layer 再改場景，而專案裡好幾處射線遮罩是用層名寫死的
    /// （例如玩家的貼地射線排除 Player / Ignore Raycast / UI），搬層要一併稽核那些遮罩，
    /// 在最終除錯階段風險太高。用 IgnoreCollision 效果一樣而且不動任何既有設定。
    /// ★ IgnoreCollision 是掛在 Collider 實例上的，物件被 Destroy／重生成就會消失，
    ///   所以每次 OnEnable 都要重新配對一遍（spawner 生出來的新狼也吃得到）。
    /// </summary>
    private void RefreshWolfPairIgnore()
    {
        if (!ignoreWolfToWolfCollision || col == null) return;

        // ★0911 升級成真正的「層級排除」：狼已經搬到專用的 Wolf 層（Layer 6），
        //   這裡用 Unity 的 Collider.excludeLayers 把自己的層整個排掉。
        //   這是物理引擎層級的排除，等同於 Layer Collision Matrix 把 Wolf×Wolf 關掉，
        //   而且不用去手改 DynamicsManager.asset 那串 256 字元的 hex 矩陣（改錯會整包壞掉）。
        //   ★跟 IgnoreCollision 的差別：IgnoreCollision 是「兩個 Collider 實例」的配對，
        //     物件一重生成就失效；excludeLayers 是掛在 Collider 上的層遮罩，
        //     新生成的狼只要在 Wolf 層、跑過這裡一次就永久有效，不用跟場上每一隻配對。
        int wolfLayerBit = 1 << gameObject.layer;
        col.excludeLayers |= wolfLayerBit;

        // 子物件上如果也有 Collider（腳、頭之類）一併處理
        foreach (Collider c in GetComponentsInChildren<Collider>(true))
        {
            if (c != null) c.excludeLayers |= wolfLayerBit;
        }

        // 保留配對式忽略當第二層保險：萬一有哪隻狼忘了設 Layer，這層還擋得住
        for (int i = _allWolves.Count - 1; i >= 0; i--)
        {
            WolfEnemy other = _allWolves[i];
            if (other == null) { _allWolves.RemoveAt(i); continue; }
            if (other == this || other.col == null) continue;
            Physics.IgnoreCollision(col, other.col, true);
        }
    }

    private void LateUpdate()
    {
        UpdateSlopeAlignment();
    }

    // 從狼腳下往下打一條射線，取得地面碰撞資訊與斜坡角度 (與 PlayerMovement.CheckGrounded 邏輯一致)
    private bool TryGetGroundSlope(out RaycastHit hit, out float slopeAngle)
    {
        hit = default;
        slopeAngle = 0f;
        if (col == null) return false;

        Vector3 origin = col.bounds.center;
        float rayLength = col.bounds.extents.y + groundCheckDistance;
        int layerMask = ~LayerMask.GetMask("Ignore Raycast");

        // ★0910 第三版：三點採樣（後腳／中心／前腳）。
        //   SphereCast 已經比細射線穩很多，但「單一個 hit」讀到的還是「一個」三角面的法線。
        //   狼身體有長度，跨在兩個面交界時，中心那一點會在相鄰兩幀之間讀到差很多的法線，
        //   坡度、坡面方向、視覺傾角就跟著跳——這是抖動的來源之一。
        //   三點取平均等於用狼的身長去「量」整體坡面，交界處的突變會被前後腳拉平。
        //   ★ 這不是延遲：三個 cast 都是這一幀即時打的，沒有沿用舊資料。
        // ★0911：採樣尺寸原本完全跟著碰撞體大小走，但場景裡狼的 CapsuleCollider 是
        //   radius 2.196（直徑 4.4 公尺）、而且 center 偏移 X = -4.29。
        //   照那個尺寸算出來的採樣球半徑會是 1.76 公尺、三點間距 4 公尺——
        //   等於拿一顆兩公尺的球去掃前後四公尺的地形，會掃到牆、掃到遠處的凸起，
        //   平均出來的法線根本不是腳下那塊地。
        //   碰撞體該不該縮是關卡/美術的決定，我不擅自改；但採樣尺寸是純內部的東西，
        //   這裡用絕對上限夾住，讓地面偵測不受那個異常尺寸影響。
        float rawRadius = Mathf.Min(col.bounds.extents.x, col.bounds.extents.z) * groundProbeRadiusScale;
        float radius = Mathf.Clamp(rawRadius, 0.05f, groundProbeMaxRadius);

        float rawSpread = col.bounds.extents.x * groundProbeSpread;
        float halfLen = Mathf.Clamp(rawSpread, 0.05f, groundProbeMaxSpread);

        Vector3 sum = Vector3.zero;
        int valid = 0;
        RaycastHit best = default;
        float bestDist = float.MaxValue;

        for (int i = -1; i <= 1; i++)
        {
            Vector3 start = origin + Vector3.up * 0.05f + new Vector3(i * halfLen, 0f, 0f);

            // ★0911 重要修正：原本用 SphereCast 只拿「最近的那一個」hit，
            //   打到別隻狼就整個採樣點作廢。但狼群疊在一起時，最近的那個常常就是別隻狼，
            //   等於三個採樣點全滅 → 沒有地面 → 走平地分支，而且更糟的是
            //   如果沒排除，別隻狼的背會被當成地面：坡度、坡面方向、Ground Snap
            //   全部拿狼的身體去算，狼就會被吸附騎到另一隻狼背上。
            //   改用 SphereCastAll 拿到路徑上「所有」的 hit，跳過狼跟玩家，
            //   繼續往下找真正的地面。
            int n = Physics.SphereCastNonAlloc(start, radius, Vector3.down, _groundHitBuf,
                                               rayLength, layerMask, QueryTriggerInteraction.Ignore);
            RaycastHit? picked = null;
            float pickedDist = float.MaxValue;

            for (int k = 0; k < n; k++)
            {
                RaycastHit h = _groundHitBuf[k];
                if (h.collider == null) continue;
                if (!IsRealGround(h.collider)) continue;

                // 濾掉明顯異常的面：垂直牆壁、天花板不是「腳下的地」，混進平均只會把坡度算歪
                if (Vector3.Angle(Vector3.up, h.normal) >= 89f) continue;

                if (h.distance < pickedDist) { pickedDist = h.distance; picked = h; }
            }

            if (picked == null) continue;
            RaycastHit g = picked.Value;

            sum += g.normal;
            valid++;

            // 中心點優先當代表 hit（貼地距離要用最接近身體中線的那個才準）
            float d = (i == 0) ? g.distance - 1000f : g.distance;
            if (d < bestDist) { bestDist = d; best = g; }
        }

        if (valid > 0)
        {
            Vector3 avg = (sum / valid).normalized;
            best.normal = avg;          // 用平均法線取代單點法線，位置資訊維持代表 hit 的
            hit = best;
            slopeAngle = Vector3.Angle(Vector3.up, avg);
            _lastGoodGroundTime = Time.time;
            return true;
        }

        // 三點全空才退回最原始的細射線，當最後保險
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit downHit, rayLength, layerMask, QueryTriggerInteraction.Ignore))
        {
            if (downHit.collider == col || downHit.collider.transform.IsChildOf(transform)) return false;
            hit = downHit;
            slopeAngle = Vector3.Angle(Vector3.up, downHit.normal);
            _lastGoodGroundTime = Time.time;
            return true;
        }
        return false;
    }

    private float _lastGoodGroundTime = -999f;
    private readonly RaycastHit[] _groundHitBuf = new RaycastHit[12];

    /// <summary>
    /// 這個 Collider 算不算「真正的地面」。
    /// 狼的身體、腳、背，還有玩家，全部不算——被當成地面的話，
    /// 坡度、坡面方向、Ground Snap 會拿別人的身體去算，狼就會被吸附騎上去。
    /// 用 GetComponentInParent 判斷而不是靠 Tag：Collider 常常掛在子物件上，Tag 不一定有設。
    /// </summary>
    private bool IsRealGround(Collider c)
    {
        if (c == col) return false;
        if (c.transform.IsChildOf(transform)) return false;
        if (c.GetComponentInParent<WolfEnemy>() != null) return false;      // 任何一隻狼（含自己）
        if (c.GetComponentInParent<PlayerMovement>() != null) return false; // 玩家
        return true;
    }

    /// <summary>腳底離地面還有多少（正值＝浮在空中）。給 Ground Snap 判斷用。</summary>
    private float best_GapToGround(RaycastHit groundHit)
    {
        if (col == null || groundHit.collider == null) return 0f;
        // 用碰撞體底部中心的高度跟地面接觸點比。斜坡上這個值本來就會有一點誤差，
        // 所以上面用 snapMinGap 當死區，小誤差不會觸發貼地。
        return col.bounds.min.y - groundHit.point.y;
    }

    /// <summary>
    /// 距離 → 追擊速度的平滑曲線（Catch-up / Rubber Band，不是作弊 AI）。
    ///
    /// 為什麼要換掉原本的兩段式切換：場景實際值是 aggroDistanceX 22、runDistanceThreshold 18、
    /// slowChaseSpeed 5、fastChaseSpeed 6，而玩家基礎速度也是 5。所以：
    ///   距離 18~22：狼 5 ＝ 玩家 5 → 追擊速度跟玩家一模一樣，永遠拉不近，等於白追
    ///   距離 0~18 ：狼 6 vs 玩家 5 → 每秒只縮短 1 單位，從 18 追到貼身要 18 秒
    /// 玩家只要一直往前跑就穩穩甩開，狼完全沒有壓迫感。
    ///
    /// 換成曲線後（錨點都照玩家速度 5 訂）：
    ///   ≤ 4  貼身 5.5：只比玩家快 0.5，玩家還跳得開、閃得掉，不會變成無法閃避
    ///   10   中距 7.0：穩定壓迫，每秒縮短 2 單位
    ///   ≥ 20 追趕 10.0：玩家的兩倍，從 20 追到貼身約 3.2 秒——追得回來但不是瞬移
    /// 中間用線性內插，所以速度是連續變化的，不會在門檻上忽快忽慢。
    /// maxCatchUpSpeed 是硬上限，再遠也不會超過。
    /// </summary>
    private float EvaluateChaseSpeed(float distanceX)
    {
        float near = Mathf.Max(0.1f, nearDistance);
        float cruise = Mathf.Max(near + 0.1f, cruiseDistance);
        float far = Mathf.Max(cruise + 0.1f, maxCatchUpDistance);

        float speed;
        if (distanceX <= near)
        {
            speed = nearChaseSpeed;
        }
        else if (distanceX <= cruise)
        {
            speed = Mathf.Lerp(nearChaseSpeed, cruiseChaseSpeed, (distanceX - near) / (cruise - near));
        }
        else if (distanceX <= far)
        {
            speed = Mathf.Lerp(cruiseChaseSpeed, maxCatchUpSpeed, (distanceX - cruise) / (far - cruise));
        }
        else
        {
            speed = maxCatchUpSpeed;
        }

        return Mathf.Min(speed, maxCatchUpSpeed);   // 硬上限，任何情況都不會超過
    }

    /// <summary>
    /// 算出「因為前面有同伴，這一步該打幾折」。回傳 0~1，永遠不會是負數。
    ///
    /// 設計上刻意只做「前後避讓」不做側向：這是 2D 橫向捲軸，
    /// 側向只有 Y（會跟重力打架）跟 Z（是畫面深度層、已經鎖住），兩個都不能拿來閃避。
    /// 所以擠在一起時的解法是「後面的放慢」，狼群會自然排成一列跟上，
    /// 而不是全部黏在同一個 X 上。沒有任何一隻會停下來等別人——
    /// 最慢也只到 (1 − separationStrength) 倍，預設還有半速。
    /// </summary>
    private float ComputeSeparationFactor(float directionX)
    {
        if (!useSoftSeparation || Mathf.Abs(directionX) < 0.01f) return 1f;

        float factor = 1f;
        for (int i = 0; i < _allWolves.Count; i++)
        {
            WolfEnemy other = _allWolves[i];
            if (other == null || other == this) continue;
            if (other.isAttached || other.isStunned) continue;   // 咬住／硬直中的狼不算障礙

            float dx = other.transform.position.x - transform.position.x;
            if (dx * directionX <= 0f) continue;                 // 只看前進方向前方的

            float dist = Mathf.Abs(dx);
            if (dist > separationRadius) continue;

            // separationRadius 處不減速，逼近到 minimumWolfDistance 時減到 (1 − strength)
            float t = Mathf.InverseLerp(separationRadius, Mathf.Min(minimumWolfDistance, separationRadius - 0.01f), dist);
            float f = 1f - t * Mathf.Clamp01(separationStrength);
            if (f < factor) factor = f;
        }
        return Mathf.Clamp01(factor);
    }

    private void ChasePlayer()
    {
        // 算出狼到玩家的 X 軸方向與正負號值 (1 或 -1)
        float dirToPlayerX = player.position.x - transform.position.x;
        float directionX = Mathf.Sign(dirToPlayerX);

        // 偵測玩家是否回頭看著狼 (玩家朝向與狼追擊方向相反)
        // ★0911 新規則：玩家在空中時不做新的 123 木頭人判定。
        //   原本只看 FacingDirection，玩家跳起來在空中轉身也算「回頭」，
        //   狼就會突然開始倒退——那不是玩家的意圖，是跳躍的副作用。
        //   ★是「暫停」不是「重置」：離地期間沿用落地前最後一次的判定結果，
        //     落地後再繼續正常判斷。這樣跳一下不會把進行中的木頭人狀態洗掉。
        //   ★用玩家真正的 isGrounded，不是用有沒有按跳躍鍵。
        bool isPlayerFacingWolf = _lastGroundedFacingWolf;
        if (playerMovement != null)
        {
            if (playerMovement.isGrounded)
            {
                float playerFacingX = playerMovement.FacingDirection.x;
                // 如果玩家面朝方向與狼追擊方向相反，代表玩家正在看著狼
                isPlayerFacingWolf = (directionX * playerFacingX < 0);
                _lastGroundedFacingWolf = isPlayerFacingWolf;   // 記住落地時的判定，供空中沿用
            }
            // 在空中：isPlayerFacingWolf 維持 _lastGroundedFacingWolf，不更新也不清掉
        }

        float currentSpeed = 0f;

        if (isPlayerFacingWolf)
        {
            // 玩家回頭看著狼：123木頭人機制，狼往後退！
            currentSpeed = retreatSpeed;
        }
        else
        {
            // 玩家背對著狼：依距離決定速度
            float distanceX = Mathf.Abs(dirToPlayerX);
            currentSpeed = useCatchUpCurve ? EvaluateChaseSpeed(distanceX)
                                           : (distanceX > runDistanceThreshold ? slowChaseSpeed : fastChaseSpeed);
        }

        // ★0911 Soft Separation：前方太靠近別隻狼就放慢，讓狼群自然排成一列而不是疊成一團。
        //   ★只縮小速度大小，絕對不改變 directionX——玩家在右邊，狼就永遠往右，
        //     不會因為要閃開同伴而往左跑。倍率夾在 0~1，乘完不可能變負數。
        //   ★只看「我前進方向的前方」那些狼。後面的狼不關我的事，不然會互相拉住誰都跑不動。
        currentSpeed *= ComputeSeparationFactor(directionX);

        // ★0910：這裡只「決定要跑多快」，真正推動身體交給 FixedUpdate。
        //   原本是在這裡直接寫 rb.linearVelocity，而 ChasePlayer() 是 Update() 呼叫的——
        //   Update 跟著畫面更新（60～144 次/秒不固定），物理是固定 50 次/秒，兩者對不上：
        //   撞到東西時 PhysX 算出來的反彈速度，下一個 Update 就被整條覆蓋掉，
        //   所以狼撞到什麼都沒反應、會硬擠過去，而且畫面上會抖。
        _targetSpeedX = directionX * currentSpeed;
        _hasTargetSpeed = true;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (isStunned || isAttached || Time.time < enableTime + spawnAttachImmunityTime) return;
        if (rb == null) rb = GetComponent<Rigidbody>();

        // 咬到玩家 (接觸)
        if (collision.gameObject.CompareTag("Player"))
        {
            // 碰到玩家瞬間收掉水平衝力，避免殘餘力量把玩家撞飛。
            // ★0910：原本是整條 Vector3.zero，連垂直速度也一起抹掉——
            //   狼在半空中咬到人會瞬間定在空中不受重力，很出戲。
            //   只收水平那一軸，垂直交還給重力。
            if (rb != null)
            {
                Vector3 hv = rb.linearVelocity;
                rb.linearVelocity = new Vector3(0f, Mathf.Min(hv.y, 0f), 0f);
                rb.angularVelocity = Vector3.zero;
            }
            _targetSpeedX = 0f;
            _hasTargetSpeed = false;

            // 觸發螢幕受傷回饋 (震動與閃紅邊)
            if (ScreenFeedbackManager.Instance != null)
            {
                ScreenFeedbackManager.Instance.TriggerHitFeedback();
            }

            AttachToPlayer();
        }
    }


    private void OnTriggerEnter(Collider other)
    {
        // 碰到 StopAttackObject 放開玩家
        if (other.CompareTag("StopAttackObject"))
        {
            DetachAndStun();
        }
    }

    // --- 核心機制：咬住玩家 ---
    private void AttachToPlayer()
    {
        isAttached = true;
        isChasing = false;

        LightMoteCollector.NotifyWolfAttached();   // ★0905 廢墟光絮：被咬住掉約 1/3（沒有 Collector 時什麼都不做）

        // 咬住主角時立即停止奔跑腳步聲音效！
        if (_runAudioSource != null && _runAudioSource.isPlaying)
        {
            _runAudioSource.Stop();
        }

        // 1. 關閉狼的物理作用，避免跟玩家的物理產生衝突亂飛
        rb.linearVelocity = Vector3.zero;
        rb.isKinematic = true;
        
        // 把碰撞體設為 Trigger，這樣就不會卡住玩家，但還能感應 StopAttackObject
        col.isTrigger = true; 

        // 2. 將狼設為玩家的子物件，這樣狼就會「黏」在玩家身上跟著動
        transform.SetParent(player);

        // 3. 呼叫 PlayerMovement 裡的 AddWolf 方法來減速
        if (playerMovement != null)
        {
            playerMovement.AddWolf();
        }
    }

    // --- 核心機制：鬆口並停止攻擊 ---
    private void DetachAndStun()
    {
        if (!isAttached && !isChasing) return; // 如果本來就沒在攻擊就不用管

        isAttached = false;
        isStunned = true; // 進入硬直狀態，暫時不會再咬人

        // 1. 脫離玩家的子物件階層
        transform.SetParent(null);
        ApplyDepthLayer();   // 鬆口之後回到自己的深度層（咬住期間跟著玩家的 Z 跑）

        // 2. 恢復物理作用，讓牠掉回地上
        rb.isKinematic = false;
        col.isTrigger = false;

        // 3. 呼叫 PlayerMovement 裡的 RemoveWolf 方法來恢復速度
        if (playerMovement != null)
        {
            playerMovement.RemoveWolf();
        }

        // 4. 【修改】給狼一個往反方向彈開的小動作，視覺效果更好
        float pushDirection = Mathf.Sign(transform.position.x - player.position.x);
        rb.linearVelocity = new Vector3(pushDirection * 3f, 5f, 0); 

        // 5. 休息 3 秒後再重新開始偵測玩家
        StartCoroutine(StunCooldown(3f));
    }

    IEnumerator StunCooldown(float time)
    {
        yield return new WaitForSeconds(time);
        isStunned = false;
        isChasing = false; // 重新判斷距離再決定要不要追
    }

    // --- IResettable 實作 ---
    public void ResetToInitialState()
    {
        StopAllCoroutines();
        if (isAttached)
        {
            transform.SetParent(_initialParent);

            // ★0910：原本這裡只解開 Parent，沒有通知玩家「我鬆口了」。
            //   玩家身上的 attachedWolvesCount 目前是靠 PlayerPetrification.ClearAllNegativeEffects()
            //   順手歸零的——等於狼的計數要靠石化系統來收尾，是個很脆的耦合：
            //   哪天那支腳本被移掉或改動，玩家重生後就會帶著「身上有 3 隻狼」的減速永遠跑不動。
            //   這裡自己收自己的尾。RemoveWolf 內部會夾在 0，重複呼叫也不會變負數。
            if (playerMovement != null) playerMovement.RemoveWolf();
        }
        isAttached = false;
        isChasing = false;
        isStunned = false;

        // Aggro Lock 只在重生／場景重置時解除，這是唯一的解鎖點。
        // 遊玩中不管高度差、距離、射線射空、Trigger 離開，都不會讓狼忘記玩家。
        _aggroLocked = false;
        _targetPlayer = null;
        _hasTargetSpeed = false;
        _targetSpeedX = 0f;
        _smoothMoveDir = Vector3.zero;
        _heldTargetDir = Vector3.zero;
        _lastGroundedFacingWolf = false;

        transform.position = _initialPosition;
        transform.rotation = _initialRotation;
        ApplyDepthLayer();   // 回到出生點之後重新分配深度層，重生後順序才不會亂掉

        // 斜坡傾斜也要歸零，不然重生後身體會維持上一次的傾角
        if (visualToAlign != null)
        {
            Vector3 e = visualToAlign.localEulerAngles;
            visualToAlign.localEulerAngles = new Vector3(e.x, e.y, 0f);
        }

        if (rb != null)
        {
            rb.isKinematic = false;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        if (col != null)
        {
            col.isTrigger = false;
        }
        if (_runAudioSource != null && _runAudioSource.isPlaying)
        {
            _runAudioSource.Stop();
        }
    }
}