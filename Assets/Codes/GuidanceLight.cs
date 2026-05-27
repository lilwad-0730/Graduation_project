using UnityEngine;
using System.Collections;

public class GuidanceLight : MonoBehaviour
{
    [Header("目標設定")]
    [Tooltip("玩家物件 (程式會自動透過 Tag 尋找)")]
    public Transform player;
    [Tooltip("精靈要飛過的路徑點 (請在場景建立多個空物件，並拉進這個陣列中)")]
    public Transform[] waypoints;

    [Header("飛行屬性")]
    [Tooltip("精靈飛行的速度")]
    public float moveSpeed = 4f;
    [Tooltip("距離路徑點多近算抵達？")]
    public float waypointThreshold = 0.5f;

    [Header("等待玩家設定 (預設追逐模式)")]
    [Tooltip("玩家距離超過多少時，精靈停下等待？")]
    public float stopDistance = 12f;
    [Tooltip("精靈停下後，玩家靠近到多少範圍內才繼續飛？")]
    public float resumeDistance = 6f;

    [Header("敘事等待模式設定 (Waypoint_WaitPlayer)")]
    [Tooltip("在此模式下，玩家要靠近到多少距離內，光絮才會飛往下一個點？(通常比追逐模式的距離更近)")]
    public float waitPlayerTriggerDistance = 3f;

    [Header("敘事鎖定延遲 (增加演出感)")]
    [Tooltip("觸發後，光絮在原地停留幾秒鐘才起飛？")]
    public float flyDelay = 0.5f;
    [Tooltip("光絮抵達下一個點後，額外凍結玩家幾秒鐘才放行？")]
    public float unlockDelay = 0.5f;

    [Header("動畫效果 (呼吸浮動)")]
    [Tooltip("上下浮動的幅度")]
    public float bobHeight = 0.3f;
    [Tooltip("上下浮動的速度")]
    public float bobSpeed = 3f;

    private int currentWaypointIndex = 0;
    private bool isWaitingForPlayerCatchup = false;
    private Vector3 logicPosition; 
    private bool isLockingPlayer = false; // 是否正在鎖定玩家看動畫

    void Start()
    {
        logicPosition = transform.position;
        if (player == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) player = p.transform;
        }
    }

    void Update()
    {
        if (waypoints == null || waypoints.Length == 0 || player == null) return;
        
        PlayerMovement pm = player.GetComponent<PlayerMovement>();

        // 規則 4：如果正在演出「飛往下一個點」的劇情鎖定狀態，Update 只負責上下浮動
        if (isLockingPlayer)
        {
            ApplyBobbing();
            return;
        }

        // 如果已經抵達最後一個點，就在原地浮動
        if (currentWaypointIndex >= waypoints.Length)
        {
            ApplyBobbing();
            return;
        }

        Transform currentWP = waypoints[currentWaypointIndex];
        string wpTag = currentWP.tag;
        
        float distToPlayer = Vector3.Distance(logicPosition, player.position);
        float distToWaypoint = Vector3.Distance(logicPosition, currentWP.position);

        // 規則 3：玩家必須真正碰到光絮 (極短距離)，且光絮不跑
        if (wpTag == "Waypoint_Touch")
        {
            if (distToWaypoint > waypointThreshold)
            {
                FlyTowards(currentWP.position); // 先飛到這個點
            }
            else if (distToPlayer <= 1.5f) // 等玩家真正碰到
            {
                AdvanceWaypoint(pm, true);
            }
        }
        // 規則 2：允許玩家靠近 (喘氣/敘事)，光絮停在此點不跑
        else if (wpTag == "Waypoint_WaitPlayer")
        {
            if (distToWaypoint > waypointThreshold)
            {
                FlyTowards(currentWP.position); // 先飛到這個點
            }
            else if (distToPlayer <= waitPlayerTriggerDistance) // 使用專屬的等待距離！
            {
                AdvanceWaypoint(pm, true);
            }
        }
        // 規則 1：預設模式 (跟玩家保持距離，跑給玩家追)
        else 
        {
            if (!isWaitingForPlayerCatchup && distToPlayer > stopDistance)
            {
                isWaitingForPlayerCatchup = true;
            }
            else if (isWaitingForPlayerCatchup && distToPlayer <= resumeDistance)
            {
                isWaitingForPlayerCatchup = false;
            }

            if (!isWaitingForPlayerCatchup)
            {
                FlyTowards(currentWP.position);
                if (distToWaypoint < waypointThreshold)
                {
                    AdvanceWaypoint(pm, false); // 預設模式不鎖定玩家，讓玩家可以邊追邊跑
                }
            }
        }

        ApplyBobbing();
    }

    private void FlyTowards(Vector3 targetPos)
    {
        logicPosition = Vector3.MoveTowards(logicPosition, targetPos, moveSpeed * Time.deltaTime);
    }

    private void AdvanceWaypoint(PlayerMovement pm, bool freezePlayer)
    {
        if (freezePlayer && pm != null && currentWaypointIndex + 1 < waypoints.Length)
        {
            StartCoroutine(CutsceneFlightSequence(pm));
        }
        else
        {
            currentWaypointIndex++;
        }
    }

    private IEnumerator CutsceneFlightSequence(PlayerMovement pm)
    {
        // 1. 立即停止玩家行動
        pm.isCutsceneFrozen = true;
        isLockingPlayer = true; 

        // 2. 停頓一下 (讓玩家感覺到「觸發了」某件事)
        yield return new WaitForSeconds(flyDelay);

        // 3. 切換目標點
        currentWaypointIndex++;
        Transform nextWP = waypoints[currentWaypointIndex];

        // 4. 開始飛行
        while (Vector3.Distance(logicPosition, nextWP.position) > waypointThreshold)
        {
            FlyTowards(nextWP.position);
            yield return null; // 等待下一幀
        }

        // 5. 到下一個路徑點停頓一下 (讓玩家視角跟上、喘口氣)
        yield return new WaitForSeconds(unlockDelay);

        // 6. 解凍玩家
        pm.isCutsceneFrozen = false;
        isLockingPlayer = false;
    }

    private void ApplyBobbing()
    {
        float newY = logicPosition.y + Mathf.Sin(Time.time * bobSpeed) * bobHeight;
        transform.position = new Vector3(logicPosition.x, newY, logicPosition.z);
    }
}
