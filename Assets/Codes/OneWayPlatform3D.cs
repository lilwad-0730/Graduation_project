using UnityEngine;

[RequireComponent(typeof(Collider))]
public class OneWayPlatform3D : MonoBehaviour
{
    private Collider platformCollider;
    private PlayerMovement playerMovement;
    private Collider playerCollider;
    private Rigidbody playerRb;

    [Header("物理設定")]
    [Tooltip("樓梯/平台的 Layer，用於射線偵測。如果不設定，會自動使用此物件本身的 Layer。")]
    public LayerMask platformLayer;

    [Tooltip("容錯高度，允許腳底低於平台表面多少以內仍判定為站立（預設 0.15）")]
    public float surfaceTolerance = 0.15f;

    void Start()
    {
        platformCollider = GetComponent<Collider>();
        
        // 尋找場景中的玩家
        playerMovement = FindFirstObjectByType<PlayerMovement>();
        if (playerMovement != null)
        {
            playerCollider = playerMovement.GetComponent<Collider>();
            playerRb = playerMovement.GetComponent<Rigidbody>();
        }
        else
        {
            Debug.LogWarning("[OneWayPlatform3D] 找不到 PlayerMovement，請確認場景中有玩家物件！");
        }
        
        // 若未指定 LayerMask，自動使用此物件所在的 Layer
        if (platformLayer == 0)
        {
            platformLayer = 1 << gameObject.layer;
        }
    }

    void Update()
    {
        if (playerCollider == null || platformCollider == null || playerRb == null) return;

        // 1. 取得玩家腳底的 Y 座標
        float playerFeetY = playerCollider.bounds.min.y;
        
        // 2. 從玩家中心點發射一條向下的射線，精確探測平台表面高度
        Vector3 rayStart = playerCollider.bounds.center;
        RaycastHit hit;
        
        // 射線長度設為玩家高度的一倍半，確保在跳躍頂點準備落下時能掃描到平台
        float rayDistance = playerCollider.bounds.size.y * 1.5f;

        if (Physics.Raycast(rayStart, Vector3.down, out hit, rayDistance, platformLayer))
        {
            // 確保掃描到的是「自己這個平台」的碰撞體
            if (hit.collider == platformCollider)
            {
                float platformSurfaceY = hit.point.y;

                // 啟用碰撞的黃金條件：
                // A. 玩家腳底的 Y 軸高於（或等於）射線打到的平台表面（加上容錯值）
                // B. 玩家此時沒有明顯的向上運動速度（即：處於站立、水平移動或下落狀態，速度 <= 0.05f）
                if (playerFeetY >= platformSurfaceY - surfaceTolerance && playerRb.linearVelocity.y <= 0.05f)
                {
                    // 恢復碰撞關係（實體化）
                    Physics.IgnoreCollision(playerCollider, platformCollider, false);
                    return;
                }
            }
        }

        // 其他所有情況（例如：玩家在平台下方、正在跳躍上升中、或者已經離開平台範圍）：
        // 忽略碰撞關係（虛無化），讓玩家可以自由穿透
        Physics.IgnoreCollision(playerCollider, platformCollider, true);
    }
}
