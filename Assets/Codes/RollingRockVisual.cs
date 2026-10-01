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
    private float _targetPushSpeedX;

    [Header("★0920 推動的重量感（只作用在水平方向）")]
    [Tooltip("巨石追上玩家推力的加速度 (單位/秒²)。越小越重、起步越慢。\n" +
             "太小會讓玩家一直撞在石頭上，有機會重新引發上坡抖動，調整時請實測上坡。")]
    public float boulderAcceleration = 12f;

    [Tooltip("玩家停止推動或放慢時，巨石收速度的減速度 (單位/秒²)。越小滑得越久、慣性越明顯")]
    public float boulderBraking = 10f;

    [Tooltip("巨石被推動時的水平速度上限 (單位/秒)")]
    public float boulderMaxSpeed = 8f;

    // ───────── ★1001 給相機重量回饋用的唯讀狀態（完全不影響推動邏輯）─────────
    /// <summary>這一刻玩家是不是真的在推這顆巨石（＝FixedUpdate 算出來的 stillPushing）。</summary>
    public bool IsBeingPushedByPlayer { get; private set; }

    /// <summary>目前正在被玩家推動的那顆巨石（沒有就是 null）。相機用這個，避免每幀 Find。</summary>
    public static RollingRockVisual ActivelyPushed { get; private set; }

    /// <summary>正在被推的巨石的水平速度絕對值（沒有就是 0）。</summary>
    public static float ActivelyPushedSpeedAbs =>
        (ActivelyPushed != null && ActivelyPushed.rb != null)
            ? Mathf.Abs(ActivelyPushed.rb.linearVelocity.x) : 0f;

    private void SetPushedState(bool pushed)
    {
        IsBeingPushedByPlayer = pushed;
        if (pushed) ActivelyPushed = this;
        else if (ActivelyPushed == this) ActivelyPushed = null;
    }

    private void OnDisable()
    {
        // static 會跨場景留著，物件被關掉／場景卸載時一定要清掉，否則相機會對著不存在的石頭下沉
        SetPushedState(false);
    }
    // ─────────────────────────────────────────────────────────────────────

    void FixedUpdate()
    {
        if (rb == null || rb.isKinematic || _pusher == null) { SetPushedState(false); return; }
        if (Time.time - _lastPushTime > PushGraceTime) { _pusher = null; SetPushedState(false); return; }
        if (!_pusher.isGrounded) { SetPushedState(false); return; }   // 空中不接續推力

        // 最近還在推：玩家仍往石頭那邊推、而且就在旁邊，巨石才繼續跟，別在剛分開時往回滑
        float input = _pusher.CurrentMoveInput;
        float dirToRock = Mathf.Sign(transform.position.x - _pusher.transform.position.x);
        float gap = Mathf.Abs(transform.position.x - _pusher.transform.position.x) - radius;
        bool stillPushing = Mathf.Abs(input) > 0.05f && Mathf.Sign(input) == dirToRock && gap < 1.5f;
        SetPushedState(stillPushing);   // ★1001 只回報狀態，不改變任何既有判定

        // ★0920 重量感：目標速度不再「當幀直接指定」，改成用加速度／減速度逼近。
        //   玩家停手時巨石會自己滑一小段，起步也要一點時間，質量才有存在感。
        //   垂直速度完全不碰，一律交給重力與地面。
        float target = stillPushing ? _targetPushSpeedX : 0f;
        float current = rb.linearVelocity.x;
        rb.linearVelocity = new Vector3(ApproachPushSpeed(current, target, Time.fixedDeltaTime), rb.linearVelocity.y, 0f);
    }

    /// <summary>朝目標水平速度逼近：加速用 boulderAcceleration，收速度（含反向）用 boulderBraking。</summary>
    private float ApproachPushSpeed(float current, float target, float dt)
    {
        bool speedingUp = Mathf.Abs(target) > Mathf.Abs(current) && (Mathf.Abs(current) < 0.01f || Mathf.Sign(target) == Mathf.Sign(current));
        float rate = speedingUp ? boulderAcceleration : boulderBraking;
        if (rate <= 0f) rate = 10f;
        return Mathf.MoveTowards(current, target, rate * dt);
    }

    /// <summary>
    /// 玩家這一刻想把巨石推到多快（水平，帶正負號）。只是「目標」，實際速度由加速度逼近。
    /// ★0919 只取玩家的水平速度，垂直永遠交給重力：0916 那版連垂直一起抄，玩家一跳巨石就跟著飛。
    /// </summary>
    private float GetPushTargetSpeedX(PlayerMovement pm, Rigidbody playerRb)
    {
        // ★0920 fallback 也要照坡度折算。
        //   玩家在斜坡上是「沿著坡面」走，baseSpeed 6.0 指的是坡面上的速度，
        //   換算成世界水平只有 6 × cos(35°) ≈ 4.91。原本 fallback 直接拿 6.0，
        //   一旦觸發，巨石的目標就比玩家實際水平速度快約 1.1 m/s：
        //   石頭衝到前面 → gap > 1.5 判定沒在推 → 煞車 → 無摩擦往回滑 → 撞回玩家，
        //   正是 0916 上坡抖動的那個迴圈。改成用玩家腳下坡度折算後才當備援值。
        // ★1001 fallback 改用 pm.CommandedHorizontalSpeed（玩家這一幀實際寫進剛體的世界水平速度）。
        //
        // Root Cause：原本是 pm.CurrentMoveInput × pm.BaseSpeed × cos(坡度)。BaseSpeed 固定 6，
        // 完全不理「咬住幾隻狼的等比減速」也不理「狼的沿坡向下拖曳」。
        // 於是只要玩家被石頭頂住、物理把她的實際 vx 吃到 0.1 以下，fallback 就會把巨石的目標
        // 拉回「沒有狼、滿速」的 4.94，巨石突然加速衝開 → gap > 1.5 判定沒在推 → 煞車 →
        // 零摩擦在 34.6° 坡上往回滑 → 撞回玩家，正是 0916 上坡抖動的那個迴圈。
        // 咬 3 隻狼時落差可達 4.0 倍（玩家 1.23 vs fallback 4.94）。
        //
        // 為什麼不用 pm.currentSpeed：那只含「咬住幾隻狼」的等比減速，不含拖曳、不含坡度折算、不含風。
        // CommandedHorizontalSpeed 是 PlayerMovement 自己算完所有因素後寫進剛體的那個值，
        // 語意上就是「她現在真的推得出多快」，而且已經是世界水平方向，不需要再乘 cos。
        float fallbackX = pm.CommandedHorizontalSpeed;

        // 防呆：若該值因故為 0（例如這一幀還沒跑過 Update），退回原本的算法，不要讓巨石整個停住
        if (Mathf.Abs(fallbackX) < 0.0001f)
        {
            fallbackX = pm.CurrentMoveInput * pm.BaseSpeed * GetPlayerSlopeHorizontalFactor(pm);
        }

        float vx = playerRb != null ? playerRb.linearVelocity.x : fallbackX;
        // 玩家被石頭頂住時物理可能把她的速度吃掉，這時退回她的指令速度，免得兩邊都停住推不動
        if (Mathf.Abs(vx) < 0.1f) vx = fallbackX;
        return Mathf.Clamp(vx, -boulderMaxSpeed, boulderMaxSpeed);
    }

    /// <summary>
    /// 玩家腳下坡度把「沿坡速度」折成「世界水平速度」的係數（平地或離地＝1；35° 坡＝0.819）。
    /// 角度範圍跟 PlayerMovement 判定「正在斜坡上走」的條件一致（0.5°～60°），超過就當平地模式。
    /// </summary>
    private static float GetPlayerSlopeHorizontalFactor(PlayerMovement pm)
    {
        if (pm == null || !pm.isGrounded) return 1f;
        float a = pm.GroundSlopeAngle;
        if (a <= 0.5f || a >= 60f) return 1f;
        return Mathf.Cos(a * Mathf.Deg2Rad);
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
                    // ★0920 這裡只登記「玩家想把石頭推到多快」，實際速度統一在 FixedUpdate 用加速度逼近。
                    //   原本在這裡直接指定速度，等於每幀瞬間同步，石頭才會完全沒有重量。
                    Rigidbody playerRb = collision.rigidbody != null ? collision.rigidbody : pm.GetComponent<Rigidbody>();
                    _pusher = pm;
                    _pusherRb = playerRb;
                    _targetPushSpeedX = GetPushTargetSpeedX(pm, playerRb);
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
