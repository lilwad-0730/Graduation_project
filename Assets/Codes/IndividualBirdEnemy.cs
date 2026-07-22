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

    [Header("音效設定")]
    [Tooltip("俯衝前發出的叫聲音效")]
    public AudioClip warningClip;

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

        if (playerTrans == null)
        {
            GameObject playerObj = GameObject.FindWithTag("Player");
            if (playerObj != null) playerTrans = playerObj.transform;
        }
    }

    private void Update()
    {
        // 核心修復 1：自動偵測玩家距離並觸發俯衝
        if (autoDetectPlayer && currentState == BirdState.Idle)
        {
            if (playerTrans == null) EnsureComponents();

            if (playerTrans != null)
            {
                float dist = Vector3.Distance(transform.position, playerTrans.position);
                if (dist <= detectionRange)
                {
                    Debug.Log($"【鳥群系統】玩家進入偵測範圍 ({dist:F1}m <= {detectionRange}m)！{gameObject.name} 發起俯衝攻擊！");
                    StartAttackSequence();
                }
            }
        }
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

        // 4. 程式切換為俯衝飛行動畫 (flying)
        PlayAnim(diveAnimName);
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
                targetPosition = playerTrans.position;
            }

            diveDirection = (targetPosition - transform.position).normalized;
            rb.linearVelocity = diveDirection * diveSpeed;

            // 使鳥物件朝向它的飛行方向
            if (diveDirection != Vector3.zero)
            {
                float angle = Mathf.Atan2(diveDirection.y, diveDirection.x) * Mathf.Rad2Deg;
                transform.rotation = Quaternion.AngleAxis(angle, Vector3.forward);
            }

            yield return null;
        }
    }

    private void UpdateTargetPosition()
    {
        if (playerTrans == null) return;

        Vector3 playerPos = playerTrans.position;

        switch (behaviorType)
        {
            case BirdBehavior.DirectPlayer:
                targetPosition = playerPos;
                break;

            case BirdBehavior.PlayerOffset:
                float xOffset = Random.value > 0.5f ? targetOffset : -targetOffset;
                targetPosition = new Vector3(playerPos.x + xOffset, playerPos.y, playerPos.z);
                break;

            case BirdBehavior.HomingPlayer:
                targetPosition = playerPos;
                break;
        }
    }

    /// <summary>
    /// 當撞擊地面物件時觸發
    /// </summary>
    public void OnHitGround()
    {
        if (currentState != BirdState.Diving) return;
        
        currentState = BirdState.Stuck;
        rb.linearVelocity = Vector3.zero;
        rb.isKinematic = true;

        // 程式切換為撞地降落動畫 (landing)
        PlayAnim(stuckAnimName);

        Debug.Log($"【鳥群系統】{gameObject.name} 撞擊地面，卡住 {stuckDuration} 秒後開始消失。");
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

    private void SetAlpha(float alpha)
    {
        if (spriteRenderer != null)
        {
            Color c = spriteRenderer.color;
            c.a = alpha;
            spriteRenderer.color = c;
        }
        else if (meshRenderer != null && meshRenderer.material != null && meshRenderer.material.HasProperty("_Color"))
        {
            Color c = meshRenderer.material.color;
            c.a = alpha;
            meshRenderer.material.color = c;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (currentState != BirdState.Diving) return;

        // 1. 碰撞到玩家護盾
        PlayerShield shield = other.GetComponentInParent<PlayerShield>();
        if (shield != null && shield.IsShieldActive)
        {
            BounceOff(shield);
            return;
        }

        // 2. 碰撞到玩家本體
        if (other.CompareTag("Player"))
        {
            PlayerPetrification petrify = other.GetComponent<PlayerPetrification>();
            if (petrify != null)
            {
                petrify.Petrify();
            }
            else
            {
                PlayerRespawnSystem respawn = other.GetComponent<PlayerRespawnSystem>();
                if (respawn != null) respawn.TriggerRespawn();
            }

            Destroy(gameObject);
        }
    }

    private void BounceOff(PlayerShield shield)
    {
        currentState = BirdState.Bounced;
        rb.linearVelocity = Vector3.zero;
        
        // 程式切換為死亡彈飛動畫 (die)
        PlayAnim(dieAnimName);

        Vector3 bounceDir = (transform.position - shield.transform.position).normalized + Vector3.up * 0.5f;
        rb.AddForce(bounceDir * shield.knockbackForce, ForceMode.Impulse);

        Debug.Log($"【鳥群系統】{gameObject.name} 撞擊玩家護盾！成功彈飛！");
        Destroy(gameObject, 1.5f);
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
