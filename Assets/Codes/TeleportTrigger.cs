using UnityEngine;

/// <summary>
/// 轉場階梯傳送觸發組件 (Teleport Trigger)
/// 1. 踩到階梯觸發傳送至上層城堡 (Teleport_Target)
/// 2. 自動強化 2.5D 深度，保證 100% 絕對觸發，不再因 Z 軸誤差而漏踩失敗
/// 3. 【防跳過/防逃課規則】：若玩家跳得太高太遠，水平 X 超過階梯且未觸發傳送，自動判定墜落並重生回最近重生點！
/// </summary>
public class TeleportTrigger : MonoBehaviour
{
    [Header("🎯 傳送設定")]
    [Tooltip("傳送的目的地 Transform (可以在目標位置放一個空 GameObject 作為錨點)")]
    public Transform destination;

    [Tooltip("傳送時是否將玩家速度歸零，避免帶著原本跑跳的慣性衝出平台")]
    public bool resetVelocity = true;

    [Tooltip("是否同時將玩家的「重生安全點」更新到目的地？(建議勾選，否則玩家如果在上面死掉會掉回最下面的起點)")]
    public bool updateRespawnPoint = true;

    [Header("🛡️ 防跳過 / 防越界重生判定 (Fail-Safe)")]
    [Tooltip("是否啟用防跳過規則：若玩家 X 軸超過階梯位置且未踩到階梯觸發傳送，自動判定為墜落虛空並重生")]
    public bool enableBypassDeathCheck = true;

    [Tooltip("防跳過警戒線的 X 座標偏移 (相對於階梯 Transform.position.x)。設為 0.5 代表主角 X 軸一旦越過階梯中心右方 0.5 米即觸發重生)")]
    public float bypassOffsetX = 0.5f;

    [Tooltip("防跳過檢查的 Y 軸高度範圍 (向下 15 米、向上 60 米均受保護，防止大跳繞過)")]
    public float checkYDown = 15f;
    public float checkYUp = 60f;

    [Header("✨ 光絮 (GuidanceLight) 聯動設定")]
    [Tooltip("要聯動的光絮 (留空的話，程式會自動在場景中尋找)")]
    public GuidanceLight guidanceLight;

    [Tooltip("傳送後，光絮要切換到哪一個路徑點 (Waypoint) 的索引？設為 -1 代表自動搜尋最靠近傳送點的 Waypoint")]
    public int targetWaypointIndex = -1;

    private bool _isTeleporting = false;
    private PlayerMovement _cachedPlayer;
    private Collider _triggerCollider;

    private void Awake()
    {
        EnsureColliderSetup();
    }

    private void Start()
    {
        EnsureColliderSetup();
        FindPlayer();
    }

    private void EnsureColliderSetup()
    {
        _triggerCollider = GetComponent<Collider>();
        if (_triggerCollider != null)
        {
            _triggerCollider.isTrigger = true; // 強制開啟 Trigger
            if (_triggerCollider is BoxCollider box)
            {
                // ★ 關鍵防護：自動給予 30 米世界 Z 軸厚度，杜絕 2.5D 視角中因 Z 軸微小誤差導致傳送失敗！
                float lossyZ = transform.lossyScale.z != 0f ? Mathf.Abs(transform.lossyScale.z) : 1f;
                Vector3 size = box.size;
                size.z = Mathf.Max(size.z, 30f / lossyZ);
                box.size = size;

                Vector3 center = box.center;
                center.z = 0f;
                box.center = center;
            }
        }

        Collider2D col2d = GetComponent<Collider2D>();
        if (col2d != null) col2d.isTrigger = true;
    }

    private void FindPlayer()
    {
        if (_cachedPlayer == null)
        {
            GameObject p = GameObject.FindWithTag("Player");
            if (p == null) p = GameObject.Find("Player");
            if (p != null) _cachedPlayer = p.GetComponent<PlayerMovement>() ?? p.GetComponentInChildren<PlayerMovement>() ?? p.GetComponentInParent<PlayerMovement>();
            if (_cachedPlayer == null) _cachedPlayer = Object.FindFirstObjectByType<PlayerMovement>();
        }
    }

    private void Update()
    {
        // -------------------------------------------------------------
        // 【防跳過階梯/越界規則 (Fail-Safe)】
        // 玩家如果跳得又高又遠，直接從空中飛過階梯上方/右側而沒有踩到傳送階梯，
        // 一旦 X 軸越過階梯防線，立刻判定墜落死亡，強制重生回最近存檔點！
        // -------------------------------------------------------------
        if (enableBypassDeathCheck && !_isTeleporting)
        {
            if (_cachedPlayer == null) FindPlayer();
            if (_cachedPlayer != null)
            {
                Vector3 playerPos = _cachedPlayer.transform.position;

                // 檢查是否在當前階梯的高度區間內 (向上 60米、向下 15米)
                float stairY = transform.position.y;
                if (playerPos.y >= (stairY - checkYDown) && playerPos.y <= (stairY + checkYUp))
                {
                    // 計算防跳過警戒線 X (以階梯 Transform.position.x 為基準)
                    float failSafeLineX = transform.position.x + bypassOffsetX;

                    // 若玩家 X 軸已經超過階梯防線，且未處於傳送過渡中
                    if (playerPos.x > failSafeLineX)
                    {
                        PlayerRespawnSystem respawnSystem = _cachedPlayer.GetComponent<PlayerRespawnSystem>();
                        if (respawnSystem == null) respawnSystem = _cachedPlayer.GetComponentInParent<PlayerRespawnSystem>();
                        if (respawnSystem == null) respawnSystem = Object.FindFirstObjectByType<PlayerRespawnSystem>();

                        if (respawnSystem != null && !respawnSystem.IsRespawning)
                        {
                            Debug.LogWarning($"⚠️【階梯防越界判定】玩家 (X:{playerPos.x:F2}, Y:{playerPos.y:F2}) 越過了階梯防線 (X:{failSafeLineX:F2}) 且未踩中階梯！立即重生回最近存檔點！");
                            respawnSystem.TriggerRespawn();
                        }
                    }
                }
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        HandleTeleport(other.gameObject);
    }

    private void OnTriggerStay(Collider other)
    {
        HandleTeleport(other.gameObject);
    }

    private void OnCollisionEnter(Collision collision)
    {
        HandleTeleport(collision.gameObject);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        HandleTeleport(other.gameObject);
    }

    private void HandleTeleport(GameObject targetObj)
    {
        if (_isTeleporting || targetObj == null) return;

        // 偵測碰撞到的是否為玩家 (先找 PlayerMovement)
        PlayerMovement player = targetObj.GetComponent<PlayerMovement>();
        if (player == null) player = targetObj.GetComponentInParent<PlayerMovement>();
        if (player == null) player = targetObj.GetComponentInChildren<PlayerMovement>();

        if (player == null && (targetObj.CompareTag("Player") || targetObj.name.Contains("Player")))
        {
            player = Object.FindFirstObjectByType<PlayerMovement>();
        }

        // 執行傳送
        if (player != null && destination != null)
        {
            _isTeleporting = true;
            Debug.Log($"✨【階梯傳送】主角已踩到轉場階梯 '{name}'！開始傳送至 '{destination.name}' ({destination.position})");

            // 處理光絮 (FairyLight / GuidanceLight) 聯動傳送
            GuidanceLight targetLight = guidanceLight;
            if (targetLight == null)
            {
                targetLight = Object.FindFirstObjectByType<GuidanceLight>();
            }

            if (targetLight != null)
            {
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
                if (updateRespawnPoint)
                {
                    respawnSystem.SetSafeGroundPosition(destination.position);
                }

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

    // 在 Unity Scene 視窗畫出傳送連線與防越界警戒線
    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.6f);
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            Gizmos.DrawWireCube(col.bounds.center, col.bounds.size);
        }

        // 畫出防跳過警戒線 (紅色)
        if (enableBypassDeathCheck)
        {
            Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.85f);
            float lineX = transform.position.x + bypassOffsetX;
            Vector3 top = new Vector3(lineX, transform.position.y + checkYUp, transform.position.z);
            Vector3 bot = new Vector3(lineX, transform.position.y - checkYDown, transform.position.z);
            Gizmos.DrawLine(top, bot);

            #if UNITY_EDITOR
            UnityEditor.Handles.Label(new Vector3(lineX, transform.position.y + 2f, transform.position.z), "⛔ 防跳過警戒線 (越過且未踩階梯即重生)");
            #endif
        }

        if (destination != null)
        {
            Gizmos.color = new Color(0.3f, 1f, 0.5f, 0.9f);
            Gizmos.DrawLine(transform.position, destination.position);
            Gizmos.DrawWireSphere(destination.position, 0.8f);

            #if UNITY_EDITOR
            UnityEditor.Handles.Label(transform.position + Vector3.up * 1f, "🪜 階梯傳送起點 (Stair Trigger)");
            UnityEditor.Handles.Label(destination.position + Vector3.up * 1f, "🏰 上層城堡目的地 (Castle Destination)");
            #endif
        }
    }
}
