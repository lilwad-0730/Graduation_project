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

    private void Start()
    {
        if (Application.isPlaying && autoGenerate)
        {
            Generate3DCollider();
        }
    }

    [ContextMenu("手動生成 3D 碰撞網格 (Generate)")]
    public void Generate3DCollider()
    {
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

        MeshCollider meshCollider = childObj.GetComponent<MeshCollider>();
        if (meshCollider == null)
        {
            meshCollider = childObj.AddComponent<MeshCollider>();
        }

        meshCollider.sharedMesh = newMesh;
        
        if (Application.isPlaying)
        {
            poly2D.enabled = false;
        }

        Debug.Log($"[{gameObject.name}] 已成功生成 3D 碰撞網格，放置於子物件 {childName} 中！");
    }
}
