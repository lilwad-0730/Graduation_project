using UnityEngine;

[DefaultExecutionOrder(-100)]
public class CameraTargetXFollower : MonoBehaviour
{
    [Tooltip("Target transform to follow horizontally (usually Player)")]
    public Transform targetToFollow;

    [Tooltip("Fixed Y position for the camera target")]
    public float fixedY = 5.29f;

    [Header("單向滾動與邊界鎖定 (One-Way Camera Scrolling)")]
    [Tooltip("開啟後，鏡頭只能向右推進，無法向左返回")]
    public bool lockLeftMovement = true;

    [Tooltip("開啟後，同時防止玩家往左走出螢幕左邊界")]
    public bool clampPlayerLeftEdge = true;

    [Tooltip("玩家相對於鏡頭中心允許移動的最左邊邊界距離 (預設 25 單位，貼合螢幕左側)")]
    public float playerLeftEdgeOffset = 25f;

    private float maxTargetX = -999999f;
    private bool isInitialized = false;

    void Start()
    {
        FindTarget();
        ResetMaxX();
    }

    void OnEnable()
    {
        ResetMaxX();
    }

    public void ResetMaxX()
    {
        FindTarget();
        if (targetToFollow != null)
        {
            maxTargetX = targetToFollow.position.x;
            isInitialized = true;
        }
        else
        {
            isInitialized = false;
        }
    }

    void FindTarget()
    {
        if (targetToFollow == null)
        {
            GameObject player = GameObject.FindWithTag("Player");
            if (player == null) player = GameObject.Find("Player");
            if (player != null) targetToFollow = player.transform;
        }
    }

    void Update()
    {
        UpdatePosition();
    }

    void LateUpdate()
    {
        UpdatePosition();
    }

    void UpdatePosition()
    {
        if (targetToFollow == null)
        {
            FindTarget();
            if (targetToFollow == null) return;
        }

        if (!isInitialized || maxTargetX < -900000f)
        {
            maxTargetX = targetToFollow.position.x;
            isInitialized = true;
        }

        Vector3 pos = transform.position;

        if (lockLeftMovement)
        {
            // 只能記錄並更新更右邊的 X 座標，絕不倒退
            if (targetToFollow.position.x > maxTargetX)
            {
                maxTargetX = targetToFollow.position.x;
            }
            pos.x = maxTargetX;

            // 限制玩家無法超越螢幕左邊界
            if (clampPlayerLeftEdge)
            {
                float minPlayerX = maxTargetX - playerLeftEdgeOffset;
                if (targetToFollow.position.x < minPlayerX)
                {
                    Vector3 playerPos = targetToFollow.position;
                    playerPos.x = minPlayerX;
                    targetToFollow.position = playerPos;

                    // 若有 Rigidbody 且正在向左移動，阻斷向左速度
                    Rigidbody rb = targetToFollow.GetComponent<Rigidbody>();
                    if (rb != null && rb.linearVelocity.x < 0f)
                    {
                        rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, rb.linearVelocity.z);
                    }
                }
            }
        }
        else
        {
            pos.x = targetToFollow.position.x;
        }

        pos.y = fixedY;
        pos.z = 0f;
        transform.position = pos;
    }
}
