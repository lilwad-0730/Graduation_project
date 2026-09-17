using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 水下穿模探針＋卡石救援。
///
/// 由 UnderwaterRockColliderHelper 自動掛到玩家身上。
/// 1. 探針：檢查玩家的碰撞箱有沒有「插進石頭裡」，抓到那一刻就知道是哪一顆石頭出問題。只印前幾次就停，不會洗版。
/// 2. ★0916 救援：水下石頭是非凸面 MeshCollider＝空心的殼，穿過殼就出不來（玩到一半卡死）。
///    三種情況任一成立就把她放回大約 1 秒前還在石頭外的位置：
///      A. 身體插在石頭裡（縮一圈的碰撞箱還碰到石頭）持續 0.4 秒——貼著表面游不會中
///      B. 被包在石頭殼裡：8 個方向裡至少 5 個，最近的東西都是同一顆石頭的「內側」
///         （★第一版要求 8 個方向全中，但石頭底部埋在海床、石頭互相疊在一起，往下那條幾乎一定打到別的東西，所以從來沒觸發過）
///      C. 保底：人在某顆石頭的範圍內、一直按方向鍵，2.5 秒卻幾乎沒移動
///    放回去之後 3 秒內又卡住（安全點本身有問題）就改走重生，保證不會卡死。
/// </summary>
public class UnderwaterPenetrationProbe : MonoBehaviour
{
    [Tooltip("最多印幾次就停止（避免洗版）")]
    public int maxReports = 8;

    [Tooltip("玩家中心往內縮多少才算「真的插進去」，避免貼著表面走路時誤報")]
    public float shrink = 0.25f;

    [Header("★0916 卡石救援")]
    public bool rescueWhenInsideRock = true;
    [Tooltip("每隔幾秒檢查一次")]
    public float checkInterval = 0.1f;
    [Tooltip("A：身體插在石頭裡持續幾秒就救")]
    public float embeddedSeconds = 0.4f;
    [Tooltip("B：8 個方向裡至少幾個打到同一顆石頭內側，算被包在裡面")]
    public int shellDirectionsNeeded = 5;
    [Tooltip("C：按著方向鍵卻幾乎沒移動幾秒就救（只在石頭範圍內才算）")]
    public float noProgressSeconds = 2.5f;
    [Tooltip("C：這段時間內移動少於幾公尺算沒移動")]
    public float noProgressDistance = 0.3f;
    [Tooltip("記錄安全位置的間隔（秒）")]
    public float safeRecordInterval = 0.25f;
    [Tooltip("救援時退回幾秒前的安全位置（太近的話她還按著方向鍵會馬上又穿進去）")]
    public float rescueLookbackSeconds = 1f;
    [Tooltip("往外打射線的長度")]
    public float rayLength = 30f;

    private Collider _playerCollider;
    private PlayerMovement _pm;
    private Rigidbody _rb;
    private int _reported;
    private float _lastReportTime = -99f;

    private readonly List<Collider> _rocks = new List<Collider>();
    private float _nextCheckTime;
    private float _nextSafeRecordTime;
    private float _embeddedTime;
    private int _shellCount;
    private float _noProgressTime;
    private Vector3 _noProgressAnchor;
    private float _lastRescueTime = -99f;
    private readonly List<Vector3> _safePositions = new List<Vector3>();

    private static readonly Vector3[] Directions =
    {
        Vector3.right, Vector3.left, Vector3.up, Vector3.down,
        new Vector3(1f, 1f, 0f).normalized, new Vector3(-1f, 1f, 0f).normalized,
        new Vector3(1f, -1f, 0f).normalized, new Vector3(-1f, -1f, 0f).normalized
    };

    private void Start()
    {
        _playerCollider = GetComponent<Collider>();
        if (_playerCollider == null) _playerCollider = GetComponentInChildren<Collider>();
        _pm = GetComponent<PlayerMovement>();
        if (_pm == null) _pm = GetComponentInParent<PlayerMovement>();
        _rb = GetComponent<Rigidbody>();
        if (_rb == null) _rb = GetComponentInParent<Rigidbody>();
        if (_playerCollider == null)
        {
            enabled = false;
            return;
        }

        foreach (var mc in FindObjectsByType<MeshCollider>(FindObjectsSortMode.None))
        {
            if (mc != null && !mc.isTrigger && IsRockName(mc.gameObject.name)) _rocks.Add(mc);
        }
        _noProgressAnchor = transform.position;
        Debug.Log($"[穿模探針] 已啟動（監看 {_rocks.Count} 顆石頭），卡進石頭會回報是哪一顆，並自動救出來。");
    }

    private void FixedUpdate()
    {
        if (_playerCollider == null) return;

        // ★ 原本印滿次數就 enabled = false 整支關掉；現在救援也在這支裡，只停止印訊息、不關掉
        if (_reported < maxReports) ReportPenetration();

        if (rescueWhenInsideRock) UpdateRescue();
    }

    private static bool IsRockName(string n)
    {
        return n.Contains("Rocks") || n.Contains("Rock") || n.Contains("rock") || n.Contains("Stone");
    }

    private void ReportPenetration()
    {
        Collider c = FindEmbeddedRock();
        if (c == null) return;

        // 同一秒內不重複回報
        if (Time.time - _lastReportTime < 1f) return;
        _lastReportTime = Time.time;
        _reported++;

        Debug.LogError($"[穿模探針] ⚠️ 玩家插進石頭「{c.gameObject.name}」裡面了！\n" +
                       $"  玩家位置：{transform.position}\n" +
                       $"  石頭位置：{c.transform.position}　石頭 Z 範圍：{c.bounds.min.z:F2}~{c.bounds.max.z:F2}\n" +
                       $"  碰撞體型別：{c.GetType().Name}" +
                       (c is MeshCollider mc ? $"（Convex={mc.convex}，Mesh={(mc.sharedMesh != null ? mc.sharedMesh.name : "無")}）" : ""));
    }

    /// <summary>用比玩家碰撞箱小一圈的範圍去測，貼著石頭游不會中，只有真的插進去才會中。</summary>
    private Collider FindEmbeddedRock()
    {
        Bounds b = _playerCollider.bounds;
        Vector3 half = b.extents - Vector3.one * shrink;
        if (half.x <= 0f || half.y <= 0f || half.z <= 0f) return null;

        foreach (var c in Physics.OverlapBox(b.center, half, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
        {
            if (c == null || c == _playerCollider || c.transform.IsChildOf(transform)) continue;
            if (IsRockName(c.gameObject.name)) return c;
        }
        return null;
    }

    // ─────────────────────────────────────────────────────────────
    private void ResetTimers()
    {
        _embeddedTime = 0f;
        _shellCount = 0;
        _noProgressTime = 0f;
        _noProgressAnchor = transform.position;
    }

    private void UpdateRescue()
    {
        if (PlayerRespawnSystem.IsAnyRespawning)
        {
            ResetTimers();
            _safePositions.Clear();   // 重生會換位置，舊的安全點不能再用
            return;
        }

        if (Time.time < _nextCheckTime) return;
        float dt = checkInterval;
        _nextCheckTime = Time.time + checkInterval;

        // 演出定住她的時候本來就不會動，不算卡住
        if (PlayerCutsceneHold.IsHeld || (_pm != null && _pm.isCutsceneFrozen))
        {
            ResetTimers();
            return;
        }

        Vector3 center = _playerCollider.bounds.center;

        // A. 身體插在石頭裡
        Collider embedded = FindEmbeddedRock();
        _embeddedTime = embedded != null ? _embeddedTime + dt : 0f;

        // B. 被包在石頭殼裡
        Collider shell = FindShellRock(center);
        _shellCount = shell != null ? _shellCount + 1 : 0;

        // C. 保底：在石頭範圍內、按著方向鍵卻沒移動
        Collider around = FindRockAround(center);
        bool pressing = Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.S) ||
                        Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.RightArrow) ||
                        Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.DownArrow);
        if (around != null && pressing)
        {
            if (Vector3.Distance(transform.position, _noProgressAnchor) > noProgressDistance)
            {
                _noProgressAnchor = transform.position;
                _noProgressTime = 0f;
            }
            else
            {
                _noProgressTime += dt;
            }
        }
        else
        {
            _noProgressAnchor = transform.position;
            _noProgressTime = 0f;
        }

        string reason = null;
        Collider rock = null;
        if (_embeddedTime >= embeddedSeconds) { reason = $"身體插在石頭裡超過 {embeddedSeconds} 秒"; rock = embedded; }
        else if (_shellCount >= 2) { reason = "被包在石頭殼裡面"; rock = shell; }
        else if (_noProgressTime >= noProgressSeconds) { reason = $"在石頭範圍內按著方向鍵 {noProgressSeconds} 秒沒移動"; rock = around; }

        if (reason != null)
        {
            ResetTimers();
            Rescue(rock, reason);
            return;
        }

        // 完全在石頭外（沒插進去、沒被包住）才記成安全點
        if (embedded == null && shell == null && Time.time >= _nextSafeRecordTime)
        {
            _nextSafeRecordTime = Time.time + safeRecordInterval;
            _safePositions.Add(transform.position);
            int keep = Mathf.Max(2, Mathf.CeilToInt(rescueLookbackSeconds / Mathf.Max(0.05f, safeRecordInterval)) + 1);
            while (_safePositions.Count > keep) _safePositions.RemoveAt(0);
        }
    }

    /// <summary>玩家中心落在哪一顆石頭的範圍（外框）內。</summary>
    private Collider FindRockAround(Vector3 center)
    {
        foreach (var c in _rocks)
        {
            if (c != null && c.enabled && c.bounds.Contains(center)) return c;
        }
        return null;
    }

    /// <summary>
    /// 從玩家中心往 8 個方向打射線（連背面也打），數「最近的東西是同一顆石頭的內側」有幾個方向。
    /// 內側的判斷：只打正面時，在同一個距離打不到那顆石頭＝剛才打到的是背面。
    /// </summary>
    private Collider FindShellRock(Vector3 origin)
    {
        Collider[] nearestCol = new Collider[Directions.Length];
        float[] nearestDist = new float[Directions.Length];

        bool oldBackfaces = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = true;
        try
        {
            for (int i = 0; i < Directions.Length; i++)
            {
                float best = float.MaxValue;
                foreach (var h in Physics.RaycastAll(origin, Directions[i], rayLength, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (h.collider == null || h.collider == _playerCollider) continue;
                    if (h.collider.transform.IsChildOf(transform)) continue;
                    if (h.distance < best) { best = h.distance; nearestCol[i] = h.collider; }
                }
                nearestDist[i] = best;
            }
        }
        finally
        {
            Physics.queriesHitBackfaces = oldBackfaces;
        }

        // 只打正面，確認哪些方向打到的是內側
        Physics.queriesHitBackfaces = false;
        Collider bestRock = null;
        int bestCount = 0;
        try
        {
            var counts = new Dictionary<Collider, int>();
            for (int i = 0; i < Directions.Length; i++)
            {
                Collider c = nearestCol[i];
                if (c == null) continue;
                if (!(c is MeshCollider mc) || mc.convex) continue;
                if (!IsRockName(c.gameObject.name)) continue;

                bool front = c.Raycast(new Ray(origin, Directions[i]), out RaycastHit fh, nearestDist[i] + 0.02f)
                             && Mathf.Abs(fh.distance - nearestDist[i]) < 0.02f;
                if (front) continue;

                counts.TryGetValue(c, out int n);
                counts[c] = n + 1;
                if (n + 1 > bestCount) { bestCount = n + 1; bestRock = c; }
            }
        }
        finally
        {
            Physics.queriesHitBackfaces = oldBackfaces;
        }
        return bestCount >= shellDirectionsNeeded ? bestRock : null;
    }

    private void Rescue(Collider rock, string reason)
    {
        string rockName = rock != null ? rock.gameObject.name : "未知";
        string context = $"  原因：{reason}\n" +
                         $"  卡住位置：{transform.position}\n" +
                         $"  速度：{(_rb != null ? _rb.linearVelocity.ToString() : "無剛體")}\n" +
                         $"  按鍵：{(Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.UpArrow) ? "上 " : "")}" +
                         $"{(Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? "下 " : "")}" +
                         $"{(Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? "左 " : "")}" +
                         $"{(Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? "右" : "")}";

        bool rescuedRecently = Time.time - _lastRescueTime < 3f;
        _lastRescueTime = Time.time;

        if (!rescuedRecently && _safePositions.Count > 0)
        {
            Vector3 target = _safePositions[0];   // 最舊的那個＝大約 1 秒前
            _safePositions.Clear();
            if (_pm != null) _pm.WarpTo(target);
            else
            {
                transform.position = target;
                if (_rb != null) { _rb.position = target; _rb.linearVelocity = Vector3.zero; }
            }
            Debug.LogError($"[卡石救援] 玩家卡在石頭「{rockName}」，已放回石頭外的位置 {target}。\n{context}");
            return;
        }

        // 沒有安全點，或剛救過又卡住（安全點本身就在問題位置）：走重生，保證不會卡死
        _safePositions.Clear();
        PlayerRespawnSystem respawn = GetComponentInChildren<PlayerRespawnSystem>();
        if (respawn == null) respawn = GetComponentInParent<PlayerRespawnSystem>();
        if (respawn == null) respawn = FindFirstObjectByType<PlayerRespawnSystem>();
        if (respawn != null) respawn.TriggerRespawn();
        Debug.LogError($"[卡石救援] 玩家卡在石頭「{rockName}」，{(rescuedRecently ? "剛救過又卡住" : "沒有可用的安全位置")}，改用重生救出來。\n{context}");
    }
}
