using UnityEngine;

/// <summary>
/// 控制 2D 狼的 Sprite 動態與動畫狀態切換。
/// 直接透過 C# 程式碼呼叫 Play/CrossFade 切換動畫，省去在 Animator 連線與設定參數的步驟。
/// </summary>
public class WolfSpriteAnimator : MonoBehaviour
{
    [Header("元件關聯")]
    [Tooltip("狼的 Animator 組件 (建議手動拖入有 Animator 的物件，若為空會自動搜尋)")]
    public Animator animator;
    
    [Tooltip("狼的 SpriteRenderer 組件 (建議手動拖入有 SpriteRenderer 的物件，若為空會自動搜尋)")]
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

    [Header("轉向設定")]
    [Tooltip("如果您的狼原圖朝向是反的 (例如主角在右邊，狼卻看左邊)，請勾選此欄位進行反轉修正")]
    public bool reverseFacingDirection = false;

    private Rigidbody rb;
    private WolfEnemy wolfEnemy;
    private Transform playerTransform;
    
    private string currentPlayingState = "";

    private void Start()
    {
        // 強化版組件搜尋：不論腳本掛載在父物件或子物件上，都能自動抓取所需組件
        rb = GetComponent<Rigidbody>();
        if (rb == null) rb = GetComponentInParent<Rigidbody>();
        if (rb == null) rb = GetComponentInChildren<Rigidbody>();

        wolfEnemy = GetComponent<WolfEnemy>();
        if (wolfEnemy == null) wolfEnemy = GetComponentInParent<WolfEnemy>();
        if (wolfEnemy == null) wolfEnemy = GetComponentInChildren<WolfEnemy>();

        if (animator == null) animator = GetComponent<Animator>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (animator == null) animator = GetComponentInParent<Animator>();

        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (spriteRenderer == null) spriteRenderer = GetComponentInParent<SpriteRenderer>();

        GameObject pObj = GameObject.FindGameObjectWithTag("Player");
        if (pObj != null)
        {
            playerTransform = pObj.transform;
        }

        // 防呆警告
        if (rb == null) Debug.LogWarning($"[WolfSpriteAnimator] '{gameObject.name}' 找不到 Rigidbody，無法取得移動速度！");
        if (wolfEnemy == null) Debug.LogWarning($"[WolfSpriteAnimator] '{gameObject.name}' 找不到 WolfEnemy 腳本，無法讀取硬直與附著狀態！");
        if (animator == null) Debug.LogWarning($"[WolfSpriteAnimator] '{gameObject.name}' 找不到 Animator，無法播放動畫！");
        if (spriteRenderer == null) Debug.LogWarning($"[WolfSpriteAnimator] '{gameObject.name}' 找不到 SpriteRenderer，無法翻轉方向！");
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
        
        // 2. 轉向與倒退走判定：狼永遠保持朝向正 X 軸 (+X / 面朝右邊)
        float targetScaleX = reverseFacingDirection ? -1f : 1f;

        if (spriteRenderer != null)
        {
            if (spriteRenderer.transform != transform)
            {
                // 1. 如果 SpriteRenderer 在子物件上，固定為正 X 軸縮放
                Vector3 currentScale = spriteRenderer.transform.localScale;
                spriteRenderer.transform.localScale = new Vector3(targetScaleX * Mathf.Abs(currentScale.x), currentScale.y, currentScale.z);
            }
            else
            {
                // 2. 如果 SpriteRenderer 掛在父物件本體上，固定 flipX
                spriteRenderer.flipX = reverseFacingDirection;
            }
        }

        if (playerTransform != null && !isAttached && !isStunned)
        {
            float directionToPlayer = Mathf.Sign(playerTransform.position.x - transform.position.x);

            // 倒退走判定：當狼向左 (-X) 移動、或其速度與玩家方向相反時，觸發專用倒退動畫
            if (absSpeedX > 0.1f)
            {
                isBackingUp = (speedX < -0.1f) || (speedX * directionToPlayer < 0f);
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
