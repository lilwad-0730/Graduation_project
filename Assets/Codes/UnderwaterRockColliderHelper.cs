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

        // 搜尋全場景中所有的岩石
        MeshRenderer[] renderers = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);

        foreach (var mr in renderers)
        {
            if (mr == null) continue;
            string n = mr.name;
            if (!n.Contains("Rocks") && !n.Contains("Rock") && !n.Contains("rock") && !n.Contains("Stone")) continue;
            if (n.Contains("[EdgeSolidify]")) continue;

            // 1. 如果身上有過去自動新增的多餘 BoxCollider，將其移除，防止方塊尖角凸出堵死通道
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

            // 2. 確保擁有精準貼合網格的 MeshCollider
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

        Debug.Log("[UnderwaterRockColliderHelper] 已全面將水下岩石切換為精準 MeshCollider，完美貼合石頭表面，暢通狹窄通道！"
                  + (solidifyForeground ? $"（另將 {solidified} 顆前景石頭邊緣實體化，玩家不會再被石頭蓋住）" : ""));
    }
}
