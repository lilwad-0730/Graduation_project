using UnityEngine;

public class GuidanceLight : MonoBehaviour
{
    [Header("目標設定")]
    [Tooltip("玩家物件 (程式會自動透過 Tag 尋找，也可以手動拖曳)")]
    public Transform player;
    [Tooltip("精靈要飛過的路徑點 (請在場景建立多個空物件，並拉進這個陣列中)")]
    public Transform[] waypoints;

    [Header("飛行屬性")]
    [Tooltip("精靈飛行的速度")]
    public float moveSpeed = 4f;
    [Tooltip("距離路徑點多近算抵達？")]
    public float waypointThreshold = 0.5f;

    [Header("等待玩家設定")]
    [Tooltip("玩家距離超過多少時，精靈停下等待？")]
    public float stopDistance = 12f;
    [Tooltip("精靈停下後，玩家靠近到多少範圍內才繼續飛？")]
    public float resumeDistance = 6f;

    [Header("動畫效果 (呼吸浮動)")]
    [Tooltip("上下浮動的幅度")]
    public float bobHeight = 0.3f;
    [Tooltip("上下浮動的速度")]
    public float bobSpeed = 3f;

    private int currentWaypointIndex = 0;
    private bool isWaiting = false;
    private Vector3 logicPosition; // 記錄不受浮動影響的真實邏輯位置

    void Start()
    {
        logicPosition = transform.position;

        // 如果沒有手動設定玩家，就自動找場景裡的 Player
        if (player == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) player = p.transform;
        }
    }

    void Update()
    {
        if (waypoints == null || waypoints.Length == 0 || player == null) return;
        
        // 如果已經抵達最後一個點，就在原地浮動
        if (currentWaypointIndex >= waypoints.Length)
        {
            ApplyBobbing();
            return;
        }

        // 計算真實邏輯座標與玩家的距離
        float distToPlayer = Vector3.Distance(logicPosition, player.position);

        // 判斷是否需要等待 (脫隊太遠)
        if (!isWaiting && distToPlayer > stopDistance)
        {
            isWaiting = true;
        }
        // 判斷玩家是否趕上了
        else if (isWaiting && distToPlayer <= resumeDistance)
        {
            isWaiting = false;
        }

        // 如果沒有在等玩家，就往前飛
        if (!isWaiting)
        {
            Vector3 targetPos = waypoints[currentWaypointIndex].position;
            
            // 使用 MoveTowards 來達到平滑的等速飛行
            logicPosition = Vector3.MoveTowards(logicPosition, targetPos, moveSpeed * Time.deltaTime);

            // 如果已經很靠近這個目標點了，就切換到下一個目標
            if (Vector3.Distance(logicPosition, targetPos) < waypointThreshold)
            {
                currentWaypointIndex++;
            }
        }

        // 每一幀都會執行浮動運算，讓它看起來是有生命的
        ApplyBobbing();
    }

    private void ApplyBobbing()
    {
        // 使用 Sin 函數，在真實 Y 座標上加上平滑的上下波動值
        float newY = logicPosition.y + Mathf.Sin(Time.time * bobSpeed) * bobHeight;
        transform.position = new Vector3(logicPosition.x, newY, logicPosition.z);
    }
}
