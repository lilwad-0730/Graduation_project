using UnityEngine;


/// <summary>
/// 追蹤 Animator 驅動的平台速度，供 PlayerMovement 跟隨平台並避免滑落。
/// 本元件不負責日常位移；僅在重生時將平台送回起點。
/// </summary>
public class HorizontalMovingPlatform : MonoBehaviour, IResettable
{
    [Header("玩家防滑落機制")]
    [Tooltip("若開啟，玩家站上平台時會成為平台子物件。")]
    public bool parentPlayerOnRide = false;

    public Vector3 Velocity { get; private set; }

    private readonly Vector3 initialPosition = new Vector3(46f, -38.5499992f, 0f);
    private Vector3 lastPosition;
    private Rigidbody rb;
    private Animator animator;

    private void Awake()
    {
        lastPosition = transform.position;
        rb = GetComponent<Rigidbody>();
        animator = GetComponent<Animator>();
        if (rb != null)
        {
            rb.isKinematic = true;
        }
    }

    private void FixedUpdate()
    {
        Vector3 currentPosition = transform.position;
        Velocity = Time.fixedDeltaTime > 0f
            ? (currentPosition - lastPosition) / Time.fixedDeltaTime
            : Vector3.zero;
        lastPosition = currentPosition;
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
        if (parentPlayerOnRide && collision.gameObject.CompareTag("Player") &&
            collision.transform.parent == transform)
        {
            collision.transform.SetParent(null);
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
        if (parentPlayerOnRide && other.CompareTag("Player") &&
            other.transform.parent == transform)
        {
            other.transform.SetParent(null);
        }
    }

    public void ResetToInitialState()
    {
        foreach (Transform child in transform)
        {
            if (child.CompareTag("Player"))
            {
                child.SetParent(null);
            }
        }

        if (animator != null)
        {
            animator.enabled = false;
            animator.Rebind();
        }

        Velocity = Vector3.zero;
        transform.position = initialPosition;
        if (rb != null)
        {
            rb.position = initialPosition;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        lastPosition = initialPosition;
    }
}
