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

        // 搜尋全場景中所有的岩石
        MeshRenderer[] renderers = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);

        foreach (var mr in renderers)
        {
            if (mr == null) continue;
            string n = mr.name;
            if (!n.Contains("Rocks") && !n.Contains("Rock") && !n.Contains("rock") && !n.Contains("Stone")) continue;
            if (n.Contains("[EdgeSolidify]")) continue;

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
                mc.enabled = true;
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

        Debug.Log("[UnderwaterRockColliderHelper] 已全面將水下岩石切換為精準 MeshCollider，完美貼合石頭表面，暢通狹窄通道！"
                  + (solidifyForeground ? $"（另將 {solidified} 顆前景石頭邊緣實體化，玩家不會再被石頭蓋住）" : "")
                  + $"　保留 BoxCollider：{noColliderMesh} 顆；完全沒有碰撞的石頭：{noColliderAtAll} 顆");
    }
}
