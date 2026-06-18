using UnityEngine;

public class TeleportTrigger : MonoBehaviour
{
    [Header("傳送設定")]
    [Tooltip("傳送的目的地 Transform (可以在目標位置放一個空 GameObject 作為錨點)")]
    public Transform destination;

    [Tooltip("傳送時是否將玩家速度歸零，避免帶著原本跑跳的慣性衝出平台")]
    public bool resetVelocity = true;

    [Tooltip("是否同時將玩家的「重生安全點」更新到目的地？(建議勾選，否則玩家如果在上面死掉會掉回最下面的起點)")]
    public bool updateRespawnPoint = true;

    [Header("光絮 (GuidanceLight) 聯動設定")]
    [Tooltip("要聯動的光絮 (留空的話，程式會自動在場景中尋找)")]
    public GuidanceLight guidanceLight;

    [Tooltip("傳送後，光絮要切換到哪一個路徑點 (Waypoint) 的索引？設為 -1 代表自動搜尋最靠近傳送點的 Waypoint")]
    public int targetWaypointIndex = -1;

    private void OnTriggerEnter(Collider other)
    {
        HandleTeleport(other.gameObject);
    }

    private void OnCollisionEnter(Collision collision)
    {
        HandleTeleport(collision.gameObject);
    }

    private void HandleTeleport(GameObject targetObj)
    {
        // 偵測碰撞到的是否為玩家 (先找 PlayerMovement)
        PlayerMovement player = targetObj.GetComponent<PlayerMovement>();
        if (player == null)
        {
            // 防呆：有時候碰撞體在子物件，往父物件尋找
            player = targetObj.GetComponentInParent<PlayerMovement>();
        }

        // 執行傳送
        if (player != null && destination != null)
        {
            // 處理光絮 (FairyLight / GuidanceLight) 聯動傳送
            GuidanceLight targetLight = guidanceLight;
            if (targetLight == null)
            {
                targetLight = FindFirstObjectByType<GuidanceLight>();
            }

            if (targetLight != null)
            {
                // 將光絮傳送到目的地玩家頭頂上方 (y + 1.5)，並更新它的下一個 Waypoint 索引
                Vector3 lightDest = destination.position + Vector3.up * 1.5f;
                targetLight.TeleportLight(lightDest, targetWaypointIndex);
            }

            PlayerRespawnSystem respawnSystem = player.GetComponent<PlayerRespawnSystem>();
            if (respawnSystem == null)
            {
                respawnSystem = player.GetComponentInParent<PlayerRespawnSystem>();
            }

            if (respawnSystem != null)
            {
                // 使用帶有轉場畫面過渡與相機對齊的傳送系統 (會自動鎖定玩家不能動)
                respawnSystem.TriggerTeleport(destination.position);
            }
            else
            {
                // 備用降級方案：如果沒掛載重生系統，進行無轉場的直接傳送
                player.WarpTo(destination.position);

                if (resetVelocity)
                {
                    Rigidbody rb = player.GetComponent<Rigidbody>();
                    if (rb != null)
                    {
                        rb.linearVelocity = Vector3.zero;
                        rb.angularVelocity = Vector3.zero;
                    }
                }
            }
        }
    }
}
