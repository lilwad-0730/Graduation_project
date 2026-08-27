using UnityEngine;

/// <summary>
/// 一張會捲起來的紙。
/// 用 CPU 變形網格，不寫 shader —— Built-in 和 URP 都能跑，不會因為算繪管線不同就變成粉紅色。
/// 局部座標：書脊在 x = 0，頁面往右延伸到 x = width；y 置中。
/// </summary>
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class PageCurl : MonoBehaviour
{
    [Header("網格密度")]
    [Range(8, 120)] public int segmentsX = 64;
    [Range(2, 40)] public int segmentsY = 14;

    [Header("尺寸")]
    public float width = 16f;
    public float height = 9f;

    [Header("捲曲")]
    [Tooltip("捲筒半徑。越小捲得越緊、越像薄紙")]
    public float curlRadius = 0.8f;

    [Tooltip("下緣比上緣鬆多少。0 = 整條一樣捲，1 = 下緣鬆一倍（比較像用手掀）")]
    [Range(0f, 1f)] public float taper = 0.5f;

    [Tooltip("捲過去那一面壓暗到多少。壓得夠暗就看不出是同一張圖的鏡像")]
    [Range(0f, 1f)] public float backDarkness = 0.18f;

    Mesh mesh;
    Vector3[] basePos, pos;
    Color[] cols;
    Vector2[] uvs;
    int[] tris;
    int builtX = -1, builtY = -1;

    public float FlatFold => width;                 // 完全攤平
    public float GoneFold => -width * 0.75f;        // 完全捲走、離開畫面

    void Awake() { EnsureMesh(); }
#if UNITY_EDITOR
    void OnValidate() { if (Application.isPlaying) { EnsureMesh(); SetFold(FlatFold); } }
#endif

    public void EnsureMesh()
    {
        if (mesh != null && builtX == segmentsX && builtY == segmentsY) return;

        builtX = segmentsX; builtY = segmentsY;
        int nx = segmentsX + 1, ny = segmentsY + 1;
        int n = nx * ny;

        basePos = new Vector3[n];
        pos = new Vector3[n];
        cols = new Color[n];
        uvs = new Vector2[n];

        for (int j = 0; j < ny; j++)
        {
            for (int i = 0; i < nx; i++)
            {
                float u = (float)i / segmentsX;
                float v = (float)j / segmentsY;
                int k = j * nx + i;
                basePos[k] = new Vector3(u * width, (v - 0.5f) * height, 0f);
                uvs[k] = new Vector2(u, v);
                cols[k] = Color.white;
            }
        }

        tris = new int[segmentsX * segmentsY * 6];
        int p = 0;
        for (int j = 0; j < segmentsY; j++)
        {
            for (int i = 0; i < segmentsX; i++)
            {
                int a = j * nx + i, b = a + 1, c = a + nx, d = c + 1;
                tris[p++] = a; tris[p++] = c; tris[p++] = b;
                tris[p++] = b; tris[p++] = c; tris[p++] = d;
            }
        }

        mesh = new Mesh { name = "PageCurl" };
        mesh.MarkDynamic();
        if (n > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = basePos;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.colors = cols;
        mesh.RecalculateBounds();

        GetComponent<MeshFilter>().sharedMesh = mesh;
        SetFold(FlatFold);
    }

    /// <summary>
    /// fold = 摺線在局部 x 的位置。
    /// fold >= width 完全攤平；fold 往負的走，紙從右緣一路捲起、越過書脊離開畫面。
    /// </summary>
    public void SetFold(float fold)
    {
        EnsureMesh();

        for (int k = 0; k < basePos.Length; k++)
        {
            Vector3 b = basePos[k];
            float d = b.x - fold;

            if (d <= 0f)
            {
                pos[k] = new Vector3(b.x, b.y, 0f);
                cols[k] = Color.white;
                continue;
            }

            // 下緣捲得比較鬆
            float vy = (b.y / height) + 0.5f;            // 0 = 下緣, 1 = 上緣
            float R = Mathf.Max(0.02f, curlRadius * (1f + taper * (1f - vy)));

            float th = d / R;
            float nx, nz;
            if (th <= Mathf.PI)
            {
                nx = fold + R * Mathf.Sin(th);
                nz = -R * (1f - Mathf.Cos(th));          // 往攝影機那一側掀起
            }
            else
            {
                nx = fold - (d - Mathf.PI * R);          // 翻過去之後平躺著往左走
                nz = -2f * R;
            }
            pos[k] = new Vector3(nx, b.y, nz);

            float s = Mathf.Clamp01(th / (Mathf.PI * 0.62f));
            float g = Mathf.Lerp(1f, backDarkness, s * s);
            cols[k] = new Color(g, g, g, 1f);
        }

        mesh.vertices = pos;
        mesh.colors = cols;
        mesh.RecalculateBounds();
    }
}
