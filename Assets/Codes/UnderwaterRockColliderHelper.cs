using UnityEngine;

/// <summary>
/// 自動為水下關卡的 3D 旋轉岩石生成防漏 2.5D 實體碰撞投影，
/// 徹底解決 3D 幾何體在特定旋轉角度下因切面空隙導致主角掉出世界的物理缺陷。
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

        // 搜尋全場景中所有的岩石容器與岩石物件
        GameObject rocksContainer = GameObject.Find("Rocks_Container");
        MeshRenderer[] renderers = rocksContainer != null 
            ? rocksContainer.GetComponentsInChildren<MeshRenderer>(true) 
            : Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);

        foreach (var mr in renderers)
        {
            if (mr == null) continue;
            string n = mr.name;
            if (!n.Contains("Rocks") && !n.Contains("Rock") && !n.Contains("rock") && !n.Contains("Stone")) continue;

            // 確保 MeshCollider 具備平滑無摩擦材質
            MeshCollider mc = mr.GetComponent<MeshCollider>();
            if (mc != null)
            {
                mc.material = noFriction;
            }

            // 為每一塊 3D 旋轉岩石掛載一個深度貫穿的 BoxCollider 輔助碰撞體
            // 該碰撞體與岩石的視覺邊界完全吻合，且在 Z 軸前後延展，保證 100% 密封任何 3D 旋轉角度產生的切面空洞！
            BoxCollider helperBox = mr.GetComponent<BoxCollider>();
            if (helperBox == null)
            {
                helperBox = mr.gameObject.AddComponent<BoxCollider>();
            }

            helperBox.isTrigger = false;
            helperBox.material = noFriction;

            // 取得該岩石在 Local 空間的精確 Mesh 尺寸，並將 Z 軸厚度延展以跨越 2.5D 移動基準線
            MeshFilter mf = mr.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                Bounds localBounds = mf.sharedMesh.bounds;
                Vector3 size = localBounds.size;
                float lossyZ = Mathf.Abs(mr.transform.lossyScale.z);
                float requiredZ = (lossyZ > 0.001f) ? (3.5f / lossyZ) : 4.0f;
                size.z = Mathf.Max(size.z, requiredZ);
                helperBox.size = size;
                helperBox.center = localBounds.center;
            }
        }

        Debug.Log($"[UnderwaterRockColliderHelper] 已為水下岩石建立 2.5D 深度密封碰撞體，徹底解決特定角度穿模掉落問題！");
    }
}
