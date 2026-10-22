using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 讓水下石頭更有層次：
///   1. 顏色變化：石頭不再全部同色，每顆隨機帶一點明暗與冷暖差（用幾個材質變體，不影響合批）
///   2. 遠景石層：在後方遠處撒一層較暗的石頭剪影，會被霧吃掉，並以視差慢慢移動 → 前、中、後景分開
/// 執行時才建立，離開 Play 就消失；不改場景裡原本的石頭（遠景石頭沒有碰撞）。
/// 放在 underwater 場景任一物件上（場景裡的「UnderwaterRockLayers」）。
/// </summary>
public class UnderwaterRockLayers : MonoBehaviour
{
    [Header("來源")]
    [Tooltip("原本的石頭都放在這底下（預設自動找名為 Rocks_Container 的物件）")]
    public Transform rocksRoot;
    [Tooltip("留空自動用 Main Camera")]
    public Camera cam;
    [Tooltip("遠景石頭不要擋到的物件（例如背景巨獸），只用來提醒 Z 的範圍，不會被改動")]
    public float keepBehindZ = 10f;

    [Header("1. 石頭顏色變化")]
    public bool useColorVariation = true;
    [Range(2, 8)] public int variantCount = 5;
    [Tooltip("明暗變化幅度 (0.15 = ±15%)")]
    [Range(0f, 0.5f)] public float brightnessJitter = 0.16f;
    [Tooltip("冷暖變化幅度 (偏青 ↔ 偏紫)")]
    [Range(0f, 0.3f)] public float hueJitter = 0.06f;

    [Header("2. 遠景石層")]
    public bool useBackdrop = true;
    [Tooltip("遠景石頭的 Z 範圍 (越大越遠、越被霧吃掉)。要比 keepBehindZ 大，才不會擋到背景巨獸")]
    public Vector2 backdropZ = new Vector2(17f, 26f);
    [Tooltip("撒石頭的格子大小 (公尺)，越小越密")]
    public float cellSize = 6f;
    [Tooltip("每一格放石頭的機率")]
    [Range(0f, 1f)] public float density = 0.6f;
    [Tooltip("遠景石頭的大小倍率範圍")]
    public Vector2 backdropScale = new Vector2(1.8f, 4f);
    [Tooltip("遠景石頭的顏色倍率 (越小越暗，像剪影)")]
    public Color backdropTint = new Color(0.28f, 0.34f, 0.45f);
    [Tooltip("視差：鏡頭移動時遠景跟著移動的比例 (0 = 不動、跟前景一樣；越大越像在很遠的地方)")]
    [Range(0f, 0.9f)] public float parallax = 0.35f;
    [Tooltip("亂數種子，固定才會每次都長一樣")]
    public int seed = 1021;

    private Transform _backdropRoot;
    private Vector3 _camStart;
    private bool _camStartSet;
    private readonly List<Material> _runtimeMats = new List<Material>();

    private static readonly int IdBaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly int IdColor = Shader.PropertyToID("_Color");

    private void Start()
    {
        if (cam == null) cam = Camera.main;
        if (rocksRoot == null)
        {
            GameObject go = GameObject.Find("Rocks_Container");
            if (go != null) rocksRoot = go.transform;
        }
        if (rocksRoot == null) { Debug.LogWarning("【水下石層】找不到 Rocks_Container，石頭層次沒套用"); return; }

        MeshRenderer[] rocks = rocksRoot.GetComponentsInChildren<MeshRenderer>(true);
        if (useColorVariation) ApplyColorVariation(rocks);
        if (useBackdrop) BuildBackdrop(rocks);
    }

    // ───────── 1. 顏色變化 ─────────
    private void ApplyColorVariation(MeshRenderer[] rocks)
    {
        var variants = new Dictionary<Material, Material[]>();
        foreach (MeshRenderer mr in rocks)
        {
            Material src = mr.sharedMaterial;
            if (src == null || !HasColor(src)) continue;
            if (!variants.TryGetValue(src, out Material[] list))
            {
                list = new Material[variantCount];
                var rng = new System.Random(seed + src.name.GetHashCode());
                for (int i = 0; i < variantCount; i++)
                {
                    float b = 1f + Mathf.Lerp(-brightnessJitter, brightnessJitter, (float)rng.NextDouble());
                    float h = Mathf.Lerp(-hueJitter, hueJitter, (float)rng.NextDouble());
                    Material m = new Material(src) { name = src.name + " (var " + i + ")" };
                    SetColor(m, Shift(GetColor(src), b, h));
                    list[i] = m;
                    _runtimeMats.Add(m);
                }
                variants[src] = list;
            }
            // 用位置決定變體，固定不會每次不同
            Vector3 p = mr.transform.position;
            int idx = Mathf.Abs(Mathf.FloorToInt(p.x * 7.13f) * 31 + Mathf.FloorToInt(p.y * 3.71f) * 17 + Mathf.FloorToInt(p.z * 5.3f)) % variantCount;
            mr.sharedMaterial = list[idx];
        }
    }

    private static Color Shift(Color c, float brightness, float hue)
    {
        Color.RGBToHSV(c, out float h, out float s, out float v);
        h = Mathf.Repeat(h + hue, 1f);
        v = Mathf.Clamp01(v * brightness);
        Color o = Color.HSVToRGB(h, s, v);
        o.a = c.a;
        return o;
    }

    // ───────── 2. 遠景石層 ─────────
    private void BuildBackdrop(MeshRenderer[] rocks)
    {
        var meshes = new List<Mesh>();
        Material srcMat = null;
        Bounds b = default;
        bool any = false;
        foreach (MeshRenderer mr in rocks)
        {
            MeshFilter mf = mr.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            if (!meshes.Contains(mf.sharedMesh)) meshes.Add(mf.sharedMesh);
            if (srcMat == null && mr.sharedMaterial != null) srcMat = mr.sharedMaterial;
            if (!any) { b = mr.bounds; any = true; } else b.Encapsulate(mr.bounds);
        }
        if (!any || srcMat == null) return;

        // 遠景用一個偏暗、偏藍的材質
        Material dark = new Material(srcMat) { name = "Mat_BackdropRock (runtime)" };
        if (HasColor(dark)) SetColor(dark, GetColor(dark) * backdropTint);
        _runtimeMats.Add(dark);

        _backdropRoot = new GameObject("UnderwaterBackdrop").transform;
        _backdropRoot.SetParent(transform, false);

        // 範圍 = 關卡範圍 + 半個畫面 (視差移動後也蓋得到)
        float halfH = cam != null && cam.orthographic ? cam.orthographicSize + 2f : 10f;
        float halfW = halfH * (cam != null ? cam.aspect : 1.78f);
        float minX = b.min.x - halfW, maxX = b.max.x + halfW;
        float minY = b.min.y - halfH, maxY = b.max.y + halfH;

        float zMin = Mathf.Max(backdropZ.x, keepBehindZ + 1f);
        float zMax = Mathf.Max(zMin, backdropZ.y);
        var rng = new System.Random(seed);
        int made = 0;
        for (float x = minX; x < maxX; x += cellSize)
        {
            for (float y = minY; y < maxY; y += cellSize)
            {
                if (rng.NextDouble() > density) continue;
                Mesh mesh = meshes[rng.Next(meshes.Count)];
                // 名稱刻意不含 Rock/Stone：UnderwaterRockColliderHelper 等腳本會用名稱找石頭並加碰撞，遠景不能有碰撞
                GameObject go = new GameObject("BackdropShape");
                go.transform.SetParent(_backdropRoot, false);
                go.transform.position = new Vector3(
                    x + (float)rng.NextDouble() * cellSize,
                    y + (float)rng.NextDouble() * cellSize,
                    Mathf.Lerp(zMin, zMax, (float)rng.NextDouble()));
                go.transform.rotation = Quaternion.Euler((float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 360f);
                float s = Mathf.Lerp(backdropScale.x, backdropScale.y, (float)rng.NextDouble());
                go.transform.localScale = new Vector3(s, s * Mathf.Lerp(0.7f, 1.3f, (float)rng.NextDouble()), s);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = dark;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                made++;
            }
        }
        Debug.Log("【水下石層】遠景石頭 " + made + " 顆，Z " + zMin + "~" + zMax);
    }

    private void LateUpdate()
    {
        if (_backdropRoot == null) return;
        if (cam == null) cam = Camera.main;
        if (cam == null) return;
        Vector3 c = cam.transform.position;
        if (!_camStartSet) { _camStart = c; _camStartSet = true; }
        Vector3 d = (c - _camStart) * parallax;
        _backdropRoot.position = new Vector3(d.x, d.y, 0f);
    }

    private void OnDestroy()
    {
        foreach (Material m in _runtimeMats) if (m != null) Destroy(m);
        _runtimeMats.Clear();
    }

    private static bool HasColor(Material m) => m.HasProperty(IdBaseColor) || m.HasProperty(IdColor);
    private static Color GetColor(Material m) => m.HasProperty(IdBaseColor) ? m.GetColor(IdBaseColor) : m.GetColor(IdColor);
    private static void SetColor(Material m, Color c)
    {
        if (m.HasProperty(IdBaseColor)) m.SetColor(IdBaseColor, c);
        if (m.HasProperty(IdColor)) m.SetColor(IdColor, c);
    }
}
