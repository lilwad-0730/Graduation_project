using UnityEngine;
using System.Collections;

public enum AttackDirection { Left, Right }

/// <summary>
/// 管理鳥群隨機或區域觸發的俯衝襲擊。
/// 包含聽聲辨位警告、反方向閃避判定以及遮陽傘安全區檢查。
/// </summary>
public class BirdFlockAttackSystem : MonoBehaviour, IResettable
{
    [Header("音效設定")]
    [Tooltip("鳥群從左邊襲擊時播放的警告音效 (可為空)")]
    public AudioClip warningSoundLeft;
    [Tooltip("鳥群從右邊襲擊時播放的警告音效 (可為空)")]
    public AudioClip warningSoundRight;
    [Tooltip("播放警告與襲擊音效的 AudioSource")]
    public AudioSource audioSource;

    [Header("時間與判定設定")]
    [Tooltip("聽叫聲警告的持續時間 (秒，預設 1.5)")]
    public float warningDuration = 1.5f;
    [Tooltip("襲擊判定時間 (秒，在此期間玩家必須保持往反方向閃避，預設 0.8)")]
    public float attackActiveDuration = 0.8f;
    [Tooltip("如果掛載在 Trigger 物件上，是否只觸發一次？")]
    public bool triggerOnce = true;

    [Header("襲擊方向模式")]
    [Tooltip("是否隨機決定叫聲來源方向。如果為 false，將固定使用下面的 presetDirection。")]
    public bool randomDirection = true;
    [Tooltip("固定模式下的鳥叫/襲擊來源方向")]
    public AttackDirection presetDirection = AttackDirection.Left;

    private bool hasTriggered = false;
    private bool isAttackInProgress = false;
    private AttackDirection currentWarningDir;

    private Transform playerTransform;
    private Rigidbody playerRb;
    private PlayerRespawnSystem playerRespawn;

    private void Start()
    {
        // 尋找玩家
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            playerTransform = playerObj.transform;
            playerRb = playerObj.GetComponent<Rigidbody>();
            playerRespawn = playerObj.GetComponent<PlayerRespawnSystem>();
        }

        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }
    }

    /// <summary>
    /// 供外部腳本或碰撞 Trigger 主動觸發襲擊
    /// </summary>
    public void TriggerAttack()
    {
        if (hasTriggered && triggerOnce) return;
        if (isAttackInProgress) return;

        hasTriggered = true;
        StartCoroutine(AttackSequence());
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            TriggerAttack();
        }
    }

    private IEnumerator AttackSequence()
    {
        isAttackInProgress = true;

        // 1. 決定方向
        currentWarningDir = randomDirection ? (AttackDirection)Random.Range(0, 2) : presetDirection;

        // 2. 播警告音效 (利用 3D 聲道進行聽聲辨位)
        if (audioSource != null && playerTransform != null)
        {
            // 將音源移至玩家相應側以產生 3D 立體警告聲
            Vector3 audioPos = playerTransform.position + (currentWarningDir == AttackDirection.Left ? Vector3.left : Vector3.right) * 5f;
            audioSource.transform.position = audioPos;

            AudioClip clip = currentWarningDir == AttackDirection.Left ? warningSoundLeft : warningSoundRight;
            if (clip != null)
            {
                audioSource.PlayOneShot(clip);
            }
            Debug.Log($"【鳥群警報】叫聲從 {currentWarningDir} 側傳來！請立刻朝相反方向閃避！");
        }

        // 警告期等待
        yield return new WaitForSeconds(warningDuration);

        // 3. 進入襲擊判定階段 (鳥群俯衝而下)
        Debug.Log("【鳥群襲擊】鳥群俯衝！開始閃避判定。");
        float elapsed = 0f;
        bool isPlayerHit = false;

        while (elapsed < attackActiveDuration)
        {
            elapsed += Time.deltaTime;

            // 若躲在遮陽傘下，直接避開攻擊
            if (UmbrellaZone.IsPlayerUnderUmbrella)
            {
                Debug.Log("【鳥群襲擊】玩家位於遮陽傘下，避開了鳥群襲擊。");
                break;
            }

            // 偵測玩家閃避方向是否正確：
            // - 左側來襲，玩家必須往右移動 (moveInput > 0.1 或 Velocity.x > 0.5)
            // - 右側來襲，玩家必須往左移動 (moveInput < -0.1 或 Velocity.x < -0.5)
            float moveInput = Input.GetAxis("Horizontal");
            float speedX = playerRb != null ? playerRb.linearVelocity.x : 0f;

            bool isDodgeCorrect = false;
            if (currentWarningDir == AttackDirection.Left)
            {
                isDodgeCorrect = (moveInput > 0.1f || speedX > 0.5f);
            }
            else
            {
                isDodgeCorrect = (moveInput < -0.1f || speedX < -0.5f);
            }

            if (!isDodgeCorrect)
            {
                isPlayerHit = true;
                break;
            }

            yield return null;
        }

        if (isPlayerHit)
        {
            Debug.LogError("【鳥群襲擊】躲避失敗！玩家遭受鳥群撞擊！");
            if (playerRespawn != null)
            {
                playerRespawn.TriggerRespawn();
            }
        }
        else
        {
            Debug.Log("【鳥群襲擊】躲避成功！");
        }

        isAttackInProgress = false;
    }

    // --- IResettable 實作 ---
    public void ResetToInitialState()
    {
        StopAllCoroutines();
        isAttackInProgress = false;
        hasTriggered = false;
    }
}
