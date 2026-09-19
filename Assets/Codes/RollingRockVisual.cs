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

    [Header("物理與音效設定")]
    [Tooltip("巨石的質量 (重量，預設 20f)")]
    public float mass = 20f;
    [Tooltip("巨石砸落地面時的撞擊音效 (例如 落石2)")]
    public AudioClip impactSFX;
    [Range(0f, 1f)] public float impactVolume = 0.9f;

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
            rb.interpolation = RigidbodyInterpolation.Interpolate; // 與主角內插同步，消除物理刷新率不一致抖動
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous; // 連續碰撞防止穿透與碰撞回彈
            rb.maxDepenetrationVelocity = 2.0f; // ★ 消除斜坡/平地夾角被強力彈開反彈引發的劇烈抖動
            rb.solverIterations = 16;
            rb.solverVelocityIterations = 16;
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

    private void OnCollisionEnter(Collision collision)
    {
        if (impactSFX != null && collision.relativeVelocity.magnitude > 2.0f)
        {
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlaySFXAt(impactSFX, collision.contacts[0].point, impactVolume);
            }
            else
            {
                AudioSource.PlayClipAtPoint(impactSFX, collision.contacts[0].point, AudioManager.ScaleSfx(impactVolume));
            }
        }
    }

    // ★0916 上坡推石抖動：短暫分開時的緩衝
    //   原本巨石一被碰到就設成「完整基本速度」的水平速度，但玩家在坡上是沿坡面走，
    //   水平速度只有 基本速度 × cos(坡度)，巨石比玩家快 → 衝開 → 沒人推、無摩擦往回滑 → 撞回玩家 → 再衝開，
    //   每秒循環好幾次就是抖動（平地 cos0=1 剛好相等，所以只有上坡會抖）。
    //   改成巨石直接照玩家「實際速度」走（含上坡的 Y），並在剛分開的一小段時間內繼續跟著，不讓它先滑回來。
    private const float PushGraceTime = 0.15f;
    private float _lastPushTime = -1f;
    private PlayerMovement _pusher;
    private Rigidbody _pusherRb;

    void FixedUpdate()
    {
        if (rb == null || rb.isKinematic || _pusher == null) return;
        if (Time.time - _lastPushTime > PushGraceTime) { _pusher = null; return; }

        // 最近還在推：玩家仍往石頭那邊推、而且就在旁邊，巨石先跟著玩家走，別往回滑
        // （還碰著的話 OnCollisionStay 會在這之後再設一次同樣的速度）
        if (!_pusher.isGrounded) return;   // 空中不接續推力
        float input = _pusher.CurrentMoveInput;
        float dirToRock = Mathf.Sign(transform.position.x - _pusher.transform.position.x);
        float gap = Mathf.Abs(transform.position.x - _pusher.transform.position.x) - radius;
        if (Mathf.Abs(input) > 0.05f && Mathf.Sign(input) == dirToRock && gap < 1.5f)
        {
            rb.linearVelocity = GetPushVelocity(_pusher, _pusherRb);
        }
    }

    /// <summary>
    /// 巨石跟著玩家走的速度。
    /// ★0919 只取玩家的「水平」速度，垂直永遠保留巨石自己的（重力／地面）。
    ///   0916 那版連玩家的垂直速度一起抄過來，玩家一跳，巨石就跟著被賦予向上的速度飛起來——
    ///   那是我上一版造成的，不是原本就有的行為。水平同步就足以解決上坡抖動，垂直本來就該交給重力。
    /// </summary>
    private Vector3 GetPushVelocity(PlayerMovement pm, Rigidbody playerRb)
    {
        float fallbackX = pm.CurrentMoveInput * pm.BaseSpeed;
        if (playerRb == null) return new Vector3(fallbackX, rb.linearVelocity.y, 0f);

        float vx = playerRb.linearVelocity.x;
        // 玩家被石頭頂住時物理可能把她的速度吃掉，這時退回原本的推力，免得兩邊都停住推不動
        if (Mathf.Abs(vx) < 0.1f) return new Vector3(fallbackX, rb.linearVelocity.y, 0f);
        return new Vector3(vx, rb.linearVelocity.y, 0f);
    }

    private void OnCollisionStay(Collision collision)
    {
        if (collision.gameObject.CompareTag("Player") || collision.gameObject.GetComponentInParent<PlayerMovement>() != null)
        {
            PlayerMovement pm = collision.gameObject.GetComponent<PlayerMovement>();
            if (pm == null) pm = collision.gameObject.GetComponentInParent<PlayerMovement>();
            if (pm != null && rb != null && !rb.isKinematic)
            {
                // ★0919 人在空中就不算在推：跳起來、從石頭上跳開時，巨石不該跟著動
                if (Mathf.Abs(pm.CurrentMoveInput) > 0.05f && pm.isGrounded)
                {
                    // ★ 玩家主動推石頭：巨石照玩家實際速度同步前進（上坡含 Y），不再比玩家快而衝開
                    Rigidbody playerRb = collision.rigidbody != null ? collision.rigidbody : pm.GetComponent<Rigidbody>();
                    rb.linearVelocity = GetPushVelocity(pm, playerRb);
                    _pusher = pm;
                    _pusherRb = playerRb;
                    _lastPushTime = Time.time;
                }
                else
                {
                    // 玩家停步/被撞擊時：將巨石水平滾動速度傳遞給主角，讓主角在平地或斜坡夾角順暢滑開，化解互頂死鎖震顫
                    pm.ApplyExternalSlopePush(new Vector3(rb.linearVelocity.x, 0f, 0f));
                }
            }
        }
    }
}
