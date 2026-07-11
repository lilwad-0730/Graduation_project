using UnityEngine;

/// <summary>
/// 控制 2D 狼的 Sprite 動態與動畫狀態切換。
/// 直接透過 C# 程式碼呼叫 Play/CrossFade 切換動畫，省去在 Animator 連線與設定參數的步驟。
/// </summary>
public class WolfSpriteAnimator : MonoBehaviour
{
    [Header("元件關聯")]
    [Tooltip("狼的 Animator 組件 (若為空，會嘗試在子物件或本機中搜尋)")]
    public Animator animator;
    
    [Tooltip("狼的 SpriteRenderer 組件 (用來控制左右翻轉)")]
    public SpriteRenderer spriteRenderer;

    [Header("動畫狀態名稱 (必須與 Animator Controller 中的 State 名字完全相同)")]
    [Tooltip("靜止/Idle 狀態的動畫名稱 (預設使用 backward 動作)")]
    public string idleStateName = "backward";

    [Tooltip("慢走狀態的動畫名稱 (預設使用 walking 動作)")]
    public string walkStateName = "walking";

    [Tooltip("奔跑狀態的動畫名稱 (預設使用 running 動作)")]
    public string runStateName = "running";

    [Tooltip("倒退走狀態的動畫名稱 (預設使用 backward 動作)")]
    public string backwardStateName = "backward";

    [Header("過渡時間")]
    [Tooltip("動畫切換時的平滑過渡時間 (秒，2D Sprite 建議設為 0 或極小如 0.02 以免殘影)")]
    public float crossFadeDuration = 0.02f;

    private Rigidbody rb;
    private WolfEnemy wolfEnemy;
    private Transform playerTransform;
    
    private string currentPlayingState = "";

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

        // 1. 獲取物理數值與狀態
        float speedX = rb.linearVelocity.x;
        float absSpeedX = Mathf.Abs(speedX);

        // 安全讀取 WolfEnemy 的 private 變數
        bool isStunned = GetWolfEnemyBool("isStunned");
        bool isAttached = GetWolfEnemyBool("isAttached");

        bool isBackingUp = false;
        
        // 2. 轉向與倒退走判定
        if (playerTransform != null && !isAttached && !isStunned)
        {
            // 判斷玩家方向 (1 右，-1 左)
            float directionToPlayer = Mathf.Sign(playerTransform.position.x - transform.position.x);

            // 翻轉 Sprite 面朝玩家
            if (spriteRenderer != null)
            {
                spriteRenderer.flipX = (directionToPlayer < 0);
            }

            // 倒退走判定：當狼在移動，且其速度方向與玩家方向相反時
            if (absSpeedX > 0.1f)
            {
                isBackingUp = (speedX * directionToPlayer < 0);
            }
        }

        // 3. 用程式直接控制動畫狀態 (CrossFade)
        string targetState = idleStateName;

        if (isAttached)
        {
            targetState = idleStateName; // 咬住玩家時播放靜止/回頭
        }
        else if (isStunned)
        {
            targetState = idleStateName; // 硬直狀態播放靜止/回頭
        }
        else if (isBackingUp)
        {
            targetState = backwardStateName; // 播放倒退動畫
        }
        else
        {
            // 根據 X 軸速度大小決定播放哪種前進動畫
            if (absSpeedX < 0.1f)
            {
                targetState = idleStateName; // 速度低於 0.1 播放靜止
            }
            else if (absSpeedX > 4.5f)
            {
                targetState = runStateName; // 速度大於 4.5 播放快跑
            }
            else
            {
                targetState = walkStateName; // 速度低於 4.5 播放慢走
            }
        }

        // 4. 直接呼叫播放 (過濾重複播放)
        PlayAnimationDirectly(targetState);
    }

    private void PlayAnimationDirectly(string stateName)
    {
        if (currentPlayingState == stateName) return;

        currentPlayingState = stateName;
        
        if (crossFadeDuration > 0f)
        {
            animator.CrossFade(stateName, crossFadeDuration);
        }
        else
        {
            animator.Play(stateName);
        }
        
        Debug.Log($"【程式碼控制動畫】已切換播放狀態為: '{stateName}'");
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
