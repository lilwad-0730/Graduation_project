using UnityEngine;

/// <summary>
/// 控制 2D 狼的 Sprite 動態與動畫參數更新。
/// 支援自動翻轉 Sprite 面朝玩家，並根據物理速度與狀態判定播放 Idle、跑、走、倒退走等動畫。
/// </summary>
public class WolfSpriteAnimator : MonoBehaviour
{
    [Header("元件關聯")]
    [Tooltip("狼的 Animator 組件 (若為空，會嘗試在子物件或本機中搜尋)")]
    public Animator animator;
    
    [Tooltip("狼的 SpriteRenderer 組件 (用來控制左右翻轉)")]
    public SpriteRenderer spriteRenderer;

    private Rigidbody rb;
    private WolfEnemy wolfEnemy;
    private Transform playerTransform;

    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        wolfEnemy = GetComponent<WolfEnemy>();

        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();

        GameObject pObj = GameObject.FindGameObjectWithTag("Player");
        if (pObj != null)
        {
            playerTransform = pObj.transform;
        }
    }

    private void Update()
    {
        if (animator == null || rb == null) return;

        // 1. 獲取基本數值與狀態
        float speedX = rb.linearVelocity.x;
        float absSpeedX = Mathf.Abs(speedX);

        bool isStunned = false;
        bool isAttached = false;

        if (wolfEnemy != null)
        {
            // 透過反射或直接訪問 WolfEnemy 內部的私有狀態 (如果有的話)
            // 由於 WolfEnemy.cs 裡 isStunned 與 isAttached 是 private，
            // 我們可以使用下面的安全讀取方式，或者在 WolfEnemy 補上 public 屬性。
            // 這裡我們先提供一組屬性對應，並在下方寫出通用防呆。
        }

        // 安全地反射獲取 WolfEnemy 的狀態以防私有變數無法存取
        isStunned = GetWolfEnemyBool("isStunned");
        isAttached = GetWolfEnemyBool("isAttached");

        // 2. 轉向與倒退判定
        if (playerTransform != null && !isAttached && !isStunned)
        {
            // 算出玩家相對於狼的方向 (1 代表玩家在右邊，-1 代表在左邊)
            float directionToPlayer = Mathf.Sign(playerTransform.position.x - transform.position.x);

            // 狼面朝玩家：如果玩家在右，狼不翻轉；玩家在左，狼翻轉 (假設原畫是面朝右)
            if (spriteRenderer != null)
            {
                spriteRenderer.flipX = (directionToPlayer < 0);
            }

            // 倒退走 (Backing Up) 判定：
            // 當狼在移動，且其「移動速度方向」與「面朝玩家方向」相反時，代表正在倒退！
            // 例如：玩家在右 (directionToPlayer > 0)，但狼的速度往左 (speedX < -0.1f)
            bool isBackingUp = false;
            if (absSpeedX > 0.1f)
            {
                isBackingUp = (speedX * directionToPlayer < 0);
            }

            animator.SetBool("IsBackingUp", isBackingUp);
        }
        else
        {
            animator.SetBool("IsBackingUp", false);
        }

        // 3. 更新 Animator 內的參數
        animator.SetFloat("Speed", absSpeedX);
        animator.SetBool("IsStunned", isStunned);
        animator.SetBool("IsAttached", isAttached);
    }

    // 利用反射安全讀取 WolfEnemy 的 private bool 狀態
    private bool GetWolfEnemyBool(string fieldName)
    {
        if (wolfEnemy == null) return false;
        try
        {
            var field = typeof(WolfEnemy).GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field != null)
            {
                return (bool)field.GetValue(wolfEnemy);
            }
        }
        catch {}
        return false;
    }
}
