using UnityEngine;

/// <summary>
/// 《我你他》可翻頁繪本。
///
/// 場景裡只要一個空物件掛上這支，其餘（底頁、翻頁網格、攝影機）Awake 時自己長出來。
/// 圖片放 Assets/Resources/Pages/，檔名開頭三位數編號決定順序（001_ 002_ …）。
///
/// 操作
///   下一頁：點畫面右半 / → / D / Space / 滾輪往下
///   上一頁：點畫面左半 / ← / A / 滾輪往上
///   手動掀頁：從右緣往左拖，放開時依進度決定翻過去或彈回來
///   回到第一頁：Home
/// </summary>
[DisallowMultipleComponent]
public class PageBook : MonoBehaviour
{
    [Header("頁面")]
    [Tooltip("留空的話，從 Resources/<資料夾> 依檔名排序自動載入")]
    public Texture2D[] pages;
    public string resourcesFolder = "Pages";

    [Header("尺寸")]
    public float pageWidth = 16f;
    public float pageHeight = 9f;
    [Tooltip("畫面四周留的邊，0 = 頁面剛好貼滿")]
    [Range(0f, 0.4f)] public float margin = 0.06f;

    [Header("翻頁")]
    public float turnSeconds = 0.9f;
    [Tooltip("放開手時超過這個進度就翻過去，否則彈回來")]
    [Range(0.1f, 0.9f)] public float releaseThreshold = 0.35f;
    public bool allowDrag = true;
    [Tooltip("翻到最後一頁之後再翻，回到第一頁")]
    public bool loopAtEnd = false;

    [Header("外觀")]
    [Tooltip("找不到會依序退回 Universal Render Pipeline/Unlit → Unlit/Texture")]
    public string shaderName = "Sprites/Default";
    public Color background = new Color(0.043f, 0.047f, 0.086f, 1f); // #0B0C16 夜空
    [Tooltip("翻頁過程中，底下那一頁被壓暗到多少")]
    [Range(0f, 1f)] public float underShade = 0.62f;

    // ── 對外
    public int Index { get { return index; } }
    public int PageCount { get { return pages != null ? pages.Length : 0; } }
    public bool IsBusy { get { return mode != Mode.Idle; } }
    public System.Action<int> OnPageChanged;   // 翻完，帶新頁碼
    public System.Action<int> OnTurnStarted;   // 開始翻，帶 +1 / -1

    enum Mode { Idle, Turning, Dragging }

    Mode mode = Mode.Idle;
    int index;
    int toIdx;              // 翻成功之後會停在哪一頁
    bool commitAtOne;       // 往後翻 = t 到 1 才算成功；往前翻 = t 到 0 才算成功
    float t;                // 0 = 攤平, 1 = 完全捲走
    float target;           // 動畫正朝哪個值走

    Camera cam;
    Transform underT, curlT;
    MeshRenderer underMR, curlMR;
    PageCurl curl;
    Material underMat, curlMat;

    static readonly int ID_MainTex = Shader.PropertyToID("_MainTex");
    static readonly int ID_BaseMap = Shader.PropertyToID("_BaseMap");
    static readonly int ID_Color = Shader.PropertyToID("_Color");
    static readonly int ID_BaseColor = Shader.PropertyToID("_BaseColor");

    // ───────────────────────────────────────── 建立

    void Awake()
    {
        LoadPagesIfNeeded();
        BuildRig();
        index = 0;
        ShowIdle();
        Debug.Log("[PageBook] 載入 " + PageCount + " 頁");
#if !ENABLE_LEGACY_INPUT_MANAGER
        Debug.LogWarning("[PageBook] 這個專案關掉了舊版 Input。到 Project Settings → Player → " +
                         "Active Input Handling 改成 Both，鍵盤滑鼠才會有反應。");
#endif
    }

    void LoadPagesIfNeeded()
    {
        if (pages != null && pages.Length > 0) return;
        var found = Resources.LoadAll<Texture2D>(resourcesFolder);

        // 只收「三位數編號_」開頭的檔案。
        // 資料夾裡混進別的圖也不會被當成多出來的頁。
        if (found != null && found.Length > 0)
        {
            var keep = new System.Collections.Generic.List<Texture2D>();
            foreach (var t in found)
            {
                string n = t != null ? t.name : null;
                if (n != null && n.Length >= 4 && n[3] == '_' &&
                    char.IsDigit(n[0]) && char.IsDigit(n[1]) && char.IsDigit(n[2]))
                    keep.Add(t);
            }
            if (keep.Count < found.Length)
                Debug.LogWarning("[PageBook] 略過 " + (found.Length - keep.Count) +
                                 " 個沒有三位數編號的檔案");
            found = keep.ToArray();
        }

        if (found == null || found.Length == 0)
        {
            Debug.LogError("[PageBook] Resources/" + resourcesFolder + " 裡沒有圖。" +
                           "把頁面放進 Assets/Resources/" + resourcesFolder + "/，檔名用 001_ 002_ … 開頭。");
            pages = new Texture2D[0];
            return;
        }
        System.Array.Sort(found, (a, b) => string.CompareOrdinal(a.name, b.name));
        pages = found;
    }

    Shader PickShader()
    {
        Shader s = Shader.Find(shaderName);
        if (s == null) s = Shader.Find("Universal Render Pipeline/Unlit");
        if (s == null) s = Shader.Find("Unlit/Texture");
        if (s == null) s = Shader.Find("Sprites/Default");
        if (s == null)
            Debug.LogError("[PageBook] 找不到可用的 shader，頁面會是粉紅色。到 Inspector 的 Shader Name 填一個專案裡有的。");
        return s;
    }

    void SetTex(Material m, Texture2D tex)
    {
        if (m.HasProperty(ID_MainTex)) m.SetTexture(ID_MainTex, tex);
        if (m.HasProperty(ID_BaseMap)) m.SetTexture(ID_BaseMap, tex);
    }

    void SetTint(Material m, float g)
    {
        Color c = new Color(g, g, g, 1f);
        if (m.HasProperty(ID_Color)) m.SetColor(ID_Color, c);
        if (m.HasProperty(ID_BaseColor)) m.SetColor(ID_BaseColor, c);
    }

    void BuildRig()
    {
        Shader sh = PickShader();
        underMat = new Material(sh) { name = "Page_Under" };
        curlMat = new Material(sh) { name = "Page_Curl" };
        underMat.renderQueue = 3000;
        curlMat.renderQueue = 3010;

        var under = new GameObject("UnderPage");
        underT = under.transform;
        underT.SetParent(transform, false);
        underT.localPosition = new Vector3(-pageWidth * 0.5f, 0f, 0.02f);
        under.AddComponent<MeshFilter>().sharedMesh = Quad(pageWidth, pageHeight);
        underMR = under.AddComponent<MeshRenderer>();
        underMR.sharedMaterial = underMat;
        underMR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        underMR.receiveShadows = false;

        var cp = new GameObject("TurningPage");
        curlT = cp.transform;
        curlT.SetParent(transform, false);
        curlT.localPosition = new Vector3(-pageWidth * 0.5f, 0f, 0f);
        cp.AddComponent<MeshFilter>();
        curlMR = cp.AddComponent<MeshRenderer>();
        curlMR.sharedMaterial = curlMat;
        curlMR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        curlMR.receiveShadows = false;
        curl = cp.AddComponent<PageCurl>();
        curl.width = pageWidth;
        curl.height = pageHeight;
        curl.EnsureMesh();

        cam = Camera.main;
        if (cam == null)
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            cam = go.AddComponent<Camera>();
            go.AddComponent<AudioListener>();
        }
        cam.orthographic = true;
        cam.transform.position = new Vector3(0f, 0f, -10f);
        cam.transform.rotation = Quaternion.identity;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = background;
        FitCamera();
    }

    static Mesh Quad(float w, float h)
    {
        var m = new Mesh { name = "PageQuad" };
        m.vertices = new[]
        {
            new Vector3(0f, -h * 0.5f, 0f), new Vector3(w, -h * 0.5f, 0f),
            new Vector3(0f,  h * 0.5f, 0f), new Vector3(w,  h * 0.5f, 0f)
        };
        m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
        m.triangles = new[] { 0, 2, 1, 1, 2, 3 };
        m.colors = new[] { Color.white, Color.white, Color.white, Color.white };
        m.RecalculateBounds();
        return m;
    }

    public void FitCamera()
    {
        if (cam == null) return;
        float k = 1f + margin;
        float byH = pageHeight * 0.5f * k;
        float aspect = (float)Screen.width / Mathf.Max(1, Screen.height);
        float byW = (pageWidth * 0.5f * k) / Mathf.Max(0.0001f, aspect);
        cam.orthographicSize = Mathf.Max(byH, byW);
    }

    // ───────────────────────────────────────── 顯示

    Texture2D Page(int i)
    {
        if (pages == null || pages.Length == 0) return null;
        return pages[Mathf.Clamp(i, 0, pages.Length - 1)];
    }

    void ShowIdle()
    {
        mode = Mode.Idle;
        SetTex(underMat, Page(index));
        SetTint(underMat, 1f);
        curlMR.enabled = false;
    }

    void ApplyFold(float t01)
    {
        t01 = Mathf.Clamp01(t01);
        curl.SetFold(Mathf.Lerp(curl.FlatFold, curl.GoneFold, t01));
        SetTint(underMat, Mathf.Lerp(underShade, 1f, Mathf.SmoothStep(0f, 1f, t01)));
    }

    // ───────────────────────────────────────── 翻頁

    bool Prepare(int dir)
    {
        if (mode != Mode.Idle) return false;
        if (pages == null || pages.Length < 2) return false;

        if (dir > 0)
        {
            bool last = index >= pages.Length - 1;
            if (last && !loopAtEnd) return false;
            toIdx = last ? 0 : index + 1;
            SetTex(curlMat, Page(index));
            SetTex(underMat, Page(toIdx));
            commitAtOne = true;
            t = 0f;
        }
        else
        {
            if (index <= 0) return false;
            toIdx = index - 1;
            SetTex(curlMat, Page(toIdx));
            SetTex(underMat, Page(index));
            commitAtOne = false;
            t = 1f;
        }

        curlMR.enabled = true;
        ApplyFold(t);
        if (OnTurnStarted != null) OnTurnStarted(dir);
        return true;
    }

    public void Next()
    {
        if (!Prepare(+1)) return;
        target = 1f; mode = Mode.Turning;
    }

    public void Prev()
    {
        if (!Prepare(-1)) return;
        target = 0f; mode = Mode.Turning;
    }

    public void GoTo(int i, bool animate = false)
    {
        i = Mathf.Clamp(i, 0, Mathf.Max(0, PageCount - 1));
        if (i == index) return;
        if (animate) { if (i > index) Next(); else Prev(); return; }
        index = i;
        ShowIdle();
        if (OnPageChanged != null) OnPageChanged(index);
    }

    void Settle()
    {
        bool committed = (target >= 0.5f) == commitAtOne;
        if (committed) index = toIdx;
        ShowIdle();
        if (committed && OnPageChanged != null) OnPageChanged(index);
    }

    // ───────────────────────────────────────── 每幀

    void Update()
    {
        FitCamera();
        HandleInput();

        if (mode == Mode.Turning)
        {
            // 緩停：離目標越近走得越慢。
            // 不用 SmoothStep 重新映射，是為了讓「放開手」那一瞬間不會跳格 —— 拖曳時 t 就是實際位置。
            float remain = Mathf.Abs(target - t);
            float k = Mathf.Lerp(0.30f, 1f, Mathf.Clamp01(remain * 2.4f));
            float step = k * Time.unscaledDeltaTime / Mathf.Max(0.05f, turnSeconds);
            t = Mathf.MoveTowards(t, target, Mathf.Max(step, Time.unscaledDeltaTime * 0.12f));
            ApplyFold(t);
            if (Mathf.Abs(t - target) < 1e-4f) { t = target; Settle(); }
        }
    }

    void HandleInput()
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D) ||
            Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.PageDown)) Next();
        if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A) ||
            Input.GetKeyDown(KeyCode.PageUp)) Prev();
        if (Input.GetKeyDown(KeyCode.Home)) GoTo(0);

        float wheel = Input.mouseScrollDelta.y;
        if (Mathf.Abs(wheel) > 0.01f) { if (wheel < 0f) Next(); else Prev(); }

        if (Input.GetMouseButtonDown(0) && mode == Mode.Idle)
        {
            float lx = LocalX(Input.mousePosition);
            bool right = lx > pageWidth * 0.5f;

            if (allowDrag && right && lx > pageWidth * 0.66f)
            {
                if (Prepare(+1)) { mode = Mode.Dragging; target = 1f; }
            }
            else if (allowDrag && !right && lx < pageWidth * 0.34f && index > 0)
            {
                if (Prepare(-1)) { mode = Mode.Dragging; target = 0f; }
            }
            else
            {
                if (right) Next(); else Prev();
            }
        }

        if (mode == Mode.Dragging)
        {
            if (Input.GetMouseButton(0))
            {
                float lx = Mathf.Clamp(LocalX(Input.mousePosition), curl.GoneFold, curl.FlatFold);
                t = Mathf.InverseLerp(curl.FlatFold, curl.GoneFold, lx);
                ApplyFold(t);
            }
            else
            {
                bool forward = commitAtOne;
                bool commit = forward ? (t >= releaseThreshold) : (t <= 1f - releaseThreshold);
                target = commit ? (forward ? 1f : 0f) : (forward ? 0f : 1f);
                mode = Mode.Turning;
            }
        }
#endif
    }

    float LocalX(Vector3 screen)
    {
        Vector3 w = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -cam.transform.position.z));
        return curlT.InverseTransformPoint(w).x;
    }
}
