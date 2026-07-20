using UnityEngine;
using System.Collections;

public enum BirdBehavior { DirectPlayer, PlayerOffset, HomingPlayer }

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

    [Header("音效設定")]
    [Tooltip("俯衝前發出的叫聲音效")]
    public AudioClip warningClip;

    private AudioSource audioSource;
    private Rigidbody rb;
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
        
        // 預設物理關閉重力，不受干擾
        rb.useGravity = false;
        rb.isKinematic = true;

        spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        meshRenderer = GetComponentInChildren<Renderer>();

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;

        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj != null) playerTrans = playerObj.transform;

        originalPosition = transform.position;
        originalRotation = transform.rotation;
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
        
        // 1. 播放警告叫聲
        if (audioSource != null && warningClip != null)
        {
            audioSource.PlayOneShot(warningClip);
        }

        // 2. 警報期等待
        yield return new WaitForSeconds(warningDuration);

        // 3. 進入俯衝
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

        // 4. 持續朝目標飛行，直到碰撞發生
        while (currentState == BirdState.Diving)
        {
            if (behaviorType == BirdBehavior.HomingPlayer && playerTrans != null)
            {
                // 鎖定追逐模式下，每幀更新目標點至玩家最新位置
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
                // 隨機在玩家的 X + 5 或 X - 5 處俯衝，製造不確定隨機感
                float xOffset = Random.value > 0.5f ? targetOffset : -targetOffset;
                targetPosition = new Vector3(playerPos.x + xOffset, playerPos.y, playerPos.z);
                break;

            case BirdBehavior.HomingPlayer:
                targetPosition = playerPos;
                break;
        }
    }

    /// <summary>
    /// 當撞擊地面物件時觸發 (由地面物件掛載的 GroundCollisionNotifier 回傳)
    /// </summary>
    public void OnHitGround()
    {
        if (currentState != BirdState.Diving) return;
        
        currentState = BirdState.Stuck;
        rb.linearVelocity = Vector3.zero;
        rb.isKinematic = true;

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

    private void OnCollisionEnter(Collision collision)
    {
        HandleCollision(collision.gameObject);
    }

    private void OnTriggerEnter(Collider other)
    {
        HandleCollision(other.gameObject);
    }

    private void HandleCollision(GameObject other)
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
        rb.isKinematic = false;
        rb.useGravity = true; // 彈開後加重力

        // 力道彈開方向：遠離護盾中心朝向鳥的方向，並強行往上拋
        Vector3 bounceDir = (transform.position - shield.transform.position).normalized;
        bounceDir.y = Mathf.Abs(bounceDir.y) + 0.5f; 
        bounceDir = bounceDir.normalized;

        rb.linearVelocity = bounceDir * shield.knockbackForce;
        Debug.LogWarning($"【鳥群系統】{gameObject.name} 碰撞到護盾被彈飛！擊退力道：{shield.knockbackForce}");

        Destroy(gameObject, 2f);
    }

    // --- IResettable 實作 ---
    public void ResetToInitialState()
    {
        StopAllCoroutines();
        rb.linearVelocity = Vector3.zero;
        rb.isKinematic = true;
        rb.useGravity = false;
        transform.position = originalPosition;
        transform.rotation = originalRotation;
        SetAlpha(1.0f);
        currentState = BirdState.Idle;
    }
}
