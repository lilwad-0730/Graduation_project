using UnityEngine;

/// <summary>
/// 水下岩石精準碰撞管理器 (Underwater Rock Collider Helper)
/// 1. 【100% 貼合岩石表面】：全面採用 MeshCollider (使用岩石自身 3D 網格)，完美貼合岩石每個有機弧度與縫隙，絕無多餘方塊凸起！
/// 2. 【清理多餘方塊碰撞體】：自動清理冗餘的 BoxCollider，徹底消除「透明空氣牆」堵住通道的問題。
/// 3. 【無摩擦力平滑物理】：確保所有岩石碰撞體賦予平滑無摩擦材質，主角滑動遊行完全不卡角。
/// </summary>
public class UnderwaterRockColliderHelper : MonoBehaviour
{
    private void Awake()
    {
        SealUnderwaterRockGaps();
    }

    public static void SealUnderwaterRockGaps()
    {
        PhysicsMaterial noFriction = new PhysicsMaterial("RockSlideMaterial")
        {
            dynamicFriction = 0f,
            staticFriction = 0f,
            frictionCombine = PhysicsMaterialCombine.Minimum,
            bounciness = 0f,
            bounceCombine = PhysicsMaterialCombine.Minimum
        };

        // 搜尋全場景中所有的岩石
        MeshRenderer[] renderers = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);

        foreach (var mr in renderers)
        {
            if (mr == null) continue;
            string n = mr.name;
            if (!n.Contains("Rocks") && !n.Contains("Rock") && !n.Contains("rock") && !n.Contains("Stone")) continue;

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
        }

        Debug.Log("[UnderwaterRockColliderHelper] 已全面將水下岩石切換為精準 MeshCollider，完美貼合石頭表面，暢通狹窄通道！");
    }
}
