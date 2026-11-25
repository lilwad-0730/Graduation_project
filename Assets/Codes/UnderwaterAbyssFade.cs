using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// ★1125 水下最後一段的「深淵過場空間」：游到最底、要進玻璃館之前，畫面一路沉進黑暗，像到了沒有底的深海。
///
/// 原本的問題：水下的出口（Transition_To_DarkGlasses）一踏進去就黑屏＋播文字卡，前面沒有任何緩衝，
///   玩家還看著正常的水下畫面就被切走。
///
/// 做法（全部程式生成，不用美術檔、不改別的腳本）：
///   1. 世界黑幕：一塊貼在世界座標上的「由上往下、透明→全黑」漸層方塊，蓋住背景與較遠的石頭。
///      放在主角「後面一點點」（有深度測試），所以主角本人不會被這塊黑幕吃掉；底下用同一種黑補到很深，鏡頭看不到邊。
///      左邊有一段水平柔邊（不是死邊），避免左側的房間（日誌、秘密石頭）被一起壓暗。
///   2. 螢幕暗化：全螢幕黑色，不透明度依「主角深度」從 0 慢慢加到 overlayMaxDarkness。
///      所以轉場開始（文字卡／淡黑）的那一刻，畫面已經差不多全暗，接起來是連續的。
///      主角重生回到上面時，暗化會跟著深度退回去（有平滑，不會突然閃）。
///   3. 出口往下挪：出口原本緊貼在石頭尾端，玩家一碰到就轉場，幾乎沒有「沉入黑暗」的時間。
///      這裡確保出口頂邊在 triggerTopY（石頭最低處 −136.5 之下、水底 −146.8 之上），多給約 3~4 秒的下沉。
///
/// 排序：世界黑幕 sortingOrder 20（水色濾鏡 15、巨大影子 16 之上）；螢幕暗化是「跟著鏡頭」的畫布 sortingOrder 25，
///   比窒息黑圈（SuffocationCanvas，30）低，所以呼吸圈、紅光不會被蓋暗；重生黑幕（999）、轉場淡黑（9999）、文字卡（10000）都在它上面。
///
/// 程式自動掛上（只在場景名含 underwater 的關卡），不用改場景；Enabled = false 整個關掉。
/// </summary>
public class UnderwaterAbyssFade : MonoBehaviour
{
    public static bool Enabled = true;

    [Header("世界黑幕：深度範圍（世界 Y）")]
    [Tooltip("黑幕頂邊（完全透明）。原本出口頂邊 −133.9，往上約 22 米，讓玩家沿著石頭縫道一路暗下來")]
    public float gradientTopY = -112f;
    [Tooltip("漸層到這裡變成全黑（要在石頭最低處 −136.5 附近，鏡頭看出去才會是一片黑）")]
    public float gradientSolidY = -136f;
    [Tooltip("全黑一路補到這個 Y（水底 −146.8 之下；鏡頭到不了，只是保險）")]
    public float solidBottomY = -260f;

    [Header("世界黑幕：水平範圍（世界 X）")]
    [Tooltip("黑幕左邊緣（這裡完全透明）。縫道在 x 約 27～36，出口框 x 19.7～41.6")]
    public float abyssMinX = 6f;
    [Tooltip("從左邊緣到這個 X 之間水平柔邊漸入；再往右才是完整的黑幕（左邊 x<18 是日誌、秘密石頭的房間，不該被壓暗）")]
    public float abyssFullX = 18f;
    [Tooltip("黑幕右邊緣。水域最右 73.4、鏡頭半寬約 12.4，126 看不到邊")]
    public float abyssMaxX = 126f;

    [Header("世界黑幕：外觀")]
    [Tooltip("黑色（帶一點點冷藍，不要死黑色才跟水的色調接得起來）")]
    public Color abyssColor = new Color(0.004f, 0.008f, 0.018f, 1f);
    [Tooltip("最黑處的不透明度")]
    [Range(0f, 1f)] public float maxAlpha = 1f;
    [Tooltip("漸層的指數：1＝平滑 S 曲線；大於 1＝上半段更淡、越往下越快變黑")]
    [Range(0.5f, 3f)] public float rampPower = 1.15f;
    [Tooltip("黑幕放在主角後面幾米（Z）。要大於 0 才不會蓋到主角；太大會有更多石頭面擋在黑幕前（沒被黑幕壓暗）")]
    public float behindPlayer = 0.3f;
    [Tooltip("黑幕的 sortingOrder（水色濾鏡 15、巨大影子 16、窒息黑圈畫布 30）")]
    public int sortingOrder = 20;

    [Header("螢幕暗化（跟著主角深度）")]
    public bool useScreenOverlay = true;
    [Tooltip("開始變暗的主角 Y（0 暗）。預設和黑幕頂邊同高")]
    public float overlayStartY = -112f;
    [Tooltip("暗到最大的主角 Y。預設約等於出口頂邊再高一點（主角樞紐比腳高約 1.45 米），碰到出口時剛好最暗")]
    public float overlayFullY = -140f;
    [Tooltip("最暗時蓋多黑（0～1）。主角也會被一起壓暗，太高主角會看不見；身邊有 DepthAtmosphere 的微光撐著")]
    [Range(0f, 1f)] public float overlayMaxDarkness = 0.7f;
    [Tooltip("暗化曲線的指數：大於 1＝前段更淡、接近出口才快速變暗")]
    [Range(0.5f, 3f)] public float overlayPower = 1.1f;
    [Tooltip("螢幕暗化的畫布排序。要比窒息黑圈（30）低、比重生黑幕（999）、轉場淡黑（9999）、文字卡（10000）低")]
    public int overlaySortingOrder = 25;
    [Tooltip("暗化程度的追隨速度（每秒 0～1）。越大越跟手；重生回到上面時靠這個平滑退回去")]
    public float overlayFollowSpeed = 2f;

    [Header("出口往下挪（轉場空間）")]
    [Tooltip("打開＝確保往玻璃館的出口頂邊不高於 Trigger Top Y，給玩家下沉進黑暗的時間")]
    public bool lowerTransitionTrigger = true;
    [Tooltip("要處理的出口：SceneTransitionZone 的 nextSceneName")]
    public string transitionSceneName = "dark glasses";
    [Tooltip("出口頂邊的目標 Y。石頭最低 −136.5、水底 −146.8，之間的空水域；原本是 −133.9")]
    public float triggerTopY = -141.9f;

    private Transform _player;
    private Camera _cam;
    private Canvas _canvas;
    private Image _image;
    private float _depth01;
    private bool _depthPrimed;
    private Texture2D _texGradient, _texSolid;
    private Sprite _spGradient, _spSolid;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Install(SceneManager.GetActiveScene());
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Install(scene);
    }

    private static void Install(Scene scene)
    {
        if (!Enabled || !scene.IsValid()) return;
        if (!scene.name.ToLower().Contains("underwater")) return;
        if (FindAnyObjectByType<UnderwaterAbyssFade>() != null) return;
        GameObject go = new GameObject("★1125_UnderwaterAbyssFade");
        SceneManager.MoveGameObjectToScene(go, scene);
        go.AddComponent<UnderwaterAbyssFade>();
    }

    private void Start()
    {
        PlayerMovement pm = FindAnyObjectByType<PlayerMovement>();
        _player = pm != null ? pm.transform : null;
        _cam = Camera.main;

        BuildWorldBlock();
        if (useScreenOverlay) BuildOverlay();
        if (lowerTransitionTrigger) EnsureTriggerLow();
    }

    // ───────────── 世界黑幕 ─────────────

    private float XFactor(float worldX)
    {
        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(abyssMinX, abyssFullX, worldX));
    }

    private void BuildWorldBlock()
    {
        const int W = 128, H = 256;
        float width = Mathf.Max(1f, abyssMaxX - abyssMinX);
        float rampH = Mathf.Max(1f, gradientTopY - gradientSolidY);
        float z = (_player != null ? _player.position.z : 0f) + behindPlayer;

        // 漸層：上緣透明、下緣全黑（smoothstep，頭尾都柔，頂邊看不到線）；水平方向左緣柔邊
        _texGradient = new Texture2D(W, H, TextureFormat.RGBA32, false);
        _texGradient.wrapMode = TextureWrapMode.Clamp;
        _texGradient.filterMode = FilterMode.Bilinear;
        Color[] px = new Color[W * H];
        for (int y = 0; y < H; y++)
        {
            float t = 1f - y / (H - 1f);                       // 貼圖最上列 = 頂邊 (t=0)
            float a = Mathf.Pow(Mathf.SmoothStep(0f, 1f, t), rampPower) * maxAlpha;
            for (int x = 0; x < W; x++)
            {
                float wx = abyssMinX + (x + 0.5f) / W * width;
                px[y * W + x] = new Color(abyssColor.r, abyssColor.g, abyssColor.b, a * XFactor(wx));
            }
        }
        _texGradient.SetPixels(px);
        _texGradient.Apply();
        _spGradient = Sprite.Create(_texGradient, new Rect(0, 0, W, H), new Vector2(0f, 1f), 1f, 0, SpriteMeshType.FullRect);

        GameObject g = new GameObject("AbyssGradient");
        g.transform.SetParent(transform, false);
        g.transform.position = new Vector3(abyssMinX, gradientTopY, z);
        g.transform.localScale = new Vector3(width / W, rampH / H, 1f);
        SpriteRenderer sr = g.AddComponent<SpriteRenderer>();
        sr.sprite = _spGradient;
        sr.sortingOrder = sortingOrder;

        // 全黑補底：同一種黑、同樣的左側柔邊，一路補到很深
        const int SH = 2;
        _texSolid = new Texture2D(W, SH, TextureFormat.RGBA32, false);
        _texSolid.wrapMode = TextureWrapMode.Clamp;
        _texSolid.filterMode = FilterMode.Bilinear;
        Color[] sp = new Color[W * SH];
        for (int y = 0; y < SH; y++)
        {
            for (int x = 0; x < W; x++)
            {
                float wx = abyssMinX + (x + 0.5f) / W * width;
                sp[y * W + x] = new Color(abyssColor.r, abyssColor.g, abyssColor.b, maxAlpha * XFactor(wx));
            }
        }
        _texSolid.SetPixels(sp);
        _texSolid.Apply();
        _spSolid = Sprite.Create(_texSolid, new Rect(0, 0, W, SH), new Vector2(0f, 1f), 1f, 0, SpriteMeshType.FullRect);

        GameObject s = new GameObject("AbyssSolid");
        s.transform.SetParent(transform, false);
        s.transform.position = new Vector3(abyssMinX, gradientSolidY, z);
        s.transform.localScale = new Vector3(width / W, Mathf.Max(1f, gradientSolidY - solidBottomY) / SH, 1f);
        SpriteRenderer ssr = s.AddComponent<SpriteRenderer>();
        ssr.sprite = _spSolid;
        ssr.sortingOrder = sortingOrder;

        Debug.Log($"【水下深淵】★1125 世界黑幕：Y {gradientTopY:F1}（透明）→ {gradientSolidY:F1}（全黑）→ {solidBottomY:F0}，X {abyssMinX:F0}～{abyssMaxX:F0}（左側 {abyssMinX:F0}～{abyssFullX:F0} 柔邊），z={z:F2}、排序 {sortingOrder}");
    }

    // ───────────── 螢幕暗化 ─────────────

    private void BuildOverlay()
    {
        GameObject cgo = new GameObject("AbyssDarkCanvas");
        cgo.transform.SetParent(transform, false);
        _canvas = cgo.AddComponent<Canvas>();
        if (_cam != null)
        {
            // 跟著鏡頭的畫布才能用 sortingOrder 夾在窒息黑圈（30）之下；Overlay 畫布一律畫在所有 Camera 畫布之上
            _canvas.renderMode = RenderMode.ScreenSpaceCamera;
            _canvas.worldCamera = _cam;
            _canvas.planeDistance = 5f;
            _canvas.sortingLayerName = "Default";
        }
        else
        {
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        }
        _canvas.sortingOrder = overlaySortingOrder;

        GameObject img = new GameObject("AbyssDark");
        img.transform.SetParent(cgo.transform, false);
        _image = img.AddComponent<Image>();
        _image.color = new Color(abyssColor.r, abyssColor.g, abyssColor.b, 0f);
        _image.raycastTarget = false;
        RectTransform r = _image.rectTransform;
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.one;
        r.offsetMin = Vector2.zero;
        r.offsetMax = Vector2.zero;
        _canvas.enabled = false;
    }

    private float TargetDepth01()
    {
        if (_player == null) return 0f;
        float t = Mathf.InverseLerp(overlayStartY, overlayFullY, _player.position.y);
        t = Mathf.Pow(Mathf.SmoothStep(0f, 1f, t), overlayPower);
        return t * XFactor(_player.position.x);
    }

    private void LateUpdate()
    {
        if (_image == null) return;
        if (_player == null)
        {
            PlayerMovement pm = FindAnyObjectByType<PlayerMovement>();
            if (pm == null) return;
            _player = pm.transform;
        }
        if (_canvas.renderMode == RenderMode.ScreenSpaceCamera && _canvas.worldCamera == null)
        {
            _cam = Camera.main;
            if (_cam != null) _canvas.worldCamera = _cam;
            else _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        }
        else if (_canvas.renderMode == RenderMode.ScreenSpaceOverlay && Camera.main != null)
        {
            _cam = Camera.main;
            _canvas.renderMode = RenderMode.ScreenSpaceCamera;
            _canvas.worldCamera = _cam;
            _canvas.planeDistance = 5f;
            _canvas.sortingOrder = overlaySortingOrder;
        }

        float target = TargetDepth01();
        if (!_depthPrimed) { _depth01 = target; _depthPrimed = true; }   // 進場第一幀直接到位，不閃
        else _depth01 = Mathf.MoveTowards(_depth01, target, overlayFollowSpeed * Time.unscaledDeltaTime);

        float a = _depth01 * overlayMaxDarkness;
        _canvas.enabled = a > 0.002f;
        if (_canvas.enabled) _image.color = new Color(abyssColor.r, abyssColor.g, abyssColor.b, a);
    }

    // ───────────── 出口往下挪 ─────────────

    private void EnsureTriggerLow()
    {
        string want = (transitionSceneName ?? "").Trim();
        if (want.Length == 0) return;
        foreach (SceneTransitionZone z in FindObjectsByType<SceneTransitionZone>(FindObjectsInactive.Include))
        {
            if (z == null || z.nextSceneName == null) continue;
            if (!string.Equals(z.nextSceneName.Trim(), want, System.StringComparison.OrdinalIgnoreCase)) continue;
            Collider col = z.GetComponent<Collider>();
            if (col == null) continue;
            float top = col.bounds.max.y;
            if (top <= triggerTopY + 0.01f) continue;   // 場景檔已經挪過了（或本來就夠低），不要再挪一次
            float shift = top - triggerTopY;
            z.transform.position += Vector3.down * shift;
            Physics.SyncTransforms();
            Debug.Log($"【水下深淵】★1125 出口「{z.name}」頂邊 {top:F1} → {triggerTopY:F1}（往下挪 {shift:F1} 米），多給一段下沉進黑暗的時間");
        }
    }

    private void OnDestroy()
    {
        if (_spGradient != null) Destroy(_spGradient);
        if (_spSolid != null) Destroy(_spSolid);
        if (_texGradient != null) Destroy(_texGradient);
        if (_texSolid != null) Destroy(_texSolid);
    }
}
