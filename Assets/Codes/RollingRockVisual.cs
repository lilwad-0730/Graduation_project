using UnityEngine;

public class RollingRockVisual : MonoBehaviour
{
    private Rigidbody rb;
    private float radius = 1f;
    private Transform visualTransform;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoAttachToRockNew()
    {
        // 自動尋找場景中名為 "rock-new" 的物件並加上此視覺腳本
        GameObject[] allObjects = GameObject.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
        foreach (var obj in allObjects)
        {
            if (obj.name.ToLower().Contains("rock-new"))
            {
                // 確保有 Rigidbody 與 Collider
                Rigidbody r = obj.GetComponent<Rigidbody>();
                Collider c = obj.GetComponent<Collider>();
                if (r != null && c != null)
                {
                    if (obj.GetComponent<RollingRockVisual>() == null)
                    {
                        obj.AddComponent<RollingRockVisual>();
                        Debug.Log($"[RollingRockVisual] 已在運行時自動附加至 {obj.name}");
                    }
                }
            }
        }
    }

    [Header("物理設定")]
    [Tooltip("巨石的質量 (重量，預設 20f)")]
    public float mass = 20f;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.mass = mass;
        }
        
        // 取得碰撞器以計算真實的世界空間半徑
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            if (col is SphereCollider sphere)
            {
                radius = sphere.radius * Mathf.Max(transform.localScale.x, transform.localScale.y);
            }
            else if (col is BoxCollider box)
            {
                radius = Mathf.Max(box.size.x, box.size.y) * 0.5f * Mathf.Max(transform.localScale.x, transform.localScale.y);
            }
            
            // 自動套用無摩擦力材質，防止摩擦力導致角色升高/抖動
            PhysicsMaterial noFriction = new PhysicsMaterial("RockNoFrictionMaterial");
            noFriction.dynamicFriction = 0f;
            noFriction.staticFriction = 0f;
            noFriction.frictionCombine = PhysicsMaterialCombine.Minimum;
            noFriction.bounciness = 0f;
            noFriction.bounceCombine = PhysicsMaterialCombine.Minimum;
            col.material = noFriction;
        }
        if (radius < 0.01f) radius = 1f;

        // 確保剛體的所有旋轉都被鎖定，防止物理摩擦力導致抖動，改用此腳本完全接管視覺旋轉
        if (rb != null)
        {
            rb.constraints = RigidbodyConstraints.FreezePositionZ | 
                             RigidbodyConstraints.FreezeRotationX | 
                             RigidbodyConstraints.FreezeRotationY | 
                             RigidbodyConstraints.FreezeRotationZ;
        }

        // 建立獨立的視覺子物件，將 SpriteRenderer 移到子物件上旋轉，保持物理碰撞器不旋轉
        SpriteRenderer parentSprite = GetComponent<SpriteRenderer>();
        if (parentSprite != null)
        {
            GameObject visualObj = new GameObject(gameObject.name + "_Visual");
            visualObj.transform.SetParent(transform);
            visualObj.transform.localPosition = Vector3.zero;
            visualObj.transform.localRotation = Quaternion.identity;
            visualObj.transform.localScale = Vector3.one;

            SpriteRenderer childSprite = visualObj.AddComponent<SpriteRenderer>();
            childSprite.sprite = parentSprite.sprite;
            childSprite.color = parentSprite.color;
            childSprite.material = parentSprite.material;
            childSprite.sortingLayerID = parentSprite.sortingLayerID;
            childSprite.sortingLayerName = parentSprite.sortingLayerName;
            childSprite.sortingOrder = parentSprite.sortingOrder;
            childSprite.flipX = parentSprite.flipX;
            childSprite.flipY = parentSprite.flipY;
            childSprite.drawMode = parentSprite.drawMode;
            childSprite.size = parentSprite.size;

            // 停用原本父物件上的 SpriteRenderer，保留組件供其他腳本獲取資訊，但不起動繪製
            parentSprite.enabled = false;

            visualTransform = visualObj.transform;
        }
        else
        {
            visualTransform = transform;
        }
    }

    void Update()
    {
        if (rb == null || visualTransform == null) return;

        // 獲取水平移動速度 (X 軸)
        float speed = rb.linearVelocity.x;

        // 根據線速度與半徑計算旋轉角度：角度變化 = (速度 / 半徑) * 弧度轉角度
        // 乘以 Time.deltaTime 得到此影格的旋轉增量
        float rotationAmount = (speed / radius) * Mathf.Rad2Deg * Time.deltaTime;

        // 僅沿著 Z 軸旋轉視覺子物件（順時針滾動，所以帶負號）
        visualTransform.Rotate(Vector3.forward, -rotationAmount, Space.Self);
    }
}
