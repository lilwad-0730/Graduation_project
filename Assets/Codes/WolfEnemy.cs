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

    [Header("斜坡方向平滑（0910 A：解決平地↔斜坡的頓挫感）")]
    [Tooltip("移動方向每秒最多能轉幾度。★注意這只平滑「移動方向」，地面偵測本身還是即時的。\n" +
             "太小 → 進坡出坡會有延遲感、方向追不上地形\n" +
             "太大 → 等於沒平滑，石頭碎面的法線抖動會直接傳到速度上\n" +
             "360 大約 0.11 秒轉完一個 40 度的坡，抖動濾得掉、轉場也跟得上")]
    [Range(60f, 1440f)]
    public float slopeDirectionSmoothSpeed = 360f;

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
    private Vector3 _lastGoodNormal = Vector3.up;    // 最後一次有效的地面法線（射空時暫時沿用）

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
    }

    public void StartChase()
    {
        if (!isChasing && aggroHowlSFX != null && AudioManager.Instance != null)
        {
            AudioManager.Instance.PlaySFXAt(aggroHowlSFX, transform.position, soundVolume);
        }
        isChasing = true;
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

            if (rb.collisionDetectionMode != CollisionDetectionMode.ContinuousSpeculative)
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            if (bodyMass > 0f && !Mathf.Approximately(rb.mass, bodyMass))
                rb.mass = bodyMass;
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
    }

    void Update()
    {
        // 如果正在硬直、或已經咬住了、或找不到玩家，就不執行追蹤邏輯
        if (isStunned || isAttached || player == null) return;

        // 計算與玩家在 X 軸的絕對距離
        float distanceX = Mathf.Abs(player.position.x - transform.position.x);

        // 【新增】：高度限制偵測
        // 如果玩家高度大於狼，且玩家不在地面上（正在跳躍/墜落中），且高度差大於閾值，則狼會跟丟主角
        bool isPlayerTooHigh = playerMovement != null && 
                               !playerMovement.isGrounded && 
                               (player.position.y - transform.position.y) > stopChaseHeightDifference;

        if (isPlayerTooHigh)
        {
            if (isChasing)
            {
                isChasing = false; // 停止追蹤
                _targetSpeedX = 0f; _hasTargetSpeed = false;   // 交給 FixedUpdate 用 braking 減速，不在 Update 硬設速度
                Debug.Log($"【狼追蹤】玩家跳得太高 (高度差：{(player.position.y - transform.position.y):F2} > {stopChaseHeightDifference})，狼停止追蹤！");
            }
        }
        else
        {
            // 只有當玩家觸地，或是高度沒有那麼高時，才執行正常的距離追逐判定
            if (distanceX <= aggroDistanceX && !isChasing)
            {
                // 統一走 StartChase()：靠距離自動進入追擊時也要發出狼嚎
                // (原本這裡直接設 isChasing = true，導致只有 WolfSpawner 生成的狼才會嚎叫)
                StartChase();
            }
            else if (distanceX > giveUpDistanceX && isChasing)
            {
                isChasing = false; // 逃太遠了，放棄追蹤
                _targetSpeedX = 0f; _hasTargetSpeed = false;   // 交給 FixedUpdate 用 braking 減速，不在 Update 硬設速度
            }
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
        float wanted = _hasTargetSpeed ? _targetSpeedX : 0f;

        _hasTargetSpeed = false;   // 這一步用掉了，等下一個 Update 再給新的
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
        if (_smoothMoveDir.sqrMagnitude < 0.0001f) _smoothMoveDir = targetMoveDir;   // 第一幀直接對齊，不要從 (0,0,0) 轉
        if (Mathf.Sign(_smoothMoveDir.x) != Mathf.Sign(targetMoveDir.x) && Mathf.Abs(targetMoveDir.x) > 0.01f)
            _smoothMoveDir = targetMoveDir;   // 左右轉向是「換方向」不是「換坡度」，不要慢慢繞過去

        float maxRad = Mathf.Max(1f, slopeDirectionSmoothSpeed) * Mathf.Deg2Rad * Time.fixedDeltaTime;
        _smoothMoveDir = Vector3.RotateTowards(_smoothMoveDir, targetMoveDir, maxRad, 0f).normalized;

        Vector3 moveDir = _smoothMoveDir;

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

        // ★防彈飛鉗制：平滑方向會落後真實坡面，上坡跑到坡頂／坡度變緩的瞬間，
        //   舊的（比較陡的）方向還帶著向上分量，狼會被自己的速度甩上天。
        //   所以向上分量永遠不准超過「真實坡面此刻允許的量」。
        //   反過來（地面變陡）不鉗制——那個方向是往下壓，不會飛起來。
        float maxUpY = targetMoveDir.y * next;
        if (nv.y > maxUpY) nv.y = maxUpY;

        if (onWalkableSlope)
        {
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

        if (!isAttached && !isStunned &&
            TryGetGroundSlope(out RaycastHit groundHit, out float slopeAngle) &&
            slopeAngle < maxWalkableSlopeAngle)
        {
            // 地面法線換算成 Z 軸傾角：平地法線是 (0,1,0) → 0 度；坡往右上升 → 正角度
            targetAngle = Mathf.Atan2(-groundHit.normal.x, groundHit.normal.y) * Mathf.Rad2Deg;
            targetAngle = Mathf.Clamp(targetAngle, -maxVisualAlignAngle, maxVisualAlignAngle);
        }
        // 離地或被咬住/硬直時 targetAngle 維持 0，身體平滑轉回直立

        Vector3 e = visualToAlign.localEulerAngles;
        float current = e.z > 180f ? e.z - 360f : e.z;
        float next = Mathf.LerpAngle(current, targetAngle, Time.deltaTime * slopeAlignSpeed);
        visualToAlign.localEulerAngles = new Vector3(e.x, e.y, next);
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

        // ★0910：原本只有一條從碰撞體中心往下的細射線。
        //   廢墟的地面是石頭的 MeshCollider，表面凹凸不平，一條細射線很容易射空、
        //   或剛好打在某個角度很怪的三角面上（讀出來的坡度超過 maxWalkableSlopeAngle）。
        //   而射空的那一幀，上面的 FixedUpdate 會走平地分支——舊版還會順手把向上速度清成 0，
        //   等於每射空一次就把爬坡速度殺掉一次。這是「上坡慢、下坡正常」的直接來源。
        //
        //   改用球形掃描（SphereCast）：用碰撞體本身的寬度去掃，會自動跨過小凹凸，
        //   讀到的是整體坡面而不是單一三角面，穩定非常多。掃不到才退回細射線。
        float radius = Mathf.Max(0.05f, Mathf.Min(col.bounds.extents.x, col.bounds.extents.z));
        Vector3 sphereStart = origin + Vector3.up * 0.05f;

        if (Physics.SphereCast(sphereStart, radius, Vector3.down, out RaycastHit sphereHit,
                               rayLength, layerMask, QueryTriggerInteraction.Ignore))
        {
            if (!(sphereHit.collider == col || sphereHit.collider.transform.IsChildOf(transform)))
            {
                hit = sphereHit;
                slopeAngle = Vector3.Angle(Vector3.up, sphereHit.normal);
                _lastGoodGroundTime = Time.time;
                return true;
            }
        }

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

    private void ChasePlayer()
    {
        // 算出狼到玩家的 X 軸方向與正負號值 (1 或 -1)
        float dirToPlayerX = player.position.x - transform.position.x;
        float directionX = Mathf.Sign(dirToPlayerX);

        // 偵測玩家是否回頭看著狼 (玩家朝向與狼追擊方向相反)
        bool isPlayerFacingWolf = false;
        if (playerMovement != null)
        {
            float playerFacingX = playerMovement.FacingDirection.x;
            // 如果玩家面朝方向與狼追擊方向相反，代表玩家正在看著狼
            isPlayerFacingWolf = (directionX * playerFacingX < 0);
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

        transform.position = _initialPosition;
        transform.rotation = _initialRotation;

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