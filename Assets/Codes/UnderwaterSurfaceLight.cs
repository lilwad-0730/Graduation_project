using UnityEngine;

/// <summary>
/// 水面灑下來的光：
///   1. 一盞從上方斜照的平行光 → 石頭頂面亮、底面暗，不再是平平的色塊
///   2. 幾條會晃、會忽明忽暗的光束（God Rays），從畫面上方灑下
/// 兩者都依玩家深度 (Y) 淡出：越深越遠離水面、越暗。執行時才建立，不改場景裡的其他物件。
/// 放在 underwater 場景任一物件上（場景裡的「UnderwaterSurfaceLight」）。
/// </summary>
public class UnderwaterSurfaceLight : MonoBehaviour
{
    [Header("參考")]
    [Tooltip("留空自動找 Tag = Player")]
    public Transform player;
    [Tooltip("留空自動用 Main Camera")]
    public Camera cam;
    [Tooltip("光束用的 Shader（Underwater/LightRay）。要拖進來，打包版才找得到")]
    public Shader rayShader;

    [Header("深度範圍 (玩家 Y)")]
    [Tooltip("這個 Y 以上算在水面附近，效果最強")]
    public float surfaceY = -5f;
    [Tooltip("光束完全消失的 Y")]
    public float raysGoneY = -55f;
    [Tooltip("平行光降到最暗的 Y")]
    public float lightDarkestY = -130f;
    [Tooltip("深度變化的平滑速度")]
    public float smoothing = 1.5f;

    [Header("上方平行光")]
    public bool useSunLight = true;
    public Color sunColor = new Color(0.72f, 0.88f, 1f);
    [Tooltip("強度 (水面 → 最深)")]
    public Vector2 sunIntensity = new Vector2(0.9f, 0.12f);
    [Tooltip("光的角度。X = 往下傾斜多少度，Y = 左右偏")]
    public Vector2 sunAngle = new Vector2(62f, -18f);
    [Tooltip("打開＝這盞光不照泡泡（BubbleClusterFx、PlayerSwimBubbles），泡泡維持原本的樣子。\n做法：把泡泡移到內建、沒在用的 TransparentFX 圖層，這盞光略過該圖層")]
    public bool excludeBubbles = true;
    [Tooltip("打開＝這盞光不照主角，主角維持原本的光影（深處的主角微光等原本的光照樣有）")]
    public bool excludePlayer = true;

    [Header("光束")]
    public bool useRays = true;
    [Range(1, 16)] public int rayCount = 7;
    public Color rayColor = new Color(0.62f, 0.86f, 1f);
    [Tooltip("光束亮度 (水面附近)")]
    public float rayIntensity = 0.26f;
    [Tooltip("光束寬度範圍 (公尺)")]
    public Vector2 rayWidth = new Vector2(1.2f, 3.6f);
    [Tooltip("光束長度範圍 (公尺)")]
    public Vector2 rayLength = new Vector2(16f, 26f);
    [Tooltip("光束傾斜角度 (度，正 = 往右下)")]
    public float rayTilt = 14f;
    [Tooltip("光束左右搖晃幅度 (度)")]
    public float raySway = 3.5f;
    [Tooltip("光束放在多深的 Z (要在石頭後面、背景巨獸前面)")]
    public float rayZ = 3f;
    [Tooltip("光束跟著鏡頭左右移動的比例 (1 = 黏在畫面上，越小越像固定在世界裡)")]
    [Range(0f, 1f)] public float rayFollow = 0.6f;

    [Header("除錯（唯讀）")]
    [SerializeField] private float debugSurface01;

    private Light _sun;
    private Transform _rayRoot;
    private Ray[] _rays;
    private Material _rayMat;
    private MaterialPropertyBlock _mpb;
    private float _surface01 = 1f;      // 1 = 水面附近, 0 = 很深
    private float _lightDepth01;        // 0 = 水面, 1 = 最深
    private bool _primed;

    private struct Ray
    {
        public Transform tr;
        public MeshRenderer mr;
        public float worldX;   // 錨定在世界裡的 X（再依 rayFollow 跟著鏡頭）
        public float width, length, seed, swaySpeed, baseTilt;
    }

    private static readonly int IdIntensity = Shader.PropertyToID("_Intensity");
    private static readonly int IdSeed = Shader.PropertyToID("_Seed");
    private static readonly int IdColor = Shader.PropertyToID("_Color");

    private void Start()
    {
        if (cam == null) cam = Camera.main;
        FindPlayer();
        if (useSunLight) CreateSun();
        if (useRays) CreateRays();
    }

    private void FindPlayer()
    {
        if (player != null) return;
        GameObject go = GameObject.FindWithTag("Player");
        if (go != null) player = go.transform;
    }

    private void CreateSun()
    {
        GameObject go = new GameObject("SurfaceSunLight");
        go.transform.SetParent(transform, false);
        go.transform.rotation = Quaternion.Euler(sunAngle.x, sunAngle.y, 0f);
        _sun = go.AddComponent<Light>();
        _sun.type = LightType.Directional;
        _sun.color = sunColor;
        _sun.intensity = sunIntensity.x;
        _sun.shadows = LightShadows.None;
    }

    private const int BubbleLayer = 1;   // 內建 TransparentFX
    private float _nextBubbleScan;

    /// <summary>
    /// 泡泡和主角維持原本的光影：把它們的模型移到不受這盞光影響的圖層。
    /// 泡泡是執行中才生成的（主角嘴邊的會一直生），所以每隔一小段時間掃一次。
    /// </summary>
    private void KeepBubblesOutOfSunLight()
    {
        if (_sun == null) return;
        bool any = excludeBubbles || excludePlayer;
        _sun.cullingMask = any ? ~(1 << BubbleLayer) : ~0;
        if (!any || Time.time < _nextBubbleScan) return;
        _nextBubbleScan = Time.time + 0.25f;

        if (excludeBubbles)
        {
            foreach (BubbleClusterFx fx in FindObjectsByType<BubbleClusterFx>())
                MoveRenderersToBubbleLayer(fx.transform);
            GameObject swimRoot = GameObject.Find("PlayerSwimBubbles_Root");
            if (swimRoot != null) MoveRenderersToBubbleLayer(swimRoot.transform);
        }
        if (excludePlayer && player != null)
            MoveRenderersToBubbleLayer(player);   // 主角身上的模型（SkinnedMeshRenderer）和身上的泡泡
    }

    private static void MoveRenderersToBubbleLayer(Transform root)
    {
        // 只動畫面上的模型本體，不動帶碰撞的物件（主角根物件、泡泡群的觸發區）
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
            if (r.gameObject.layer != BubbleLayer && r.GetComponent<Collider>() == null && !(r is LineRenderer) && !(r is TrailRenderer))
                r.gameObject.layer = BubbleLayer;
    }

    private void CreateRays()
    {
        Shader sh = rayShader != null ? rayShader : Shader.Find("Underwater/LightRay");
        if (sh == null) { Debug.LogWarning("【水下光束】找不到 Underwater/LightRay shader，光束沒建立"); return; }
        _rayMat = new Material(sh) { name = "Mat_UnderwaterLightRay (runtime)" };
        _mpb = new MaterialPropertyBlock();

        _rayRoot = new GameObject("SurfaceLightRays").transform;
        _rayRoot.SetParent(transform, false);

        Mesh quad = BuildTopPivotQuad();
        _rays = new Ray[rayCount];
        float span = ViewWidth() * 1.6f;
        Vector3 c = cam != null ? cam.transform.position : Vector3.zero;
        for (int i = 0; i < rayCount; i++)
        {
            GameObject go = new GameObject("Ray_" + i);
            go.transform.SetParent(_rayRoot, false);
            go.AddComponent<MeshFilter>().sharedMesh = quad;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _rayMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            Ray r = new Ray
            {
                tr = go.transform,
                mr = mr,
                worldX = c.x * (1f - rayFollow) - span * 0.5f + span * (i + Random.Range(0.15f, 0.85f)) / rayCount,
                width = Random.Range(rayWidth.x, rayWidth.y),
                length = Random.Range(rayLength.x, rayLength.y),
                seed = Random.Range(0f, 100f),
                swaySpeed = Random.Range(0.12f, 0.3f),
                baseTilt = rayTilt + Random.Range(-4f, 4f),
            };
            _rays[i] = r;
        }
    }

    private static Mesh BuildTopPivotQuad()
    {
        // 軸心在上緣中央：往下延伸 1 單位，方便用縮放控制長度
        Mesh m = new Mesh { name = "LightRayQuad" };
        m.vertices = new[] { new Vector3(-0.5f, -1f, 0f), new Vector3(0.5f, -1f, 0f), new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f) };
        m.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
        m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        m.RecalculateBounds();
        m.bounds = new Bounds(new Vector3(0f, -0.5f, 0f), new Vector3(2f, 2f, 1f)); // 傾斜後也不會被剔除
        return m;
    }

    private float ViewWidth()
    {
        if (cam == null) return 30f;
        float h = cam.orthographic ? cam.orthographicSize * 2f : 2f * Mathf.Abs(rayZ - cam.transform.position.z) * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        return h * cam.aspect;
    }

    private float ViewHeight()
    {
        return cam == null ? 14f : ViewWidth() / Mathf.Max(0.01f, cam.aspect);
    }

    private void LateUpdate()
    {
        if (cam == null) cam = Camera.main;
        if (player == null) FindPlayer();

        float y = player != null ? player.position.y : (cam != null ? cam.transform.position.y : surfaceY);
        float targetSurface = 1f - Mathf.InverseLerp(surfaceY, raysGoneY, y);
        targetSurface = targetSurface * targetSurface * (3f - 2f * targetSurface);
        float targetLight = Mathf.InverseLerp(surfaceY, lightDarkestY, y);
        if (!_primed)
        {
            _surface01 = targetSurface;
            _lightDepth01 = targetLight;
            _primed = true;
        }
        float step = smoothing * Time.deltaTime;
        _surface01 = Mathf.MoveTowards(_surface01, targetSurface, step);
        _lightDepth01 = Mathf.MoveTowards(_lightDepth01, targetLight, step);
        debugSurface01 = _surface01;

        if (_sun != null)
        {
            _sun.enabled = useSunLight;
            _sun.color = sunColor;
            _sun.intensity = Mathf.Lerp(sunIntensity.x, sunIntensity.y, _lightDepth01);
            _sun.transform.rotation = Quaternion.Euler(sunAngle.x, sunAngle.y, 0f);
            KeepBubblesOutOfSunLight();
        }

        UpdateRays();
    }

    private void UpdateRays()
    {
        if (_rays == null || cam == null) return;
        bool show = useRays && _surface01 > 0.001f;
        _rayRoot.gameObject.SetActive(show);
        if (!show) return;

        Vector3 c = cam.transform.position;
        float viewW = ViewWidth();
        float span = viewW * 1.6f;
        float top = c.y + ViewHeight() * 0.5f + 1.5f;
        float t = Time.time;

        for (int i = 0; i < _rays.Length; i++)
        {
            Ray r = _rays[i];
            // 鏡頭移動 dx 時光束只移動 rayFollow·dx（看起來在比較遠的地方）；跑出畫面就從另一側繞回來
            float x = c.x + Mathf.Repeat(r.worldX - c.x * (1f - rayFollow) + span * 0.5f, span) - span * 0.5f;

            float tilt = r.baseTilt + Mathf.Sin(t * r.swaySpeed + r.seed) * raySway;
            float widthPulse = 1f + 0.12f * Mathf.Sin(t * r.swaySpeed * 1.7f + r.seed * 2f);

            r.tr.position = new Vector3(x, top, rayZ);
            r.tr.rotation = Quaternion.Euler(0f, 0f, tilt);
            r.tr.localScale = new Vector3(r.width * widthPulse, r.length, 1f);

            r.mr.GetPropertyBlock(_mpb);
            _mpb.SetFloat(IdIntensity, rayIntensity * _surface01);
            _mpb.SetFloat(IdSeed, r.seed);
            _mpb.SetColor(IdColor, rayColor);
            r.mr.SetPropertyBlock(_mpb);
            _rays[i] = r;
        }
    }

    private void OnDestroy()
    {
        if (_rayMat != null) Destroy(_rayMat);
    }
}
