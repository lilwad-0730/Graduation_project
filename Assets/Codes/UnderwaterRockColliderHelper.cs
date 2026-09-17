using UnityEngine;

/// <summary>
/// 水下岩石精準碰撞管理器 (Underwater Rock Collider Helper)
/// 1. 【100% 貼合岩石表面】：全面採用 MeshCollider (使用岩石自身 3D 網格)，完美貼合岩石每個有機弧度與縫隙，絕無多餘方塊凸起！
/// 2. 【清理多餘方塊碰撞體】：自動清理冗餘的 BoxCollider，徹底消除「透明空氣牆」堵住通道的問題。
/// 3. 【無摩擦力平滑物理】：確保所有岩石碰撞體賦予平滑無摩擦材質，主角滑動遊行完全不卡角。
/// 4. 【前景石頭邊緣實體化】：整顆位於玩家平面前方（更靠近鏡頭）的石頭，把它的形狀複製一份
///    移到玩家所在的 Z 平面上當隱形實體，玩家就游不進石頭「後面」、不會被石頭整顆蓋住。
/// </summary>
public class UnderwaterRockColliderHelper : MonoBehaviour
{
    [Tooltip("是否把整顆在玩家前方的石頭邊緣實體化（複製形狀到玩家平面），防止玩家被石頭蓋住")]
    public bool solidifyForegroundRocks = true;

    private void Awake()
    {
        SealUnderwaterRockGaps(solidifyForegroundRocks);
    }

    public static void SealUnderwaterRockGaps(bool solidifyForeground = true)
    {
        PhysicsMaterial noFriction = new PhysicsMaterial("RockSlideMaterial")
        {
            dynamicFriction = 0f,
            staticFriction = 0f,
            frictionCombine = PhysicsMaterialCombine.Minimum,
            bounciness = 0f,
            bounceCombine = PhysicsMaterialCombine.Minimum
        };

        // 玩家所在的 Z 平面（找不到玩家時用預設 -0.4）
        float playerZ = -0.4f;
        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj == null)
        {
            PlayerMovement pm = Object.FindFirstObjectByType<PlayerMovement>();
            if (pm != null) playerObj = pm.gameObject;
        }
        if (playerObj != null) playerZ = playerObj.transform.position.z;

        int solidified = 0;
        int noColliderMesh = 0;
        int noColliderAtAll = 0;
        int matchedRocks = 0;   // 名稱篩選實際抓到幾顆石頭 (0 代表關鍵字對不上，整支腳本等於沒作用)

        // 搜尋全場景中所有的岩石
        MeshRenderer[] renderers = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);

        foreach (var mr in renderers)
        {
            if (mr == null) continue;
            string n = mr.name;
            if (!n.Contains("Rocks") && !n.Contains("Rock") && !n.Contains("rock") && !n.Contains("Stone")) continue;
            if (n.Contains("[EdgeSolidify]")) continue;

            matchedRocks++;

            // 1. 先把精準貼合網格的 MeshCollider 準備好
            MeshCollider mc = mr.GetComponent<MeshCollider>();
            if (mc == null)
            {
                mc = mr.gameObject.AddComponent<MeshCollider>();
            }

            MeshFilter mf = mr.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null && mc.sharedMesh == null)
            {
                mc.sharedMesh = mf.sharedMesh;
            }

            if (mc != null)
            {
                mc.material = noFriction;
                // 已經把碰撞搬到玩家平面的石頭，原本那份要保持關閉，不然兩份碰撞疊在一起
                mc.enabled = mr.transform.Find(PlaneColliderName) == null;
            }

            bool meshColliderUsable = mc != null && mc.sharedMesh != null;

            // 2. 確認 MeshCollider 真的可用之後，才移除多餘的 BoxCollider
            //    ★ 原本是「先刪 BoxCollider、再補 MeshCollider」，
            //      若這顆石頭補不到 mesh (例如網格不在同一層)，就會變成
            //      「BoxCollider 已經刪掉、MeshCollider 卻是空的」＝完全沒有碰撞，
            //      玩家會直接穿過石頭墜落。
            if (meshColliderUsable)
            {
                BoxCollider[] boxes = mr.GetComponents<BoxCollider>();
                foreach (var b in boxes)
                {
                    // 若非自定義 Trigger，安全移除
                    if (!b.isTrigger)
                    {
                        if (Application.isPlaying)
                        {
                            Destroy(b);
                        }
                        else
                        {
                            DestroyImmediate(b);
                        }
                    }
                }
            }
            else
            {
                // 補不到 mesh：保留原本的 BoxCollider 當作實體，並把空的 MeshCollider 關掉
                if (mc != null) mc.enabled = false;
                noColliderMesh++;
                Debug.LogWarning($"[UnderwaterRockColliderHelper] 石頭「{n}」找不到可用的網格，" +
                                 "保留原本的 BoxCollider 避免變成沒有碰撞的空殼。");
            }

            // 3. 前景石頭邊緣實體化：整顆石頭都在玩家平面前方（更靠近鏡頭）時，
            //    玩家可以游到它「後面」被整顆遮住。把石頭形狀複製一份移到玩家平面上，
            //    讓玩家頂多貼著石頭邊緣，不會躲進石頭後面消失。
            if (solidifyForeground && mf != null && mf.sharedMesh != null)
            {
                Bounds wb = mr.bounds;
                if (wb.max.z < playerZ - 0.05f && mr.transform.Find("[EdgeSolidify]") == null)
                {
                    GameObject edge = new GameObject("[EdgeSolidify]");
                    edge.transform.SetParent(mr.transform, false);
                    edge.transform.position = mr.transform.position + new Vector3(0f, 0f, playerZ - wb.center.z);
                    MeshCollider emc = edge.AddComponent<MeshCollider>();
                    emc.sharedMesh = mf.sharedMesh;
                    emc.material = noFriction;
                    solidified++;
                }
            }
        }

        // ★0916 卡石根治：把碰撞搬到石頭「最厚的那一層」
        int aligned = AlignRockCollidersToPlayerPlane(renderers, playerZ, noFriction);

        // 最後掃一遍：列出「完全沒有有效實體碰撞」的石頭。
        // 玩家穿過石頭墜落，最直接的原因就是那顆石頭根本沒有能擋住她的碰撞體。
        foreach (var mr in renderers)
        {
            if (mr == null) continue;
            string nm = mr.name;
            if (!nm.Contains("Rocks") && !nm.Contains("Rock") && !nm.Contains("rock") && !nm.Contains("Stone")) continue;
            if (nm.Contains("[EdgeSolidify]")) continue;

            bool hasSolid = false;
            foreach (var c in mr.GetComponentsInChildren<Collider>(true))
            {
                if (c == null || !c.enabled || c.isTrigger) continue;
                if (c is MeshCollider meshCol && meshCol.sharedMesh == null) continue;
                hasSolid = true;
                break;
            }

            if (!hasSolid)
            {
                noColliderAtAll++;
                Debug.LogError($"[UnderwaterRockColliderHelper] ⚠️ 石頭「{nm}」沒有任何有效的實體碰撞體，" +
                               "玩家會直接穿過去墜落！請在 Inspector 補上碰撞體。");
            }
        }

        Debug.Log($"[UnderwaterRockColliderHelper] 掃描 {renderers.Length} 個 MeshRenderer，" +
                  $"名稱符合石頭關鍵字的有 {matchedRocks} 顆。"
                  + (solidifyForeground ? $"　前景石頭邊緣實體化：{solidified} 顆；" : "　")
                  + $"保留 BoxCollider：{noColliderMesh} 顆；完全沒有碰撞的石頭：{noColliderAtAll} 顆；"
                  + $"碰撞搬到玩家平面：{aligned} 顆");

        // ★ 2.5D 深度對不上的診斷：
        //   石頭是 3D 網格，玩家被鎖在單一 Z 平面上。
        //   如果某顆石頭的網格所在的 Z 範圍跟玩家的 Z 範圍完全沒有交集，
        //   畫面上看起來擋在路中間，物理上卻在不同深度——玩家會直接穿過去墜落。
        //   既有的前景實體化只處理「整顆在玩家前方」，不處理「整顆在玩家後方」。
        float playerHalfZ = 0.5f;
        if (playerObj != null)
        {
            Collider pc = playerObj.GetComponent<Collider>();
            if (pc == null) pc = playerObj.GetComponentInChildren<Collider>();
            if (pc != null) playerHalfZ = Mathf.Max(0.05f, pc.bounds.extents.z);
        }
        float bandMin = playerZ - playerHalfZ;
        float bandMax = playerZ + playerHalfZ;

        int offPlane = 0;
        string offPlaneNames = "";
        foreach (var mr in renderers)
        {
            if (mr == null) continue;
            string nm = mr.name;
            if (!nm.Contains("Rocks") && !nm.Contains("Rock") && !nm.Contains("rock") && !nm.Contains("Stone")) continue;
            if (nm.Contains("[EdgeSolidify]")) continue;

            Bounds b = mr.bounds;
            if (b.max.z < bandMin || b.min.z > bandMax)
            {
                offPlane++;
                if (offPlane <= 10)
                {
                    offPlaneNames += $"\n  ・{nm}　(石頭 Z: {b.min.z:F2}~{b.max.z:F2})";
                }
            }
        }

        Debug.Log($"[UnderwaterRockColliderHelper] 玩家碰撞的 Z 範圍：{bandMin:F2} ~ {bandMax:F2}。" +
                  $"與此範圍完全沒有交集的石頭：{offPlane} / {matchedRocks} 顆。" +
                  (offPlane > 0
                      ? "　這些石頭畫面上看得到、但物理上不在玩家的深度，玩家會直接穿過去："
                        + offPlaneNames + (offPlane > 10 ? $"\n  ...(還有 {offPlane - 10} 顆)" : "")
                      : "　所有石頭的深度都涵蓋到玩家平面，不是深度對不上的問題。"));

        // 掛上穿模探針：玩家一旦插進石頭裡就回報是哪一顆
        if (playerObj != null && playerObj.GetComponent<UnderwaterPenetrationProbe>() == null)
        {
            playerObj.AddComponent<UnderwaterPenetrationProbe>();
        }

        CheckImportantPointsNotInsideRocks(playerZ, playerObj);

        if (matchedRocks == 0)
        {
            Debug.LogWarning("[UnderwaterRockColliderHelper] ⚠️ 一顆石頭都沒抓到！" +
                             "這支腳本是用名稱關鍵字 (Rocks / Rock / rock / Stone) 篩選的，" +
                             "如果場景裡的石頭不是這樣命名，整支腳本等於完全沒有作用，" +
                             "所有碰撞修正與前景實體化都不會發生。");
        }
    }

    // ─────────────────────────────────────────────────────────────
    // ★0916 卡石根治
    //
    // 為什麼好幾顆石頭都會把玩家吃進去：
    //   石頭是隨機轉向的 3D 網格，全部擺在 Z≈-1.3（Rocks_Container 還把 Z 拉長 2 倍），
    //   網格的 Z 範圍大約是 -5.2 ~ +0.4；玩家卻被鎖在 Z=-0.4。
    //   等於玩家走的那一層，是切在石頭「背面收尾」的地方（離最後面只剩 0.4～1.2 公尺）。
    //   那一帶的石頭表面幾乎是正對鏡頭／背對鏡頭的，表面朝向是 ±Z。
    //   玩家撞上去時，物理引擎沿著表面朝向把她往 ±Z 推——但玩家的 Z 是鎖死的（FreezePositionZ），
    //   推力全部被吃掉，X/Y 方向沒有任何東西把她推出來，她就一路陷進石頭裡，穿過殼就出不來。
    //   所以不是某幾顆石頭壞掉，是所有石頭的擺法都一樣，只是剛好游到的那幾顆先中。
    //
    // 修法：畫面（MeshRenderer）完全不動，只把「碰撞」複製一份往 Z 挪，
    //   讓玩家那一層正好切在石頭的中段（最厚、表面朝向是上下左右的地方），
    //   撞上去時推力是 X/Y 方向，會正常把她推出來；原本那份碰撞關掉。
    //   中段的剖面也更接近畫面上看到的石頭輪廓（原本切在收尾處，剖面比畫面小一圈，玩家看起來會陷進石頭圖裡）。
    // ─────────────────────────────────────────────────────────────
    public const string PlaneColliderName = "RockPlaneCollider";

    private static int AlignRockCollidersToPlayerPlane(MeshRenderer[] renderers, float playerZ, PhysicsMaterial mat)
    {
        int aligned = 0;
        foreach (var mr in renderers)
        {
            if (mr == null) continue;
            string n = mr.name;
            if (!n.Contains("Rocks") && !n.Contains("Rock") && !n.Contains("rock") && !n.Contains("Stone")) continue;
            if (n.Contains("[EdgeSolidify]")) continue;
            if (mr.transform.Find(PlaneColliderName) != null) continue;   // 已經搬過（重複呼叫）

            MeshCollider mc = mr.GetComponent<MeshCollider>();
            MeshFilter mf = mr.GetComponent<MeshFilter>();
            if (mc == null || !mc.enabled || mc.convex || mc.sharedMesh == null || mf == null) continue;

            float dz = playerZ - mr.bounds.center.z;
            if (Mathf.Abs(dz) < 0.05f) continue;   // 本來就切在中段，不用搬

            GameObject go = new GameObject(PlaneColliderName);
            go.layer = mr.gameObject.layer;
            go.tag = mr.gameObject.tag;
            go.transform.SetParent(mr.transform, false);
            go.transform.position = mr.transform.position + new Vector3(0f, 0f, dz);

            MeshCollider copy = go.AddComponent<MeshCollider>();
            copy.sharedMesh = mc.sharedMesh;
            copy.material = mat;
            copy.cookingOptions = mc.cookingOptions;

            mc.enabled = false;
            aligned++;
        }
        Physics.SyncTransforms();
        return aligned;
    }

    /// <summary>
    /// 碰撞變成中段剖面後比原本大一圈。檢查重要的點（玩家起點、日誌、存檔點、收集物等觸發區）
    /// 有沒有剛好被包進石頭裡，有的話印出來——那些東西會拿不到或卡住。
    /// </summary>
    private static void CheckImportantPointsNotInsideRocks(float playerZ, GameObject playerObj)
    {
        var points = new System.Collections.Generic.List<(string name, Vector3 pos)>();
        if (playerObj != null) points.Add(("玩家起點", playerObj.transform.position));

        foreach (var c in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
        {
            if (c == null || !c.isTrigger || !c.enabled) continue;
            if (Mathf.Abs(c.bounds.center.z - playerZ) > 3f) continue;
            if (c.bounds.size.x > 30f || c.bounds.size.y > 30f) continue;   // 大範圍區域觸發器（水域、BGM 區）不用檢查
            points.Add((c.gameObject.name, new Vector3(c.bounds.center.x, c.bounds.center.y, playerZ)));
        }
        foreach (var gl in Object.FindObjectsByType<GuidanceLight>(FindObjectsSortMode.None))
        {
            if (gl == null || gl.waypoints == null) continue;
            foreach (var wp in gl.waypoints)
            {
                if (wp != null) points.Add(($"光絮路徑點 {wp.name}", new Vector3(wp.position.x, wp.position.y, playerZ)));
            }
        }

        Vector3[] dirs = { Vector3.right, Vector3.left, Vector3.up, Vector3.down };
        int bad = 0;
        string list = "";
        bool old = Physics.queriesHitBackfaces;
        try
        {
            foreach (var p in points)
            {
                // 4 個方向裡至少 3 個「最近的東西是石頭碰撞的內側」＝這個點在石頭裡面
                var inside = new System.Collections.Generic.Dictionary<Collider, int>();
                foreach (var d in dirs)
                {
                    Physics.queriesHitBackfaces = true;
                    RaycastHit best = default;
                    float bestDist = float.MaxValue;
                    foreach (var h in Physics.RaycastAll(p.pos, d, 40f, ~0, QueryTriggerInteraction.Ignore))
                    {
                        if (h.collider == null) continue;
                        if (playerObj != null && h.collider.transform.IsChildOf(playerObj.transform)) continue;
                        if (h.distance < bestDist) { bestDist = h.distance; best = h; }
                    }
                    if (best.collider == null || best.collider.gameObject.name != PlaneColliderName) continue;

                    Physics.queriesHitBackfaces = false;
                    bool front = best.collider.Raycast(new Ray(p.pos, d), out RaycastHit fh, bestDist + 0.02f)
                                 && Mathf.Abs(fh.distance - bestDist) < 0.02f;
                    if (front) continue;
                    inside.TryGetValue(best.collider, out int k);
                    inside[best.collider] = k + 1;
                }
                foreach (var kv in inside)
                {
                    if (kv.Value < 3) continue;
                    bad++;
                    if (bad <= 15) list += $"\n  ・{p.name} {p.pos} 在石頭「{kv.Key.transform.parent.name}」裡面";
                    break;
                }
            }
        }
        finally
        {
            Physics.queriesHitBackfaces = old;
        }

        if (bad == 0)
            Debug.Log($"[UnderwaterRockColliderHelper] 檢查 {points.Count} 個重要位置（起點、日誌、存檔點、收集物、光絮路徑點），碰撞搬移後沒有任何一個被包進石頭裡。");
        else
            Debug.LogError($"[UnderwaterRockColliderHelper] ⚠️ 碰撞搬移後，有 {bad} 個重要位置被包進石頭裡（可能拿不到或卡住），請把這段貼給 Claude：" + list +
                           (bad > 15 ? $"\n  ...(還有 {bad - 15} 個)" : ""));
    }
}
