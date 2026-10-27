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
        [Tooltip("字外圍光暈的顏色（A 是濃淡）。預設 #FFF7F0 晨光暖白（呼應日出的天色）")]
        public Color glowColor = new Color(1f, 0.97f, 0.94f, 0.9f);
        [Tooltip("光暈寬度（0＝沒有光暈）")]
        [Range(0f, 0.5f)] public float glowWidth = 0.25f;
        [Tooltip("光暈邊緣柔和度（0＝硬邊，越大越朦朧）")]
        [Range(0f, 1f)] public float glowSoftness = 0.2f;

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
    }

    private static VoiceSubtitle _instance;
    private TextMeshProUGUI _label;
    private Image _panel;
    private float _alpha;
    private Vector4 _panelKey = new Vector4(-1f, -1f, -1f, -1f);
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
        m.SetFloat("_FaceDilate", st.boldness);
        m.SetColor("_OutlineColor", st.glowColor);
        m.SetFloat("_OutlineWidth", st.glowWidth);
        m.SetFloat("_OutlineSoftness", st.glowSoftness);

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

        _panel.color = new Color(st.panelColor.r, st.panelColor.g, st.panelColor.b, st.panelColor.a * _alpha);
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
        float startA = _label.color.a;
        while (true)
        {
            t += Time.unscaledDeltaTime;
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

    private void SetAlpha(float a)
    {
        _alpha = a;
        Color c = _label.color;
        c.a = a;
        _label.color = c;
        if (_panel != null && _style != null)
        {
            Color pc = _style.panelColor;
            _panel.color = new Color(pc.r, pc.g, pc.b, pc.a * a);
        }
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }
}
