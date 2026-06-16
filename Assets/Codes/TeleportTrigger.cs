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

    private void OnTriggerEnter(Collider other)
    {
        // 偵測碰撞到的是否為玩家 (先找 PlayerMovement)
        PlayerMovement player = other.GetComponent<PlayerMovement>();
        if (player == null)
        {
            // 防呆：有時候碰撞體在子物件，往父物件尋找
            player = other.GetComponentInParent<PlayerMovement>();
        }

        // 執行傳送
        if (player != null && destination != null)
        {
            PlayerRespawnSystem respawnSystem = player.GetComponent<PlayerRespawnSystem>();
            if (respawnSystem == null)
            {
                respawnSystem = player.GetComponentInParent<PlayerRespawnSystem>();
            }

            if (respawnSystem != null)
            {
                // 使用帶有轉場畫面過渡與相機對齊的傳送系統
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
