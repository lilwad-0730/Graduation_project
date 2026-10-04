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

        UpdateSlopePushVisualPull();
    }

    // ───────── ★1004 上坡推石的視覺間隙補償（只動外觀，物理完全不碰）─────────
    // Root Cause（Editor.log「【推石間距】」實測）：
    //   平地：石頭碰到玩家碰撞框的右側面，往下 0.72，看起來貼著身體。
    //   35° 坡：石頭碰到的是碰撞框「頂部右上角」（往下 −0.01），而且推進方向（法線 0.86,0.51）幾乎就是順著坡面
    //   直直頂進那個角。石頭是半徑 2.7 的圓，碰撞框是 2.13×3.43 的直立長方形，這個幾何下石頭只會停在
    //   「離玩家頭頂角一個半徑」的位置，身體跟石頭之間就留下一大塊斜坡空隙。碰撞框跟坡面平行與否無關，
    //   也不是碰撞框位置錯，是圓碰角的必然結果，改 Collider 只會讓平地也跟著變。
    // 修法：跟玩家的「斜坡視覺空隙補償」同一個做法——石頭外觀沿著坡面往玩家方向滑一小段。
    //   沿坡面平移不會讓石頭陷進或浮離坡面；碰撞體不動，所以推石手感、加速、煞車、撞狼全部不變。
    [Header("★1004 上坡推石視覺補償")]
    [Tooltip("在斜坡上被推時，石頭外觀沿坡面往玩家方向滑多遠 (公尺)，35° 以上達到滿值。0＝關閉。\n" +
             "建議 0.4～1.0；超過 1.2 石頭外觀會明顯蓋進玩家身上")]
    [Range(0f, 2f)]
    public float slopePushVisualPull = 0.7f;

    [Tooltip("補償量追上目標的平滑速度")]
    public float slopePushVisualSmooth = 8f;

    private Vector3 _visualPullWorld = Vector3.zero;

    private void UpdateSlopePushVisualPull()
    {
        if (visualTransform == null || visualTransform == transform) return;

        Vector3 target = Vector3.zero;
        if (slopePushVisualPull > 0.001f && !IsRollingBack && _pusher != null && IsBeingPushedByPlayer
            && TryGetGroundNormal(out Vector3 n))
        {
            float angle = Vector3.Angle(Vector3.up, n);
            if (angle > 5f)
            {
                // 沿坡面、朝玩家那一側（她在下坡側）
                Vector3 tangent = new Vector3(n.y, -n.x, 0f).normalized;
                float towardPlayer = Mathf.Sign(_pusher.transform.position.x - transform.position.x);
                if (Mathf.Sign(tangent.x) != towardPlayer) tangent = -tangent;
                target = tangent * slopePushVisualPull * Mathf.Clamp01(angle / 35f);
            }
        }

        float t = 1f - Mathf.Exp(-Mathf.Max(0.1f, slopePushVisualSmooth) * Time.deltaTime);
        _visualPullWorld = Vector3.Lerp(_visualPullWorld, target, t);
        visualTransform.localPosition = transform.InverseTransformVector(_visualPullWorld);
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
        IsRollingBack = false;
    }
    // ─────────────────────────────────────────────────────────────────────

    // ───────── ★1002 失敗下滾（Rollback State）─────────
    // 狼咬到人＝這次挑戰失敗。BoulderChallengeController 呼叫 BeginRollback() 後，石頭在 State 層級接管：
    //   ・FixedUpdate 不再跑「玩家推石」那一套（加速度逼近玩家速度），改成沿目前坡面往下逼近 rollbackSpeed
    //   ・OnCollisionStay 不再登記推石者，玩家按前進也推不回去（沒有拔河）
    // 不碰 boulderAcceleration／boulderBraking／boulderMaxSpeed，也不用 AddForce，所以正常推石完全沒變。
    [Header("★1002 失敗下滾（狼咬失敗時才會用到）")]
    [Tooltip("下滾時沿坡面的目標速度 (單位/秒)")]
    public float rollbackSpeed = 7f;

    [Tooltip("下滾時往目標速度加速的加速度 (單位/秒²)")]
    public float rollbackAcceleration = 14f;

    [Tooltip("地面法線與水平的夾角小於這個角度，才算「滾到平面了」(度)")]
    public float rollbackFlatAngle = 5f;

    [Tooltip("必須連續維持在平面這麼久 (秒) 才算真的到平面，避免坡面接縫的法線單幀抖動就提早結束")]
    public float rollbackFlatStableSeconds = 0.35f;

    [Tooltip("下滾至少要移動這麼遠 (公尺) 才允許結束（Fail Exit 也一樣），避免一開始就重置")]
    public float rollbackMinDistance = 4f;

    [Tooltip("到平面後石頭減速到停下的減速度 (單位/秒²)")]
    public float rollbackFlatBrake = 8f;

    /// <summary>是否正在失敗下滾。</summary>
    public bool IsRollingBack { get; private set; }

    /// <summary>下滾已經滾進平面（連續穩定、且移動夠遠）。控制器靠這個決定何時重置。</summary>
    public bool RollbackReachedFlat { get; private set; }

    /// <summary>下滾開始後石頭已經移動的距離 (公尺)。</summary>
    public float RollbackDistance => IsRollingBack || _rollbackStartSet ? Vector3.Distance(transform.position, _rollbackStartPos) : 0f;

    private float _rollbackFallbackDirX = -1f;
    private readonly RaycastHit[] _slopeHitBuf = new RaycastHit[8];
    private Vector3 _rollbackStartPos;
    private bool _rollbackStartSet;
    private float _rollbackFlatTimer;
    private Rigidbody _rollbackPlayerRb;
    private float _rollbackPlayerContactTime = -999f;
    private float _nextRollbackLog = 0f;

    /// <summary>
    /// 開始失敗下滾。playerTransform 只用來決定「平地或空中時往哪邊滾」的備援方向：
    /// 玩家把石頭往坡上推，所以她在坡下，朝她的方向就是下坡。
    /// </summary>
    public void BeginRollback(Transform playerTransform)
    {
        if (rb == null) rb = GetComponent<Rigidbody>();
        IsRollingBack = true;
        _pusher = null;
        _pusherRb = null;
        _lastPushTime = -1f;
        SetPushedState(false);

        float dir = 0f;
        if (playerTransform != null) dir = Mathf.Sign(playerTransform.position.x - transform.position.x);
        if (Mathf.Approximately(dir, 0f) && rb != null) dir = -Mathf.Sign(rb.linearVelocity.x);
        _rollbackFallbackDirX = Mathf.Approximately(dir, 0f) ? -1f : dir;

        _rollbackStartPos = transform.position;
        _rollbackStartSet = true;
        _rollbackFlatTimer = 0f;
        RollbackReachedFlat = false;
        _rollbackPlayerContactTime = -999f;
        _rollbackPlayerRb = null;
        if (playerTransform != null)
        {
            _rollbackPlayerRb = playerTransform.GetComponent<Rigidbody>();
            if (_rollbackPlayerRb == null) _rollbackPlayerRb = playerTransform.GetComponentInParent<Rigidbody>();
        }
    }

    public void EndRollback()
    {
        IsRollingBack = false;
        RollbackReachedFlat = false;
        _rollbackStartSet = false;
        _rollbackPlayerRb = null;
    }

    /// <summary>
    /// 目前腳下坡面的「下坡方向」（XY 平面單位向量）。從石頭中心往下打射線取地面法線，
    /// 切線 t = (n.y, -n.x)，取 y 為負的那一邊。平地、空中（打不到地面）回傳 false。
    /// 地面判定排除自己、玩家、狼與 Trigger，跟推石邏輯用同一批排除對象。
    /// </summary>
    private bool TryGetDownhillDirection(out Vector3 dir)
    {
        dir = Vector3.zero;
        if (!TryGetGroundNormal(out Vector3 normal)) return false;

        Vector3 t = new Vector3(normal.y, -normal.x, 0f);
        if (t.y > 0f) t = -t;
        if (t.sqrMagnitude < 0.0001f || Mathf.Abs(t.y) < 0.02f) return false;   // 幾乎平的
        dir = t.normalized;
        return true;
    }

    /// <summary>石頭正下方的地面法線（排除自己、玩家、狼、Trigger）。打不到地面（空中）回傳 false。</summary>
    private bool TryGetGroundNormal(out Vector3 normal)
    {
        normal = Vector3.up;
        int n = Physics.RaycastNonAlloc(transform.position, Vector3.down, _slopeHitBuf, radius + 1.0f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        bool found = false;
        for (int i = 0; i < n; i++)
        {
            Collider c = _slopeHitBuf[i].collider;
            if (c == null || c.transform == transform || c.transform.IsChildOf(transform)) continue;
            if (c.CompareTag("Player") || c.GetComponentInParent<PlayerMovement>() != null) continue;
            if (c.GetComponentInParent<WolfEnemy>() != null) continue;
            if (_slopeHitBuf[i].distance < best)
            {
                best = _slopeHitBuf[i].distance;
                normal = new Vector3(_slopeHitBuf[i].normal.x, _slopeHitBuf[i].normal.y, 0f).normalized;
                found = true;
            }
        }
        return found;
    }

    private void RollbackStep()
    {
        if (rb == null || rb.isKinematic) return;

        Vector3 v = rb.linearVelocity;
        float dt = Time.fixedDeltaTime;
        Vector3 newV;

        // ── 判斷「滾到平面了」：地面法線夾角夠小、連續穩定、而且已經滾夠遠。單幀法線抖動不算 ──
        bool groundFound = TryGetGroundNormal(out Vector3 groundNormal);
        float groundAngle = groundFound ? Vector3.Angle(Vector3.up, groundNormal) : 90f;
        if (groundFound && groundAngle <= rollbackFlatAngle) _rollbackFlatTimer += dt;
        else _rollbackFlatTimer = 0f;
        if (!RollbackReachedFlat && _rollbackFlatTimer >= rollbackFlatStableSeconds && RollbackDistance >= rollbackMinDistance)
        {
            RollbackReachedFlat = true;
        }

        if (RollbackReachedFlat)
        {
            // 到平面了：水平減速到停（垂直交給重力）
            float nextX = Mathf.MoveTowards(v.x, 0f, Mathf.Max(0.1f, rollbackFlatBrake) * dt);
            newV = new Vector3(nextX, v.y, 0f);
        }
        else if (TryGetDownhillDirection(out Vector3 downhill))
        {
            // 在坡上：整個速度向量（含 y）貼著坡面往下逼近，才不會飛離坡面
            Vector3 target = downhill * rollbackSpeed;
            Vector3 next = Vector3.MoveTowards(new Vector3(v.x, v.y, 0f), target, rollbackAcceleration * dt);
            newV = new Vector3(next.x, next.y, 0f);
        }
        else
        {
            // 平地或空中：只動水平，垂直交給重力
            float nextX = Mathf.MoveTowards(v.x, _rollbackFallbackDirX * rollbackSpeed, rollbackAcceleration * dt);
            newV = new Vector3(nextX, v.y, 0f);
        }
        rb.linearVelocity = newV;

        if (Application.isEditor && Time.time >= _nextRollbackLog)
        {
            _nextRollbackLog = Time.time + 0.4f;
            Vector3 pp = _rollbackPlayerRb != null ? _rollbackPlayerRb.position : Vector3.zero;
            Vector3 pv = _rollbackPlayerRb != null ? _rollbackPlayerRb.linearVelocity : Vector3.zero;
            Debug.Log($"【下滾診斷】石頭 ({transform.position.x:F2},{transform.position.y:F2}) v=({newV.x:F2},{newV.y:F2}) | " +
                      $"玩家 ({pp.x:F2},{pp.y:F2}) v=({pv.x:F2},{pv.y:F2}) | 接觸距今 {Time.time - _rollbackPlayerContactTime:F2}s | " +
                      $"地面夾角 {(groundFound ? groundAngle.ToString("F1") : "無")}° 平面計時 {_rollbackFlatTimer:F2} 已滾 {RollbackDistance:F1}m 到平面={RollbackReachedFlat}");
        }

        // ── 帶著玩家一起走 ──
        // Root Cause（實機 log：玩家整段下滾只位移約 5 公尺、速度 0.3～1.5 m/s，最後靠逾時重置）：
        //   玩家每幀被 PlayerMovement 重寫速度，被押的速度是「石頭水平速度投影到坡面」＝石頭速度的 0.82 倍
        //   （水平分量只剩 0.67 倍）。石頭比玩家快，每幀都撞上她，而玩家質量 10、石頭 300 卻被她的速度覆寫擋住，
        //   接觸求解把石頭拉回她的速度 → 石頭 = 0.67 × 石頭 → 速度收斂到接近 0，看起來像「被鎖住然後重生」。
        // 修法：接觸中的玩家速度直接等於石頭速度，兩者沒有相對速度，就不會互相拖慢。
        //   只在 FixedUpdate、物理步進前寫入；玩家自己的 Update 之後怎麼改，下一個物理步又會被蓋回來。
        if (_rollbackPlayerRb != null && !_rollbackPlayerRb.isKinematic && Time.time - _rollbackPlayerContactTime < 0.25f)
        {
            // ★1004 坡腳凹角：順坡向下的速度會把玩家的底角直接頂進平地地板，卡在夾角。
            //   往前掃一小段，前面有地形（不是石頭、不是 Trigger）就把速度沿接觸面滑開：
            //   碰到平地 → 變成純水平，順勢被石頭押著沿地板往前；沒有障礙時完全等於原本的 newV。
            Vector3 carry = newV;
            Vector3 dir3 = new Vector3(carry.x, carry.y, 0f);
            float dist = dir3.magnitude * Time.fixedDeltaTime * 2f + 0.08f;
            if (dir3.sqrMagnitude > 0.01f
                && _rollbackPlayerRb.SweepTest(dir3.normalized, out RaycastHit hit, dist, QueryTriggerInteraction.Ignore)
                && hit.collider != null
                && hit.collider.attachedRigidbody != rb
                && !hit.collider.transform.IsChildOf(transform)
                && hit.collider.GetComponentInParent<WolfEnemy>() == null
                && Vector3.Dot(hit.normal, dir3) < 0f)
            {
                carry = Vector3.ProjectOnPlane(dir3, new Vector3(hit.normal.x, hit.normal.y, 0f).normalized);
                carry.z = 0f;
                // 滑開後不能比石頭水平速度還慢，不然石頭又追上來擠她
                if (Mathf.Abs(carry.x) < Mathf.Abs(newV.x) * 0.9f) carry.x = newV.x;
            }
            _rollbackPlayerRb.linearVelocity = carry;
        }
    }
    // ─────────────────────────────────────────────────────────────────────

    void FixedUpdate()
    {
        if (IsRollingBack) { RollbackStep(); return; }   // ★1002 失敗下滾：整段接管，不跑玩家推石邏輯
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

    // ───────── ★1002 玩家↔巨石距離診斷（只印 Log，不改任何數值）─────────
    // 山坡上「人跟石頭看起來隔很遠」還沒有實測證據，所以先不動 Collider。
    // 勾起來後在平地與山坡各推一小段，把 Console 裡的「【推石間距】」貼回來，就能看出接觸點在玩家 Collider 的哪個位置。
    [Header("★1002 診斷")]
    [Tooltip("勾選＝推石時每 0.5 秒印一行：玩家與巨石的 Collider 範圍、中心水平距離、實際接觸點、坡度")]
    public bool debugPushGapLog = false;
    private float _nextPushGapLog = 0f;

    private void LogPushGap(Collision collision, PlayerMovement pm)
    {
        if (Time.time < _nextPushGapLog) return;
        _nextPushGapLog = Time.time + 0.5f;

        Collider pc = collision.collider;
        Bounds pb = pc != null ? pc.bounds : new Bounds();
        Vector3 contact = collision.contactCount > 0 ? collision.GetContact(0).point : Vector3.zero;
        Vector3 normal = collision.contactCount > 0 ? collision.GetContact(0).normal : Vector3.zero;
        SpriteRenderer psr = pm.GetComponentInChildren<SpriteRenderer>();
        string visual = psr != null ? $"玩家外觀範圍 x[{psr.bounds.min.x:F2},{psr.bounds.max.x:F2}] y[{psr.bounds.min.y:F2},{psr.bounds.max.y:F2}]" : "玩家外觀 無";
        float dx = transform.position.x - pm.transform.position.x;
        float dxBounds = transform.position.x - pb.center.x;
        Debug.Log($"【推石間距】坡度 {pm.GroundSlopeAngle:F1}° | 玩家 pivot x={pm.transform.position.x:F2} y={pm.transform.position.y:F2} | " +
                  $"玩家Collider 中心 ({pb.center.x:F2},{pb.center.y:F2}) 寬 {pb.size.x:F2} 高 {pb.size.y:F2} | " +
                  $"巨石 中心 ({transform.position.x:F2},{transform.position.y:F2}) 半徑 {radius:F2} | " +
                  $"中心水平距 pivot {dx:F2} / Collider {dxBounds:F2} | gap(判定用) {Mathf.Abs(dx) - radius:F2} | " +
                  $"接觸點 ({contact.x:F2},{contact.y:F2}) 相對玩家Collider頂部 {(contact.y - pb.max.y):F2} 右緣 {(contact.x - pb.max.x):F2} | 接觸法線 ({normal.x:F2},{normal.y:F2}) | Collider範圍 x[{pb.min.x:F2},{pb.max.x:F2}] y[{pb.min.y:F2},{pb.max.y:F2}] | {visual}");
    }

    private void OnCollisionStay(Collision collision)
    {
        if (collision.gameObject.CompareTag("Player") || collision.gameObject.GetComponentInParent<PlayerMovement>() != null)
        {
            PlayerMovement pm = collision.gameObject.GetComponent<PlayerMovement>();
            if (pm == null) pm = collision.gameObject.GetComponentInParent<PlayerMovement>();
            // 編輯器內推石時一律記錄（0.5 秒一行，只在玩家按著前進時），不需要先去 Inspector 勾；打包後不會有
            if ((debugPushGapLog || Application.isEditor) && pm != null && !IsRollingBack && Mathf.Abs(pm.CurrentMoveInput) > 0.05f) LogPushGap(collision, pm);
            if (pm != null && rb != null && !rb.isKinematic)
            {
                // ★1002 失敗下滾中：不登記推石者（玩家推不回去），只把石頭的水平速度傳給玩家，押著她下坡
                if (IsRollingBack)
                {
                    _rollbackPlayerContactTime = Time.time;
                    pm.ApplyExternalSlopePush(new Vector3(rb.linearVelocity.x, 0f, 0f));
                    return;
                }

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
