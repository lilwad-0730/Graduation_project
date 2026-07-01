using UnityEngine;
using System.Collections;

[RequireComponent(typeof(Rigidbody), typeof(Collider))]
public class WolfEnemy : MonoBehaviour
{
    [Header("追蹤設定")]
    [Tooltip("背對狼逃跑時，狼的追擊速度 (快速追擊)")]
    public float fastChaseSpeed = 8f;
    [Tooltip("回頭看狼時，狼的減速速度 (大於 0 仍會緩慢移動)")]
    public float slowChaseSpeed = 1.5f;
    public float aggroDistanceX = 6f; // 靠近到 x=6 開始追蹤
    public float giveUpDistanceX = 12f; // 【新增】逃遠到 x=12 放棄追蹤

    private Transform player;
    private PlayerMovement playerMovement; 
    private Rigidbody rb;
    private Collider col;

    // 狀態鎖
    private bool isChasing = false;
    private bool isAttached = false;
    private bool isStunned = false; // 被 StopAttackObject 打到時的硬直狀態

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        col = GetComponent<Collider>();
        
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

        // 【修改】判斷要不要追蹤，或是要不要放棄
        if (distanceX <= aggroDistanceX && !isChasing)
        {
            isChasing = true; // 進入範圍，開始追！
        }
        else if (distanceX > giveUpDistanceX && isChasing)
        {
            isChasing = false; // 逃太遠了，放棄追蹤
            rb.linearVelocity = new Vector3(0, rb.linearVelocity.y, rb.linearVelocity.z); // 原地煞車
        }

        // 執行追蹤
        if (isChasing)
        {
            ChasePlayer();
        }
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

        // 根據玩家朝向決定追擊速度
        float currentSpeed = isPlayerFacingWolf ? slowChaseSpeed : fastChaseSpeed;

        rb.linearVelocity = new Vector3(directionX * currentSpeed, rb.linearVelocity.y, rb.linearVelocity.z);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (isStunned || isAttached) return;

        // 咬到玩家 (接觸)
        if (collision.gameObject.CompareTag("Player"))
        {
            // 【新增】碰到玩家瞬間，先把狼的速度清空，避免殘餘力量撞飛玩家
            rb.linearVelocity = Vector3.zero; 
            rb.angularVelocity = Vector3.zero;
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
}