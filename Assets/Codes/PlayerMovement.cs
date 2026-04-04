using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class PlayerMovement : MonoBehaviour
{
    [Header("基本移動設定")]
    public float baseSpeed = 5f;       
    public float pullRange = 2f;
    
    private Rigidbody rb;
    private GameObject pulledObject;
    private Vector3 facingDirection = Vector3.right; 

    [Header("狼群減速狀態 (可調整)")]
    [Tooltip("幾隻狼能讓玩家完全停下？(建議設低一點才明顯)")]
    public float maxWolvesToStop = 3f; // 【修改】改成 3 隻就完全停下，效果會超明顯！
    
    [Header("觀察用 (不要手動改)")]
    public int attachedWolvesCount = 0; 
    public float currentSpeed;          

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        currentSpeed = baseSpeed;
    }

    void Update()
    {
        float moveInput = Input.GetAxis("Horizontal"); 
        if (moveInput > 0.1f) facingDirection = Vector3.right;
        if (moveInput < -0.1f) facingDirection = Vector3.left;

        if (Input.GetKeyDown(KeyCode.LeftShift)) TryGrabObject();
        if (Input.GetKeyUp(KeyCode.LeftShift)) ReleaseObject();

        float finalSpeed = (pulledObject != null) ? currentSpeed / 2f : currentSpeed;
        rb.linearVelocity = new Vector3(moveInput * finalSpeed, rb.linearVelocity.y, rb.linearVelocity.z);

        if (Input.GetKeyDown(KeyCode.Space) && Mathf.Abs(rb.linearVelocity.y) < 0.05f)
        {
            rb.AddForce(Vector3.up * 5f, ForceMode.Impulse);
        }
    }

    // ==========================================
    // 狼群減速邏輯
    // ==========================================
    public void AddWolf()
    {
        attachedWolvesCount++;
        CalculateSpeed();
        Debug.Log($"狼咬！目前身上有 {attachedWolvesCount} 隻狼，玩家速度降為：{currentSpeed}");
    }

    public void RemoveWolf()
    {
        attachedWolvesCount--;
        if (attachedWolvesCount < 0) attachedWolvesCount = 0; 
        CalculateSpeed();
        Debug.Log($"狼鬆口！目前身上有 {attachedWolvesCount} 隻狼，玩家速度恢復為：{currentSpeed}");
    }

    private void CalculateSpeed()
    {
        float speedPenaltyPerWolf = baseSpeed / maxWolvesToStop;
        float newSpeed = baseSpeed - (speedPenaltyPerWolf * attachedWolvesCount);
        currentSpeed = Mathf.Max(0f, newSpeed); // 最低就是 0，不會倒退
    }

    // ==========================================
    // 抓取物件邏輯 (維持不變)
    // ==========================================
    void TryGrabObject()
    {
        Debug.DrawRay(transform.position, facingDirection * pullRange, Color.red, 2f);
        RaycastHit[] hits = Physics.RaycastAll(transform.position, facingDirection, pullRange);
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider.gameObject == this.gameObject) continue;
            if (hit.collider.CompareTag("Pushable"))
            {
                pulledObject = hit.collider.gameObject;
                pulledObject.transform.SetParent(this.transform);
                pulledObject.GetComponent<Rigidbody>().isKinematic = true;
                return; 
            }
        }
    }

    void ReleaseObject()
    {
        if (pulledObject != null)
        {
            pulledObject.transform.SetParent(null);
            pulledObject.GetComponent<Rigidbody>().isKinematic = false;
            pulledObject = null;
        }
    }
}