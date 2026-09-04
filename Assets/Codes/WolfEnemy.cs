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
                rb.linearVelocity = new Vector3(0, rb.linearVelocity.y, rb.linearVelocity.z); // 原地煞車
                Debug.Log($"【狼追蹤】玩家跳得太高 (高度差：{(player.position.y - transform.position.y):F2} > {stopChaseHeightDifference})，狼停止追蹤！");
            }
        }
        else
        {
            // 只有當玩家觸地，或是高度沒有那麼高時，才執行正常的距離追逐判定
            if (distanceX <= aggroDistanceX && !isChasing)
            {
                isChasing = true; // 進入範圍，開始追！
            }
            else if (distanceX > giveUpDistanceX && isChasing)
            {
                isChasing = false; // 逃太遠了，放棄追蹤
                rb.linearVelocity = new Vector3(0, rb.linearVelocity.y, rb.linearVelocity.z); // 原地煞車
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
        // 沿著地面前進：追擊中若腳下是可行走的斜坡，速度沿斜坡表面投影貼地爬升；
        // 若不是斜坡（例如撞到台階邊緣被物理彈起），才清掉向上速度避免飛起來
        if (!keepOnGroundWhileChasing || rb == null || rb.isKinematic) return;
        if (!isChasing || isAttached || isStunned) return;

        Vector3 v = rb.linearVelocity;

        if (TryGetGroundSlope(out RaycastHit groundHit, out float slopeAngle) &&
            slopeAngle > 0.5f && slopeAngle < maxWalkableSlopeAngle)
        {
            Vector3 horizontal = new Vector3(v.x, 0f, 0f);
            Vector3 projected = Vector3.ProjectOnPlane(horizontal, groundHit.normal);
            if (projected.sqrMagnitude > 0.0001f)
            {
                projected = projected.normalized * Mathf.Abs(v.x);
                rb.linearVelocity = new Vector3(projected.x, projected.y, v.z);
            }
        }
        else if (v.y > 0f)
        {
            v.y = 0f;
            rb.linearVelocity = v;
        }
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

        if (Physics.Raycast(origin, Vector3.down, out RaycastHit downHit, rayLength, layerMask, QueryTriggerInteraction.Ignore))
        {
            if (downHit.collider == col || downHit.collider.transform.IsChildOf(transform)) return false;
            hit = downHit;
            slopeAngle = Vector3.Angle(Vector3.up, downHit.normal);
            return true;
        }
        return false;
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
            // 玩家背對著狼：根據距離決定是慢走還是快跑
            float distanceX = Mathf.Abs(dirToPlayerX);
            if (distanceX > runDistanceThreshold)
            {
                currentSpeed = slowChaseSpeed; // 慢慢走 (預設 3)
            }
            else
            {
                currentSpeed = fastChaseSpeed; // 奔跑 (預設 6)
            }
        }

        rb.linearVelocity = new Vector3(directionX * currentSpeed, rb.linearVelocity.y, rb.linearVelocity.z);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (isStunned || isAttached || Time.time < enableTime + spawnAttachImmunityTime) return;
        if (rb == null) rb = GetComponent<Rigidbody>();

        // 咬到玩家 (接觸)
        if (collision.gameObject.CompareTag("Player"))
        {
            // 碰到玩家瞬間，先把狼的速度清空，避免殘餘力量撞飛玩家
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero; 
                rb.angularVelocity = Vector3.zero;
            }

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
        }
        isAttached = false;
        isChasing = false;
        isStunned = false;

        transform.position = _initialPosition;
        transform.rotation = _initialRotation;

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