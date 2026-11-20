using UnityEngine;

/// <summary>
/// ★1116 玻璃館「鏡箱」：玻璃牆前的箱子，推到牆邊當墊腳石才翻得過牆。
///
/// 操作照巨石的規則（PlayerMovement ★1116 鏡箱）：
///   ・按住 Shift、面向箱子、貼著它＝抓住：往箱子走是推、往後退是拉（拉比推慢）。放開 Shift 或起跳＝放手。
///   ・沒按 Shift 走過去只會被擋住，推不動。
/// 抓住時箱子變 kinematic、每個物理步跟著她（保持抓住時的水平距離；不掛到她身上——她用剛體速度移動，掛在底下的剛體不會跟著走）；
///   kinematic 自己不會被牆擋，所以由 IsBlockedToward 告訴她「箱子前面有東西」，她就停下來。
/// 推拉的速度由質量決定（她的速度 × 10 ÷ (10 + 質量)）：質量 10＝推約 3 m/s、拉約 2 m/s。
///
/// ★1116 不浮空：箱子一律 kinematic（避免空中被頂走），所以重力由這支自己做——
///   底下沒地板（被推出邊緣、底下的玻璃被怪物打碎）就往下掉、落到地板上；掉太深（掉進坑裡拿不回來）就回到原位。
///   「站在地上」的高度＝場景裡擺的高度（記下碰撞框底部離地多少），所以不會把手動擺好的位置拉走。
/// ★1116 提示：不用畫面下方的字，改用線條畫（LineHintDirector 放在箱子上方的 Hints/Glass_CratePush_light：SHIFT＋D 推箱子到牆邊、踩上去翻牆）。
/// 重生時放手、回到原位。
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class MirrorCrate : MonoBehaviour, IResettable
{
    [Tooltip("箱子質量。推拉速度＝她的速度 × 10 ÷ (10 + 質量)：10＝推約 3 m/s、拉約 2 m/s；越重越慢")]
    public float crateMass = 10f;
    [Tooltip("抓著箱子時，箱子前面離障礙物多近就算撞到（公尺）")]
    public float blockCheckDistance = 0.12f;

    [Header("★1116 不浮空")]
    [Tooltip("往下掉的重力加速度")]
    public float fallGravity = 25f;
    [Tooltip("箱子中間多寬的範圍底下有地板才算站得住（0～1，箱子寬度的比例）：0.4＝中間 40%，箱子中心推出邊緣一點就會掉")]
    [Range(0.1f, 1f)] public float supportWidth = 0.4f;
    [Tooltip("掉超過幾公尺還沒落地＝掉進坑裡了，回到原位")]
    public float lostFallDistance = 8f;

    private Rigidbody _rb;
    private Collider _col;
    private SpriteRenderer _sr;
    private Vector3 _home;
    private readonly RaycastHit[] _hits = new RaycastHit[8];
    private readonly RaycastHit[] _downHits = new RaycastHit[8];
    private PlayerMovement _holder;
    private float _holdOffsetX;

    private float _groundGap;      // 場景裡擺好時，碰撞框底部離地多高（負的＝陷進地板）
    private bool _falling;
    private float _fallSpeed;
    private float _fallStartY;
    private bool _hidden;          // 掉進坑、原位的地板也碎了：先藏起來，重生時再出現

    /// <summary>正在往下掉、或被藏起來的時候抓不住。</summary>
    public bool CanBeHeld => !_falling && !_hidden;
    public bool IsHeld => _holder != null;

    /// <summary>被她抓住：記下跟她的水平距離，之後每個物理步跟著她（kinematic）。</summary>
    public void BeginHold(PlayerMovement holder)
    {
        _holder = holder;
        _holdOffsetX = transform.position.x - holder.transform.position.x;
        if (_rb == null) _rb = GetComponent<Rigidbody>();
        _rb.isKinematic = true;
    }

    /// <summary>放手：停在原地（維持 kinematic，碰到也不會被頂走）。底下沒地板的話，下一個物理步就開始往下掉。</summary>
    public void EndHold()
    {
        _holder = null;
        if (_rb == null) _rb = GetComponent<Rigidbody>();
        _rb.isKinematic = true;
    }

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _col = GetComponent<Collider>();
        _sr = GetComponent<SpriteRenderer>();
        _rb.useGravity = false;
        _rb.mass = Mathf.Max(0.1f, crateMass);
        _rb.linearDamping = 10f;
        _rb.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionZ;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        // ★1116 沒被抓著的時候一律 kinematic：只有「按住 Shift、站在地上抓住」才動得了。
        //   Root Cause（M 11/16：「玩家在懸空時推得動物件」）：放手後箱子是一般剛體，她跳起來或掉下來撞到箱子側面，物理就把箱子頂走了。
        // ★1116 位置完全照場景擺的（不往下貼地）：M 11/16「手動調整位置，測試時就被調回原本的位置」。
        _rb.isKinematic = true;
        _home = transform.position;
    }

    private void Start()
    {
        // 記下場景擺放時離地多高：之後「落地」就落回這個高度，跟手動擺的一樣
        float gap = GroundGap(3f);
        _groundGap = float.IsInfinity(gap) ? 0f : gap;
        if (float.IsInfinity(gap)) Debug.LogWarning($"【鏡箱】{name} 開場底下 3 公尺內找不到地板，會直接往下掉");
    }

    private void FixedUpdate()
    {
        if (_rb == null || _col == null || _hidden) return;

        if (_holder != null)
        {
            // 被推出邊緣（箱子中間底下沒地板了）：她手一滑放開，箱子掉下去
            if (float.IsInfinity(GroundGap(Mathf.Max(0f, _groundGap) + 0.3f)))
            {
                Debug.Log($"【鏡箱】{name} 被推出地板邊緣，放手、往下掉");
                _holder.ReleaseObject();   // 會呼叫 EndHold
            }
            else
            {
                Vector3 p = _rb.position;
                p.x = _holder.transform.position.x + _holdOffsetX;
                _rb.MovePosition(p);
                return;
            }
        }

        UpdateFall();
    }

    /// <summary>自己做的重力：底下沒地板就掉，落到地板上停在「場景擺放的離地高度」。</summary>
    private void UpdateFall()
    {
        float dt = Time.fixedDeltaTime;
        float step = _falling ? (_fallSpeed + fallGravity * dt) * dt : 0f;
        float gap = GroundGap(Mathf.Max(0f, _groundGap) + step + 0.3f);
        float extra = float.IsInfinity(gap) ? float.PositiveInfinity : gap - _groundGap;   // 比擺放時多高出多少

        if (!_falling)
        {
            if (extra <= 0.05f) return;   // 站得好好的
            _falling = true;
            _fallSpeed = 0f;
            _fallStartY = _rb.position.y;
            return;
        }

        _fallSpeed += fallGravity * dt;
        Vector3 pos = _rb.position;
        if (extra <= step)
        {
            // 落地
            pos.y -= Mathf.Max(0f, extra);
            _rb.MovePosition(pos);
            _falling = false;
            _fallSpeed = 0f;
            Debug.Log($"【鏡箱】{name} 落地（y={pos.y:F2}）");
            return;
        }

        pos.y -= step;
        _rb.MovePosition(pos);

        if (_fallStartY - pos.y > lostFallDistance) ReturnHome();
    }

    /// <summary>掉進坑裡拿不回來：回到原位。原位的地板也碎了（她已經走過去）就先藏起來，重生時再出現。</summary>
    private void ReturnHome()
    {
        _falling = false;
        _fallSpeed = 0f;
        _rb.position = _home;
        transform.position = _home;
        Physics.SyncTransforms();
        if (float.IsInfinity(GroundGap(Mathf.Max(0f, _groundGap) + 0.3f)))
        {
            SetHidden(true);
            Debug.Log($"【鏡箱】{name} 掉進坑裡、原位的地板也沒了：先藏起來");
        }
        else
        {
            Debug.Log($"【鏡箱】{name} 掉進坑裡：回到原位");
        }
    }

    private void SetHidden(bool hidden)
    {
        _hidden = hidden;
        if (_sr != null) _sr.enabled = !hidden;
        if (_col != null) _col.enabled = !hidden;
    }

    /// <summary>
    /// 碰撞框底部離下面的地板多高（負的＝陷進去）。只看箱子中間 supportWidth 寬的範圍；
    /// 往下 maxDown 公尺內都沒地板就回傳正無限大。她自己、觸發區不算。
    /// </summary>
    private float GroundGap(float maxDown)
    {
        if (_col == null || !_col.enabled) return float.PositiveInfinity;
        Bounds b = _col.bounds;
        const float halfY = 0.02f;
        Vector3 half = new Vector3(Mathf.Max(0.05f, b.extents.x * supportWidth), halfY, Mathf.Max(0.05f, b.extents.z * 0.9f));
        float castDist = b.extents.y + Mathf.Max(0.05f, maxDown);
        int n = Physics.BoxCastNonAlloc(b.center, half, Vector3.down, _downHits, Quaternion.identity, castDist, ~0, QueryTriggerInteraction.Ignore);
        float best = float.PositiveInfinity;
        for (int i = 0; i < n; i++)
        {
            Collider c = _downHits[i].collider;
            if (c == null || c == _col || c.transform.IsChildOf(transform)) continue;
            if (_downHits[i].distance <= 0f && _downHits[i].point == Vector3.zero) continue;   // 一開始就重疊的不算
            if (c.GetComponentInParent<PlayerMovement>() != null) continue;
            if (_downHits[i].distance < best) best = _downHits[i].distance;
        }
        if (float.IsInfinity(best)) return best;
        // 地板頂面 = 中心 − halfY − 距離；箱子底部 = 中心 − extents.y
        return best + halfY - b.extents.y;
    }

    /// <summary>箱子往 directionX 再走一小段會不會撞到東西（她自己、觸發區、腳下的地板不算）。</summary>
    public bool IsBlockedToward(float directionX, Transform holder)
    {
        if (_col == null) return false;
        Bounds b = _col.bounds;
        Vector3 dir = directionX > 0f ? Vector3.right : Vector3.left;
        // 往上抬一點、上下縮一點，不會掃到腳下的地板
        Vector3 center = b.center + Vector3.up * 0.08f;
        Vector3 half = new Vector3(b.extents.x * 0.98f, Mathf.Max(0.05f, b.extents.y - 0.15f), b.extents.z * 0.9f);
        int n = Physics.BoxCastNonAlloc(center, half, dir, _hits, Quaternion.identity, Mathf.Max(0.02f, blockCheckDistance), ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            Collider c = _hits[i].collider;
            if (c == null || c == _col) continue;
            if (holder != null && (c.transform == holder || c.transform.IsChildOf(holder))) continue;
            if (_hits[i].distance <= 0f && _hits[i].point == Vector3.zero) continue;   // 一開始就重疊的（例如貼著的地板）不算
            return true;
        }
        return false;
    }

    public void ResetToInitialState()
    {
        // 重生時她可能還抓著：先放手（不然箱子掛在她身上跟著傳送）
        if (_holder != null) _holder.ReleaseObject();
        _holder = null;

        if (_rb == null) _rb = GetComponent<Rigidbody>();
        _rb.isKinematic = true;
        _rb.position = _home;
        transform.position = _home;
        _falling = false;
        _fallSpeed = 0f;
        SetHidden(false);
    }
}
