using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 人聲字幕：畫面下方置中、淡入淡出，跟著語音長度顯示。
/// 字幕後方有一朵淡淡的雲朵形底板，避免字和背景糊在一起。
/// 樣式全部在 Style 裡，掛在 GuidanceLight 的 Inspector 上可以直接調（Play 中改也會馬上套用）。
/// 用法：VoiceSubtitle.Show("字幕", 秒數, style);
/// </summary>
public class VoiceSubtitle : MonoBehaviour
{
    [System.Serializable]
    public class Style
    {
        [Header("字型")]
        [Tooltip("字幕字型（TMP Font Asset）。預設霞鶩文楷 TC Medium")]
        public TMP_FontAsset font;
        [Tooltip("字的大小（以 1920×1080 畫面為準）")]
        [Range(20f, 80f)] public float fontSize = 40f;
        [Tooltip("字距（0＝正常，正數拉開）")]
        [Range(-10f, 30f)] public float characterSpacing = 3f;
        [Tooltip("行距（0＝正常，正數拉開）")]
        [Range(-20f, 80f)] public float lineSpacing = 25f;
        [Tooltip("字的粗細（正數變粗，負數變細）")]
        [Range(-0.3f, 0.3f)] public float boldness = 0.05f;

        [Header("顏色")]
        [Tooltip("字的顏色。預設 #4D5285 暮色藍紫（取自棉花堡雲朵的陰影色）")]
        public Color textColor = new Color(0.30f, 0.32f, 0.52f, 1f);
        [Tooltip("字外圍白邊的顏色（A 是濃淡）。預設純白不透明")]
        public Color glowColor = new Color(1f, 1f, 1f, 1f);   // ★1109l 白邊加強：不透明
        [Tooltip("光暈寬度（0＝沒有光暈）")]
        [Range(0f, 0.5f)] public float glowWidth = 0.30f;   // ★1109l 加粗（受字型圖集 padding 8 限制，再大陰影就沒空間）
        [Tooltip("光暈邊緣柔和度（0＝硬邊，越大越朦朧）")]
        [Range(0f, 1f)] public float glowSoftness = 0.05f;   // ★1109l 邊緣更銳利，視覺上更強

        // ★1109l 陰影：字（含白邊）右下方一圈柔和的深色投影，讓字幕在亮底和雜亂背景上都能讀
        [Header("陰影")]
        [Tooltip("要不要顯示陰影")]
        public bool shadowOn = true;
        [Tooltip("陰影顏色（A 是濃淡）。會跟著字幕一起淡入淡出")]
        public Color shadowColor = new Color(0.08f, 0.06f, 0.10f, 0.8f);
        [Tooltip("陰影偏移（X 正＝往右，Y 負＝往下；TMP Underlay 單位，字型圖集 padding 只有 8，實際位移會被自動壓縮，所以要設大一點才看得出來）")]
        public Vector2 shadowOffset = new Vector2(1f, -1f);
        [Tooltip("陰影邊緣柔和度（0＝硬邊，越大越朦朧）")]
        [Range(0f, 1f)] public float shadowSoftness = 0.4f;
        [Tooltip("陰影膨脹（讓陰影蓋住白邊的範圍；0＝只有字本體大小）")]
        [Range(-1f, 1f)] public float shadowDilate = 0.45f;

        [Header("位置（畫面比例，0＝下／左，1＝上／右）")]
        [Tooltip("字幕區的下緣")]
        [Range(0f, 0.5f)] public float bottom = 0.08f;
        [Tooltip("字幕區的上緣")]
        [Range(0.05f, 0.6f)] public float top = 0.20f;
        [Tooltip("左右各留多少空白（字太長會自動換行）")]
        [Range(0f, 0.4f)] public float sideMargin = 0.15f;

        [Header("淡入淡出（秒）")]
        [Range(0f, 2f)] public float fadeIn = 0.4f;
        [Range(0f, 3f)] public float fadeOut = 0.6f;

        [Header("雲朵底板（字幕後方的淡色雲朵，大小跟著字幕自動調整）")]
        [Tooltip("要不要顯示底板")]
        public bool showPanel = true;
        [Tooltip("底板顏色（A 是濃淡，越小越淡）。預設 #FFF7F0 晨光暖白，和字的光暈同色系")]
        public Color panelColor = new Color(1f, 0.97f, 0.94f, 0.38f);
        [Tooltip("文字到雲朵邊緣的距離（左右, 上下）")]
        public Vector2 panelPadding = new Vector2(90f, 42f);
        [Tooltip("邊緣淡出的寬度（越大邊緣越像霧一樣慢慢散開）")]
        [Range(0f, 80f)] public float panelEdgeFade = 48f;
        [Tooltip("雲朵的蓬鬆程度（0＝圓滑膠囊形，1＝上下緣一團一團很明顯）")]
        [Range(0f, 1f)] public float panelPuffiness = 0.6f;

        // ★1109m 玩家模型走到字幕後面時，字幕整體（字、白邊、陰影、底板）淡成半透明，離開再淡回來
        [Header("玩家重疊時淡化")]
        [Tooltip("玩家模型在畫面上和字幕文字範圍重疊時，字幕要不要自動變淡")]
        public bool fadeWhenPlayerOverlaps = true;
        [Tooltip("重疊時字幕的不透明度倍率（1＝不變淡，0＝完全消失）")]
        [Range(0f, 1f)] public float overlapAlpha = 0.3f;
        [Tooltip("淡化／恢復所需的時間（秒，不受 Time.timeScale 影響）")]
        [Range(0f, 1f)] public float overlapFadeTime = 0.25f;
        // ★1109n overlapPadding 預設 20 → 0，範圍開放負值（可縮小文字判定框）
        [Tooltip("判定重疊時，文字範圍往外多留的邊距（以 1920×1080 畫面為準的畫布單位）。0＝剛好貼齊文字，正數＝玩家快碰到字就先淡化，負數＝縮小文字判定框")]
        [Range(-40f, 100f)] public float overlapPadding = 0f;
        // ★1109n 玩家判定框改用人形骨架，這裡是身體厚度（世界公尺），四周都加
        [Tooltip("玩家判定框（由人形骨架算出）四周加的身體厚度，單位是世界公尺")]
        [Range(0f, 1f)] public float playerBodyMargin = 0.15f;
        // ★1109o 側面視角骨頭幾乎疊成一直線，頭髮和帽T的寬度要另外補
        [Tooltip("玩家判定框左右各加的寬度（世界公尺）。側面看骨頭擠在一起，要靠這個補上頭髮、衣服的寬度")]
        [Range(0f, 1.5f)] public float playerBodyMarginX = 0.55f;
        [Tooltip("Play 中在畫面上畫出玩家判定框（綠/紅）與字幕判定框（黃），調整用")]
        public bool showOverlapDebug = false;
    }

    private static VoiceSubtitle _instance;
    private TextMeshProUGUI _label;
    private Image _panel;
    private float _alpha;   // 淡入淡出的基礎 alpha（不含玩家重疊倍率）
    private float _overlapMul = 1f;   // ★1109m 玩家重疊淡化倍率（1 → overlapAlpha，平滑移動）
    private Canvas _canvas;
    private Transform _player;   // ★1109m 玩家（Tag=Player），被銷毀就重找
    private readonly System.Collections.Generic.List<Renderer> _playerRenderers = new System.Collections.Generic.List<Renderer>();
    private readonly System.Collections.Generic.List<Renderer> _rendererScratch = new System.Collections.Generic.List<Renderer>();
    private float _nextRendererRefresh;
    // ★1109n 人形骨架判定 + 除錯框
    private readonly System.Collections.Generic.List<Transform> _playerBones = new System.Collections.Generic.List<Transform>();
    private Transform _headBone, _neckBone;
    private Rect _dbgPlayerRect, _dbgSubRect;
    private bool _dbgHasPlayer, _dbgHasSub, _dbgOverlap;
    private Texture2D _dbgTex;
    private Vector4 _panelKey = new Vector4(-1f, -1f, -1f, -1f);
    private Vector4 _padKey = new Vector4(-9f, -9f, -9f, -9f);   // ★1109l 上次通知 TMP 重算 padding 時的參數
    private Coroutine _routine;
    private Style _style;


    public static void Show(string text, float seconds, Style style)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (style == null) style = new Style();
        if (_instance == null) _instance = Create();
        _instance._style = style;
        _instance.Apply();
        if (_instance._routine != null) _instance.StopCoroutine(_instance._routine);
        _instance._label.text = text;
        _instance._routine = _instance.StartCoroutine(_instance.Run(Mathf.Max(1f, seconds)));
    }

    private static VoiceSubtitle Create()
    {
        GameObject canvasObj = new GameObject("[人聲字幕]");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 8500;   // 在一般 UI 之上、文字卡（黑幕）之下
        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // 底板放在字幕前面建立，才會畫在字的後方
        GameObject panelObj = new GameObject("SubtitlePanel");
        panelObj.transform.SetParent(canvasObj.transform, false);
        Image panel = panelObj.AddComponent<Image>();
        panel.type = Image.Type.Simple;
        panel.raycastTarget = false;
        panel.color = Color.clear;

        GameObject go = new GameObject("Subtitle");
        go.transform.SetParent(canvasObj.transform, false);
        TextMeshProUGUI label = go.AddComponent<TextMeshProUGUI>();
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.raycastTarget = false;
        label.color = new Color(1f, 1f, 1f, 0f);

        VoiceSubtitle s = canvasObj.AddComponent<VoiceSubtitle>();
        s._label = label;
        s._panel = panel;
        s._canvas = canvas;
        return s;
    }

    /// <summary>把 Style 套到字幕上（每幀都套，Play 中在 Inspector 調整會馬上看到）。</summary>
    private void Apply()
    {
        Style st = _style;
        if (st.font != null && _label.font != st.font) _label.font = st.font;
        _label.fontSize = st.fontSize;
        _label.characterSpacing = st.characterSpacing;
        _label.lineSpacing = st.lineSpacing;
        _label.color = new Color(st.textColor.r, st.textColor.g, st.textColor.b, _label.color.a);

        // 只改這個字幕自己的材質，不影響其他文字
        Material m = _label.fontMaterial;
        // ★1109l TMP 的描邊是「跨在字邊緣上」往內外各長一半，描邊越寬字身越被吃掉（細字會變灰白）。
        //        字身多膨脹 glowWidth 把內側那一半補回來，描邊就只往外長，字身維持原本粗細（再加 boldness）
        m.SetFloat("_FaceDilate", st.boldness + (st.glowWidth > 0f ? st.glowWidth : 0f));
        m.SetColor("_OutlineColor", st.glowColor);
        m.SetFloat("_OutlineWidth", st.glowWidth);
        m.SetFloat("_OutlineSoftness", st.glowSoftness);
        // ★1109l 這個 shader 的白邊要開 OUTLINE_ON 才會畫（只用 SetFloat 設寬度不會開）
        if (st.glowWidth > 0f) m.EnableKeyword(ShaderUtilities.Keyword_Outline);
        else m.DisableKeyword(ShaderUtilities.Keyword_Outline);

        // ★1109l 陰影＝TMP 的 Underlay；shader 會把 Underlay 乘上頂點 alpha，所以自動跟著字幕淡入淡出
        if (st.shadowOn) m.EnableKeyword(ShaderUtilities.Keyword_Underlay);
        else m.DisableKeyword(ShaderUtilities.Keyword_Underlay);
        m.SetColor("_UnderlayColor", st.shadowColor);
        m.SetFloat("_UnderlayOffsetX", st.shadowOffset.x);
        m.SetFloat("_UnderlayOffsetY", st.shadowOffset.y);
        m.SetFloat("_UnderlaySoftness", st.shadowSoftness);
        m.SetFloat("_UnderlayDilate", st.shadowDilate);
        // 材質參數是直接寫的，字的外擴範圍（padding）要自己通知 TMP 重算，不然陰影會被字框裁掉
        Vector4 padKey = new Vector4(st.glowWidth * 3f + st.glowSoftness, st.shadowOn ? 1f : 0f,
                                     st.shadowOffset.x + st.shadowOffset.y * 3f + st.shadowSoftness * 7f, st.shadowDilate + st.boldness * 5f);
        if (padKey != _padKey)
        {
            _padKey = padKey;
            ShaderUtilities.GetShaderPropertyIDs();
            _label.UpdateMeshPadding();
            _label.havePropertiesChanged = true;
            _label.SetVerticesDirty();
        }

        RectTransform rt = _label.rectTransform;
        rt.anchorMin = new Vector2(st.sideMargin, st.bottom);
        rt.anchorMax = new Vector2(1f - st.sideMargin, Mathf.Max(st.top, st.bottom + 0.02f));
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        ApplyPanel(st);
    }

    /// <summary>雲朵底板：貼齊實際文字範圍，往外留 padding，再加上邊緣淡出的範圍。</summary>
    private void ApplyPanel(Style st)
    {
        bool show = st.showPanel && !string.IsNullOrEmpty(_label.text);
        _panel.enabled = show;
        if (!show) return;

        _label.ForceMeshUpdate();
        Bounds b = _label.textBounds;
        float w = b.size.x + st.panelPadding.x * 2f;
        float h = Mathf.Max(b.size.y + st.panelPadding.y * 2f, 40f);
        float margin = st.panelEdgeFade + h * 0.3f;   // 雲泡會稍微凸出去，加上淡出範圍

        // 只有大小或樣式變了才重畫雲朵
        Vector4 key = new Vector4(Mathf.Round(w), Mathf.Round(h), st.panelEdgeFade, st.panelPuffiness);
        if (key != _panelKey || _panel.sprite == null)
        {
            _panelKey = key;
            if (_panel.sprite != null) { Destroy(_panel.sprite.texture); Destroy(_panel.sprite); }
            _panel.sprite = BuildCloudSprite(w, h, margin, st.panelEdgeFade, st.panelPuffiness);
        }

        RectTransform lrt = _label.rectTransform;
        RectTransform prt = _panel.rectTransform;
        Vector2 center = (lrt.anchorMin + lrt.anchorMax) * 0.5f;
        prt.anchorMin = prt.anchorMax = center;
        prt.pivot = new Vector2(0.5f, 0.5f);
        prt.anchoredPosition = b.center;
        prt.sizeDelta = new Vector2(w + margin * 2f, h + margin * 2f);

        _panel.color = new Color(st.panelColor.r, st.panelColor.g, st.panelColor.b, st.panelColor.a * _alpha * _overlapMul);
    }

    /// <summary>
    /// 產生雲朵形狀的貼圖：中間一條圓滑主體，上下緣疊幾顆大小不一的圓泡，
    /// 用平滑聯集融在一起，邊緣再慢慢淡出。單位是畫布單位（w×h 是雲朵本體大小）。
    /// </summary>
    private static Sprite BuildCloudSprite(float w, float h, float margin, float fade, float puff)
    {
        const float res = 0.5f;   // 1 像素＝2 畫布單位，柔邊放大看不出差別
        int tw = Mathf.Max(4, Mathf.CeilToInt((w + margin * 2f) * res));
        int th = Mathf.Max(4, Mathf.CeilToInt((h + margin * 2f) * res));

        // 形狀：下緣較平、上緣一團團往中間拱起（像場景裡的雲）
        float coreR = h * 0.36f;
        float coreL = Mathf.Max(w * 0.5f - h * 0.45f, 0f);
        float span = coreL * 2f;
        var puffs = new System.Collections.Generic.List<Vector3>();   // x, y, 半徑

        int n = Mathf.Max(2, Mathf.RoundToInt(span / h) + 1);
        for (int i = 0; i < n; i++)   // 上緣的大雲泡，越靠中間越大越高
        {
            float u = i / (float)(n - 1);
            float dome = Mathf.Sin(Mathf.PI * u);
            float x = -coreL + span * u + (Hash(i, 1) - 0.5f) * h * 0.3f;
            puffs.Add(new Vector3(x, h * (0.08f + 0.22f * dome * puff + 0.06f * Hash(i, 2) * puff),
                                     h * (0.24f + 0.20f * dome * puff + 0.16f * Hash(i, 3))));
        }
        for (int i = 0; i < n - 1; i++)   // 大雲泡之間的小雲泡，打破太整齊的節奏
        {
            float u = (i + 0.5f) / (n - 1);
            float dome = Mathf.Sin(Mathf.PI * u);
            float x = -coreL + span * u + (Hash(i, 6) - 0.5f) * h * 0.2f;
            puffs.Add(new Vector3(x, h * (0.14f + 0.10f * dome * puff), h * (0.18f + 0.08f * Hash(i, 7))));
        }
        int m = Mathf.Max(1, n - 2);
        for (int i = 0; i < m; i++)   // 下緣：低而平
        {
            float u = (i + 1f) / (m + 1f);
            float x = -coreL + span * u + (Hash(i, 4) - 0.5f) * h * 0.3f;
            puffs.Add(new Vector3(x, -h * 0.08f, h * (0.29f + 0.05f * Hash(i, 5))));
        }
        puffs.Add(new Vector3(-coreL - h * 0.06f, -h * 0.02f, h * 0.33f));   // 左右兩端
        puffs.Add(new Vector3(coreL + h * 0.06f, -h * 0.04f, h * 0.31f));

        float k = h * 0.10f;                       // 雲泡之間融合的圓滑度
        float inner = Mathf.Max(fade * 0.7f, 1f);  // 邊緣往內、往外淡出的距離
        float outer = Mathf.Max(fade * 0.7f, 1f);
        float ox = (w + margin * 2f) * 0.5f, oy = (h + margin * 2f) * 0.5f;

        Color32[] px = new Color32[tw * th];
        for (int y = 0; y < th; y++)
            for (int x = 0; x < tw; x++)
            {
                float cx = (x + 0.5f) / res - ox;
                float cy = (y + 0.5f) / res - oy;
                float dx = Mathf.Max(Mathf.Abs(cx) - coreL, 0f);
                float d = Mathf.Sqrt(dx * dx + cy * cy) - coreR;
                foreach (Vector3 c in puffs)
                {
                    float ex = cx - c.x, ey = cy - c.y;
                    d = SmoothMin(d, Mathf.Sqrt(ex * ex + ey * ey) - c.z, k);
                }
                float a = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-inner, outer, d));
                px[y * tw + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }

        Texture2D tex = new Texture2D(tw, th, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, tw, th), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
    }

    private static float SmoothMin(float a, float b, float k)
    {
        float t = Mathf.Clamp01(0.5f + 0.5f * (b - a) / k);
        return Mathf.Lerp(b, a, t) - k * t * (1f - t);
    }

    /// <summary>固定的偽亂數（同一句字幕每次形狀都一樣）。</summary>
    private static float Hash(int i, int salt)
    {
        float v = Mathf.Sin(i * 12.9898f + salt * 78.233f) * 43758.5453f;
        return v - Mathf.Floor(v);
    }

    private IEnumerator Run(float seconds)
    {
        float t = 0f;
        float startA = _alpha;   // ★1109m 用基礎 alpha，不把重疊倍率算進淡入起點
        while (true)
        {
            t += Time.unscaledDeltaTime;
            UpdateOverlap();
            float fadeIn = _style.fadeIn, fadeOut = _style.fadeOut;
            if (t >= seconds + fadeOut) break;
            float a = t < fadeIn ? Mathf.Lerp(startA, 1f, t / fadeIn)
                    : t < seconds ? 1f
                    : 1f - (t - seconds) / Mathf.Max(0.0001f, fadeOut);
            Apply();
            SetAlpha(Mathf.Clamp01(a));
            yield return null;
        }
        SetAlpha(0f);
        _routine = null;
    }

    /// <summary>a＝淡入淡出的基礎 alpha；實際畫出來的 alpha ＝ a × 玩家重疊倍率。</summary>
    private void SetAlpha(float a)
    {
        _alpha = a;
        float eff = a * _overlapMul;
        Color c = _label.color;
        c.a = eff;   // 字面、白邊、陰影（Underlay）都吃頂點 alpha，一起淡
        _label.color = c;
        if (_panel != null && _style != null)
        {
            Color pc = _style.panelColor;
            _panel.color = new Color(pc.r, pc.g, pc.b, pc.a * eff);
        }
    }

    // ───────── ★1109m 玩家重疊淡化 ─────────

    /// <summary>每幀更新重疊倍率：玩家螢幕範圍和字幕文字範圍（含 padding）重疊就往 overlapAlpha 走，否則回 1。</summary>
    private void UpdateOverlap()
    {
        Style st = _style;
        if (st == null) return;
        float target = 1f;
        // ★1109n 除錯框開著時，就算不淡化也要算判定框
        bool overlap = (st.fadeWhenPlayerOverlaps || st.showOverlapDebug) && PlayerOverlapsSubtitle(st);
        if (st.fadeWhenPlayerOverlaps && overlap) target = Mathf.Clamp01(st.overlapAlpha);
        float rate = Time.unscaledDeltaTime / Mathf.Max(0.0001f, st.overlapFadeTime) * Mathf.Max(0.05f, 1f - st.overlapAlpha);   // 淡出與恢復速度一致（走完 1 - overlapAlpha 的距離剛好 overlapFadeTime）
        _overlapMul = Mathf.MoveTowards(_overlapMul, target, rate);
    }

    private bool PlayerOverlapsSubtitle(Style st)
    {
        _dbgHasPlayer = _dbgHasSub = _dbgOverlap = false;
        if (_label == null || string.IsNullOrEmpty(_label.text) || _canvas == null) return false;
        Camera cam = Camera.main;
        if (cam == null) return false;

        // 字幕螢幕範圍：textBounds 是字幕物件的本地座標，轉世界座標（Overlay 畫布的世界座標＝螢幕像素），再往外加 padding
        Bounds tb = _label.textBounds;
        if (tb.size.x <= 0f || tb.size.y <= 0f) return false;
        RectTransform lrt = _label.rectTransform;
        Vector3 p0 = lrt.TransformPoint(new Vector3(tb.min.x, tb.min.y, 0f));
        Vector3 p1 = lrt.TransformPoint(new Vector3(tb.max.x, tb.max.y, 0f));
        float pad = st.overlapPadding * _canvas.scaleFactor;
        float sx0 = Mathf.Min(p0.x, p1.x) - pad, sx1 = Mathf.Max(p0.x, p1.x) + pad;
        float sy0 = Mathf.Min(p0.y, p1.y) - pad, sy1 = Mathf.Max(p0.y, p1.y) + pad;
        if (sx1 < sx0) sx0 = sx1 = (sx0 + sx1) * 0.5f;   // padding 為負且縮過頭時收成一條線，不要出現負寬度
        if (sy1 < sy0) sy0 = sy1 = (sy0 + sy1) * 0.5f;
        Rect subRect = Rect.MinMaxRect(sx0, sy0, sx1, sy1);
        _dbgSubRect = subRect;
        _dbgHasSub = true;

        Rect playerRect;
        if (!TryGetPlayerScreenRect(cam, st, out playerRect)) return false;
        _dbgPlayerRect = playerRect;
        _dbgHasPlayer = true;
        _dbgOverlap = subRect.Overlaps(playerRect);
        return _dbgOverlap;
    }

    /// <summary>
    /// ★1109n 玩家螢幕外框。有人形骨架就用骨頭位置（頭頂另外補上頭高、四周補身體厚度），
    /// 沒有才退回 renderer bounds（bounds 常常比身體鬆很多，會誤判重疊）。
    /// </summary>
    private bool TryGetPlayerScreenRect(Camera cam, Style st, out Rect rect)
    {
        rect = default(Rect);
        if (_player == null)   // 場景會換，玩家被銷毀就重找（每 1 秒最多找一次）
        {
            if (Time.unscaledTime < _nextRendererRefresh) return false;
            _nextRendererRefresh = Time.unscaledTime + 1f;
            GameObject go = null;
            try { go = GameObject.FindGameObjectWithTag("Player"); } catch (UnityException) { }
            if (go == null) { _playerRenderers.Clear(); return false; }
            _player = go.transform;
            RefreshPlayerRenderers();
        }
        else if (Time.unscaledTime >= _nextRendererRefresh)   // 定期重抓 renderer 清單（換裝、特效物件開關）
        {
            _nextRendererRefresh = Time.unscaledTime + 1f;
            RefreshPlayerRenderers();
        }

        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        bool any = false;

        if (_playerBones.Count > 0)
        {
            Vector3 center = Vector3.zero;
            int n = 0;
            for (int i = 0; i < _playerBones.Count; i++)
            {
                Transform t = _playerBones[i];
                if (t == null) continue;
                Vector3 wp = t.position;
                center += wp; n++;
                Vector3 sp = cam.WorldToScreenPoint(wp);
                if (sp.z <= 0f) continue;
                any = true;
                if (sp.x < minX) minX = sp.x;
                if (sp.x > maxX) maxX = sp.x;
                if (sp.y < minY) minY = sp.y;
                if (sp.y > maxY) maxY = sp.y;
            }
            if (!any || n == 0) return false;

            // 頭頂：Head 骨頭只在脖子上端，往上補 1.6 × |Head − Neck|（世界公尺）
            if (_headBone != null && _neckBone != null)
            {
                Vector3 hp = _headBone.position;
                float headH = 1.6f * Vector3.Distance(hp, _neckBone.position);
                Vector3 sp = cam.WorldToScreenPoint(hp + Vector3.up * headH);
                if (sp.z > 0f)
                {
                    if (sp.y > maxY) maxY = sp.y;
                    if (sp.y < minY) minY = sp.y;
                }
            }

            // 身體厚度：用「身體中心 1 公尺在螢幕上多少像素」換算（正交相機精確，透視相機是近似）
            center /= n;
            Vector3 s0 = cam.WorldToScreenPoint(center);
            Vector3 s1 = cam.WorldToScreenPoint(center + cam.transform.right);
            float pxPerM = Mathf.Abs(s1.x - s0.x);
            float m = st.playerBodyMargin * pxPerM;
            float mx = st.playerBodyMarginX * pxPerM;   // ★1109o 左右另外加寬
            minX -= mx; maxX += mx; minY -= m; maxY += m;
            rect = Rect.MinMaxRect(minX, minY, maxX, maxY);
            return true;
        }

        // 退路：renderer bounds（略過沒有 mesh／零大小的 renderer）
        bool has = false;
        Bounds b = default(Bounds);
        for (int i = 0; i < _playerRenderers.Count; i++)
        {
            Renderer r = _playerRenderers[i];
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
            Bounds rb = r.bounds;
            if (rb.size.sqrMagnitude <= 1e-6f) continue;
            if (!has) { b = rb; has = true; }
            else b.Encapsulate(rb);
        }
        if (!has) return false;

        Vector3 c = b.center, e = b.extents;
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
            Vector3 sp = cam.WorldToScreenPoint(corner);
            if (sp.z <= 0f) continue;   // 在相機後面的角不算
            any = true;
            if (sp.x < minX) minX = sp.x;
            if (sp.x > maxX) maxX = sp.x;
            if (sp.y < minY) minY = sp.y;
            if (sp.y > maxY) maxY = sp.y;
        }
        if (!any) return false;
        rect = Rect.MinMaxRect(minX, minY, maxX, maxY);
        return true;
    }

    /// <summary>只收「看得到的模型」：SkinnedMeshRenderer／MeshRenderer。粒子、拖尾、線、Sprite（護盾弧、影子）一律不算。</summary>
    private void RefreshPlayerRenderers()
    {
        _playerRenderers.Clear();
        if (_player == null) return;
        _player.GetComponentsInChildren<Renderer>(false, _rendererScratch);
        for (int i = 0; i < _rendererScratch.Count; i++)
        {
            Renderer r = _rendererScratch[i];
            if (r == null || !r.enabled) continue;
            // ★1109n 沒有 mesh 的 renderer（例如空的 SkinnedMeshRenderer）bounds 是零大小，不收
            SkinnedMeshRenderer smr = r as SkinnedMeshRenderer;
            if (smr != null) { if (smr.sharedMesh != null) _playerRenderers.Add(r); continue; }
            if (r is MeshRenderer)
            {
                MeshFilter mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) _playerRenderers.Add(r);
            }
        }
        _rendererScratch.Clear();

        // ★1109n 人形骨架（有的話優先用來算判定框）
        _playerBones.Clear();
        _headBone = _neckBone = null;
        Animator anim = _player.GetComponentInChildren<Animator>(true);
        if (anim != null && anim.isHuman)
        {
            _headBone = anim.GetBoneTransform(HumanBodyBones.Head);
            _neckBone = anim.GetBoneTransform(HumanBodyBones.Neck);
            for (int i = 0; i < s_boneIds.Length; i++)
            {
                Transform t = anim.GetBoneTransform(s_boneIds[i]);
                if (t != null) _playerBones.Add(t);
            }
        }
    }

    private static readonly HumanBodyBones[] s_boneIds =
    {
        HumanBodyBones.Head, HumanBodyBones.Neck, HumanBodyBones.Hips,
        HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest,
        HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm,
        HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm,
        HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
        HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg,
        HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg,
        HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot,
        HumanBodyBones.LeftToes, HumanBodyBones.RightToes,
    };

    // ───────── ★1109n 判定框除錯顯示 ─────────

    private void OnGUI()
    {
        Style st = _style;
        if (st == null || !st.showOverlapDebug || _routine == null || _alpha <= 0.01f) return;
        if (Event.current.type != EventType.Repaint) return;
        if (_dbgTex == null)
        {
            _dbgTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _dbgTex.SetPixel(0, 0, Color.white);
            _dbgTex.Apply();
            _dbgTex.hideFlags = HideFlags.HideAndDontSave;
        }
        if (_dbgHasSub) DrawDebugRect(_dbgSubRect, Color.yellow);
        if (_dbgHasPlayer) DrawDebugRect(_dbgPlayerRect, _dbgOverlap ? Color.red : Color.green);
    }

    /// <summary>畫螢幕座標（左下為原點）的矩形外框；GUI 的 y 是由上往下，這裡轉換。</summary>
    private void DrawDebugRect(Rect r, Color color)
    {
        float x = r.xMin, y = Screen.height - r.yMax, w = r.width, h = r.height, t = 2f;
        Color old = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(new Rect(x, y, w, t), _dbgTex);
        GUI.DrawTexture(new Rect(x, y + h - t, w, t), _dbgTex);
        GUI.DrawTexture(new Rect(x, y, t, h), _dbgTex);
        GUI.DrawTexture(new Rect(x + w - t, y, t, h), _dbgTex);
        GUI.color = old;
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
        if (_dbgTex != null) Destroy(_dbgTex);
    }
}
