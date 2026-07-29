using System.Collections;
using UnityEngine;

/// <summary>
/// 2D 直線循環移動平台 (適用於 2D 物理與 Rigidbody2D)
/// 支援水平、垂直及任意斜線移動、端點停留、固定速度平滑移動與玩家穩定跟隨機制。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D))]
public class LinearMovingPlatform2D : MonoBehaviour
{
    [Header("端點座標設定 (世界座標)")]
    [SerializeField]
    [Tooltip("移動起點的世界座標 (Vector2)。點擊組件右鍵選單可將當前位置設為起點。")]
    private Vector2 startPoint;

    [SerializeField]
    [Tooltip("移動終點的世界座標 (Vector2)。點擊組件右鍵選單可將當前位置設為終點。")]
    private Vector2 endPoint;

    [SerializeField]
    [Tooltip("遊戲開始時，是否自動將平台當前位置作為起點？")]
    private bool useCurrentPosAsStartOnAwake = true;

    [Header("移動參數設定")]
    [SerializeField]
    [Tooltip("平台的固定移動速度 (單位：世界單位/秒)。整個路程保持勻速，不減速。")]
    private float moveSpeed = 4.0f;

    [SerializeField]
    [Tooltip("抵達起點或終點時的停留時間 (秒)。設為 0 則不停留。")]
    private float waitTime = 2.0f;

    [SerializeField]
    [Tooltip("遊戲開始 (Start) 時是否立即自動開始移動。")]
    private bool autoStart = true;

    [Header("玩家判定機制")]
    [SerializeField]
    [Tooltip("玩家物件的 Layer Mask。若設為 Nothing，則改為以 Tag 判斷。")]
    private LayerMask playerLayer;

    [SerializeField]
    [Tooltip("玩家物件的 Tag 標籤 (預設為 'Player')。")]
    private string playerTag = "Player";

    [SerializeField]
    [Tooltip("觸碰判定法線 Y 軸門檻：當碰撞點法線朝下於此數值時，認定玩家踩在平台上方。")]
    private float topCollisionNormalThreshold = -0.5f;

    [Header("Scene 視覺輔助 (Gizmos)")]
    [SerializeField]
    [Tooltip("是否在 Scene 視窗中繪製起點、終點與移動軌跡。")]
    private bool drawGizmos = true;

    [SerializeField]
    [Tooltip("起點標記顏色。")]
    private Color startPointColor = Color.green;

    [SerializeField]
    [Tooltip("終點標記顏色。")]
    private Color endPointColor = Color.red;

    [SerializeField]
    [Tooltip("移動路線軌跡顏色。")]
    private Color pathLineColor = Color.yellow;

    [SerializeField]
    [Tooltip("端點圓形標記半徑。")]
    private float pointGizmoRadius = 0.3f;

    // --- 快取與內部狀態變數 ---
    private Rigidbody2D rb;
    private Vector2 targetPoint;
    private bool isMoving = false;
    private bool isWaiting = false;
    private Coroutine waitCoroutine;

    // 公開屬性存取
    public Vector2 StartPoint { get => startPoint; set => startPoint = value; }
    public Vector2 EndPoint { get => endPoint; set => endPoint = value; }
    public float MoveSpeed { get => moveSpeed; set => moveSpeed = Mathf.Max(0f, value); }
    public float WaitTime { get => waitTime; set => waitTime = Mathf.Max(0f, value); }
    public bool IsMoving => isMoving;
    public bool IsWaiting => isWaiting;

    private void Awake()
    {
        // 1. 快取 Rigidbody2D 並預防性檢查
        rb = GetComponent<Rigidbody2D>();
        if (rb == null)
        {
            Debug.LogError($"[{nameof(LinearMovingPlatform2D)}] 物件 '{gameObject.name}' 找不到 Rigidbody2D 組件！腳本已被停用。", this);
            enabled = false;
            return;
        }

        // 2. 強制將 Rigidbody2D 設為 Kinematic BodyType
        if (rb.bodyType != RigidbodyType2D.Kinematic)
        {
            rb.bodyType = RigidbodyType2D.Kinematic;
            Debug.Log($"[{nameof(LinearMovingPlatform2D)}] 物件 '{gameObject.name}' 的 Rigidbody2D 已自動設為 Kinematic。", this);
        }

        // 3. 關閉重力影響
        rb.gravityScale = 0f;

        // 4. 起點自動初始化
        if (useCurrentPosAsStartOnAwake)
        {
            startPoint = transform.position;
        }
    }

    private void Start()
    {
        // 預設將目標設為終點
        targetPoint = endPoint;
        isMoving = autoStart;

        // 平台放置於起點位置
        if (rb != null)
        {
            rb.position = startPoint;
        }
    }

    private void FixedUpdate()
    {
        if (!isMoving || isWaiting || rb == null) return;

        Vector2 currentPos = rb.position;

        // 1. 勻速移動 (固定 moveSpeed * Time.fixedDeltaTime)
        Vector2 nextPos = Vector2.MoveTowards(currentPos, targetPoint, moveSpeed * Time.fixedDeltaTime);

        // 2. 使用 Rigidbody2D.MovePosition 移動以確保物理碰撞與跟隨正常
        rb.MovePosition(nextPos);

        // 3. 檢查是否抵達目標端點
        if (Vector2.Distance(nextPos, targetPoint) <= 0.001f)
        {
            // 精準貼合端點位置
            rb.MovePosition(targetPoint);

            // 若有設定停留時間，啟動停留協程
            if (waitTime > 0f)
            {
                if (waitCoroutine != null)
                {
                    StopCoroutine(waitCoroutine);
                }
                waitCoroutine = StartCoroutine(WaitRoutine());
            }
            else
            {
                SwitchTarget();
            }
        }
    }

    /// <summary>
    /// 端點停留協程
    /// </summary>
    private IEnumerator WaitRoutine()
    {
        isWaiting = true;
        yield return new WaitForSeconds(waitTime);
        SwitchTarget();
        isWaiting = false;
    }

    /// <summary>
    /// 反轉移動目標 (起點 <-> 終點)
    /// </summary>
    private void SwitchTarget()
    {
        targetPoint = (targetPoint == endPoint) ? startPoint : endPoint;
    }

    /// <summary>
    /// 外部控制開始 / 暫停移動
    /// </summary>
    public void SetMoving(bool active)
    {
        isMoving = active;
    }

    // --- 玩家跟隨機制 (防止玩家滑落與甩開) ---

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (IsPlayerObject(collision.gameObject))
        {
            // 判斷撞擊點是否位於平台上方 (法線朝下)
            foreach (ContactPoint2D contact in collision.contacts)
            {
                if (contact.normal.y < topCollisionNormalThreshold)
                {
                    collision.transform.SetParent(transform);
                    break;
                }
            }
        }
    }

    private void OnCollisionExit2D(Collision2D collision)
    {
        if (IsPlayerObject(collision.gameObject))
        {
            if (collision.transform.parent == transform)
            {
                collision.transform.SetParent(null);
            }
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (IsPlayerObject(other.gameObject))
        {
            other.transform.SetParent(transform);
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (IsPlayerObject(other.gameObject))
        {
            if (other.transform.parent == transform)
            {
                other.transform.SetParent(null);
            }
        }
    }

    /// <summary>
    /// 驗證物件是否為玩家
    /// </summary>
    private bool IsPlayerObject(GameObject go)
    {
        if (go == null) return false;

        // 若有指定 LayerMask 則優先判定 Layer
        if (playerLayer.value != 0)
        {
            if (((1 << go.layer) & playerLayer.value) != 0)
            {
                return true;
            }
        }

        // 次要以 Tag 判定
        if (!string.IsNullOrEmpty(playerTag) && go.CompareTag(playerTag))
        {
            return true;
        }

        return false;
    }

    // --- Context Menu 便利功能 ---

    [ContextMenu("Set Current Position as Start")]
    private void SetCurrentAsStart()
    {
        startPoint = transform.position;
        Debug.Log($"[{gameObject.name}] 已將起點設定為: {startPoint}");
    }

    [ContextMenu("Set Current Position as End")]
    private void SetCurrentAsEnd()
    {
        endPoint = transform.position;
        Debug.Log($"[{gameObject.name}] 已將終點設定為: {endPoint}");
    }

    // --- Gizmos 繪製 ---

    private void OnDrawGizmos()
    {
        if (!drawGizmos) return;

        Vector2 drawStart = (useCurrentPosAsStartOnAwake && !Application.isPlaying) ? (Vector2)transform.position : startPoint;
        Vector2 drawEnd = endPoint;

        // 畫移動路線
        Gizmos.color = pathLineColor;
        Gizmos.DrawLine(drawStart, drawEnd);

        // 畫起點與終點標記小圓形
        Gizmos.color = startPointColor;
        Gizmos.DrawWireSphere(drawStart, pointGizmoRadius);
        Gizmos.DrawSphere(drawStart, pointGizmoRadius * 0.5f);

        Gizmos.color = endPointColor;
        Gizmos.DrawWireSphere(drawEnd, pointGizmoRadius);
        Gizmos.DrawSphere(drawEnd, pointGizmoRadius * 0.5f);
    }
}
