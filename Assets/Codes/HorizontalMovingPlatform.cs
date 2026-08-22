using UnityEngine;

/// <summary>
/// 控制平台在固定 Y 軸上，於指定 X 軸範圍 (如 22.36 ~ 46.0) 來回自動平滑移動。
/// 計算即時移動速度 Velocity，供 PlayerMovement 讀取達成 100% 完美的跟隨滾動與動態防滑落！
/// </summary>
public class HorizontalMovingPlatform : MonoBehaviour, IResettable
{
    [Header("X 軸移動範圍")]
    [Tooltip("移動的最小 X 座標 (例如 22.36)")]
    public float minX = 22.36f;

    [Tooltip("移動的最大 X 座標 (例如 46.0)")]
    public float maxX = 46.0f;

    [Header("時間與頻率")]
    [Tooltip("來回一次完整循環所需時間 (秒)")]
    public float cycleDuration = 6.0f;

    [Tooltip("固定 Y 軸座標 (預設自動使用 Awake 時的位置 Y)")]
    public float fixedY = 0f;

    [Tooltip("是否自動平滑漸進 (SmoothStep 緩入緩出)")]
    public bool smoothMovement = true;

    [Header("玩家防滑落機制 (由 PlayerMovement 剛體速度直接接管)")]
    [Tooltip("若開啟，將玩家設為子物件 (預設關閉：PlayerMovement 已原生依據 Velocity 平滑帶動，避免 2 倍速滑動與縮放變形)")]
    public bool parentPlayerOnRide = false;

    /// <summary>
    /// 平台目前在世界座標下的即時移動速度向量
    /// </summary>
    public Vector3 Velocity { get; private set; }

    private float elapsedTime = 0f;
    private Vector3 initialPosition;
    private Vector3 lastPosition;
    private Rigidbody rb;

    private void Awake()
    {
        initialPosition = transform.position;
        lastPosition = initialPosition;
        if (Mathf.Approximately(fixedY, 0f))
        {
            fixedY = initialPosition.y;
        }

        rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
        }
    }

    private void FixedUpdate()
    {
        if (cycleDuration <= 0f) return;

        elapsedTime += Time.fixedDeltaTime;

        // 計算 0 ~ 1 來回 PingPong 數值
        float pingPong = Mathf.PingPong(elapsedTime * (2.0f / cycleDuration), 1.0f);

        // 如果勾選平滑移動，使用 SmoothStep 達成無縫緩入緩出
        float t = smoothMovement ? Mathf.SmoothStep(0f, 1f, pingPong) : pingPong;

        // 計算目標 X 座標
        float targetX = Mathf.Lerp(minX, maxX, t);
        Vector3 targetPos = new Vector3(targetX, fixedY, transform.position.z);

        // 計算即時速度向量
        if (Time.fixedDeltaTime > 0)
        {
            Velocity = (targetPos - transform.position) / Time.fixedDeltaTime;
        }

        if (rb != null)
        {
            rb.MovePosition(targetPos);
        }
        else
        {
            transform.position = targetPos;
        }

        lastPosition = targetPos;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (parentPlayerOnRide && collision.gameObject.CompareTag("Player"))
        {
            if (collision.contacts.Length > 0 && collision.contacts[0].normal.y < -0.3f)
            {
                collision.transform.SetParent(transform);
            }
        }
    }

    private void OnCollisionExit(Collision collision)
    {
        if (parentPlayerOnRide && collision.gameObject.CompareTag("Player"))
        {
            if (collision.transform.parent == transform)
            {
                collision.transform.SetParent(null);
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (parentPlayerOnRide && other.CompareTag("Player"))
        {
            other.transform.SetParent(transform);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (parentPlayerOnRide && other.CompareTag("Player"))
        {
            if (other.transform.parent == transform)
            {
                other.transform.SetParent(null);
            }
        }
    }

    public void ResetToInitialState()
    {
        elapsedTime = 0f;
        Velocity = Vector3.zero;
        if (rb != null)
        {
            rb.position = initialPosition;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        transform.position = initialPosition;
        lastPosition = initialPosition;

        foreach (Transform child in transform)
        {
            if (child.CompareTag("Player"))
            {
                child.SetParent(null);
            }
        }
    }
}
