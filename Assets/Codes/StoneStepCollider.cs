using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 廢墟浮空石階的碰撞體（掛在每塊 Stone Step 上）。
/// 執行時依照石頭圖片的外形產生兩個 3D 碰撞體：
///   1. 站立面：一片薄板，放在石頭頂面的中間高度（腳才會踩在石面上，而不是踩在圖片最上緣）
///   2. 岩石本體：沿著石頭輪廓的凸面碰撞體，從側面或下方都穿不過去
/// 狼要不要穿過這塊石頭由 wolvesPassThrough 決定（包括之後才生出來的狼）。
/// 選取物件時 Scene 視窗會畫出綠色（站立面）與黃色（本體）的範圍。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
public class StoneStepCollider : MonoBehaviour
{
    [Header("站立面（單位：圖片的本地單位，會跟著物件縮放）")]
    [Tooltip("站立面從圖片最上緣往下沉多少（石頭頂面那條淺色石面約 0.5 高，0.25 ≈ 踩在石面中間）")]
    [Range(0f, 1f)] public float surfaceSink = 0.25f;
    [Tooltip("站立面的厚度")]
    [Range(0.1f, 1f)] public float surfaceThickness = 0.5f;
    [Tooltip("左右各往內縮多少（避免站在石頭圓角外的空氣上）")]
    [Range(0f, 1.5f)] public float edgeInset = 0.35f;

    [Header("岩石本體")]
    [Tooltip("要不要產生岩石本體碰撞（關掉就只剩站立面）")]
    public bool solidBody = true;

    [Header("狼")]
    [Tooltip("狼可以直接穿過這塊石頭（狼追擊時不會被石階擋住或卡在上面）")]
    public bool wolvesPassThrough = false;

    /// <summary>所有「狼可以穿過」的石階碰撞體；WolfEnemy 在 Start 時會把它們全部忽略。</summary>
    public static readonly List<Collider> WolfPassThroughColliders = new List<Collider>();

    [Header("前後厚度")]
    [Tooltip("碰撞體在 Z 軸的厚度（本地單位），要夠厚才能涵蓋玩家所在的 Z")]
    public float depth = 4f;

    private const string SurfaceName = "StepSurface_Collider";
    private const string BodyName = "StepBody_Collider";

    private void Awake()
    {
        if (!Application.isPlaying) return;
        if (!TryComputeShape(out Rect surface, out List<Vector2> hull)) return;

        List<Collider> created = new List<Collider>();

        BoxCollider box = GetChild(SurfaceName).AddComponent<BoxCollider>();
        box.center = new Vector3(surface.center.x, surface.center.y, 0f);
        box.size = new Vector3(surface.width, surface.height, depth);
        created.Add(box);

        if (solidBody && hull.Count >= 3)
        {
            MeshCollider mc = GetChild(BodyName).AddComponent<MeshCollider>();
            mc.sharedMesh = BuildPrism(hull, depth);
            mc.convex = true;
            created.Add(mc);
        }

        if (wolvesPassThrough)
        {
            _registered.AddRange(created);
            WolfPassThroughColliders.AddRange(created);
            // 已經跑過 Start 的狼（例如這塊石頭比較晚才產生）現在補忽略
            foreach (WolfEnemy wolf in FindObjectsByType<WolfEnemy>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                IgnoreForWolf(wolf);
        }
    }

    private readonly List<Collider> _registered = new List<Collider>();

    private void OnDestroy()
    {
        foreach (Collider c in _registered) WolfPassThroughColliders.Remove(c);
    }

    /// <summary>讓這隻狼忽略所有「狼可以穿過」的石階碰撞體。</summary>
    public static void IgnoreForWolf(WolfEnemy wolf)
    {
        if (wolf == null) return;
        WolfPassThroughColliders.RemoveAll(c => c == null);
        foreach (Collider wolfCol in wolf.GetComponentsInChildren<Collider>(true))
            foreach (Collider c in WolfPassThroughColliders)
                Physics.IgnoreCollision(wolfCol, c, true);
    }

    private GameObject GetChild(string childName)
    {
        Transform t = transform.Find(childName);
        if (t != null) Destroy(t.gameObject);
        GameObject go = new GameObject(childName);
        go.transform.SetParent(transform, false);
        go.layer = gameObject.layer;
        go.tag = gameObject.tag;
        return go;
    }

    /// <summary>從圖片的物理外形（Sprite Physics Shape）算出站立面和本體輪廓（本地單位）。</summary>
    private bool TryComputeShape(out Rect surface, out List<Vector2> hull)
    {
        surface = default;
        hull = null;
        SpriteRenderer sr = GetComponent<SpriteRenderer>();
        Sprite sprite = sr != null ? sr.sprite : null;
        if (sprite == null) return false;

        List<Vector2> points = new List<Vector2>();
        List<Vector2> shape = new List<Vector2>();
        for (int i = 0; i < sprite.GetPhysicsShapeCount(); i++)
        {
            shape.Clear();
            sprite.GetPhysicsShape(i, shape);
            points.AddRange(shape);
        }
        if (points.Count < 3)
        {
            Bounds b = sprite.bounds;
            points.Add(new Vector2(b.min.x, b.min.y));
            points.Add(new Vector2(b.max.x, b.min.y));
            points.Add(new Vector2(b.max.x, b.max.y));
            points.Add(new Vector2(b.min.x, b.max.y));
        }
        if (sr.flipX) for (int i = 0; i < points.Count; i++) points[i] = new Vector2(-points[i].x, points[i].y);
        if (sr.flipY) for (int i = 0; i < points.Count; i++) points[i] = new Vector2(points[i].x, -points[i].y);

        float top = float.MinValue, minX = float.MaxValue, maxX = float.MinValue;
        foreach (Vector2 p in points) { top = Mathf.Max(top, p.y); minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); }

        float surfaceTop = top - surfaceSink;
        float left = minX + edgeInset, right = maxX - edgeInset;
        if (right - left < 0.5f) { left = minX; right = maxX; }
        surface = Rect.MinMaxRect(left, surfaceTop - surfaceThickness, right, surfaceTop);

        // 本體：輪廓壓到站立面稍下方、左右夾在站立面寬度內，再取凸包（單一凸面不會卡腳）
        List<Vector2> clamped = new List<Vector2>(points.Count);
        float bodyTop = surfaceTop - 0.02f;
        foreach (Vector2 p in points)
            clamped.Add(new Vector2(Mathf.Clamp(p.x, left, right), Mathf.Min(p.y, bodyTop)));
        hull = ConvexHull(clamped);
        return true;
    }

    private static List<Vector2> ConvexHull(List<Vector2> pts)
    {
        List<Vector2> p = new List<Vector2>(pts);
        p.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
        List<Vector2> h = new List<Vector2>();
        for (int pass = 0; pass < 2; pass++)
        {
            int start = h.Count;
            for (int i = 0; i < p.Count; i++)
            {
                Vector2 q = pass == 0 ? p[i] : p[p.Count - 1 - i];
                while (h.Count >= start + 2 && Cross(h[h.Count - 2], h[h.Count - 1], q) <= 0f) h.RemoveAt(h.Count - 1);
                h.Add(q);
            }
            h.RemoveAt(h.Count - 1);
        }
        return h;   // 逆時針
    }

    private static float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);

    private static Mesh BuildPrism(List<Vector2> hull, float depth)
    {
        int n = hull.Count;
        float zf = -depth * 0.5f, zb = depth * 0.5f;
        Vector3[] v = new Vector3[n * 2];
        for (int i = 0; i < n; i++)
        {
            v[i] = new Vector3(hull[i].x, hull[i].y, zf);
            v[i + n] = new Vector3(hull[i].x, hull[i].y, zb);
        }
        List<int> tri = new List<int>();
        for (int i = 1; i < n - 1; i++)
        {
            tri.Add(0); tri.Add(i + 1); tri.Add(i);               // 前面
            tri.Add(n); tri.Add(n + i); tri.Add(n + i + 1);       // 後面
        }
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            tri.Add(i); tri.Add(j); tri.Add(j + n);
            tri.Add(i); tri.Add(j + n); tri.Add(i + n);
        }
        Mesh m = new Mesh { name = "StoneStepBody" };
        m.vertices = v;
        m.triangles = tri.ToArray();
        m.RecalculateBounds();
        m.RecalculateNormals();
        return m;
    }

    private void OnDrawGizmosSelected()
    {
        if (!TryComputeShape(out Rect s, out List<Vector2> hull)) return;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0.2f, 1f, 0.3f, 0.9f);
        Gizmos.DrawWireCube(new Vector3(s.center.x, s.center.y, 0f), new Vector3(s.width, s.height, depth));
        if (!solidBody || hull == null) return;
        Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.9f);
        for (int i = 0; i < hull.Count; i++) Gizmos.DrawLine(hull[i], hull[(i + 1) % hull.Count]);
    }
}
