using UnityEngine;

/// <summary>
/// 海草葉身彎曲擺動：把 SpriteRenderer 的圖改用細分網格畫出，逐頂點往左右彎，
/// 根部固定、越往上擺幅越大，且越往上相位越晚 (像水波沿葉身往上傳)，形成 S 型擺動。
/// 掛在有 SpriteRenderer 的海草物件上即可；原本的 SpriteRenderer 保留 (只是不顯示)，拿掉此元件就恢復原狀。
/// 圖片需為「直立」的海草 (根在下、葉尖在上)，物件本身的旋轉/縮放照常生效。
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(SpriteRenderer))]
public class SeaweedBend : MonoBehaviour
{
    [Header("編輯與控制")]
    [Tooltip("勾選即可暫停擺動 (方便編輯其他物件)")]
    public bool pauseSway = false;
    [Tooltip("在編輯模式 (未按 Play) 也預覽擺動")]
    public bool previewInEditor = true;

    [Header("擺動")]
    [Tooltip("葉尖最大左右擺幅 (世界單位)")]
    public float tipAmplitude = 0.35f;
    [Tooltip("擺動一次來回的秒數")]
    public float cycleDuration = 5f;
    [Tooltip("擺幅沿葉身的分佈曲線指數：越大越集中在上半段 (根部更穩)")]
    [Range(1f, 4f)]
    public float bendCurve = 1.8f;
    [Tooltip("從根到葉尖的相位延遲 (弧度)：越大 S 型越明顯")]
    [Range(0f, 6.28f)]
    public float waveLag = 2.2f;
    [Tooltip("疊加的小波紋擺幅比例 (讓葉尖多一點抖動，0 = 關閉)")]
    [Range(0f, 1f)]
    public float rippleAmount = 0.25f;
    [Tooltip("小波紋相對主擺動的頻率倍數")]
    public float rippleFrequency = 2.3f;
    [Tooltip("隨機相位 (避免多株同步)。為 0 時依位置自動產生")]
    public float phaseOffset = 0f;

    [Header("網格")]
    [Tooltip("畫海草用的材質 (Custom/SeaweedBendUnlit)。留空時自動用該 shader 建立")]
    public Material bendMaterial;
    [Tooltip("沿葉身方向的切分段數 (越多彎曲越平滑)")]
    [Range(4, 64)]
    public int rows = 24;
    [Tooltip("橫向的切分段數")]
    [Range(1, 16)]
    public int columns = 4;

    private const string MeshChildName = "__SeaweedBendMesh";

    private SpriteRenderer _sr;
    private Transform _meshTf;
    private MeshRenderer _mr;
    private Mesh _mesh;
    private Vector3[] _baseVerts;
    private Vector3[] _verts;
    private float[] _heightT;
    private float _rootY, _tipY, _minX, _maxX;
    private Sprite _builtFor;
    private int _builtRows, _builtCols;
    private MaterialPropertyBlock _mpb;
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static Material _fallbackMaterial;

    private void OnEnable()
    {
        _sr = GetComponent<SpriteRenderer>();
        if (phaseOffset == 0f)
            phaseOffset = Mathf.Repeat(transform.position.x * 12.3f + transform.position.y * 7.7f, Mathf.PI * 2f);

        Build();

#if UNITY_EDITOR
        UnityEditor.EditorApplication.update -= EditorTick;
        UnityEditor.EditorApplication.update += EditorTick;
#endif
    }

    private void OnDisable()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.update -= EditorTick;
#endif
        Teardown();
    }

    private void Teardown()
    {
        if (_sr != null) _sr.forceRenderingOff = false;
        if (_meshTf != null) DestroySafe(_meshTf.gameObject);
        if (_mesh != null) DestroySafe(_mesh);
        _meshTf = null;
        _mr = null;
        _mesh = null;
        _builtFor = null;
    }

    private static void DestroySafe(Object o)
    {
        if (Application.isPlaying) Destroy(o);
        else DestroyImmediate(o);
    }

    private void Build()
    {
        Teardown();
        if (_sr == null || _sr.sprite == null) return;

        // 清掉可能殘留的舊網格物件 (例如編譯重載時)
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform c = transform.GetChild(i);
            if (c.name == MeshChildName) DestroySafe(c.gameObject);
        }

        Sprite sprite = _sr.sprite;
        Vector2[] sv = sprite.vertices;
        _minX = float.MaxValue; _maxX = float.MinValue; _rootY = float.MaxValue; _tipY = float.MinValue;
        foreach (Vector2 v in sv)
        {
            _minX = Mathf.Min(_minX, v.x); _maxX = Mathf.Max(_maxX, v.x);
            _rootY = Mathf.Min(_rootY, v.y); _tipY = Mathf.Max(_tipY, v.y);
        }

        int cols = Mathf.Max(1, columns);
        int rws = Mathf.Max(2, rows);
        int vx = cols + 1, vy = rws + 1;
        _baseVerts = new Vector3[vx * vy];
        _verts = new Vector3[vx * vy];
        _heightT = new float[vx * vy];
        Vector2[] uvs = new Vector2[vx * vy];
        Color[] colors = new Color[vx * vy];
        int[] tris = new int[cols * rws * 6];

        Texture2D tex = sprite.texture;
        // 精靈本地座標 → 貼圖 UV：直接用 Sprite 自己的頂點/UV 對應 (貼圖被匯入設定縮小時也正確)
        Vector2[] suv = sprite.uv;
        float uMin = float.MaxValue, uMax = float.MinValue, vMin = float.MaxValue, vMax = float.MinValue;
        foreach (Vector2 u in suv)
        {
            uMin = Mathf.Min(uMin, u.x); uMax = Mathf.Max(uMax, u.x);
            vMin = Mathf.Min(vMin, u.y); vMax = Mathf.Max(vMax, u.y);
        }
        Vector2 ToUV(float x, float y)
        {
            return new Vector2(
                Mathf.Lerp(uMin, uMax, Mathf.InverseLerp(_minX, _maxX, x)),
                Mathf.Lerp(vMin, vMax, Mathf.InverseLerp(_rootY, _tipY, y)));
        }

        for (int j = 0; j < vy; j++)
        {
            float t = j / (float)rws;
            float y = Mathf.Lerp(_rootY, _tipY, t);
            for (int i = 0; i < vx; i++)
            {
                float x = Mathf.Lerp(_minX, _maxX, i / (float)cols);
                int k = j * vx + i;
                _baseVerts[k] = new Vector3(x, y, 0f);
                _heightT[k] = t;
                uvs[k] = ToUV(x, y);
                colors[k] = Color.white;
            }
        }

        int ti = 0;
        for (int j = 0; j < rws; j++)
        {
            for (int i = 0; i < cols; i++)
            {
                int a = j * vx + i, b = a + 1, c = a + vx, d = c + 1;
                tris[ti++] = a; tris[ti++] = c; tris[ti++] = b;
                tris[ti++] = b; tris[ti++] = c; tris[ti++] = d;
            }
        }

        _mesh = new Mesh { name = "SeaweedBend" };
        _mesh.hideFlags = HideFlags.HideAndDontSave;
        _mesh.MarkDynamic();
        _mesh.vertices = _baseVerts;
        _mesh.uv = uvs;
        _mesh.colors = colors;
        _mesh.triangles = tris;

        GameObject go = new GameObject(MeshChildName);
        go.hideFlags = HideFlags.HideAndDontSave;
        _meshTf = go.transform;
        _meshTf.SetParent(transform, false);
        go.layer = gameObject.layer;
        go.AddComponent<MeshFilter>().sharedMesh = _mesh;
        _mr = go.AddComponent<MeshRenderer>();
        _mr.sharedMaterial = GetMaterial();
        _mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _mr.receiveShadows = false;

        if (_mpb == null) _mpb = new MaterialPropertyBlock();
        _mpb.Clear();
        _mpb.SetTexture(MainTexId, tex);
        _mr.SetPropertyBlock(_mpb);

        _builtFor = sprite;
        _builtRows = rows;
        _builtCols = columns;
        _sr.forceRenderingOff = true;

        SyncRenderer();
        Deform(CurrentTime());
    }

    private void SyncRenderer()
    {
        if (_mr == null) return;
        _mr.sortingLayerID = _sr.sortingLayerID;
        _mr.sortingOrder = _sr.sortingOrder;
        Material mat = GetMaterial();
        if (_mr.sharedMaterial != mat) _mr.sharedMaterial = mat;
        _meshTf.localScale = new Vector3(_sr.flipX ? -1f : 1f, _sr.flipY ? -1f : 1f, 1f);

        Color col = _sr.color;
        Color[] colors = _mesh.colors;
        if (colors.Length > 0 && colors[0] != col)
        {
            for (int i = 0; i < colors.Length; i++) colors[i] = col;
            _mesh.colors = colors;
        }
    }

    // URP 的 Sprite shader 需要 SpriteRenderer 才畫得出來，網格改用專用的簡單 unlit 透明 shader
    private Material GetMaterial()
    {
        if (bendMaterial != null) return bendMaterial;
        if (_fallbackMaterial == null)
        {
            Shader sh = Shader.Find("Custom/SeaweedBendUnlit");
            if (sh != null) _fallbackMaterial = new Material(sh) { name = "SeaweedBend (auto)", hideFlags = HideFlags.HideAndDontSave };
        }
        return _fallbackMaterial;
    }

    private float CurrentTime()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying) return (float)UnityEditor.EditorApplication.timeSinceStartup;
#endif
        return Time.time;
    }

    private void Update()
    {
        if (_sr == null) return;
        if (_sr.sprite != _builtFor || rows != _builtRows || columns != _builtCols || _meshTf == null)
        {
            Build();
            if (_meshTf == null) return;
        }

        SyncRenderer();

        if (pauseSway) return;
        if (!Application.isPlaying && !previewInEditor) return;
        Deform(CurrentTime());
    }

    private void Deform(float time)
    {
        if (_mesh == null) return;

        // 擺幅以世界單位設定，換算成本地 X (物件常有很扁的非等比縮放)
        float sx = Mathf.Abs(transform.lossyScale.x);
        float sy = Mathf.Abs(transform.lossyScale.y);
        float localAmp = sx > 1e-5f ? tipAmplitude / sx : 0f;
        float worldLen = (_tipY - _rootY) * sy;
        float omega = Mathf.PI * 2f / Mathf.Max(0.05f, cycleDuration);
        float phase = time * omega + phaseOffset;

        for (int k = 0; k < _baseVerts.Length; k++)
        {
            float t = _heightT[k];
            float w = Mathf.Pow(t, bendCurve);
            float p = phase - t * waveLag;
            float s = Mathf.Sin(p) + rippleAmount * Mathf.Sin(p * rippleFrequency + 1.3f) * t;
            Vector3 v = _baseVerts[k];
            float dx = localAmp * w * s;
            v.x += dx;
            // 彎出去時該段稍微下沉 (近似保持葉長)，避免看起來被拉長
            float worldDx = dx * sx;
            float segLen = worldLen * t;
            if (segLen > 1e-4f && sy > 1e-5f) v.y -= worldDx * worldDx / (2f * segLen) / sy;
            _verts[k] = v;
        }

        _mesh.vertices = _verts;
        Bounds b = new Bounds();
        b.SetMinMax(new Vector3(_minX - localAmp * 1.5f, _rootY, -0.01f), new Vector3(_maxX + localAmp * 1.5f, _tipY, 0.01f));
        _mesh.bounds = b;
    }

#if UNITY_EDITOR
    private void EditorTick()
    {
        if (Application.isPlaying || pauseSway || !previewInEditor) return;
        UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
        UnityEditor.SceneView.RepaintAll();
    }
#endif
}
