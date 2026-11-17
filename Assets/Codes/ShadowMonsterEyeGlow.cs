using UnityEngine;

/// <summary>
/// ★1116 玻璃館書頭怪：眼罩底下透出來的紅光（M：「加強書頭怪眼睛部份的紅光」）。
///
/// 不是把眼睛畫回來（ShadowMonsterDread 的紅線：眼睛、牙齒網格一律不畫），而是眼罩縫裡漏出的光：
///   ・★1117 形狀是半圓（M：「我們是側面展示它，紅光不應該是圓形，應該接近半圓」
///     「圓弧在右半邊，因為左邊是書的部分，光不會透過去」）：
///     平的一邊在書那側（直的）、圓弧朝臉的前方，像側面看到的眼睛。三層（光暈、核心、最亮的縫）共用同一條平邊。
///   ・★1117 平邊貼齊書的封面（M：「請讓半圓的邊貼齊書的封面」）：位置、角度都照封面量——
///     實測（Body 網格烘焙後換到頭骨座標）：封面中心 (右 0.1, 上 3.1, 前 2.72)、往後仰 16.7°、高 4.75；
///     平邊就貼在這個面上、跟它一樣斜，長度不超出封面上下。
///   ・核心：亮紅半圓，靠平邊的地方偏橘白，會微微閃爍、偶爾抽一下。
///   ・光暈：外圈一大片柔和的暗紅半圓。
///   ・點光源：把眼罩、頭的輪廓染紅（怪物是 URP Lit 材質，吃得到光）。
/// 位置跟著頭骨（mixamorig:Head）：用實測的書封面相對頭骨的位置（怪物原始比例 21.67），
///   怪物縮小（每點一根燭火）時偏移、大小一起縮。
/// 透明度跟著怪物（ShadowMonsterController.VisualAlpha）：看不見的時候紅光也不見。
/// boost：GlassFinaleBlackout 熄燈時調高，黑暗裡只剩這雙紅光。
/// 由 ShadowMonsterController.EnsureInitialized 自動掛上，不用改場景。
/// </summary>
[DisallowMultipleComponent]
public class ShadowMonsterEyeGlow : MonoBehaviour
{
    public static bool Enabled = true;

    [Header("顏色與亮度")]
    public Color coreColor = new Color(1f, 0.22f, 0.12f, 1f);
    public Color hotColor = new Color(1f, 0.75f, 0.55f, 1f);
    public Color haloColor = new Color(0.9f, 0.04f, 0.04f, 0.55f);
    [Tooltip("點光源強度（染紅眼罩和頭）")]
    public float lightIntensity = 14f;
    [Tooltip("點光源範圍（怪物原始大小時，公尺）")]
    public float lightRange = 7f;

    [Header("大小與形狀（怪物原始大小時，公尺）")]
    [Tooltip("半圓：x＝平邊的寬（直徑）、y＝從平邊到圓弧最低點的高。y＝x 的一半就是正半圓，小一點更扁")]
    public Vector2 coreSize = new Vector2(1.7f, 0.75f);
    [Tooltip("最亮的縫（同上，貼著平邊）")]
    public Vector2 hotSize = new Vector2(1.03f, 0.3f);
    [Tooltip("光暈半圓的寬（高＝寬的一半）")]
    public float haloSize = 9f;
    [Tooltip("半圓的方向（度）：90＝平邊在後（書擋住那一側）、圓弧朝臉的前方（預設，M 11/17）；0＝平邊在上、圓弧朝下；-90＝圓弧朝後腦。怪物轉身時自動左右翻")]
    [Range(-180f, 180f)] public float shapeAngle = 90f;

    [Header("閃爍")]
    [Range(0f, 0.5f)] public float flicker = 0.18f;
    [Tooltip("偶爾突然暗一下又亮回來（每秒機率）")]
    public float blinkChance = 0.25f;

    [Header("位置：貼著書的封面（相對頭骨，怪物原始大小時，公尺）")]
    [Tooltip("書封面的中心：x＝頭的右方、y＝頭的上方、z＝頭的前方。實測 (0.1, 3.1, 2.72)")]
    public Vector3 coverCenter = new Vector3(0.1f, 3.1f, 2.72f);
    [Tooltip("封面往後仰幾度（相對頭骨的上方）。實測 16.7。平邊會跟封面一樣斜")]
    public float coverTilt = 16.7f;
    [Tooltip("封面的高。光的平邊最長就這麼長，不會超出書的上下")]
    public float coverHeight = 4.75f;
    [Tooltip("眼睛在封面上的高度（從封面中心沿著封面往上為正）：核心和最亮的縫的中心。光暈自動置中在封面上")]
    public float eyeHeight = -0.6f;
    [Tooltip("平邊離封面多遠（0＝貼齊；正的＝往前空一點、負的＝往書裡縮一點）")]
    public float coverGap = 0f;
    [Tooltip("往鏡頭方向拉多少（避免被書擋住；沿著視線拉，畫面上的位置不變）")]
    public float towardCamera = 3f;

    /// <summary>熄燈時調高（1＝平常）。</summary>
    [System.NonSerialized] public float boost = 1f;

    private const float RefScale = 21.67f;   // 量偏移時怪物的大小

    private ShadowMonsterController _ctrl;
    private Transform _head;
    private GameObject _root;
    private SpriteRenderer _halo, _core, _hot;
    private Light _light;
    private float _blinkT;
    private float _seed;

    /// <summary>紅光現在在世界的哪裡（熄燈的黑幕在這裡開一個小洞）。</summary>
    public Vector3 AnchorWorld { get; private set; }
    /// <summary>紅光現在看得見嗎（怪物看得見、而且找得到頭）。</summary>
    public bool Visible { get; private set; }
    /// <summary>怪物目前相對原始大小的比例。</summary>
    public float ScaleFactor { get; private set; } = 1f;

    private void Start()
    {
        _ctrl = GetComponent<ShadowMonsterController>();
        _seed = Random.value * 100f;
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
            if (t.name == "mixamorig:Head") { _head = t; break; }
        if (_head == null) { Debug.LogWarning("【書頭怪紅光】找不到頭骨 mixamorig:Head，紅光不放"); enabled = false; return; }
        Build();
        Debug.Log("【書頭怪紅光】★1116 眼罩紅光已掛上（核心＋光暈＋點光源，跟著頭骨）");
    }

    private void Build()
    {
        _root = new GameObject("書頭怪_眼罩紅光（自動生成）");
        Material mat = new Material(Shader.Find("Sprites/Default"));
        Sprite soft = MakeHalfDiscSprite(128, 1f, 0.18f);    // 光暈：從平邊中心往外一路淡掉
        Sprite solid = MakeHalfDiscSprite(128, 0.35f, 0.06f); // 核心、縫：裡面實、邊緣柔

        _halo = MakeSprite("光暈", soft, mat, 1003);
        _core = MakeSprite("核心", solid, mat, 1004);
        _hot = MakeSprite("最亮的縫", solid, mat, 1005);

        GameObject lg = new GameObject("紅光");
        lg.transform.SetParent(_root.transform, false);
        _light = lg.AddComponent<Light>();
        _light.type = LightType.Point;
        _light.color = coreColor;
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

        // 書的封面：中心、沿封面往上的方向、封面朝外的方向（頭的前方往上仰 coverTilt）
        float tr = coverTilt * Mathf.Deg2Rad;
        Vector3 coverUp = _head.up * Mathf.Cos(tr) - _head.forward * Mathf.Sin(tr);
        Vector3 coverOut = _head.forward * Mathf.Cos(tr) + _head.up * Mathf.Sin(tr);
        Vector3 cc = _head.position + (_head.right * coverCenter.x + _head.up * coverCenter.y + _head.forward * coverCenter.z) * s
                     + coverOut * coverGap * s;

        // 每層的平邊長度不超過封面，中心夾在封面範圍內
        float half = Mathf.Max(0.01f, coverHeight * 0.5f);
        float boostGrow = Mathf.Lerp(1f, 1.35f, Mathf.Clamp01(boost - 1f));
        Vector2 core = new Vector2(Mathf.Min(coreSize.x, coverHeight), coreSize.y);
        Vector2 hot = new Vector2(Mathf.Min(hotSize.x, coverHeight), hotSize.y);
        Vector2 halo = new Vector2(Mathf.Min(haloSize * boostGrow, coverHeight), haloSize * 0.5f * boostGrow);
        Vector3 pEye = cc + coverUp * Mathf.Clamp(eyeHeight, -half + core.x * 0.5f, half - core.x * 0.5f) * s;
        Vector3 pHot = cc + coverUp * Mathf.Clamp(eyeHeight, -half + hot.x * 0.5f, half - hot.x * 0.5f) * s;
        Vector3 pHalo = cc + coverUp * Mathf.Clamp(eyeHeight, -half + halo.x * 0.5f, half - halo.x * 0.5f) * s;

        // 半圓的角度：照封面朝外的方向（不是頭的前方——封面比頭仰 16.7°）
        Vector3 outOnScreen = cam != null ? cam.transform.InverseTransformDirection(Vector3.ProjectOnPlane(coverOut, cam.transform.forward)) : coverOut;
        float face = outOnScreen.x < 0f ? -1f : 1f;              // 臉朝右＝1、朝左＝-1（shapeAngle 的「前方」跟著翻）
        float tilt = outOnScreen.sqrMagnitude > 1e-6f ? Mathf.Atan2(outOnScreen.y, Mathf.Abs(outOnScreen.x)) * Mathf.Rad2Deg * face : 0f;
        float angle = tilt + shapeAngle * face;
        Quaternion camRot = cam != null ? cam.transform.rotation : Quaternion.identity;
        _root.transform.rotation = camRot;

        // 熄燈黑幕的洞開在核心半圓的中間（平邊往圓弧方向約 0.4 個高）
        Vector3 arcDir = camRot * (Quaternion.Euler(0f, 0f, angle) * Vector3.down);
        AnchorWorld = pEye + arcDir * core.y * 0.4f * s;

        // 閃爍：柔和的起伏＋偶爾猛地暗一下
        float t = Time.time;
        float n = Mathf.PerlinNoise(_seed, t * 6f) * 2f - 1f;
        if (_blinkT <= 0f && Random.value < blinkChance * Time.deltaTime) _blinkT = 0.12f;
        float blink = 1f;
        if (_blinkT > 0f) { _blinkT -= Time.deltaTime; blink = 0.25f; }
        float k = (1f + flicker * n) * blink * Mathf.Max(0f, boost);

        // 每層沿著視線往鏡頭拉（畫面上的位置不變，只是不會被書擋住）
        SetSprite(_halo, Pull(pHalo, cam, s), halo * s, haloColor, alpha * Mathf.Min(1f, k), angle);
        SetSprite(_core, Pull(pEye, cam, s), core * s, coreColor, alpha * Mathf.Min(1f, k), angle);
        SetSprite(_hot, Pull(pHot, cam, s), hot * s, hotColor, alpha * Mathf.Min(1f, k * 0.95f), angle);

        _light.transform.position = Pull(pEye + arcDir * core.y * 0.5f * s, cam, s);
        _light.intensity = lightIntensity * alpha * k;
        _light.range = lightRange * s * Mathf.Lerp(1f, 1.4f, Mathf.Clamp01(boost - 1f));
    }

    private Vector3 Pull(Vector3 p, Camera cam, float s)
    {
        Vector3 toCam = cam != null ? (cam.orthographic ? -cam.transform.forward : (cam.transform.position - p).normalized) : Vector3.back;
        return p + toCam * towardCamera * s;
    }

    private static void SetSprite(SpriteRenderer sr, Vector3 pos, Vector2 size, Color c, float a, float angle)
    {
        sr.transform.position = pos;
        // 半圓貼圖是 1 公尺寬、0.5 公尺高（軸心在平邊中間）：x 直接當寬、y 乘 2 當高
        sr.transform.localScale = new Vector3(size.x, size.y * 2f, 1f);
        sr.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
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
    /// 半圓貼圖：平邊在上（軸心在平邊中間）、圓弧朝下。寬 size px、高 size/2 px，世界大小 1×0.5 公尺。
    /// fade＝從圓弧往裡多少比例開始淡（1＝從中心一路淡到邊，0.35＝裡面實、只有外圈淡）；
    /// edge＝平邊那一側柔多少（高的比例），不會切得像貼紙。
    /// </summary>
    private static Sprite MakeHalfDiscSprite(int size, float fade, float edge)
    {
        int w = size, h = size / 2;
        Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, false);
        t.wrapMode = TextureWrapMode.Clamp;
        Color32[] px = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f - w * 0.5f) / (w * 0.5f);   // -1～1（左右）
                float v = (h - (y + 0.5f)) / h;                  // 0＝平邊、1＝圓弧最低點
                float d = Mathf.Sqrt(u * u + v * v);
                float a = Mathf.Clamp01((1f - d) / Mathf.Max(0.01f, fade));
                a = a * a * (3f - 2f * a);
                float e = Mathf.Clamp01(v / Mathf.Max(0.001f, edge));
                a *= e * e * (3f - 2f * e);
                px[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        t.SetPixels32(px);
        t.Apply(false, true);
        return Sprite.Create(t, new Rect(0, 0, w, h), new Vector2(0.5f, 1f), w);
    }
}
