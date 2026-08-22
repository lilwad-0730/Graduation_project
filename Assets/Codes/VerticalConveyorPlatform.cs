using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 垂直輸送帶 / 循環電梯平台系統 (適用於 3D 物理系統：3D Rigidbody + 3D Collider)
/// 平台維持相同 X/Z 座標，依照指定距離 (預設 Y 軸 8~10 個單位) 由上往下 (或由下往上) 移動。
/// 當平台抵達底部離開範圍後，會無縫重置回最上方，達成無限循環。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class VerticalConveyorPlatform : MonoBehaviour
{
    public enum MoveDirection
    {
        TopToBottom, // 由上往下 (預設)
        BottomToTop  // 由下往上
    }

    [Header("範圍與間距設定 (世界座標)")]
    [SerializeField]
    [Tooltip("平台的最高 Y 軸重置點 (Top Y)")]
    private float topY = 14.41f;

    [SerializeField]
    [Tooltip("平台的最低 Y 軸重置點 (Bottom Y)")]
    private float bottomY = -60.0f;

    [SerializeField]
    [Tooltip("固定 X 座標 (預設自動使用 Awake 時的位置 X)")]
    private float fixedX = 117.65f;

    [SerializeField]
    [Tooltip("固定 Z 座標 (預設自動使用 Awake 時的位置 Z)")]
    private float fixedZ = 0f;

    [SerializeField]
    [Tooltip("多個平台之間的 Y 軸間隔距離 (例如 8.0 個單位)")]
    private float spacingY = 8.0f;

    [SerializeField]
    [Tooltip("移動方向 (預設：由上往下 TopToBottom)")]
    private MoveDirection direction = MoveDirection.TopToBottom;

    [Header("移動速度")]
    [SerializeField]
    [Tooltip("平台移動速度 (單位：單位/秒)")]
    private float moveSpeed = 3.0f;

    [Header("生成多平台設定 (Manager 功能)")]
    [SerializeField]
    [Tooltip("是否在此物件下自動生成多個平台來填滿範圍？")]
    private bool autoGenerateClonePlatforms = true;

    [SerializeField]
    [Tooltip("填滿 topY 到 bottomY 範圍的平台總數量。設為 0 或小於計算值時會自動計算。")]
    private int totalPlatformCount = 10;

    [Header("玩家判定與跟隨機制")]
    [SerializeField]
    [Tooltip("用於判定玩家的 Layer Mask。若未設定則以 Tag 判斷。")]
    private LayerMask playerLayer;

    [SerializeField]
    [Tooltip("玩家物件 Tag (預設為 'Player')")]
    private string playerTag = "Player";

    [SerializeField]
    [Tooltip("觸碰法線 Y 軸門檻，確認玩家踩在平台上 (法線朝下)")]
    private float topCollisionNormalThreshold = -0.5f;

    [Header("Scene 視覺輔助 (Gizmos)")]
    [SerializeField]
    [Tooltip("是否在 Scene 視窗繪製範圍與路線")]
    private bool drawGizmos = true;

    [SerializeField]
    [Tooltip("頂部重置點顏色")]
    private Color topGizmoColor = Color.cyan;

    [SerializeField]
    [Tooltip("底部重置點顏色")]
    private Color bottomGizmoColor = Color.magenta;

    [SerializeField]
    [Tooltip("中間間隔點標記顏色")]
    private Color spacingGizmoColor = Color.yellow;

    // --- 內部屬性與快取 ---
    private Rigidbody rb;
    private List<VerticalConveyorPlatform> spawnedPlatforms = new List<VerticalConveyorPlatform>();

    public float TopY => topY;
    public float BottomY => bottomY;
    public float MoveSpeed => moveSpeed;

    private static bool _isGeneratingClones = false;

    private void Awake()
    {
        // 快取位置座標
        fixedX = transform.position.x;
        fixedZ = transform.position.z;

        // 若開啟自動生成多個平台，且非克隆生成過程中
        if (autoGenerateClonePlatforms && !_isGeneratingClones && spawnedPlatforms.Count == 0)
        {
            GeneratePlatforms();
        }
        else
        {
            SetupRigidbody();
        }
    }

    private void SetupRigidbody()
    {
        rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
        }
        rb.isKinematic = true;
        rb.useGravity = false;
    }

    private void GeneratePlatforms()
    {
        _isGeneratingClones = true;
        try
        {
            SetupRigidbody();
            spawnedPlatforms.Add(this);

            // 確保單一平台複製品不會再重複生成
            autoGenerateClonePlatforms = false;

        float totalSpan = Mathf.Abs(topY - bottomY);
        int autoCalculatedCount = (spacingY > 0) ? Mathf.CeilToInt(totalSpan / spacingY) : 1;
        
        // 自動補充足夠的平台數量
        if (totalPlatformCount < autoCalculatedCount)
        {
            totalPlatformCount = autoCalculatedCount;
        }

        float startY = (direction == MoveDirection.TopToBottom) ? topY : bottomY;
        float step = (direction == MoveDirection.TopToBottom) ? -spacingY : spacingY;

        // 設定第一個平台位置
        transform.position = new Vector3(fixedX, startY, fixedZ);

        // 複製建立其餘平台 (保持相距 spacingY 單位)
        for (int i = 1; i < totalPlatformCount; i++)
        {
            float targetY = startY + (step * i);
            GameObject clone = Instantiate(gameObject, new Vector3(fixedX, targetY, fixedZ), transform.rotation, transform.parent);
            clone.name = $"{gameObject.name}_{i + 1}";

            var cloneComp = clone.GetComponent<VerticalConveyorPlatform>();
            if (cloneComp != null)
            {
                cloneComp.autoGenerateClonePlatforms = false;
                cloneComp.fixedX = fixedX;
                cloneComp.fixedZ = fixedZ;
                cloneComp.topY = topY;
                cloneComp.bottomY = bottomY;
                cloneComp.moveSpeed = moveSpeed;
                cloneComp.direction = direction;
                cloneComp.playerLayer = playerLayer;
                cloneComp.playerTag = playerTag;
                spawnedPlatforms.Add(cloneComp);
            }
        }
        }
        finally
        {
            _isGeneratingClones = false;
        }
    }

    private void FixedUpdate()
    {
        if (rb == null) return;

        Vector3 currentPos = rb.position;
        float moveDelta = moveSpeed * Time.fixedDeltaTime;

        if (direction == MoveDirection.TopToBottom)
        {
            // 向下移動
            float nextY = currentPos.y - moveDelta;

            // 判斷是否低於底部重置點 (Bottom Y)
            if (nextY <= bottomY)
            {
                // 1. 若玩家站在即將重置的平台上，先解除 Parent 關係，避免玩家被瞬間拉回頂部
                UnparentPlayersOnPlatform();

                // 2. 無縫計算溢出距離並重置回最上方 Top Y
                float overshoot = bottomY - nextY;
                nextY = topY - overshoot;
            }

            rb.MovePosition(new Vector3(fixedX, nextY, fixedZ));
        }
        else
        {
            // 向上移動
            float nextY = currentPos.y + moveDelta;

            // 判斷是否高於頂部重置點 (Top Y)
            if (nextY >= topY)
            {
                UnparentPlayersOnPlatform();

                float overshoot = nextY - topY;
                nextY = bottomY + overshoot;
            }

            rb.MovePosition(new Vector3(fixedX, nextY, fixedZ));
        }
    }

    /// <summary>
    /// 解除任何在該平台上玩家的父物件關聯
    /// </summary>
    private void UnparentPlayersOnPlatform()
    {
        foreach (Transform child in transform)
        {
            if (IsPlayerObject(child.gameObject))
            {
                child.SetParent(null);
            }
        }
    }

    // --- 3D 碰撞跟隨機制 (3D Collision & Trigger) ---

    private void OnCollisionEnter(Collision collision)
    {
        if (IsPlayerObject(collision.gameObject))
        {
            foreach (ContactPoint contact in collision.contacts)
            {
                if (contact.normal.y < topCollisionNormalThreshold)
                {
                    collision.transform.SetParent(transform);
                    break;
                }
            }
        }
    }

    private void OnCollisionExit(Collision collision)
    {
        if (IsPlayerObject(collision.gameObject))
        {
            if (collision.transform.parent == transform)
            {
                collision.transform.SetParent(null);
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (IsPlayerObject(other.gameObject))
        {
            other.transform.SetParent(transform);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (IsPlayerObject(other.gameObject))
        {
            if (other.transform.parent == transform)
            {
                other.transform.SetParent(null);
            }
        }
    }

    private bool IsPlayerObject(GameObject go)
    {
        if (go == null) return false;

        if (playerLayer.value != 0)
        {
            if (((1 << go.layer) & playerLayer.value) != 0)
                return true;
        }

        if (!string.IsNullOrEmpty(playerTag) && go.CompareTag(playerTag))
        {
            return true;
        }

        return false;
    }

    // --- Gizmos 繪製 ---

    private void OnDrawGizmos()
    {
        if (!drawGizmos) return;

        Vector3 topPos = new Vector3(fixedX, topY, fixedZ);
        Vector3 bottomPos = new Vector3(fixedX, bottomY, fixedZ);

        // 繪製移動路線
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(topPos, bottomPos);

        // 繪製頂點與底點
        Gizmos.color = topGizmoColor;
        Gizmos.DrawWireSphere(topPos, 0.4f);
        Gizmos.DrawSphere(topPos, 0.2f);

        Gizmos.color = bottomGizmoColor;
        Gizmos.DrawWireSphere(bottomPos, 0.4f);
        Gizmos.DrawSphere(bottomPos, 0.2f);

        // 繪製間隔預測點
        if (spacingY > 0)
        {
            Gizmos.color = spacingGizmoColor;
            float totalSpan = Mathf.Abs(topY - bottomY);
            int count = Mathf.FloorToInt(totalSpan / spacingY);
            for (int i = 1; i < count; i++)
            {
                float y = (direction == MoveDirection.TopToBottom) ? topY - (spacingY * i) : bottomY + (spacingY * i);
                Gizmos.DrawWireSphere(new Vector3(fixedX, y, fixedZ), 0.2f);
            }
        }
    }
}
