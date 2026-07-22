using UnityEngine;
using System.Collections;

public enum BirdBehavior { DirectPlayer, PlayerOffset, HomingPlayer }

/// <summary>
/// 個別鳥類敵人控制器：
/// 1. 支援程式碼強行接管並播放各種動畫 (Idle, 警報, 俯衝, 撞地卡住, 死亡/彈飛)。
/// 2. 支援發出聲音警報 ➔ 高速俯衝 ➔ 撞擊護盾彈飛 / 撞擊玩家石化 / 撞擊地面卡住消退。
/// </summary>
public class IndividualBirdEnemy : MonoBehaviour, IResettable
{
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

    [Header("動畫控制 (可直接在 Inspector 指定狀態區塊名稱)")]
    [Tooltip("待機/盤旋動畫名稱 (預設 flyStraight)")]
    public string idleAnimName = "flyStraight";

    [Tooltip("警報/準備俯衝動畫名稱 (預設 worried 或 watch01)")]
    public string warningAnimName = "worried";

    [Tooltip("高速俯衝動畫名稱 (預設 flyStraight)")]
    public string diveAnimName = "flyStraight";

    [Tooltip("撞地卡住動畫名稱 (預設 landing 或 peck)")]
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

        // 自動搜尋 Animator (支援本體與子物件)
        animator = GetComponent<Animator>();
        if (animator == null) animator = GetComponentInChildren<Animator>();

        spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        meshRenderer = GetComponentInChildren<Renderer>();

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;

        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj != null) playerTrans = playerObj.transform;

        originalPosition = transform.position;
        originalRotation = transform.rotation;

        // 預設播放待機飛行動畫
        PlayAnim(idleAnimName);
    }

    /// <summary>
    /// 【程式控制動畫核心方法】：根據傳入的動畫區塊名稱，強制動態切換播放！
    /// </summary>
    public void PlayAnim(string animName)
    {
        if (animator == null || string.IsNullOrEmpty(animName)) return;

        // 若 Animator 內有對應狀態，直接進行 CrossFade 過渡切換
        if (animator.HasState(0, Animator.StringToHash(animName)))
        {
            animator.CrossFade(animName, 0.1f);
        }
        else
        {
            // 若為 FBX 自帶的預設 Clip，嘗試用 Play 播放
            animator.Play(animName);
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
        
        // 1. 程式切換為警報動畫 (例如 worried/watch01)
        PlayAnim(warningAnimName);

        // 2. 播放叫聲
        if (audioSource != null && warningClip != null)
        {
            audioSource.PlayOneShot(warningClip);
        }

        // 3. 警報期等待
        yield return new WaitForSeconds(warningDuration);

        // 4. 程式切換為俯衝飛行動畫
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

        // 程式切換為撞地/降落動畫 (如 landing 或 peck)
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
        
        // 程式切換為死亡/彈飛動畫 (如 die)
        PlayAnim(dieAnimName);

        // 向後上方反彈
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
