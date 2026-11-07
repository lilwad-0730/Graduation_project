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

    [Tooltip("★1107 已經站在平台上時，向上速度要超過這個值才算「起跳」而開始穿透（小於它視為落地推擠）")]
    public float jumpVelocity = 2f;

    private bool _solid;

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

        // ★1107 站上去會掉下去的三個原因，一起處理：
        //   (1) 只從玩家正中心打一條射線：站在台階邊緣、中心已經踩空時，判成「不在平台上」→ 關碰撞 → 掉下去。改成左／中／右三條。
        //   (2) 射線被別的碰撞體先擋住就判失敗 → 改用 RaycastAll，忽略觸發區，只找自己這個平台。
        //   (3) 站在上面時，剛落地／走上台階的物理推擠會讓向上速度冒出 0.05 以上 → 以前這一格就把碰撞關掉、人整個沉下去。
        //       現在「已經站在上面」時，向上速度要超過 jumpVelocity（真的起跳）才放開。
        Bounds pb = playerCollider.bounds;
        float playerFeetY = pb.min.y;
        float rayDistance = pb.size.y * 1.5f;   // 射線長度設為玩家高度的一倍半，確保在跳躍頂點準備落下時能掃描到平台
        float inset = Mathf.Min(0.05f, pb.extents.x * 0.2f);
        float[] xs = { pb.center.x, pb.min.x + inset, pb.max.x - inset };
        float platformSurfaceY = 0f;
        bool found = false;
        for (int i = 0; i < xs.Length; i++)
        {
            // ★1107 真正的根因：台階的碰撞盒只有 0.06 米厚（Z 在 0），玩家的 Z 在 -0.92，
            //   從玩家中心往下打的射線永遠從台階「前面」擦過去打不到 → 一律判成「不在平台上」→ 關碰撞 → 站不住。
            //   所以射線的 Z 要對準平台自己的 Z。
            Vector3 rayStart = new Vector3(xs[i], pb.center.y, platformCollider.bounds.center.z);
            RaycastHit[] hits = Physics.RaycastAll(rayStart, Vector3.down, rayDistance, platformLayer, QueryTriggerInteraction.Ignore);
            for (int k = 0; k < hits.Length; k++)
            {
                if (hits[k].collider != platformCollider) continue;
                if (!found || hits[k].point.y > platformSurfaceY) platformSurfaceY = hits[k].point.y;
                found = true;
            }
        }

        if (found)
        {
            float vy = playerRb.linearVelocity.y;
            bool feetOnTop = playerFeetY >= platformSurfaceY - surfaceTolerance;
            // 啟用碰撞：腳底在平台表面之上，而且沒有在往上衝（站立、走動、下落）；
            // 已經站在上面時，容許小於 jumpVelocity 的向上速度（落地推擠），真的跳起來才穿透
            if (feetOnTop && (vy <= 0.05f || (_solid && vy < jumpVelocity)))
            {
                _solid = true;
                Physics.IgnoreCollision(playerCollider, platformCollider, false);
                return;
            }
        }

        // 其他所有情況（玩家在平台下方、正在跳躍上升中、已離開平台範圍）：忽略碰撞，讓玩家可以穿透
        _solid = false;
        Physics.IgnoreCollision(playerCollider, platformCollider, true);
    }
}
