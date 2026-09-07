using UnityEngine;

/// <summary>
/// 水下穿模探針。
///
/// 由 UnderwaterRockColliderHelper 自動掛到玩家身上。
/// 每幀檢查玩家的碰撞箱有沒有「插進石頭裡」——玩家會穿過石頭墜落，
/// 過程中一定會有幾幀是身體卡在石頭內部的，抓到那一刻就知道是哪一顆石頭出問題。
///
/// 只印前幾次就自動停，不會洗版。
/// </summary>
public class UnderwaterPenetrationProbe : MonoBehaviour
{
    [Tooltip("最多印幾次就停止（避免洗版）")]
    public int maxReports = 8;

    [Tooltip("玩家中心往內縮多少才算「真的插進去」，避免貼著表面走路時誤報")]
    public float shrink = 0.25f;

    private Collider _playerCollider;
    private int _reported;
    private float _lastReportTime = -99f;

    private void Start()
    {
        _playerCollider = GetComponent<Collider>();
        if (_playerCollider == null) _playerCollider = GetComponentInChildren<Collider>();
        if (_playerCollider == null)
        {
            enabled = false;
            return;
        }
        Debug.Log("[穿模探針] 已啟動，玩家一旦卡進石頭內部就會回報是哪一顆。");
    }

    private void FixedUpdate()
    {
        if (_reported >= maxReports) { enabled = false; return; }
        if (_playerCollider == null) return;

        // 用比玩家碰撞箱小一圈的範圍去測，貼著石頭走不會誤報，只有真的插進去才會中
        Bounds b = _playerCollider.bounds;
        Vector3 half = b.extents - Vector3.one * shrink;
        if (half.x <= 0f || half.y <= 0f || half.z <= 0f) return;

        Collider[] hits = Physics.OverlapBox(b.center, half, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
        foreach (var c in hits)
        {
            if (c == null || c == _playerCollider) continue;
            if (c.transform.IsChildOf(transform)) continue;

            string n = c.gameObject.name;
            bool isRock = n.Contains("Rocks") || n.Contains("Rock") || n.Contains("rock") || n.Contains("Stone");
            if (!isRock) continue;

            // 同一秒內不重複回報
            if (Time.time - _lastReportTime < 1f) return;
            _lastReportTime = Time.time;
            _reported++;

            Debug.LogError($"[穿模探針] ⚠️ 玩家插進石頭「{n}」裡面了！\n" +
                           $"  玩家位置：{transform.position}\n" +
                           $"  石頭位置：{c.transform.position}　石頭 Z 範圍：{c.bounds.min.z:F2}~{c.bounds.max.z:F2}\n" +
                           $"  碰撞體型別：{c.GetType().Name}" +
                           (c is MeshCollider mc ? $"（Convex={mc.convex}，Mesh={(mc.sharedMesh != null ? mc.sharedMesh.name : "無")}）" : ""));
            return;
        }
    }
}
