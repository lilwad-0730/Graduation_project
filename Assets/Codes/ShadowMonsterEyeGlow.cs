using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ★1116 玻璃館書頭怪：眼睛透出來的紅光。
///
/// ★1118e 改版（M：「紅色眼睛的光暈還是太大，只要微微發光；光一定要貼在眼球上，不能離眼球很遠」）：
///   原本的光是半圓，貼在眼前那本書的封面上（光暈 9 公尺寬、核心 1.7 公尺、點光源整顆頭染紅），離真正的眼球很遠。
///   現在：
///   ・每一幀從模型的 Eye 網格（SkinnedMeshRenderer）算出眼球的真正世界位置——
///     逐頂點手動蒙皮（骨頭世界矩陣 × bindpose，跟 MonsterArmShedding／最後死亡演出量身體一樣，不用 BakeMesh），
///     網格裡如果有兩顆眼睛，取離鏡頭近的那顆（看得到的那一側）；眼球半徑也從頂點範圍量。
///     所以走路、跑、揮爪、被打、倒下，紅光都釘在眼球上。
///   ・光（核心＋光暈）的圓心就是眼球的畫面位置；只沿視線往鏡頭拉（正交視角下畫面位置不變），拉到怪物最靠近鏡頭的面前，
///     不會被頭、書擋住。
///   ・大小：核心≈眼球（約 0.7 公尺），光暈只比眼球大一點（約 1.7 公尺），邊緣柔、透明度低，是微光不是一大塊；
///     形狀是圓的（半圓是給書封面用的，現在不在封面上了）。閃爍保留但很小。
///   ・點光源：強度低、範圍小，只染到眼睛附近一點。
///   ・AnchorWorld ＝ 眼球位置（熄燈黑幕在這裡開洞）。
/// 眼睛網格仍然不畫（ShadowMonsterDread 的紅線）——頂點照樣跟著頭骨動，所以量得到。
/// 透明度跟著怪物（ShadowMonsterController.VisualAlpha）：看不見的時候紅光也不見。
/// boost：GlassFinaleBlackout 熄燈時調高，黑暗裡只剩這一點紅光。
/// 由 ShadowMonsterController.EnsureInitialized 自動掛上，不用改場景。
/// </summary>
// ★1118e 要排在 ShadowMonsterDread（1000，LateUpdate 裡抽動頭骨）之後：不然讀到的是頭骨抽動「前」的姿勢，紅光會差一個抽動的距離
[DefaultExecutionOrder(2000)]
[DisallowMultipleComponent]
public class ShadowMonsterEyeGlow : MonoBehaviour
{
    public static bool Enabled = true;

    [Header("顏色與亮度")]
    public Color coreColor = new Color(1f, 0.2f, 0.1f, 0.7f);
    public Color haloColor = new Color(0.9f, 0.04f, 0.04f, 0.28f);
    [Tooltip("點光源強度（只染眼睛附近一點）")]
    public float lightIntensity = 3f;
    [Tooltip("點光源範圍（怪物原始大小時，公尺）")]
    public float lightRange = 3f;
    [Tooltip("點光源離眼球往鏡頭方向多遠（怪物原始大小時，公尺）。要在頭的表面前面一點點，才照得到眼睛周圍")]
    public float lightPull = 1.5f;

    [Header("大小（相對量到的眼球大小）")]
    [Tooltip("核心直徑 ＝ 眼球直徑 × 這個值。1＝跟眼球一樣大（約 0.7 公尺）")]
    public float coreScale = 1.0f;
    [Tooltip("光暈直徑 ＝ 眼球直徑 × 這個值。2.4＝約 1.7 公尺，只比眼球大一點")]
    public float haloScale = 2.4f;
    [Tooltip("量不到眼球（網格讀不到）時的預設眼球直徑（怪物原始大小時，公尺）")]
    public float fallbackEyeDiameter = 0.65f;

    [Header("閃爍")]
    [Range(0f, 0.5f)] public float flicker = 0.1f;
    [Tooltip("偶爾突然暗一下又亮回來（每秒機率）")]
    public float blinkChance = 0.15f;

    [Header("位置")]
    [Tooltip("光往鏡頭拉到「怪物最靠近鏡頭的面」之前再多拉幾公尺（正交視角畫面位置不變）。只是為了不被頭、書擋住")]
    public float pullMargin = 0.6f;
    [Tooltip("光往鏡頭最少拉幾公尺（怪物原始大小時）")]
    public float minPull = 2f;
    [Tooltip("光往鏡頭最多拉幾公尺（怪物原始大小時）")]
    public float maxPull = 12f;

    /// <summary>熄燈時調高（1＝平常）。</summary>
    [System.NonSerialized] public float boost = 1f;

    private const float RefScale = 21.67f;   // 原始大小時怪物的縮放

    private ShadowMonsterController _ctrl;
    private Transform _head;
    private GameObject _root;
    private SpriteRenderer _halo, _core;
    private Light _light;
    private float _blinkT;
    private float _seed;

    // 眼睛網格（每幀手動蒙皮）
    private SkinnedMeshRenderer _eyeSmr;
    private SkinnedMeshRenderer _bodySmr;
    private Vector3[] _verts;
    private BoneWeight[] _bw;
    private Matrix4x4[] _bind;
    private Transform[] _bones;
    private Matrix4x4[] _skin;
    private int[] _cluster;           // 每個頂點屬於哪一顆眼（0 或 1）
    private int _clusterCount = 1;
    private Vector3[] _skinned;
    private bool _meshOk;

    /// <summary>紅光現在在世界的哪裡：眼球的中心（熄燈黑幕在這裡開洞）。</summary>
    public Vector3 AnchorWorld { get; private set; }
    /// <summary>紅光現在看得見嗎（怪物看得見、而且找得到眼睛）。</summary>
    public bool Visible { get; private set; }
    /// <summary>怪物目前相對原始大小的比例。</summary>
    public float ScaleFactor { get; private set; } = 1f;
    /// <summary>眼球半徑（世界公尺，從 Eye 網格量）。</summary>
    public float EyeRadiusWorld { get; private set; }
    /// <summary>光（核心）實際放的世界位置（沿視線往鏡頭拉過）。</summary>
    public Vector3 GlowWorld => _core != null ? _core.transform.position : AnchorWorld;
    /// <summary>核心直徑／光暈直徑（世界公尺）。</summary>
    public float CoreDiameterWorld { get; private set; }
    public float HaloDiameterWorld { get; private set; }
    /// <summary>Eye 網格有沒有讀到（false＝用頭骨加偏移的備案）。</summary>
    public bool MeasuredFromMesh => _meshOk;
    /// <summary>Eye 網格裡有幾顆眼（1 或 2）。</summary>
    public int EyeClusterCount => _clusterCount;

    private void Start()
    {
        _ctrl = GetComponent<ShadowMonsterController>();
        _seed = Random.value * 100f;
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
            if (t.name == "mixamorig:Head") { _head = t; break; }
        if (_head == null) { Debug.LogWarning("【書頭怪紅光】找不到頭骨 mixamorig:Head，紅光不放"); enabled = false; return; }
        FindEyeMesh();
        Build();
        Debug.Log($"【書頭怪紅光】★1118e 眼球紅光已掛上（核心＋光暈＋點光源，釘在 Eye 網格的眼球上；網格讀取={(_meshOk ? "成功" : "失敗，用頭骨備案")}）");
    }

    private void FindEyeMesh()
    {
        foreach (SkinnedMeshRenderer s in GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (s == null || s.sharedMesh == null) continue;
            string n = s.name.ToLowerInvariant();
            if (_eyeSmr == null && n.Contains("eye")) _eyeSmr = s;
            if (s.name == "Body") _bodySmr = s;
        }
        if (_bodySmr == null)
        {
            int best = -1;
            foreach (SkinnedMeshRenderer s in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (s == null || s.sharedMesh == null) continue;
                string n = s.name.ToLowerInvariant();
                if (n.Contains("eye") || n.Contains("teeth") || n.Contains("tooth")) continue;
                if (s.sharedMesh.vertexCount > best) { best = s.sharedMesh.vertexCount; _bodySmr = s; }
            }
        }
        if (_eyeSmr == null) return;
        Mesh m = _eyeSmr.sharedMesh;
        if (!m.isReadable) { Debug.LogWarning("【書頭怪紅光】Eye 網格不能讀（isReadable=false），改用頭骨備案"); return; }
        _verts = m.vertices;
        _bw = m.boneWeights;
        _bind = m.bindposes;
        _bones = _eyeSmr.bones;
        if (_verts.Length == 0 || _bw.Length != _verts.Length || _bones == null || _bones.Length != _bind.Length) return;
        _skin = new Matrix4x4[_bones.Length];
        _skinned = new Vector3[_verts.Length];

        // 網格裡有兩顆眼睛嗎：沿網格 x 軸排序，最大的空隙超過總寬的 1/3 就切成兩顆
        _cluster = new int[_verts.Length];
        int[] order = new int[_verts.Length];
        for (int i = 0; i < order.Length; i++) order[i] = i;
        System.Array.Sort(order, (a, b) => _verts[a].x.CompareTo(_verts[b].x));
        float width = _verts[order[order.Length - 1]].x - _verts[order[0]].x;
        float bestGap = 0f; int cut = -1;
        for (int i = 1; i < order.Length; i++)
        {
            float g = _verts[order[i]].x - _verts[order[i - 1]].x;
            if (g > bestGap) { bestGap = g; cut = i; }
        }
        if (cut > 0 && width > 1e-6f && bestGap > width / 3f)
        {
            _clusterCount = 2;
            for (int i = cut; i < order.Length; i++) _cluster[order[i]] = 1;
        }
        _meshOk = true;
        Debug.Log($"【書頭怪紅光】Eye 網格 {_eyeSmr.name}：{_verts.Length} 個頂點、{(_clusterCount == 2 ? "兩顆眼（取離鏡頭近的）" : "一顆眼")}、網格寬 {width:F4}");
    }

    private void Build()
    {
        _root = new GameObject("書頭怪_眼球紅光（自動生成）");
        Material mat = new Material(Shader.Find("Sprites/Default"));
        _halo = MakeSprite("光暈", MakeDiscSprite(128, 1f), mat, 1003);       // 從中心一路柔柔地淡到邊
        _core = MakeSprite("核心", MakeDiscSprite(128, 0.55f), mat, 1004);     // 裡面實、只有外圈淡

        GameObject lg = new GameObject("紅光");
        lg.transform.SetParent(_root.transform, false);
        _light = lg.AddComponent<Light>();
        _light.type = LightType.Point;
        _light.color = new Color(1f, 0.2f, 0.1f);
        _light.shadows = LightShadows.None;
    }

    private SpriteRenderer MakeSprite(string n, Sprite s, Material m, int order)
    {
        GameObject g = new GameObject(n);
        g.transform.SetParent(_root.transform, false);
        SpriteRenderer sr = g.AddComponent<SpriteRenderer>();
        sr.sprite = s;
        sr.sharedMaterial = m;
        sr.sortingOrder = order;
        return sr;
    }

    /// <summary>量出眼球：世界中心＋半徑（只看離鏡頭近的那一顆）。</summary>
    private bool MeasureEye(Camera cam, out Vector3 center, out float radius)
    {
        center = _head.position; radius = fallbackEyeDiameter * 0.5f * ScaleFactor;
        if (!_meshOk || _eyeSmr == null) return false;
        for (int i = 0; i < _bones.Length; i++) _skin[i] = _bones[i] != null ? _bones[i].localToWorldMatrix * _bind[i] : Matrix4x4.identity;
        for (int i = 0; i < _verts.Length; i++)
        {
            BoneWeight bw = _bw[i];
            Vector3 v = _verts[i], w = Vector3.zero;
            if (bw.weight0 > 0f) w += _skin[bw.boneIndex0].MultiplyPoint3x4(v) * bw.weight0;
            if (bw.weight1 > 0f) w += _skin[bw.boneIndex1].MultiplyPoint3x4(v) * bw.weight1;
            if (bw.weight2 > 0f) w += _skin[bw.boneIndex2].MultiplyPoint3x4(v) * bw.weight2;
            if (bw.weight3 > 0f) w += _skin[bw.boneIndex3].MultiplyPoint3x4(v) * bw.weight3;
            _skinned[i] = w;
        }
        // 每顆眼的中心；挑離鏡頭近的
        int pick = 0;
        if (_clusterCount == 2)
        {
            Vector3 c0 = Vector3.zero, c1 = Vector3.zero; int n0 = 0, n1 = 0;
            for (int i = 0; i < _skinned.Length; i++) { if (_cluster[i] == 0) { c0 += _skinned[i]; n0++; } else { c1 += _skinned[i]; n1++; } }
            if (n0 == 0 || n1 == 0) pick = n0 == 0 ? 1 : 0;
            else
            {
                c0 /= n0; c1 /= n1;
                Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;
                Vector3 fwd = cam != null ? cam.transform.forward : Vector3.forward;
                pick = Vector3.Dot(c0 - camPos, fwd) <= Vector3.Dot(c1 - camPos, fwd) ? 0 : 1;
            }
        }
        Vector3 sum = Vector3.zero; int n = 0;
        for (int i = 0; i < _skinned.Length; i++) if (_cluster[i] == pick) { sum += _skinned[i]; n++; }
        if (n == 0) return false;
        center = sum / n;
        // 半徑：用「畫面上」的範圍（鏡頭的右、上方向）量，取大的那邊的一半
        Vector3 right = cam != null ? cam.transform.right : Vector3.right, up = cam != null ? cam.transform.up : Vector3.up;
        float minR = float.MaxValue, maxR = float.MinValue, minU = float.MaxValue, maxU = float.MinValue;
        for (int i = 0; i < _skinned.Length; i++)
        {
            if (_cluster[i] != pick) continue;
            Vector3 d = _skinned[i] - center;
            float r = Vector3.Dot(d, right), u = Vector3.Dot(d, up);
            if (r < minR) minR = r; if (r > maxR) maxR = r;
            if (u < minU) minU = u; if (u > maxU) maxU = u;
        }
        // 圓心取「畫面上輪廓範圍的中心」而不是頂點平均（半圓球的頂點偏向一側，平均會歪），深度仍用平均
        center += right * ((minR + maxR) * 0.5f) + up * ((minU + maxU) * 0.5f);
        radius = Mathf.Max(0.02f, Mathf.Max(maxR - minR, maxU - minU) * 0.5f);
        return true;
    }

    private void LateUpdate()
    {
        if (_root == null || _head == null) return;
        float alpha = (Enabled && _ctrl != null) ? Mathf.Clamp01(_ctrl.VisualAlpha) : 0f;
        Visible = alpha > 0.01f;
        if (_root.activeSelf != Visible) _root.SetActive(Visible);
        if (!Visible) return;

        float s = Mathf.Max(0.05f, Mathf.Abs(transform.lossyScale.x) / RefScale);
        ScaleFactor = s;
        Camera cam = Camera.main;

        // 1. 眼球的世界位置與半徑（每一幀，從蒙皮後的 Eye 頂點量）
        Vector3 eye; float eyeR;
        if (!MeasureEye(cam, out eye, out eyeR))
        {   // 備案：頭骨往上一點（量不到網格時才用）
            eye = _head.position + _head.up * 1.7f * s + _head.forward * 0.1f * s;
        }
        AnchorWorld = eye;
        EyeRadiusWorld = eyeR;

        // 2. 圓形的光：核心≈眼球、光暈比眼球大一點
        float eyeD = eyeR * 2f;
        float boostGrow = Mathf.Lerp(1f, 1.15f, Mathf.Clamp01(boost - 1f));
        float coreD = eyeD * Mathf.Max(0.1f, coreScale);
        float haloD = eyeD * Mathf.Max(coreScale, haloScale) * boostGrow;
        CoreDiameterWorld = coreD; HaloDiameterWorld = haloD;

        // 3. 沿視線往鏡頭拉：拉到怪物最靠近鏡頭的面前（畫面位置不變，只是不被頭、書擋住）
        float pull = Mathf.Clamp(PullDistance(cam, eye), minPull * s, maxPull * s);
        Vector3 pos = Pull(eye, cam, pull);
        Quaternion camRot = cam != null ? cam.transform.rotation : Quaternion.identity;
        _root.transform.rotation = camRot;

        // 4. 閃爍：柔和的起伏＋偶爾暗一下（比原本小）
        float n = Mathf.PerlinNoise(_seed, Time.time * 6f) * 2f - 1f;
        if (_blinkT <= 0f && Random.value < blinkChance * Time.deltaTime) _blinkT = 0.12f;
        float blink = 1f;
        if (_blinkT > 0f) { _blinkT -= Time.deltaTime; blink = 0.5f; }
        float k = (1f + flicker * n) * blink * Mathf.Max(0f, boost);

        SetSprite(_halo, pos, haloD, haloColor, alpha * Mathf.Min(1f, k));
        SetSprite(_core, pos, coreD, coreColor, alpha * Mathf.Min(1f, k));

        _light.transform.position = Pull(eye, cam, Mathf.Min(pull, lightPull * s));
        _light.intensity = lightIntensity * alpha * k;
        _light.range = Mathf.Max(0.5f, lightRange * s * Mathf.Lerp(1f, 1.25f, Mathf.Clamp01(boost - 1f)));
    }

    /// <summary>從眼球沿視線，到怪物最靠近鏡頭的那一面還要多遠（再加 pullMargin）。</summary>
    private float PullDistance(Camera cam, Vector3 eye)
    {
        float fallback = 3f * ScaleFactor;
        if (cam == null || _bodySmr == null) return fallback;
        Bounds b = _bodySmr.bounds;
        Vector3 fwd = cam.transform.forward;
        float eyeDepth = Vector3.Dot(eye - cam.transform.position, fwd);
        float nearest = float.MaxValue;
        Vector3 c = b.center, e = b.extents;
        for (int i = 0; i < 8; i++)
        {
            Vector3 p = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
            nearest = Mathf.Min(nearest, Vector3.Dot(p - cam.transform.position, fwd));
        }
        return Mathf.Max(0f, eyeDepth - nearest) + pullMargin * ScaleFactor;
    }

    private static Vector3 Pull(Vector3 p, Camera cam, float dist)
    {
        Vector3 toCam = cam != null ? (cam.orthographic ? -cam.transform.forward : (cam.transform.position - p).normalized) : Vector3.back;
        return p + toCam * dist;
    }

    private static void SetSprite(SpriteRenderer sr, Vector3 pos, float diameter, Color c, float a)
    {
        sr.transform.position = pos;
        sr.transform.localScale = new Vector3(diameter, diameter, 1f);   // 圓貼圖寬高 1 公尺
        c.a *= Mathf.Clamp01(a);
        sr.color = c;
    }

    private void OnDisable()
    {
        if (_root != null) _root.SetActive(false);
        Visible = false;
    }

    private void OnDestroy()
    {
        if (_root != null) Destroy(_root);
    }

    /// <summary>
    /// 圓形柔邊貼圖（寬高 size px、世界大小 1×1 公尺，軸心在中心）。
    /// solid＝從中心往外多少比例以內是實的（1＝一直是實的到邊就淡掉不會有；0.55＝中間 55% 實、外圈淡到 0）。
    /// 光暈用 solid=1 特別處理成「從中心一路平滑淡掉」。
    /// </summary>
    private static Sprite MakeDiscSprite(int size, float solid)
    {
        Texture2D t = new Texture2D(size, size, TextureFormat.RGBA32, false);
        t.wrapMode = TextureWrapMode.Clamp;
        Color32[] px = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f - size * 0.5f) / (size * 0.5f), v = (y + 0.5f - size * 0.5f) / (size * 0.5f);
                float d = Mathf.Sqrt(u * u + v * v);
                float a;
                if (solid >= 0.999f) { a = Mathf.Clamp01(1f - d); a = a * a * (3f - 2f * a); a *= a; }   // 光暈：中間最亮、平方淡出
                else { a = Mathf.Clamp01((1f - d) / Mathf.Max(0.01f, 1f - solid)); a = a * a * (3f - 2f * a); }
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        t.SetPixels32(px);
        t.Apply(false, true);
        return Sprite.Create(t, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }
}
