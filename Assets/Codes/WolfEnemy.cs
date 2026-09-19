using UnityEngine;
using System.Collections;
using System.Collections.Generic;

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
    public float aggroDistanceX = 6f;
    public float giveUpDistanceX = 12f;

    [Header("狼群個體速度微差與動態競速")]
    [Tooltip("狼群距離正常時的基礎速度微差 (預設 0.03，即 ±3%，範圍約 0.97 ~ 1.03)")]
    [SerializeField, Range(0f, 0.05f)]
    private float baseSpeedVariation = 0.03f;

    [Tooltip("狼群極度重疊時的最大速度微差 (預設 0.08，即最高 ±8%，範圍約 0.92 ~ 1.08)")]
    [SerializeField, Range(0.03f, 0.15f)]
    private float maxOverlapSpeedVariation = 0.08f;

    [Tooltip("去同步效果平滑過渡速度 (單位/秒，預設 2.0，約 0.5 秒平滑過渡，消除速度階躍突變)")]
    [SerializeField, Range(0.5f, 5f)]
    private float desyncTransitionSpeed = 2.0f;

    [Tooltip("本隻狼的個體速度傾向係數 (-1.0 ~ +1.0，初始化時決定一次且整場固定，負數偏慢、正數偏快)")]
    [SerializeField]
    private float individualSpeedBiasRatio = 0f;

    [Tooltip("本隻狼目前的有效速度倍率 (隨同伴距離平滑動態擴展，供 Inspector 觀察)")]
    [SerializeField]
    private float currentEffectiveSpeedMultiplier = 1f;
    public float IndividualSpeedMultiplier => currentEffectiveSpeedMultiplier;

    // 平滑後的重疊強度 (0 ~ 1)
    private float _smoothedOverlapIntensity = 0f;

    [Header("最低有效追擊速度保證")]
    [Tooltip("狼在正常追擊玩家時，速度必須高於玩家基礎速度的最低額外差額 (預設 0.4，確保玩家全力奔跑時狼仍可穩定縮短距離咬人)")]
    [SerializeField, Range(0.1f, 1.5f)]
    private float minimumChaseSpeedAbovePlayer = 0.4f;

    [Tooltip("當無法取得玩家組件時使用的預設玩家速度 (預設 5.0)")]
    [SerializeField]
    private float defaultPlayerSpeed = 5.0f;

    [Header("★0919 貼身速度跟著玩家實際速度走")]
    [Tooltip("開啟後，貼身追擊速度＝玩家「實際水平速度」× 下面的倍率（而不是固定的 nearChaseSpeed）。\n" +
             "實測：玩家推巨石上坡時實際只有 1.4～2.2 m/s，但狼的貼身速度是固定 6.8，等於瞬間就咬到；\n" +
             "改成跟著玩家走，狼永遠只快一點點，壓迫感在但有得跑。關掉＝完全回到原本的固定速度。")]
    public bool useAdaptivePlayerReference = true;

    [Tooltip("咬到距離內，狼是玩家速度的幾倍 (1.05 ＝ 快 5%)。這是「已經貼上去之後」的跟隨速度")]
    [Range(1.0f, 1.6f)]
    public float finalChaseSpeedRatio = 1.05f;

    [Tooltip("狼與玩家的中心距離小於這個值時視為已經貼上（碰撞體實際接觸約 2.3 公尺），進入上面的跟隨速度")]
    public float biteApproachDistance = 2.5f;

    [Tooltip("還沒貼上時，狼每秒要比玩家多前進幾公尺（固定差額，不是百分比）。\n" +
             "百分比在玩家很慢的時候會失效：玩家推巨石只有 2.5 m/s，×1.05 只快 0.12 m/s，" +
             "從 4 公尺接近到碰得到的 2.3 公尺要花 14 秒，實機上就是「一直逼近卻永遠咬不到」。")]
    [Range(0.2f, 3f)]
    public float minClosingSpeed = 0.8f;

    [Tooltip("玩家停下來或非常慢時，用來計算貼身速度的最低參考速度 (避免玩家站著不動時狼也停住)")]
    public float minPlayerReferenceSpeed = 2.5f;

    [Tooltip("玩家速度的平滑秒數 (避免玩家一頓一頓時狼的速度跟著抖)")]
    [Range(0.05f, 1f)]
    public float playerSpeedSmoothing = 0.25f;

    [Header("物理免疫設定")]
    [Tooltip("狼要忽略碰撞的物件 Collider 清單")]
    public List<Collider> collidersToIgnore = new List<Collider>();

    [Header("高度追蹤限制")]
    public float stopChaseHeightDifference = 3.0f;

    [Header("貼地與斜坡追擊")]
    [Tooltip("追擊時狼永遠沿著地面前進")]
    public bool keepOnGroundWhileChasing = true;
    [Tooltip("腳下地面偵測射線的額外長度")]
    public float groundCheckDistance = 0.4f;
    [Tooltip("視為可行走斜坡的最大角度，超過此角度視同牆壁")]
    public float maxWalkableSlopeAngle = 55f;

    [Header("狼群避讓與圖層分離")]
    [Tooltip("狼與狼之間不產生物理推擠")]
    public bool ignoreWolfToWolfCollision = true;

    [Tooltip("前方太靠近同伴時微幅放慢，避免狼群完全擠在同一點")]
    public bool useSoftSeparation = true;

    [Tooltip("開始注意前方同伴的距離")]
    [Range(0.5f, 6f)]
    public float separationRadius = 2.0f;

    [Tooltip("希望維持的最小間距")]
    [Range(0.2f, 4f)]
    public float minimumWolfDistance = 1.2f;

    [Tooltip("最大減速幅度（最多放慢 25%，絕不卡死）")]
    [Range(0f, 0.5f)]
    public float separationStrength = 0.25f;

    [Tooltip("依出生順序給每隻狼不同的 SpriteRenderer sortingOrder，在畫面上有前後圖層感")]
    public bool useDepthLayering = true;

    [Header("追擊節奏曲線（Catch-up AI，越遠越狂暴加速）")]
    public bool useCatchUpCurve = true;
    [Tooltip("【貼身距離】小於此距離時的速度")]
    public float nearDistance = 4f;
    [Tooltip("【貼身速度】必須高於玩家基礎速度(6.0)，才能真正追上並咬住主角")]
    public float nearChaseSpeed = 6.8f;
    [Tooltip("【中距離】到此距離使用 cruiseChaseSpeed")]
    public float cruiseDistance = 9f;
    [Tooltip("【中距離速度】穩定壓迫，明顯比主角快")]
    public float cruiseChaseSpeed = 9.0f;
    [Tooltip("【追趕距離】拉開到此距離以上就用滿 maxCatchUpSpeed")]
    public float maxCatchUpDistance = 18f;
    [Tooltip("【追趕速度上限】距離遠時的全力衝刺速度（玩家的兩倍，壓迫感拉滿）")]
    public float maxCatchUpSpeed = 12.5f;

    [Header("移動物理設定")]
    [Tooltip("起步加速度 (單位/秒²)")]
    public float acceleration = 40f;
    [Tooltip("煞車加速度 (單位/秒²)")]
    public float braking = 60f;
    [Tooltip("狼的體重")]
    public float bodyMass = 12f;
    [Tooltip("自動將 Rigidbody 設為 Interpolate 與 Continuous 避免抖動與穿模")]
    public bool autoFixRigidbodySettings = true;

    [Header("身體貼合斜坡角度與高度")]
    [Tooltip("狼在斜坡上時，身體是否跟著斜坡傾斜")]
    public bool alignVisualToSlope = true;
    [Tooltip("要傾斜與貼地的視覺物件 (留空自動抓子物件的 SpriteRenderer)")]
    public Transform visualToAlign;
    [Tooltip("身體轉向斜坡的平滑速度")]
    public float slopeAlignSpeed = 8f;
    [Tooltip("身體最多傾斜幾度")]
    public float maxVisualAlignAngle = 40f;
    [Tooltip("斜坡上視覺貼地的高度微調 (負值向下貼近地面，預設 -0.45 解決寬碰撞盒在斜坡浮空問題)")]
    public float slopeVisualYOffset = -0.45f;

    [Header("安全防護")]
    public float spawnAttachImmunityTime = 1.0f;
    private float enableTime = -999f;

    [Header("🎵 狼群音效")]
    public AudioClip aggroHowlSFX;
    public AudioClip runSFX;
    [Range(0f, 1f)] public float soundVolume = 0.85f;

    [Header("🔍 斜坡除錯 Log")]
    public bool debugSlopeLog = false;
    public float debugSlopeLogInterval = 0.5f;

    // 元件快取
    private Transform player;
    private PlayerMovement playerMovement;
    private Rigidbody rb;
    private Collider col;
    private AudioSource _runAudioSource;

    // 狀態鎖 (保留 private 命名供外部反射如 WolfSpriteAnimator 讀取)
    private bool isChasing = false;
    private bool isAttached = false;
    private bool isStunned = false;

    // 移動目標
    private float _targetSpeedX = 0f;
    private bool _hasTargetSpeed = false;
    private float _lastFacingX = 1f;
    private float _currentSpeed = 0f;

    // 斜坡與視覺
    private Vector3 _visualGroundNormal = Vector3.up;
    private bool _visualHasGround = false;
    private Vector3 _lastGoodNormal = Vector3.up;
    private float _lastGoodGroundTime = -999f;
    private bool _lastGroundedFacingWolf = false;

    // Aggro 鎖定
    private bool _aggroLocked = false;
    private Transform _targetPlayer = null;

    // 出生還原
    private Vector3 _initialPosition;
    private Quaternion _initialRotation;
    private Transform _initialParent;

    // 靜態狼群清單
    private static readonly List<WolfEnemy> _allWolves = new List<WolfEnemy>();
    private readonly RaycastHit[] _groundHitBuf = new RaycastHit[16];

    // Log 取樣
    private float _dbgNextLog = 0f;
    private Vector3 _dbgLastPos;
    private float _dbgLastTime = -1f;
    // ★0917 追擊速度決策的中間值（只給 debugSlopeLog 印，不參與計算）
    private float _dbgTerrainFactor = 1f;
    private float _dbgCatchUpSpeed;
    private float _dbgNearTarget;
    private float _dbgCloseSpeed;
    private float _dbgSepFactor = 1f;
    private float _dbgDistance;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        col = GetComponent<Collider>();
        _initialPosition = transform.position;
        _initialRotation = transform.rotation;
        _initialParent = transform.parent;

        // ★ 核心保證：剛體物理絕對鎖定在 Z = 0！
        // 地面 BoxCollider 厚度僅 0.2（Z 範圍 -0.1 到 +0.1），偏離 Z = 0 會掉出地面或踩在側面！
        transform.position = new Vector3(transform.position.x, transform.position.y, 0f);

        // 每隻狼在初始化/Spawn 時決定一次固定的個體速度傾向 (-1.0 ~ +1.0)，整場保持固定，不每幀重新隨機
        individualSpeedBiasRatio = Random.Range(-1f, 1f);
        currentEffectiveSpeedMultiplier = 1f + (individualSpeedBiasRatio * baseSpeedVariation);
    }

    private void OnEnable()
    {
        enableTime = Time.time;
        if (col == null) col = GetComponent<Collider>();
        if (rb == null) rb = GetComponent<Rigidbody>();

        if (!_allWolves.Contains(this)) _allWolves.Add(this);
        RefreshWolfPairIgnore();
        foreach (WolfEnemy w in _allWolves)
        {
            if (w != null && w != this) w.RefreshWolfPairIgnore();
        }

        ApplyDepthLayer();
    }

    private void OnDisable()
    {
        _allWolves.Remove(this);
    }

    /// <summary>
    /// 設定視覺前後順序。只調整 SpriteRenderer 的 sortingOrder，絕不動剛體物理的 Z 座標！
    /// </summary>
    private void ApplyDepthLayer()
    {
        // 剛體物理永遠保持 Z = 0
        transform.position = new Vector3(transform.position.x, transform.position.y, 0f);

        if (!useDepthLayering) return;
        int index = _allWolves.IndexOf(this);
        if (index < 0) index = 0;

        SpriteRenderer sr = GetComponentInChildren<SpriteRenderer>();
        if (sr != null)
        {
            sr.sortingOrder = 5 + index;
        }
    }

    public void StartChase()
    {
        if (!isChasing && aggroHowlSFX != null && AudioManager.Instance != null)
        {
            AudioManager.Instance.PlaySFXAt(aggroHowlSFX, transform.position, soundVolume);
        }
        isChasing = true;
        _aggroLocked = true;
        if (_targetPlayer == null) _targetPlayer = player;
    }

    void Start()
    {
        if (rb == null) rb = GetComponent<Rigidbody>();
        if (col == null) col = GetComponent<Collider>();

        if (autoFixRigidbodySettings && rb != null)
        {
            if (rb.interpolation != RigidbodyInterpolation.Interpolate)
                rb.interpolation = RigidbodyInterpolation.Interpolate;

            if (rb.collisionDetectionMode != CollisionDetectionMode.Continuous)
                rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

            if (bodyMass > 0f && !Mathf.Approximately(rb.mass, bodyMass))
                rb.mass = bodyMass;

            // 鎖定 Z 軸與所有旋轉
            rb.constraints = RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotation;
        }

        // 碰撞忽略清單
        if (col != null && collidersToIgnore != null)
        {
            foreach (Collider targetCol in collidersToIgnore)
            {
                if (targetCol != null)
                {
                    Physics.IgnoreCollision(col, targetCol, true);
                }
            }
        }

        GameObject pObj = GameObject.FindGameObjectWithTag("Player");
        if (pObj != null)
        {
            player = pObj.transform;
            playerMovement = pObj.GetComponent<PlayerMovement>();
        }

        // 自動校準追擊速度（若場景或 Prefab 留有舊數值，自動升級為具備強烈壓迫感的數值）
        if (nearChaseSpeed < 6.2f) nearChaseSpeed = 6.8f;
        if (cruiseChaseSpeed < 8.0f) cruiseChaseSpeed = 9.0f;
        if (maxCatchUpSpeed < 11.0f) maxCatchUpSpeed = 12.5f;
    }

    void Update()
    {
        if (isStunned || isAttached || player == null) return;

        float distanceX = Mathf.Abs(player.position.x - transform.position.x);

        if (!_aggroLocked)
        {
            if (distanceX <= aggroDistanceX)
            {
                _aggroLocked = true;
                _targetPlayer = player;
                StartChase();
            }
        }
        else
        {
            if (_targetPlayer != null) player = _targetPlayer;
            if (!isChasing) isChasing = true;
        }

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
                    _runAudioSource.spatialBlend = 1f;
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
        if (rb == null || rb.isKinematic) return;
        if (isAttached || isStunned) { _hasTargetSpeed = false; return; }

        // 確保剛體永遠位於 Z = 0
        if (Mathf.Abs(transform.position.z) > 0.001f)
        {
            transform.position = new Vector3(transform.position.x, transform.position.y, 0f);
        }

        float wanted = _hasTargetSpeed ? _targetSpeedX : 0f;
        if (Mathf.Abs(wanted) > 0.01f) _lastFacingX = Mathf.Sign(wanted);

        bool groundFound = TryGetGroundSlope(out RaycastHit groundHit, out float slopeAngle);
        bool onWalkableSlope = keepOnGroundWhileChasing && groundFound
                            && slopeAngle > 0.5f && slopeAngle < maxWalkableSlopeAngle;

        Vector3 v = rb.linearVelocity;
        float targetSpeed = Mathf.Abs(wanted);

        // 平滑加速與煞車
        float rate = (targetSpeed > _currentSpeed) ? acceleration : braking;
        if (rate <= 0f) rate = 40f;
        _currentSpeed = Mathf.MoveTowards(_currentSpeed, targetSpeed, rate * Time.fixedDeltaTime);
        float speed = _currentSpeed;

        if (onWalkableSlope)
        {
            // 計算狼底部與地面間距 (gap)
            float gap = col != null ? (col.bounds.min.y - groundHit.point.y) : 0f;

            // 若有些微浮空 (gap > 0.06m) 保留重力拉回，貼地時 (gap <= 0.06m) 關閉重力流暢滑行
            rb.useGravity = (gap > 0.06f);

            // 移動方向投影到地面法線上（法線已投影到 XY 平面，Z 永遠為 0）
            Vector3 moveDir = new Vector3(_lastFacingX, 0f, 0f);
            Vector3 slopeDir = Vector3.ProjectOnPlane(moveDir, groundHit.normal).normalized;

            if (targetSpeed < 0.01f)
            {
                rb.linearVelocity = Vector3.zero;
            }
            else
            {
                // ★★★ 斜坡攀爬速度補償：
                // 在斜坡上，水平推進力會被 cos(坡度) 瓜分（35度坡水平速度會少 18%）
                // 這裡補償回坡面切線總速度，確保在斜坡上的前進推進力跟平地一樣強悍！
                float cosSlope = Mathf.Max(0.65f, Mathf.Abs(slopeDir.x));
                float climbSpeed = speed / cosSlope;

                float vy = slopeDir.y * climbSpeed;
                // 若有些微浮空則施加柔和向下微調，防止狼在空中平行漂浮
                if (gap > 0.04f) vy -= Mathf.Min(gap * 5f, 2.5f);
                rb.linearVelocity = new Vector3(slopeDir.x * climbSpeed, vy, 0f);
            }

            _visualHasGround = true;
            _visualGroundNormal = groundHit.normal;
        }
        else
        {
            // 平地或懸空：開啟重力，水平依照目標推動，垂直完全交給重力與碰撞（絕不強制 y = 0）
            rb.useGravity = true;

            float vx = (targetSpeed < 0.01f) ? 0f : (_lastFacingX * speed);
            rb.linearVelocity = new Vector3(vx, v.y, 0f);

            _visualHasGround = groundFound;
            _visualGroundNormal = groundFound ? groundHit.normal : Vector3.up;
        }

        if (debugSlopeLog)
        {
            LogSlopeDiagnostics(groundFound, onWalkableSlope, slopeAngle, groundHit, speed);
        }
    }

    /// <summary>
    /// 乾淨的地面與斜坡多點射線偵測。
    /// 永遠在 Z = 0 射出射線，並將法線完全投影在 2D (XY) 平面，絕不受 3D BoxCollider 側面干擾。
    /// </summary>
    private bool TryGetGroundSlope(out RaycastHit bestHit, out float slopeAngle)
    {
        bestHit = default;
        slopeAngle = 0f;
        if (col == null) return false;

        Vector3 center = col.bounds.center;
        float extentsY = col.bounds.extents.y;
        float extentsX = col.bounds.extents.x * 0.7f;
        float rayLength = extentsY + groundCheckDistance + 0.2f;

        // 探測點：嚴格取狼本體下方（後腳、中央、前腳），不讓超前探測點提前懸空爬坡
        Vector3 c0 = new Vector3(center.x, center.y + 0.1f, 0f);
        Vector3[] checkPoints = new Vector3[]
        {
            c0,
            c0 + new Vector3(-extentsX, 0f, 0f),
            c0 + new Vector3(extentsX, 0f, 0f)
        };

        int layerMask = ~(LayerMask.GetMask("Ignore Raycast") | LayerMask.GetMask("Wolf"));
        float minDistance = float.MaxValue;
        bool found = false;
        Vector3 chosenNormal = Vector3.up;

        foreach (var origin in checkPoints)
        {
            int n = Physics.RaycastNonAlloc(origin, Vector3.down, _groundHitBuf, rayLength, layerMask, QueryTriggerInteraction.Ignore);
            for (int k = 0; k < n; k++)
            {
                RaycastHit h = _groundHitBuf[k];
                if (h.collider == null || !IsRealGround(h.collider)) continue;

                // ★★★ 關鍵修復：將法線嚴格投影到 XY 平面，消除任何 Z 軸分量雜訊！
                Vector3 flatN = new Vector3(h.normal.x, h.normal.y, 0f);
                if (flatN.sqrMagnitude < 0.001f) continue;
                flatN.Normalize();

                float angle = Vector3.Angle(Vector3.up, flatN);
                if (angle > 85f) continue; // 濾除垂直牆面

                // 優先選擇斜坡（讓狼在進入坡道前夕就提早順暢轉入斜坡向量）
                bool isSlope = (angle > 0.5f && angle < maxWalkableSlopeAngle);
                bool currentIsSlope = (slopeAngle > 0.5f && slopeAngle < maxWalkableSlopeAngle);

                if (!found || (isSlope && !currentIsSlope) || (isSlope == currentIsSlope && h.distance < minDistance))
                {
                    minDistance = h.distance;
                    bestHit = h;
                    bestHit.normal = flatN; // 覆寫為乾淨的 2D 法線
                    slopeAngle = angle;
                    chosenNormal = flatN;
                    found = true;
                }
            }
        }

        if (found)
        {
            bestHit.normal = chosenNormal;
            _lastGoodNormal = chosenNormal;
            _lastGoodGroundTime = Time.time;
            return true;
        }

        // 短暫漏打時沿用上一幀的有效法線
        if (Time.time - _lastGoodGroundTime <= 0.15f)
        {
            bestHit.normal = _lastGoodNormal;
            slopeAngle = Vector3.Angle(Vector3.up, _lastGoodNormal);
            return true;
        }

        return false;
    }

    private bool IsRealGround(Collider c)
    {
        if (c == col) return false;
        if (c.transform.IsChildOf(transform)) return false;
        if (c.GetComponentInParent<WolfEnemy>() != null) return false;
        if (c.GetComponentInParent<PlayerMovement>() != null) return false;
        return true;
    }

    private void ChasePlayer()
    {
        float dirToPlayerX = player.position.x - transform.position.x;
        float directionX = Mathf.Sign(dirToPlayerX);

        // 偵測玩家是否回頭看著狼 (123 木頭人)
        bool isPlayerFacingWolf = false;
        if (playerMovement != null)
        {
            float playerFacingX = playerMovement.FacingDirection.x;
            isPlayerFacingWolf = (directionX * playerFacingX < 0);
        }

        float currentSpeed = 0f;

        if (isPlayerFacingWolf)
        {
            currentSpeed = retreatSpeed;
            // 123 木頭人退後狀態下平滑淡出去同步
            _smoothedOverlapIntensity = Mathf.MoveTowards(_smoothedOverlapIntensity, 0f, desyncTransitionSpeed * Time.deltaTime);
        }
        else
        {
            // ★★★ 智能追擊 (Catch-up AI) 核心優化：
            // 使用真實 2D 平面距離 (XY 距離)，而不是純 X 軸距離！
            // 在斜坡上，主角跑得越高、XY 真實距離就越大，狼才能真正觸發遠距狂暴加速（越遠越狂暴）！
            Vector2 wolfPos = new Vector2(transform.position.x, transform.position.y);
            Vector2 playerPos = new Vector2(player.position.x, player.position.y);
            float realDistance = Vector2.Distance(wolfPos, playerPos);

            // ★0919 速度基準重新定義（見 Header 說明）：
            //   ・貼身：跟著玩家「實際水平速度」× finalChaseSpeedRatio
            //   ・遠距：維持原本的 Catch-up 絕對速度（乘地形係數），才追得回落後的距離
            //   ・Clamp 移到 Separation／個體差「之前」，讓那兩個系統仍然能微調最終速度（原本會被 Clamp 吃掉）
            float terrainFactor = GetPlayerTerrainSpeedFactor();
            float pBaseSpeed = (playerMovement != null) ? playerMovement.BaseSpeed : defaultPlayerSpeed;
            float playerRef = useAdaptivePlayerReference ? GetPlayerReferenceSpeed(pBaseSpeed) : pBaseSpeed * terrainFactor;

            // ★0919 分成兩段：
            //   ・shadowSpeed：已經貼到咬得到的距離 → 玩家速度 × finalChaseSpeedRatio（跟著她跑，不硬擠）
            //   ・closeSpeed ：還沒貼上 → 玩家速度 ＋ minClosingSpeed（固定差額，保證真的會縮短距離）
            //   只用百分比會在玩家慢的時候失效，這就是「一直逼近卻咬不到」的成因。
            float shadowSpeed = useAdaptivePlayerReference
                ? playerRef * finalChaseSpeedRatio
                : (pBaseSpeed + minimumChaseSpeedAbovePlayer) * terrainFactor;
            float closeSpeed = useAdaptivePlayerReference
                ? Mathf.Max(shadowSpeed, playerRef + minClosingSpeed)
                : shadowSpeed;
            float ceiling = maxCatchUpSpeed * terrainFactor;

            // 1. Base Chase Speed (Catch-up 曲線或基礎跑速)
            currentSpeed = useCatchUpCurve ? EvaluateChaseSpeed(realDistance, shadowSpeed, closeSpeed, terrainFactor)
                                           : (realDistance > runDistanceThreshold ? slowChaseSpeed : fastChaseSpeed) * terrainFactor;
            currentSpeed = Mathf.Clamp(currentSpeed, shadowSpeed, Mathf.Max(shadowSpeed, ceiling));
            float nearTarget = shadowSpeed;   // 診斷用

            // 2. Separation (柔和同伴避讓與重疊強度計算)
            float sepFactor = ComputeSeparationFactor(directionX, Mathf.Abs(currentSpeed), realDistance, out float targetOverlap);
            currentSpeed *= sepFactor;

            // 3. Individual Speed Variation (距離越近速度差越大，由 ±3% 平滑放大至最高 ±8%)
            _smoothedOverlapIntensity = Mathf.MoveTowards(_smoothedOverlapIntensity, targetOverlap, desyncTransitionSpeed * Time.deltaTime);
            float currentVariation = Mathf.Lerp(baseSpeedVariation, maxOverlapSpeedVariation, _smoothedOverlapIntensity);
            currentEffectiveSpeedMultiplier = 1f + (individualSpeedBiasRatio * currentVariation);
            currentSpeed *= currentEffectiveSpeedMultiplier;

            // 4. 安全上下限：
            //    上限＝設計的追趕上限；下限＝玩家當下的速度（Separation／個體差可以讓狼慢下來錯開，
            //    但不能慢到比玩家還慢而被甩開，否則又會變成永遠追不到）
            currentSpeed = Mathf.Clamp(currentSpeed,
                                       Mathf.Min(playerRef, shadowSpeed),
                                       Mathf.Max(shadowSpeed, ceiling));

            _dbgTerrainFactor = terrainFactor;
            _dbgNearTarget = nearTarget;
            _dbgCloseSpeed = closeSpeed;
            _dbgCatchUpSpeed = useCatchUpCurve ? EvaluateChaseSpeed(realDistance, shadowSpeed, closeSpeed, terrainFactor) : 0f;
            _dbgSepFactor = sepFactor;
            _dbgDistance = realDistance;
        }

        _targetSpeedX = directionX * currentSpeed;
        _hasTargetSpeed = true;
    }

    /// <summary>
    /// 玩家腳下坡度對她水平速度的影響（1＝平地或離地；35° 坡＝0.819）。
    /// 坡度範圍跟 PlayerMovement 判定「在斜坡上走」的條件一致（0.5°～60°，超過視為牆／平地模式）。
    /// </summary>
    private float GetPlayerTerrainSpeedFactor()
    {
        if (playerMovement == null || !playerMovement.isGrounded) return 1f;
        float a = playerMovement.GroundSlopeAngle;
        if (a <= 0.5f || a >= 60f) return 1f;
        return Mathf.Cos(a * Mathf.Deg2Rad);
    }

    /// <summary>
    /// 距離 → 速度曲線。貼身那一端改吃 nearSpeed（跟著玩家實際速度算出來的），
    /// 中距離與遠距離維持原本的絕對速度，只乘上地形係數，才追得回被拉開的距離。
    /// </summary>
    private float EvaluateChaseSpeed(float distance, float shadowSpeed, float closeSpeed, float terrainFactor)
    {
        float bite = Mathf.Max(0.05f, biteApproachDistance);
        float near = Mathf.Max(bite + 0.1f, nearDistance);
        float cruise = Mathf.Max(near + 0.1f, cruiseDistance);
        float far = Mathf.Max(cruise + 0.1f, maxCatchUpDistance);

        closeSpeed = Mathf.Max(shadowSpeed, closeSpeed);
        float cruiseSpeed = Mathf.Max(closeSpeed, cruiseChaseSpeed * terrainFactor);
        float maxSpeed = Mathf.Max(cruiseSpeed, maxCatchUpSpeed * terrainFactor);

        float speed;
        if (distance <= bite)
        {
            // 已經貼上：跟著玩家跑，不硬擠
            speed = shadowSpeed;
        }
        else if (distance <= near)
        {
            // 最後一段：從固定差額的接近速度平滑收斂到跟隨速度
            speed = Mathf.Lerp(shadowSpeed, closeSpeed, (distance - bite) / (near - bite));
        }
        else if (distance <= cruise)
        {
            speed = Mathf.Lerp(closeSpeed, cruiseSpeed, (distance - near) / (cruise - near));
        }
        else if (distance <= far)
        {
            speed = Mathf.Lerp(cruiseSpeed, maxSpeed, (distance - cruise) / (far - cruise));
        }
        else
        {
            speed = maxSpeed;
        }

        return Mathf.Min(speed, maxSpeed);
    }

    // 玩家水平速度的平滑值：整群狼共用，每幀只算一次
    private static float _sharedPlayerSpeed;
    private static int _sharedPlayerSpeedFrame = -1;

    /// <summary>
    /// 貼身速度的參考值＝玩家「實際」水平速度（平滑過），夾在 minPlayerReferenceSpeed 與玩家基礎速度之間。
    /// 玩家停下或推著巨石慢慢走時，狼不會跟著停死；玩家全速跑時，狼也不會超出設計上限。
    /// </summary>
    private float GetPlayerReferenceSpeed(float pBaseSpeed)
    {
        if (playerMovement == null) return pBaseSpeed;

        if (_sharedPlayerSpeedFrame != Time.frameCount)
        {
            _sharedPlayerSpeedFrame = Time.frameCount;
            Rigidbody prb = playerMovement.GetComponent<Rigidbody>();
            float raw = prb != null ? Mathf.Abs(prb.linearVelocity.x) : pBaseSpeed;
            raw = Mathf.Min(raw, pBaseSpeed);
            float t = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.01f, playerSpeedSmoothing));
            _sharedPlayerSpeed = Mathf.Lerp(_sharedPlayerSpeed, raw, t);
        }

        return Mathf.Clamp(_sharedPlayerSpeed, minPlayerReferenceSpeed, pBaseSpeed);
    }

    private float ComputeSeparationFactor(float directionX, float ownSpeedAbs, float distToPlayer, out float overlapIntensity)
    {
        overlapIntensity = 0f;
        if (!useSoftSeparation || Mathf.Abs(directionX) < 0.01f || ownSpeedAbs < 0.01f) return 1f;

        float factor = 1f;
        float minOtherDist = float.MaxValue;

        for (int i = 0; i < _allWolves.Count; i++)
        {
            WolfEnemy other = _allWolves[i];
            if (other == null || other == this) continue;
            if (other.isAttached || other.isStunned) continue;

            float dx = other.transform.position.x - transform.position.x;
            float dy = other.transform.position.y - transform.position.y;
            float dist = new Vector2(dx, dy).magnitude;

            // 統計與同伴的最近距離（用於去同步重疊判定）
            if (dist < minOtherDist) minOtherDist = dist;

            // 當主角拉開距離 (遠距追擊) 時，群體衝鋒不減速，全力追趕主角
            if (distToPlayer > nearDistance) continue;

            if (dx * directionX <= 0f) continue;
            if (dist > separationRadius) continue;

            // 僅在貼身準備咬人時微幅拉開間距，絕不卡死
            float t = Mathf.InverseLerp(minimumWolfDistance, separationRadius, dist);
            float minF = Mathf.Clamp(1f - separationStrength, 0.85f, 1f);
            float f = Mathf.Lerp(minF, 1f, t);
            if (f < factor) factor = f;
        }

        // 當同伴距離小於 separationRadius 時計算重疊強度 (0 ~ 1)，越近強度越高
        if (minOtherDist < separationRadius)
        {
            overlapIntensity = Mathf.Clamp01(1f - (minOtherDist / separationRadius));
        }

        return factor;
    }

    private void RefreshWolfPairIgnore()
    {
        if (!ignoreWolfToWolfCollision || col == null) return;

        int wolfLayerBit = 1 << gameObject.layer;
        col.excludeLayers |= wolfLayerBit;

        foreach (Collider c in GetComponentsInChildren<Collider>(true))
        {
            if (c != null) c.excludeLayers |= wolfLayerBit;
        }

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
        float targetYOffset = 0f;

        if (!isAttached && !isStunned && _visualHasGround)
        {
            targetAngle = Mathf.Atan2(-_visualGroundNormal.x, _visualGroundNormal.y) * Mathf.Rad2Deg;
            targetAngle = Mathf.Clamp(targetAngle, -maxVisualAlignAngle, maxVisualAlignAngle);

            // 依斜坡角度線性補償高度（35度斜坡時達到最大補償量 slopeVisualYOffset，讓狼爪穩穩踩在斜坡上）
            float slopeRatio = Mathf.Clamp01(Mathf.Abs(targetAngle) / 35f);
            targetYOffset = slopeVisualYOffset * slopeRatio;
        }

        Vector3 e = visualToAlign.localEulerAngles;
        float currentAngle = e.z > 180f ? e.z - 360f : e.z;
        float t = 1f - Mathf.Exp(-Mathf.Max(0.01f, slopeAlignSpeed) * Time.deltaTime);

        float nextAngle = Mathf.LerpAngle(currentAngle, targetAngle, t);
        visualToAlign.localEulerAngles = new Vector3(e.x, e.y, nextAngle);

        Vector3 p = visualToAlign.localPosition;
        float nextY = Mathf.Lerp(p.y, targetYOffset, t);
        visualToAlign.localPosition = new Vector3(p.x, nextY, p.z);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (isStunned || isAttached || Time.time < enableTime + spawnAttachImmunityTime) return;

        if (collision.gameObject.CompareTag("Player"))
        {
            if (rb != null)
            {
                Vector3 hv = rb.linearVelocity;
                rb.linearVelocity = new Vector3(0f, Mathf.Min(hv.y, 0f), 0f);
                rb.angularVelocity = Vector3.zero;
            }
            _targetSpeedX = 0f;
            _hasTargetSpeed = false;

            if (ScreenFeedbackManager.Instance != null)
            {
                ScreenFeedbackManager.Instance.TriggerHitFeedback();
            }

            AttachToPlayer();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("StopAttackObject"))
        {
            DetachAndStun();
        }
    }

    private void AttachToPlayer()
    {
        isAttached = true;
        isChasing = false;

        LightMoteCollector.NotifyWolfAttached();

        if (_runAudioSource != null && _runAudioSource.isPlaying)
        {
            _runAudioSource.Stop();
        }

        rb.linearVelocity = Vector3.zero;
        rb.isKinematic = true;
        col.isTrigger = true;

        transform.SetParent(player);

        if (playerMovement != null)
        {
            playerMovement.AddWolf();
        }
    }

    private void DetachAndStun()
    {
        if (!isAttached && !isChasing) return;

        isAttached = false;
        isStunned = true;

        transform.SetParent(null);
        transform.position = new Vector3(transform.position.x, transform.position.y, 0f);

        if (visualToAlign != null)
        {
            visualToAlign.localPosition = Vector3.zero;
        }

        rb.isKinematic = false;
        col.isTrigger = false;

        if (playerMovement != null)
        {
            playerMovement.RemoveWolf();
        }

        float pushDirection = Mathf.Sign(transform.position.x - player.position.x);
        rb.linearVelocity = new Vector3(pushDirection * 3f, 5f, 0);

        StartCoroutine(StunCooldown(3f));
    }

    IEnumerator StunCooldown(float time)
    {
        yield return new WaitForSeconds(time);
        isStunned = false;
        isChasing = false;
    }

    public void ResetToInitialState()
    {
        StopAllCoroutines();
        if (isAttached)
        {
            transform.SetParent(_initialParent);
            if (playerMovement != null) playerMovement.RemoveWolf();
        }
        isAttached = false;
        isChasing = false;
        isStunned = false;

        _aggroLocked = false;
        _targetPlayer = null;
        _hasTargetSpeed = false;
        _targetSpeedX = 0f;
        _currentSpeed = 0f;
        _smoothedOverlapIntensity = 0f;
        currentEffectiveSpeedMultiplier = 1f + (individualSpeedBiasRatio * baseSpeedVariation);
        _lastGroundedFacingWolf = false;

        transform.position = new Vector3(_initialPosition.x, _initialPosition.y, 0f);
        transform.rotation = _initialRotation;
        ApplyDepthLayer();

        if (visualToAlign != null)
        {
            Vector3 e = visualToAlign.localEulerAngles;
            visualToAlign.localEulerAngles = new Vector3(e.x, e.y, 0f);
            visualToAlign.localPosition = Vector3.zero;
        }

        if (rb != null)
        {
            rb.isKinematic = false;
            rb.useGravity = true;
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

    private void LogSlopeDiagnostics(bool groundFound, bool onSlope, float slopeAngle, RaycastHit hit, float speed)
    {
        if (Time.time < _dbgNextLog) return;
        _dbgNextLog = Time.time + debugSlopeLogInterval;

        float measuredSpeed = -1f;
        if (_dbgLastTime > 0f)
        {
            float dt = Time.time - _dbgLastTime;
            if (dt > 0.0001f) measuredSpeed = Vector3.Distance(transform.position, _dbgLastPos) / dt;
        }
        _dbgLastPos = transform.position;
        _dbgLastTime = Time.time;

        Vector3 v = rb.linearVelocity;
        Debug.Log($"🐺【狼運行】{gameObject.name} | 實測速度: {measuredSpeed:F2} | 坡度: {slopeAngle:F0}° (沿坡={onSlope}) | 速度: x={v.x:F2} y={v.y:F2} | 法線: {hit.normal}");

        // ★0917 追擊速度決策：一行對照玩家實際水平速度與狼的目標／實際水平速度
        Rigidbody prb = playerMovement != null ? playerMovement.GetComponent<Rigidbody>() : null;
        float playerVx = prb != null ? prb.linearVelocity.x : 0f;
        float playerSlope = (playerMovement != null && playerMovement.isGrounded) ? playerMovement.GroundSlopeAngle : 0f;
        Debug.Log($"🐺【狼追擊決策】{gameObject.name} | 玩家水平速度 {Mathf.Abs(playerVx):F2}（腳下坡度 {playerSlope:F0}°） | " +
                  $"狼目標水平 {Mathf.Abs(_targetSpeedX):F2} | 狼實際水平 {Mathf.Abs(v.x):F2}（自己坡度 {slopeAngle:F0}°） | " +
                  $"距離 {_dbgDistance:F1} | 貼上跟隨 {_dbgNearTarget:F2} | 接近速度 {_dbgCloseSpeed:F2} | Catch-up {_dbgCatchUpSpeed:F2} | 分離係數 {_dbgSepFactor:F2} | 個體倍率 {currentEffectiveSpeedMultiplier:F3} | 地形係數 {_dbgTerrainFactor:F3} | 123 退後 {( _targetSpeedX * Mathf.Sign(player != null ? player.position.x - transform.position.x : 1f) < 0f ? "是" : "否")}");
    }
}