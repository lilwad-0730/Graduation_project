using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

/// <summary>
/// ★1011 水下石頭「全面體檢」：不是等玩家卡到才救，而是進關時把整個關卡的游動空間掃一遍，
/// 找出所有「玩家的碰撞盒會被夾住、困住」的地方，而且是每一顆石頭都檢查（不是只查某一顆）。
///
/// 做法（用玩家真實的碰撞盒大小）：
///   1. 把關卡切成 0.3 公尺的格子，每一格問物理引擎：「玩家的碰撞盒放在這格的中心，會不會碰到任何實體？」
///      不會碰到＝可游動格。
///   2. 把可游動格連起來分成幾塊（上下左右斜向相連才算同一塊）。最大的一塊是主要游動區。
///   3. 找三種有風險的地方：
///        針孔／口袋：跟主要游動區不相連的小塊，而且貼近主要游動區（玩家只要碰撞稍微滑一下就可能鑽進去、再也出不來）。
///        死角尖端：主要游動區裡，只剩一個方向可以走的格子（很窄的縫的盡頭，玩家會被夾在最底）。
///   4. 每個風險點都會用附近的碰撞體找出是哪一顆石頭（印出石頭本體名字），完整名單寫到 Logs/rock_audit_underwater.txt。
///   5. 自動封口（autoSeal）：在風險點放一個看不見的小碰撞盒把它塞住，讓玩家進不去。
///      封口之前先在格子上模擬：塞完之後，主要游動區必須維持連在一起，而且起點、日誌、存檔點、收集物、光絮路徑點
///      這些重要位置都還在主要游動區裡；有任何一項不成立就整批不封，只印報告。所以不會把正常路線擋掉。
///
/// 只在名稱含 underwater 的場景自動啟動，分幀執行（每幀最多花幾毫秒），不會卡畫面。
/// 關掉：UnderwaterRockTrapAudit.Enabled = false；只報告不封口：autoSeal = false。
/// </summary>
public class UnderwaterRockTrapAudit : MonoBehaviour
{
    public static bool Enabled = true;
    public static bool autoSeal = true;

    const float CellSize = 0.3f;
    const int MaxCells = 2500000;
    const float FrameBudgetMs = 5f;
    const float PocketNearMeters = 3f;
    const int MaxPocketCells = 800;
    const int MaxSeals = 60;
    const float SealCellSize = 0.3f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        TryAttach(SceneManager.GetActiveScene());
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        TryAttach(scene);
    }

    static void TryAttach(Scene scene)
    {
        if (!Enabled || !scene.name.ToLower().Contains("underwater")) return;
        if (FindAnyObjectByType<UnderwaterRockTrapAudit>() != null) return;
        new GameObject("[UnderwaterRockTrapAudit]").AddComponent<UnderwaterRockTrapAudit>();
    }

    static bool IsRockName(string n)
    {
        return n.Contains("Rock") || n.Contains("rock") || n.Contains("Stone");
    }

    // 格子資料
    int _w, _h;
    float _cell, _x0, _y0, _z, _hx, _hy, _hz;
    bool[] _free;
    int[] _label;

    IEnumerator Start()
    {
        yield return new WaitForSeconds(2.5f);   // 等石頭碰撞搬移（UnderwaterRockColliderHelper）與玩家就位

        PlayerMovement pm = FindFirstObjectByType<PlayerMovement>();
        if (pm == null) { Debug.LogWarning("[石頭體檢] 找不到玩家，略過。"); yield break; }
        Collider pc = null;
        foreach (Collider c in pm.GetComponentsInChildren<Collider>())
            if (c != null && c.enabled && !c.isTrigger) { pc = c; break; }
        if (pc == null) { Debug.LogWarning("[石頭體檢] 找不到玩家碰撞體，略過。"); yield break; }

        // ── 範圍：所有石頭碰撞體的外框 ＋ 玩家位置，各邊多留 3 公尺 ──
        Bounds area = new Bounds(pc.bounds.center, Vector3.zero);
        int rockCount = 0;
        var rockParents = new HashSet<Transform>();
        foreach (Collider c in FindObjectsByType<Collider>(FindObjectsInactive.Exclude))
        {
            if (c == null || !c.enabled || c.isTrigger || c.transform.IsChildOf(pm.transform)) continue;
            if (!IsRockName(c.gameObject.name)) continue;
            area.Encapsulate(c.bounds);
            Transform body = RockBody(c);
            if (rockParents.Add(body)) rockCount++;
        }
        area.Expand(new Vector3(6f, 6f, 0f));

        _z = pc.bounds.center.z;
        _hx = Mathf.Max(0.1f, pc.bounds.extents.x - 0.02f);
        _hy = Mathf.Max(0.1f, pc.bounds.extents.y - 0.02f);
        _hz = Mathf.Max(0.05f, pc.bounds.extents.z - 0.02f);

        _cell = CellSize;
        float cells = (area.size.x / _cell) * (area.size.y / _cell);
        if (cells > MaxCells) _cell = Mathf.Sqrt(area.size.x * area.size.y / MaxCells);
        _w = Mathf.Max(1, Mathf.CeilToInt(area.size.x / _cell));
        _h = Mathf.Max(1, Mathf.CeilToInt(area.size.y / _cell));
        _x0 = area.min.x;
        _y0 = area.min.y;
        _free = new bool[_w * _h];

        // ── 1. 每一格問物理引擎（分幀）──
        var buf = new Collider[24];
        var sw = Stopwatch.StartNew();
        int freeCount = 0;
        for (int i = 0; i < _free.Length; i++)
        {
            int cx = i % _w, cy = i / _w;
            Vector3 center = CellCenter(cx, cy);
            int n = Physics.OverlapBoxNonAlloc(center, new Vector3(_hx, _hy, _hz), buf, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            bool blocked = n >= buf.Length;
            for (int k = 0; k < n && !blocked; k++)
            {
                Collider c = buf[k];
                if (c == null || c == pc || c.transform.IsChildOf(pm.transform)) continue;
                blocked = true;
            }
            if (!blocked) { _free[i] = true; freeCount++; }

            if ((i & 255) == 0 && sw.Elapsed.TotalMilliseconds > FrameBudgetMs)
            {
                yield return null;
                sw.Restart();
            }
        }

        // ── 2. 連通分塊 ──
        _label = new int[_free.Length];
        var compSize = new List<int> { 0 };
        int mainComp = Label(compSize);
        if (mainComp <= 0) { Debug.LogWarning("[石頭體檢] 整個關卡找不到任何可游動的格子，略過（玩家碰撞盒可能太大或範圍算錯）。"); yield break; }

        // ── 重要位置（起點、觸發區、光絮路徑點）──
        var important = new List<(string name, int cell)>();
        AddImportant(important, "玩家目前位置", pc.bounds.center);
        foreach (GuidanceLight gl in FindObjectsByType<GuidanceLight>(FindObjectsInactive.Exclude))
        {
            if (gl == null || gl.waypoints == null) continue;
            foreach (Transform wp in gl.waypoints) if (wp != null) AddImportant(important, "光絮路徑點 " + wp.name, wp.position);
        }
        foreach (Collider c in FindObjectsByType<Collider>(FindObjectsInactive.Exclude))
        {
            if (c == null || !c.isTrigger || !c.enabled) continue;
            Bounds b = c.bounds;
            if (b.size.x > 30f || b.size.y > 30f || Mathf.Abs(b.center.z - _z) > 3f) continue;
            AddImportant(important, "觸發區 " + c.gameObject.name, b.center);
        }
        var importantInMain = new HashSet<int>();
        for (int i = 0; i < important.Count; i++)
            if (important[i].cell >= 0 && _label[important[i].cell] == mainComp) importantInMain.Add(i);

        // ── 3. 找風險點 ──
        var risky = new List<Risk>();

        // 3a. 不相連的小塊（針孔／口袋），而且離主要游動區很近
        var compBounds = new Dictionary<int, int[]>();   // comp -> minx,miny,maxx,maxy
        for (int i = 0; i < _label.Length; i++)
        {
            int L = _label[i];
            if (L <= 0 || L == mainComp || compSize[L] > MaxPocketCells) continue;
            int cx = i % _w, cy = i / _w;
            if (!compBounds.TryGetValue(L, out int[] bb)) { bb = new[] { cx, cy, cx, cy }; compBounds[L] = bb; }
            else { if (cx < bb[0]) bb[0] = cx; if (cy < bb[1]) bb[1] = cy; if (cx > bb[2]) bb[2] = cx; if (cy > bb[3]) bb[3] = cy; }
        }
        int near = Mathf.CeilToInt(PocketNearMeters / _cell);
        foreach (var kv in compBounds)
        {
            int[] bb = kv.Value;
            if (!TouchesMainWithin(bb, near, mainComp)) continue;
            var r = new Risk { kind = compSize[kv.Key] <= 3 ? "針孔" : "口袋", size = compSize[kv.Key] };
            for (int y = bb[1]; y <= bb[3]; y++)
                for (int x = bb[0]; x <= bb[2]; x++)
                    if (_label[y * _w + x] == kv.Key) r.cells.Add(y * _w + x);
            risky.Add(r);
        }

        // 3b. 主要游動區裡的死角尖端：只剩一個（或零個）方向可以走的格子
        for (int i = 0; i < _free.Length; i++)
        {
            if (_label[i] != mainComp) continue;
            int cx = i % _w, cy = i / _w;
            if (FreeNeighbors(cx, cy) > 1) continue;
            var r = new Risk { kind = "死角尖端", size = 1 };
            r.cells.Add(i);
            risky.Add(r);
        }

        // ── 4. 報告 ──
        foreach (Risk r in risky)
        {
            Vector3 c = Vector3.zero;
            foreach (int cell in r.cells) c += CellCenter(cell % _w, cell / _w);
            r.center = c / Mathf.Max(1, r.cells.Count);
            r.rocks = NearbyRocks(r.center, pm);
        }
        // 死角尖端很多時，同一條縫會有一串，距離 1.5 公尺內合併成一筆（取第一個）
        var shown = new List<Risk>();
        foreach (Risk r in risky)
        {
            bool dup = false;
            foreach (Risk s in shown) if (s.kind == r.kind && Vector3.Distance(s.center, r.center) < 1.5f) { dup = true; s.merged++; break; }
            if (!dup) shown.Add(r);
        }

        var sbFile = new StringBuilder();
        sbFile.AppendLine("水下石頭體檢  格子 " + _w + "x" + _h + "（每格 " + _cell.ToString("F2") + " m），玩家碰撞盒半寬 " + _hx.ToString("F2") + " x " + _hy.ToString("F2"));
        sbFile.AppendLine("石頭 " + rockCount + " 顆、可游動 " + freeCount + " 格、分成 " + (compSize.Count - 1) + " 塊（主要游動區 " + compSize[mainComp] + " 格）");
        int listed = 0;
        var sbLog = new StringBuilder();
        foreach (Risk r in shown)
        {
            string line = "  [" + r.kind + "] 位置 " + r.center.ToString("F1") + "  大小 " + r.size + " 格" + (r.merged > 0 ? "（同一條縫還有 " + r.merged + " 個）" : "") + "  附近石頭：" + (r.rocks.Count > 0 ? string.Join("、", r.rocks) : "無");
            sbFile.AppendLine(line);
            if (listed < 25) { sbLog.AppendLine(line); listed++; }
        }

        // ── 5. 自動封口（先模擬，確定不會擋掉正常路線才真的放碰撞盒）──
        string sealResult = "沒有風險點，不需要封口";
        if (shown.Count > 0)
        {
            sealResult = autoSeal ? TrySeal(risky, mainComp, important, importantInMain, pc) : "autoSeal 關閉，只報告不封口";
        }
        sbFile.AppendLine("封口結果：" + sealResult);

        string summary = "[石頭體檢] 檢查 " + rockCount + " 顆石頭、" + freeCount + " 個可游動格：風險點 " + shown.Count + " 個（針孔／口袋／死角尖端）。封口：" + sealResult;
        if (shown.Count == 0) Debug.Log(summary);
        else Debug.LogWarning(summary + "\n" + sbLog + (shown.Count > listed ? "  …還有 " + (shown.Count - listed) + " 個，完整名單看 Logs/rock_audit_underwater.txt" : ""));

        try
        {
            string dir = Path.Combine(Application.dataPath, "../Logs");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "rock_audit_underwater.txt"), sbFile.ToString());
        }
        catch (System.Exception e) { Debug.LogWarning("[石頭體檢] 名單檔寫入失敗：" + e.Message); }
    }

    // ── 工具 ─────────────────────────────────────────────
    class Risk
    {
        public string kind;
        public int size;
        public int merged;
        public List<int> cells = new List<int>();
        public Vector3 center;
        public List<string> rocks = new List<string>();
    }

    Vector3 CellCenter(int cx, int cy)
    {
        return new Vector3(_x0 + (cx + 0.5f) * _cell, _y0 + (cy + 0.5f) * _cell, _z);
    }

    int CellOf(Vector3 p)
    {
        int cx = Mathf.FloorToInt((p.x - _x0) / _cell), cy = Mathf.FloorToInt((p.y - _y0) / _cell);
        if (cx < 0 || cy < 0 || cx >= _w || cy >= _h) return -1;
        return cy * _w + cx;
    }

    void AddImportant(List<(string name, int cell)> list, string name, Vector3 p)
    {
        int cell = CellOf(p);
        if (cell < 0) return;
        // 重要位置本身可能剛好在碰撞邊上（格子不算可游動），就找旁邊最近的可游動格
        if (!_free[cell])
        {
            int best = -1; float bestD = float.MaxValue;
            int cx0 = cell % _w, cy0 = cell / _w;
            for (int dy = -3; dy <= 3; dy++)
                for (int dx = -3; dx <= 3; dx++)
                {
                    int x = cx0 + dx, y = cy0 + dy;
                    if (x < 0 || y < 0 || x >= _w || y >= _h || !_free[y * _w + x]) continue;
                    float d = dx * dx + dy * dy;
                    if (d < bestD) { bestD = d; best = y * _w + x; }
                }
            cell = best;
        }
        if (cell >= 0) list.Add((name, cell));
    }

    int FreeNeighbors(int cx, int cy)
    {
        int n = 0;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int x = cx + dx, y = cy + dy;
                if (x >= 0 && y >= 0 && x < _w && y < _h && _free[y * _w + x]) n++;
            }
        return n;
    }

    /// <summary>可游動格分塊（8 向相連）。回傳最大那一塊的編號（主要游動區）。</summary>
    int Label(List<int> compSize)
    {
        System.Array.Clear(_label, 0, _label.Length);
        compSize.Clear();
        compSize.Add(0);
        var queue = new Queue<int>();
        int bestComp = 0, bestSize = 0;
        for (int start = 0; start < _free.Length; start++)
        {
            if (!_free[start] || _label[start] != 0) continue;
            int id = compSize.Count;
            int size = 0;
            _label[start] = id;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                size++;
                int cx = cur % _w, cy = cur / _w;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int x = cx + dx, y = cy + dy;
                        if (x < 0 || y < 0 || x >= _w || y >= _h) continue;
                        int ni = y * _w + x;
                        if (!_free[ni] || _label[ni] != 0) continue;
                        _label[ni] = id;
                        queue.Enqueue(ni);
                    }
            }
            compSize.Add(size);
            if (size > bestSize) { bestSize = size; bestComp = id; }
        }
        return bestComp;
    }

    bool TouchesMainWithin(int[] bb, int pad, int mainComp)
    {
        int x0 = Mathf.Max(0, bb[0] - pad), y0 = Mathf.Max(0, bb[1] - pad);
        int x1 = Mathf.Min(_w - 1, bb[2] + pad), y1 = Mathf.Min(_h - 1, bb[3] + pad);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                if (_label[y * _w + x] == mainComp) return true;
        return false;
    }

    static Transform RockBody(Collider c)
    {
        // 碰撞是掛在 RockPlaneCollider 子物件上，真正的石頭本體是它的父物件
        return (c.gameObject.name == UnderwaterRockColliderHelper.PlaneColliderName && c.transform.parent != null) ? c.transform.parent : c.transform;
    }

    List<string> NearbyRocks(Vector3 p, PlayerMovement pm)
    {
        var names = new List<string>();
        foreach (Collider c in Physics.OverlapSphere(p, 1.6f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (c == null || c.transform.IsChildOf(pm.transform)) continue;
            string n = RockBody(c).name;
            if (!names.Contains(n)) names.Add(n);
            if (names.Count >= 5) break;
        }
        return names;
    }

    // ── 封口 ─────────────────────────────────────────────
    string TrySeal(List<Risk> risky, int mainComp, List<(string name, int cell)> important, HashSet<int> importantInMain, Collider pc)
    {
        // 要封的格子（風險點本身的格子；一個口袋可能很多格，最多取 MaxSeals 個代表）
        var sealCells = new List<int>();
        foreach (Risk r in risky)
        {
            int step = Mathf.Max(1, r.cells.Count / 12);
            for (int i = 0; i < r.cells.Count; i += step) sealCells.Add(r.cells[i]);
        }
        if (sealCells.Count == 0) return "沒有要封的格子";
        if (sealCells.Count > MaxSeals) return "風險點太多（" + sealCells.Count + " 個 > " + MaxSeals + "），不自動封口，請看名單人工處理";

        // 封口碰撞盒會讓玩家的碰撞盒擋在 (玩家半寬 + 封口半寬) 之外：先在格子上模擬，把受影響的格子標成不可游動
        float rx = _hx + SealCellSize * 0.5f, ry = _hy + SealCellSize * 0.5f;
        bool[] original = (bool[])_free.Clone();
        int[] originalLabel = (int[])_label.Clone();
        int mainBefore = 0;
        for (int i = 0; i < _label.Length; i++) if (_label[i] == mainComp) mainBefore++;

        int removedMain = 0;
        foreach (int sc in sealCells)
        {
            Vector3 f = CellCenter(sc % _w, sc / _w);
            int nx = Mathf.CeilToInt(rx / _cell), ny = Mathf.CeilToInt(ry / _cell);
            int fx = sc % _w, fy = sc / _w;
            for (int y = Mathf.Max(0, fy - ny); y <= Mathf.Min(_h - 1, fy + ny); y++)
                for (int x = Mathf.Max(0, fx - nx); x <= Mathf.Min(_w - 1, fx + nx); x++)
                {
                    Vector3 c = CellCenter(x, y);
                    if (Mathf.Abs(c.x - f.x) < rx && Mathf.Abs(c.y - f.y) < ry) _free[y * _w + x] = false;
                }
        }
        for (int i = 0; i < _free.Length; i++) if (original[i] && !_free[i] && originalLabel[i] == mainComp) removedMain++;

        var compSize2 = new List<int>();
        int newMain = Label(compSize2);
        int newMainSize = newMain > 0 ? compSize2[newMain] : 0;

        bool connected = newMainSize == mainBefore - removedMain;   // 沒被切成兩塊：剩下的主要游動區全部還連在一起
        var lost = new List<string>();
        foreach (int idx in importantInMain)
        {
            int cell = important[idx].cell;
            if (!_free[cell] || _label[cell] != newMain) lost.Add(important[idx].name);
        }

        if (!connected || lost.Count > 0)
        {
            System.Array.Copy(original, _free, _free.Length);
            System.Array.Copy(originalLabel, _label, _label.Length);
            return "模擬後會" + (!connected ? "把主要游動區切成兩塊" : "") + (lost.Count > 0 ? "擋掉重要位置：" + string.Join("、", lost.GetRange(0, Mathf.Min(5, lost.Count))) : "") + "，不自動封口，請看名單人工處理";
        }

        // 模擬通過 → 真的放碰撞盒
        GameObject root = new GameObject("[RockTrapSeal]");
        foreach (int sc in sealCells)
        {
            GameObject go = new GameObject("Seal");
            go.transform.SetParent(root.transform, false);
            go.transform.position = CellCenter(sc % _w, sc / _w);
            BoxCollider bc = go.AddComponent<BoxCollider>();
            bc.size = new Vector3(SealCellSize, SealCellSize, Mathf.Max(1f, _hz * 4f));
        }
        Physics.SyncTransforms();
        return "已封 " + sealCells.Count + " 處（模擬確認不會擋路，主要游動區少 " + removedMain + " 格、仍連成一塊，重要位置 " + importantInMain.Count + " 個全部保留）";
    }
}
