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
    [Header("★1011 被夾住（四面都貼著東西）快速救援")]
    [Tooltip("上下左右四個方向都貼著碰撞體、又按著方向鍵沒移動，這麼多秒就救。比 noProgressSeconds 短：四面都擋住＝真的出不去，不用再等 2.5 秒")]
    public float pinnedNoProgressSeconds = 0.8f;
    [Tooltip("判斷『貼著』的距離（公尺）")]
    public float pinnedProbeDistance = 0.12f;
    [Tooltip("記錄安全位置的間隔（秒）")]
    public float safeRecordInterval = 0.25f;
    [Tooltip("救援時退回幾秒前的安全位置（太近的話她還按著方向鍵會馬上又穿進去）")]
    public float rescueLookbackSeconds = 1f;
    [Tooltip("★0920 救援目標至少要離目前位置這麼遠才算數 (公尺)。\n" +
             "必須明顯大於 noProgressDistance(0.3) 與 shrink(0.25)，不然「救援」等於原地傳送。\n" +
             "C 條件成立的前提就是『2.5 秒內移動不到 0.3 公尺』，所以卡住期間記下來的安全點\n" +
             "一定都貼在卡住位置旁邊，沒有這道門檻就會挑到它們。")]
    public float rescueMinDistance = 1.0f;
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
        // ★1012 只在「真的需要救」的時候計時：
        //   ・四面都被擋住（被夾住）：按什麼都出不去
        //   ・按著的方向是空的、卻動不了：碰撞把她卡住（不是牆）
        //   只是頂著牆（按的方向被擋、別的方向其實是空的）不算——她按另一個方向就走得出來。
        //   原本「在石頭範圍內按著任何方向鍵 2.5 秒沒移動」就救，頂著牆走路也會被當成卡住，
        //   而且身邊沒有 1 米外的安全點時還會直接走重生，等於撞牆就死。
        bool countsAsStuck = false;
        if (around != null && pressing)
        {
            bool pinnedNow = IsPinnedOnAllSides(out _);
            bool pressUp = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.UpArrow);
            bool pressDown = Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow);
            bool pressLeft = Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow);
            bool pressRight = Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow);
            bool pressingFreeSide = (pressUp && _sideFree[0]) || (pressDown && _sideFree[1]) || (pressLeft && _sideFree[2]) || (pressRight && _sideFree[3]);
            countsAsStuck = pinnedNow || pressingFreeSide;
        }
        if (countsAsStuck)
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

        // ★1011 四面都被擋住＝真的被夾住（不是只有往牆的方向被擋，還有別的方向可以游走），不用等滿 noProgressSeconds
        bool pinned = countsAsStuck && _noProgressTime >= pinnedNoProgressSeconds
                      && !_sideFree[0] && !_sideFree[1] && !_sideFree[2] && !_sideFree[3];

        string reason = null;
        Collider rock = null;
        if (_embeddedTime >= embeddedSeconds) { reason = $"身體插在石頭裡超過 {embeddedSeconds} 秒"; rock = embedded; }
        else if (_shellCount >= 2) { reason = "被包在石頭殼裡面"; rock = shell; }
        else if (pinned) { reason = $"上下左右四面都被擋住（被夾住），按著方向鍵 {_noProgressTime:F1} 秒沒移動"; rock = around; }
        else if (_noProgressTime >= noProgressSeconds) { reason = $"在石頭範圍內按著方向鍵 {noProgressSeconds} 秒沒移動（還有別的方向可以走：{DescribeFreeSides()}）"; rock = around; }

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
            int keep = SafePositionCapacity();
            while (_safePositions.Count > keep) _safePositions.RemoveAt(0);
        }
    }

    /// <summary>
    /// ★0920 安全點要保留幾筆。
    ///
    /// 原本是只照 rescueLookbackSeconds 算，預設 1 秒 ÷ 0.25 ＋1 ＝ 5 筆。
    /// 但 C 條件要「2.5 秒沒移動」才觸發，而卡住期間那些位置照樣會被記成安全點
    /// （記錄條件只擋 embedded／shell，不擋 around），於是 5 筆全部落在卡住之後，
    /// 救援必然挑到貼著卡住位置的點——這就是「已放回石頭外的位置」卻原地沒動的成因。
    ///
    /// 改成至少涵蓋 noProgressSeconds + rescueLookbackSeconds，卡住時才留得住「卡住之前」的位置。
    /// 依現有參數動態算（2.5 + 1.0）÷ 0.25 ＋1 ＝ 15 筆，不寫死。
    /// 只是加長保留量，記錄條件與 A／B 的判定完全沒動。
    /// </summary>
    private int SafePositionCapacity()
    {
        float interval = Mathf.Max(0.05f, safeRecordInterval);
        float requiredSeconds = Mathf.Max(0f, noProgressSeconds) + Mathf.Max(0f, rescueLookbackSeconds);
        return Mathf.Max(2, Mathf.CeilToInt(requiredSeconds / interval) + 1);
    }

    /// <summary>
    /// ★0920 從「最新 → 最舊」挑第一個離目前位置至少 rescueMinDistance 的安全點。
    /// 卡住期間記下來的那一串近距離點會被逐一跳過，一路退到真正還游得動的位置。
    /// farthest 是掃過的候選裡最遠的距離，只在找不到時拿來寫 Log（找到時會提早 break，不完整也用不到）。
    /// </summary>
    private Vector3 _farthestSafePos;

    private bool TryPickSafePosition(Vector3 current, out Vector3 target, out float distance, out float farthest)
    {
        target = current;
        distance = 0f;
        farthest = 0f;
        _farthestSafePos = current;

        for (int i = _safePositions.Count - 1; i >= 0; i--)
        {
            float d = Vector3.Distance(current, _safePositions[i]);
            if (d > farthest) { farthest = d; _farthestSafePos = _safePositions[i]; }
            if (d >= rescueMinDistance)
            {
                target = _safePositions[i];
                distance = d;
                return true;
            }
        }
        return false;
    }

    private static readonly Vector3[] SideDirs = { Vector3.up, Vector3.down, Vector3.left, Vector3.right };
    private static readonly string[] SideNames = { "上", "下", "左", "右" };
    private string _lastFreeSides = "";
    private readonly bool[] _sideFree = new bool[4];   // 上、下、左、右：true＝這一面是空的

    /// <summary>
    /// ★1011 上、下、左、右四個方向，玩家的碰撞盒往外 pinnedProbeDistance 公尺內是不是都有東西（非觸發、不是自己）。
    /// 四面都有＝被夾住，按什麼方向都出不去。同時記下哪幾面是空的（寫進救援 log 用）。
    /// </summary>
    private bool IsPinnedOnAllSides(out string blocked)
    {
        Bounds b = _playerCollider.bounds;
        Vector3 half = b.extents - Vector3.one * 0.02f;
        half.x = Mathf.Max(0.05f, half.x); half.y = Mathf.Max(0.05f, half.y); half.z = Mathf.Max(0.05f, half.z);

        int blockedCount = 0;
        blocked = "";
        string free = "";
        for (int i = 0; i < SideDirs.Length; i++)
        {
            bool hit = false;
            foreach (RaycastHit h in Physics.BoxCastAll(b.center, half, SideDirs[i], Quaternion.identity, pinnedProbeDistance, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.collider == null || h.collider == _playerCollider || h.collider.transform.IsChildOf(transform)) continue;
                hit = true;
                break;
            }
            _sideFree[i] = !hit;
            if (hit) { blockedCount++; blocked += SideNames[i]; }
            else free += SideNames[i];
        }
        _lastFreeSides = free;
        return blockedCount == SideDirs.Length;
    }

    private string DescribeFreeSides()
    {
        // 沒有量過（還沒到 pinned 檢查時間）就現在量一次，只為了寫進 log
        IsPinnedOnAllSides(out _);
        return string.IsNullOrEmpty(_lastFreeSides) ? "無" : _lastFreeSides;
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
        // 碰撞是掛在 RockPlaneCollider 子物件上的，名字都一樣，真正要找的是它的父物件（石頭本體）
        string rockName = "未知";
        string rockInfo = "";
        if (rock != null)
        {
            Transform rt = rock.transform;
            string parentName = rt.parent != null ? rt.parent.name : "（無父物件）";
            rockName = rock.gameObject.name + "（石頭本體：" + parentName + "）";
            Bounds rb2 = rock.bounds;
            rockInfo = $"  石頭位置：{rt.position}　範圍 x {rb2.min.x:F1}~{rb2.max.x:F1}、y {rb2.min.y:F1}~{rb2.max.y:F1}\n";
        }
        Vector3 current = transform.position;
        string context = $"  原因：{reason}\n" +
                         $"  卡住位置：{current}\n" +
                         $"  速度：{(_rb != null ? _rb.linearVelocity.ToString() : "無剛體")}\n" +
                         $"  按鍵：{(Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.UpArrow) ? "上 " : "")}" +
                         $"{(Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? "下 " : "")}" +
                         $"{(Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? "左 " : "")}" +
                         $"{(Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? "右" : "")}\n" +
                         rockInfo;

        bool rescuedRecently = Time.time - _lastRescueTime < 3f;
        _lastRescueTime = Time.time;

        // ★0920 不再無腦拿 _safePositions[0]。從最新往最舊掃，挑第一個「離目前位置夠遠」的點：
        //   C 條件卡住期間記下來的那一串，距離都小於 noProgressDistance(0.3)，會被全部跳過。
        int sampleCount = _safePositions.Count;
        bool found = TryPickSafePosition(current, out Vector3 target, out float moved, out float farthest);
        // ★1012 沒有「夠遠」的安全點時，只要最遠的那個也有 0.4 米，就先用它——放回去總比直接走重生（黑幕＋回存檔點）好。
        //   實際發生過：安全點最遠只有 0.98 米（門檻 1.0），結果為了差 2 公分走了整套重生。
        if (!found && farthest >= 0.4f) { found = true; target = _farthestSafePos; moved = farthest; }

        if (!rescuedRecently && found)
        {
            _safePositions.Clear();
            if (_pm != null) _pm.WarpTo(target);
            else
            {
                transform.position = target;
                if (_rb != null) { _rb.position = target; _rb.linearVelocity = Vector3.zero; }
            }
            Debug.LogWarning($"[卡石救援] 玩家卡在石頭「{rockName}」，已放回較早的有效安全位置。\n" +
                           $"  救援位置：{target}\n" +
                           $"  救援距離：{moved:F2}m（門檻 {rescueMinDistance:F2}m）\n" +
                           $"{context}");
            return;
        }

        // 沒有夠遠的安全點，或剛救過又卡住（安全點本身就在問題位置）：走現有的重生退路，保證不會卡死
        string why;
        if (rescuedRecently) why = "剛救過又卡住";
        else if (sampleCount == 0) why = "沒有任何安全位置紀錄";
        else why = $"沒有找到距離目前位置 >= {rescueMinDistance:F2}m 的安全點"
                   + $"（共 {sampleCount} 筆，最遠只有 {farthest:F2}m）";

        _safePositions.Clear();
        PlayerRespawnSystem respawn = GetComponentInChildren<PlayerRespawnSystem>();
        if (respawn == null) respawn = GetComponentInParent<PlayerRespawnSystem>();
        if (respawn == null) respawn = FindFirstObjectByType<PlayerRespawnSystem>();
        if (respawn != null) respawn.TriggerRespawn();
        Debug.LogError($"[卡石救援] 玩家卡在石頭「{rockName}」，{why}，改用現有 Respawn fallback。\n{context}");
    }
}
