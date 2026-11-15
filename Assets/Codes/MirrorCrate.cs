using UnityEngine;

/// <summary>
/// ★1116 玻璃館「鏡箱」：玻璃牆前的箱子，推到牆邊當墊腳石才翻得過牆（見 MirrorWallCandleReflect 的說明）。
///
/// 操作照巨石的規則（PlayerMovement ★1116 鏡箱）：
///   ・按住 Shift、面向箱子、貼著它＝抓住：往箱子走是推、往後退是拉（拉比推慢）。放開 Shift 或起跳＝放手。
///   ・沒按 Shift 走過去只會被擋住，推不動。
/// 抓住時箱子變 kinematic、每個物理步跟著她（保持抓住時的水平距離；不掛到她身上——她用剛體速度移動，掛在底下的剛體不會跟著走）；
///   kinematic 自己不會被牆擋，所以由 IsBlockedToward 告訴她「箱子前面有東西」，她就停下來。
/// 推拉的速度由質量決定（她的速度 × 10 ÷ (10 + 質量)）：質量 10＝推約 3 m/s、拉約 2 m/s。
/// 重生時放手、回到原位。
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class MirrorCrate : MonoBehaviour, IResettable
{
    [Tooltip("箱子質量。推拉速度＝她的速度 × 10 ÷ (10 + 質量)：10＝推約 3 m/s、拉約 2 m/s；越重越慢")]
    public float crateMass = 10f;
    [Tooltip("抓著箱子時，箱子前面離障礙物多近就算撞到（公尺）")]
    public float blockCheckDistance = 0.12f;

    private Rigidbody _rb;
    private Collider _col;
    private Vector3 _home;
    private readonly RaycastHit[] _hits = new RaycastHit[8];
    private PlayerMovement _holder;
    private float _holdOffsetX;

    /// <summary>被她抓住：記下跟她的水平距離，之後每個物理步跟著她（kinematic）。</summary>
    public void BeginHold(PlayerMovement holder)
    {
        _holder = holder;
        _holdOffsetX = transform.position.x - holder.transform.position.x;
        if (_rb == null) _rb = GetComponent<Rigidbody>();
        _rb.isKinematic = true;
    }

    /// <summary>放手：停在原地（維持 kinematic，碰到也不會被頂走）。</summary>
    public void EndHold()
    {
        _holder = null;
        if (_rb == null) _rb = GetComponent<Rigidbody>();
        _rb.isKinematic = true;
    }

    public bool IsHeld => _holder != null;

    private void FixedUpdate()
    {
        if (_holder == null) return;
        Vector3 p = _rb.position;
        p.x = _holder.transform.position.x + _holdOffsetX;
        _rb.MovePosition(p);
    }

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _col = GetComponent<Collider>();
        _rb.useGravity = false;
        _rb.mass = Mathf.Max(0.1f, crateMass);
        _rb.linearDamping = 10f;
        _rb.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionZ;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        // ★1116 沒被抓著的時候一律 kinematic：只有「按住 Shift、站在地上抓住」才動得了。
        //   Root Cause（M 11/16：「玩家在懸空時推得動物件」）：放手後箱子是一般剛體，她跳起來或掉下來撞到箱子側面，物理就把箱子頂走了。
        // ★1116 位置完全照場景擺的（不往下貼地）：M 11/16「手動調整位置，測試時就被調回原本的位置」——
        //   之前開場會把「碰撞框底部」往下貼到地面，碰撞框跟圖對不齊時，擺好的位置就被拉走。
        //   所以箱子的碰撞框底部要自己對齊地面（不對齊的話推的時候會浮著或陷進地板）。
        _rb.isKinematic = true;
        _home = transform.position;
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
    }
}
