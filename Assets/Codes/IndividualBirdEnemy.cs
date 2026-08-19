using UnityEngine;
using System.Collections;

public enum BirdBehavior { DirectPlayer, PlayerOffset, HomingPlayer }

/// <summary>
/// 個別鳥類敵人控制器：
/// 1. 自動偵測玩家距離 (detectionRange)：玩家靠近自動發出警報並俯衝！
/// 2. 相容 living birds 的動畫控制器 (flying, worried, landing, die)。
/// 3. 發出叫聲警報 ➔ 高速俯衝 ➔ 撞擊護盾彈飛 / 撞擊玩家石化 / 撞擊地面卡住消退。
/// </summary>
public class IndividualBirdEnemy : MonoBehaviour, IResettable
{
    [Header("自動偵測玩家攻擊")]
    [Tooltip("是否在玩家進入範圍時自動引爆俯衝攻擊？(預設開啟)")]
    public bool autoDetectPlayer = true;

    [Tooltip("自動偵測玩家的攻擊距離 (米，預設 12)")]
    public float detectionRange = 12f;

    [Header("鳥類敵人類型與移動")]
    [Tooltip("此隻鳥的俯衝行為類型")]
    public BirdBehavior behaviorType = BirdBehavior.DirectPlayer;

    [Tooltip("俯衝攻擊的速度 (預設 12)")]
    public float diveSpeed = 12f;

    [Tooltip("【偏移模式限定】X 軸的偏移量 (預設 5)")]
    public float targetOffset = 5f;

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

    [Header("地面與環境偵測")]
    [Tooltip("地面的 Tag (預設 Floor，自動支援 Floor, Ground, Terrain)")]
    public string groundTag = "Floor";

    [Header("音效設定")]
    [Tooltip("俯衝前發出的叫聲音效 (例如 鳥鳴.mp3)")]
    public AudioClip warningClip;
    [Tooltip("高速俯衝飛行的振翅音效 (例如 鳥振翅1.mp3, 鳥振翅4.mp3)")]
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

        // 預設播放待機飛行動畫 (flying)
        PlayAnim(idleAnimName);
    }

    private void EnsureComponents()
    {
        if (animator == null)
        {
            animator = GetComponent<Animator>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
        }

        if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (meshRenderer == null) meshRenderer = GetComponentInChildren<Renderer>();

        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
        }

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
        // 核心修復 1：自動偵測玩家距離並觸發俯衝
        if (autoDetectPlayer && currentState == BirdState.Idle)
        {
            if (playerTrans == null) EnsureComponents();

            if (playerTrans != null)
            {
                // 計算 2.5D 水平距離與 3D 距離 (防止天空高處的鳥因為 Y 軸落差過大而無法觸發)
                float xDist = Mathf.Abs(transform.position.x - playerTrans.position.x);
                float totalDist = Vector3.Distance(transform.position, playerTrans.position);

                if (xDist <= detectionRange || totalDist <= detectionRange)
                {
                    Debug.LogWarning($"【鳥群系統】玩家進入偵測範圍 (水平距離 {xDist:F1}m <= {detectionRange}m)！{gameObject.name} 正式發起俯衝攻擊！");
                    StartAttackSequence();
                }
            }
        }

        // 核心修復 2：待機與警報狀態下，自動平滑面向玩家 (左或右)
        if (currentState == BirdState.Idle || currentState == BirdState.Warning)
        {
            if (playerTrans != null)
            {
                float dx = playerTrans.position.x - transform.position.x;
                Vector3 lookDir = dx < 0 ? Vector3.left : Vector3.right;
                Quaternion targetRot = Quaternion.LookRotation(lookDir, Vector3.up) * Quaternion.Euler(modelRotationOffset);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * 5f);
            }
        }
    }

    // 在 Scene 視窗繪製可視化感應範圍圈
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, detectionRange);
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
            else if (anim.HasState(0, Animator.StringToHash("flying")))
            {
                anim.CrossFade("flying", 0.1f);
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
    /// 全域靜態方法，讓外部一鍵命令場景中所有鳥類同時發出警報並攻擊！
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
        if (audioSource != null && warningClip != null)
        {
            audioSource.PlayOneShot(warningClip);
        }

        // 3. 警報期等待
        yield return new WaitForSeconds(warningDuration);

        // 4. 程式切換為俯衝飛行動畫 (flying) 並播放振翅音效
        PlayAnim(diveAnimName);
        if (audioSource != null && flapClip != null)
        {
            audioSource.PlayOneShot(flapClip);
        }
        currentState = BirdState.Diving;
        rb.isKinematic = false;

        if (playerTrans != null)
        {
            UpdateTargetPosition();
        }
        else
        {
            targetPosition = transform.position + Vector3.down * 15f;
        }

        // 5. 持續朝目標飛行，直到碰撞發生
        while (currentState == BirdState.Diving)
        {
            if (behaviorType == BirdBehavior.HomingPlayer && playerTrans != null)
            {
                targetPosition = new Vector3(playerTrans.position.x, playerTrans.position.y - 1.8f, playerTrans.position.z);
            }

            // 強制切除 Z 軸，只在 2D (X/Y) 平面上衝刺，防止俯衝時漂移到背景牆裡面
            targetPosition.z = originalPosition.z;
            Vector3 currentPos = transform.position;
            currentPos.z = originalPosition.z;

            diveDirection = (targetPosition - currentPos).normalized;
            diveDirection.z = 0f; // 確保方向完全沒有 Z 軸向量

            rb.linearVelocity = diveDirection * diveSpeed;

            // 2D 飛行朝向：使 3D 鳥嘴/頭部 (+Z) 完全對準飛行向量 (diveDirection)，背部保持朝上 (+Y)
            if (diveDirection != Vector3.zero)
            {
                Quaternion targetRot = Quaternion.LookRotation(diveDirection, Vector3.up) * Quaternion.Euler(modelRotationOffset);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * 12f);
            }

            yield return null;
        }
    }

    /// <summary>
    /// 自動判斷物件是否為地表/地形 (支援 Tag = Floor / Ground / Terrain 等)
    /// </summary>
    private bool IsGroundObject(GameObject obj)
    {
        if (obj == null) return false;

        // 核心排除：石柱/掩體/岩石/區域觸發器絕對不是地表，不能讓鳥在柱子上方插地！
        string lowerName = obj.name.ToLower();
        if (lowerName.Contains("pillar") || lowerName.Contains("shelter") || lowerName.Contains("rock") || lowerName.Contains("zone") || lowerName.Contains("trigger")) return false;

        // 1. 安全 Tag 比對 (使用 obj.tag == "..." 安全比對，防止未定義 Tag 引發 Unity 拋出例外崩潰)
        string t = obj.tag;
        if (t == "Floor" || t == "Ground" || t == "Terrain") return true;
        if (!string.IsNullOrEmpty(groundTag) && t == groundTag) return true;

        // 2. 物件名稱比對 (純地表地板)
        if (lowerName.Contains("floor") || lowerName.Contains("ground_texture") || lowerName.Contains("tile") || lowerName.Contains("ground")) return true;

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
        if (playerTrans == null) return;

        Vector3 playerPos = playerTrans.position;
        Vector3 groundTargetPos = new Vector3(playerPos.x, playerPos.y - 1.8f, playerPos.z);

        switch (behaviorType)
        {
            case BirdBehavior.DirectPlayer:
                targetPosition = groundTargetPos;
                break;

            case BirdBehavior.PlayerOffset:
                float xOffset = Random.value > 0.5f ? targetOffset : -targetOffset;
                targetPosition = new Vector3(playerPos.x + xOffset, groundTargetPos.y, playerPos.z);
                break;

            case BirdBehavior.HomingPlayer:
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

        Debug.Log($"【鳥群系統】{gameObject.name} 撞擊地表！立刻停格鎖定姿態插在地面，{stuckDuration} 秒後漸漸消失。");
        StartCoroutine(FadeAndDestroyCoroutine());
    }

    private IEnumerator FadeAndDestroyCoroutine()
    {
        yield return new WaitForSeconds(stuckDuration);

        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float alpha = 1.0f - (elapsed / fadeDuration);
            SetAlpha(alpha);
            yield return null;
        }

        Destroy(gameObject);
    }

    private IEnumerator FadeAndDestroyCoroutineAfterBounce()
    {
        yield return new WaitForSeconds(1.2f);

        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float alpha = 1.0f - (elapsed / fadeDuration);
            SetAlpha(alpha);
            yield return null;
        }

        Destroy(gameObject);
    }

    /// <summary>
    /// 遞迴降低所有 Renderer (MeshRenderer / SpriteRenderer) 的透明度
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
            else if (r.material != null)
            {
                if (r.material.HasProperty("_Color"))
                {
                    Color c = r.material.color;
                    c.a = alpha;
                    r.material.color = c;
                }
                if (r.material.HasProperty("_BaseColor"))
                {
                    Color c = r.material.GetColor("_BaseColor");
                    c.a = alpha;
                    r.material.SetColor("_BaseColor", c);
                }
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        HandleCollision(other.gameObject);
    }

    private void OnCollisionEnter(Collision collision)
    {
        HandleCollision(collision.gameObject);
    }

    private void HandleCollision(GameObject hitObj)
    {
        if (currentState == BirdState.Bounced || currentState == BirdState.Stuck) return;

        // 1. 碰撞到玩家護盾
        PlayerShield shield = hitObj.GetComponentInParent<PlayerShield>();
        if (shield == null) shield = hitObj.GetComponent<PlayerShield>();

        if (shield != null && shield.IsShieldActive)
        {
            BounceOff(shield);
            return;
        }

        // 2. 碰撞到玩家本體：觸發玩家重生，絕不觸發石化效果！
        if (hitObj.tag == "Player" || hitObj.name.ToLower().Contains("player") || hitObj.GetComponentInParent<PlayerMovement>() != null)
        {
            if (currentState == BirdState.Diving)
            {
                PlayerRespawnSystem respawn = hitObj.GetComponent<PlayerRespawnSystem>();
                if (respawn == null) respawn = hitObj.GetComponentInParent<PlayerRespawnSystem>();
                if (respawn != null)
                {
                    respawn.TriggerRespawn();
                }

                // 撞到玩家後彈開淡出
                BounceOff(shield);
                return;
            }
        }

        // 3. 俯衝期間碰撞到 Floor 地面 (純物理碰撞觸發，不使用任何射線) ➔ 立刻以當前角度精確插進地面！
        if (currentState == BirdState.Diving && IsGroundObject(hitObj))
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
    /// 撞擊護盾時反彈飛開效果：播放 DIE 動畫，並依據 Inspector 填寫的數值進行 100% 精確掌控的 2D 拋物線彈飛與漸隱！
    /// </summary>
    public void BounceOff(PlayerShield shield)
    {
        if (currentState == BirdState.Bounced) return;

        StopAllCoroutines(); // 說明：立即停止俯衝攜程與其他運動
        currentState = BirdState.Bounced;

        // 關閉物理隨機運動，採用 100% 精確控制的拋物線插值
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }

        // 恢復動畫速度並強制觸發 DIE 動畫
        if (animator != null) animator.speed = 1f;
        PlayAnim(dieAnimName);

        StartCoroutine(ControlledBounceCoroutine(shield));
    }

    /// <summary>
    /// 100% 可控拋物線反彈協程：飛行距離(bounceDistance)、弧度高度(bounceHeight)、飛行時間(bounceDuration)
    /// </summary>
    private IEnumerator ControlledBounceCoroutine(PlayerShield shield)
    {
        Vector3 startPos = transform.position;
        Vector3 shieldCenter = (shield != null ? shield.transform.position : (playerTrans != null ? playerTrans.position : startPos - Vector3.right));

        float realBounceDuration = bounceDuration > 0.05f ? bounceDuration : 0.6f;
        float realFadeDuration = fadeDuration > 0.05f ? fadeDuration : 1.0f;
        float realBounceDist = bounceDistance > 0.1f ? bounceDistance : 2.5f;

        float dirX = (startPos.x >= shieldCenter.x) ? 1.0f : -1.0f;
        Vector3 targetPos = new Vector3(startPos.x + dirX * realBounceDist, startPos.y, originalPosition.z);

        Quaternion startRot = transform.rotation;
        Quaternion endRot = startRot * Quaternion.Euler(0, 0, dirX * -bounceSpinAngle);

        Debug.Log($"【鳥群反彈】100% 可控反彈啟動！距離: {realBounceDist}m, 高度: {bounceHeight}m, 時間: {realBounceDuration}s");

        float elapsed = 0f;
        while (elapsed < realBounceDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / realBounceDuration);

            Vector3 currentPos = Vector3.Lerp(startPos, targetPos, t);
            float arcY = 4f * bounceHeight * t * (1f - t);
            currentPos.y += arcY;
            currentPos.z = originalPosition.z;

            transform.position = currentPos;
            transform.rotation = Quaternion.Slerp(startRot, endRot, t);

            yield return null;
        }

        float fadeElapsed = 0f;
        while (fadeElapsed < realFadeDuration)
        {
            fadeElapsed += Time.deltaTime;
            float alpha = 1.0f - (fadeElapsed / realFadeDuration);
            SetAlpha(alpha);
            yield return null;
        }

        Destroy(gameObject);
    }

    // --- IResettable 實作 ---
    public void ResetToInitialState()
    {
        StopAllCoroutines();
        currentState = BirdState.Idle;
        transform.position = originalPosition;
        transform.rotation = originalRotation;
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.isKinematic = true;
        }
        SetAlpha(1.0f);
        PlayAnim(idleAnimName);
    }
}
