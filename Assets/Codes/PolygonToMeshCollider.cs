using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(PolygonCollider2D))]
[ExecuteAlways]
public class PolygonToMeshCollider : MonoBehaviour
{
    [Header("3D 碰撞框設定")]
    [Tooltip("加厚的深度 (Z 軸)，建議設大一點 (如 3) 確保玩家不會漏踩")]
    public float depth = 3f;

    [Tooltip("勾選後會自動生成 (建議保持勾選)")]
    public bool autoGenerate = true;

    [Tooltip("★1008 autoGenerate 關著、但這個物件和子物件都沒有任何可用的 3D 碰撞體（也沒有人工放的 BoxCollider）時，執行期仍補生成一次，免得變成沒有實體的平台。\n" +
             "（10/07 存場景時，拉桿平台 Stone Step 的 autoGenerate 被關掉、Generated_3D_Collider 被停用，卻沒有像 slope1 那樣補人工碰撞體，玩家會直接穿過去）")]
    public bool generateIfNoCollider = true;

    private void Start()
    {
        if (Application.isPlaying && IsManualColliderManagedCloudSlope())
        {
            DisableGeneratedColliderChild();
            return;
        }

        if (Application.isPlaying && autoGenerate)
        {
            Generate3DCollider();
            return;
        }

        // ★1008 autoGenerate 關著又什麼碰撞體都沒有：補生成（有人工 BoxCollider 的 slope1、ChatGPT Image 不會進來）
        if (Application.isPlaying && generateIfNoCollider && !HasUsable3DCollider())
        {
            Debug.LogWarning($"[{gameObject.name}] autoGenerate 關著，但這個物件和子物件都沒有任何 3D 碰撞體：先補生成一次（場景裡要嘛把 autoGenerate 打開，要嘛放人工 BoxCollider）");
            Generate3DCollider();
        }
    }

    /// <summary>★1008 自己或子物件上有沒有「啟用、物件也啟用、有網格」的 3D 碰撞體（含 trigger）。</summary>
    private bool HasUsable3DCollider()
    {
        Collider[] cols = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            Collider c = cols[i];
            if (c == null || !c.enabled || !c.gameObject.activeInHierarchy) continue;
            MeshCollider mc = c as MeshCollider;
            if (mc != null && mc.sharedMesh == null) continue;
            return true;
        }
        return false;
    }

    [ContextMenu("手動生成 3D 碰撞網格 (Generate)")]
    public void Generate3DCollider()
    {
        if (IsManualColliderManagedCloudSlope())
        {
            DisableGeneratedColliderChild();
            return;
        }

        PolygonCollider2D poly2D = GetComponent<PolygonCollider2D>();
        if (poly2D == null || poly2D.pathCount == 0) return;

        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();

        float zFront = -depth / 2f;
        float zBack = depth / 2f;

        for (int p = 0; p < poly2D.pathCount; p++)
        {
            Vector2[] points = poly2D.GetPath(p);
            int pointCount = points.Length;
            int startIndex = vertices.Count;

            for (int i = 0; i < pointCount; i++)
            {
                vertices.Add(new Vector3(points[i].x, points[i].y, zFront));
            }
            for (int i = 0; i < pointCount; i++)
            {
                vertices.Add(new Vector3(points[i].x, points[i].y, zBack));
            }

            for (int i = 0; i < pointCount; i++)
            {
                int current = startIndex + i;
                int next = startIndex + ((i + 1) % pointCount);
                int currentBack = current + pointCount;
                int nextBack = next + pointCount;

                triangles.Add(current);
                triangles.Add(nextBack);
                triangles.Add(next);

                triangles.Add(current);
                triangles.Add(currentBack);
                triangles.Add(nextBack);

                triangles.Add(current);
                triangles.Add(next);
                triangles.Add(nextBack);

                triangles.Add(current);
                triangles.Add(nextBack);
                triangles.Add(currentBack);
            }
        }

        Mesh newMesh = new Mesh();
        newMesh.name = "ExtrudedColliderMesh";
        newMesh.vertices = vertices.ToArray();
        newMesh.triangles = triangles.ToArray();
        newMesh.RecalculateNormals();

        // 【解決衝突方案】：在子物件建立 MeshCollider，避免 2D 與 3D 碰撞器放在同一層報錯
        string childName = "Generated_3D_Collider";
        Transform childTransform = transform.Find(childName);
        GameObject childObj;

        if (childTransform != null)
        {
            childObj = childTransform.gameObject;
        }
        else
        {
            childObj = new GameObject(childName);
            childObj.transform.SetParent(transform, false);
            // 繼承父物件的 Layer，這樣才能正常發生碰撞！
            childObj.layer = gameObject.layer; 
        }

        // ★1008 場景裡存成停用的生成碰撞體（Stone Step 10/07 那次存檔）：既然要生成，就把它打開
        if (!childObj.activeSelf) childObj.SetActive(true);

        MeshCollider meshCollider = childObj.GetComponent<MeshCollider>();
        if (meshCollider == null)
        {
            meshCollider = childObj.AddComponent<MeshCollider>();
        }
        meshCollider.enabled = true;

        meshCollider.sharedMesh = newMesh;

        // ★1001 convex 只在「真的掛了 Rigidbody」時才強制開啟。
        //
        // Root Cause：原本這裡無條件 convex = true。Unity 的凸面 MeshCollider 用的是整個網格的
        // 【凸包】，而這些地形的 PolygonCollider2D 輪廓是凹的，凸包會從實際美術表面上方脹出去，
        // 玩家踩到的是凸包、看到的是美術，就變成浮空。實測場景裡的抬高量（上表面取樣的最大值）：
        //     slope1                      219 點 → 凸包 32 點   抬高 1.922
        //     ChatGPT Image …09_05_37_0   202 點 → 凸包 30 點   抬高 0.818
        //     Stone Step / Step2          132 點 → 凸包 29 點   抬高 0.022（頂面本來就平，所以幾乎沒差）
        // 這個排序與實機「哪些地形還有明顯空隙」完全一致。
        //
        // 原本註解寫的理由（非凸面 MeshCollider 會讓 PhysX 卡死）只在掛了 Rigidbody 時成立——
        // Unity 的限制是「Rigidbody 上的 MeshCollider 必須 convex」，靜態碰撞體可以是凹的。
        // slope1 與 ChatGPT Image 這類地形都沒有 Rigidbody，所以不需要犧牲形狀精度。
        // 唯一真的掛在剛體底下的是玩家的 Shield（父層有玩家的 Rigidbody），它會自動繼續走 convex。
        //
        // 另外：存檔的場景裡這 4 個 Generated_3D_Collider 的 m_Convex 本來就是 0（編輯器狀態正確），
        // 是 autoGenerate 在 Play Mode 重新生成時才被強制改成 true，所以這個問題只在執行時出現。
        bool needsConvex = GetComponentInParent<Rigidbody>() != null;
        meshCollider.convex = needsConvex;
        if (poly2D != null && poly2D.isTrigger)
        {
            meshCollider.isTrigger = true;
        }
        else if (transform.name.Contains("Shield") || transform.GetComponentInParent<PlayerShield>() != null)
        {
            meshCollider.isTrigger = true;
        }
        else
        {
            meshCollider.isTrigger = false;
        }
        
        if (Application.isPlaying)
        {
            poly2D.enabled = false;
        }

        Debug.Log($"[{gameObject.name}] 已成功生成 3D 碰撞網格，放置於子物件 {childName} 中！");
    }

    private bool IsManualColliderManagedCloudSlope()
    {
        string objectName = gameObject.name;
        SpriteRenderer spriteRenderer = GetComponent<SpriteRenderer>();
        string spriteName = spriteRenderer != null && spriteRenderer.sprite != null
            ? spriteRenderer.sprite.name
            : string.Empty;

        return objectName.Contains("09_05_37")
            || spriteName.Contains("09_05_37")
            || spriteName.Contains("09_05_46");
    }

    private void DisableGeneratedColliderChild()
    {
        Transform childTransform = transform.Find("Generated_3D_Collider");
        if (childTransform == null) return;

        MeshCollider meshCollider = childTransform.GetComponent<MeshCollider>();
        if (meshCollider != null)
        {
            meshCollider.enabled = false;
        }

        childTransform.gameObject.SetActive(false);
    }
}
