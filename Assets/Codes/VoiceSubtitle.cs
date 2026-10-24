using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 人聲字幕：畫面下方置中、淡入淡出，跟著語音長度顯示。
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
    }

    private static VoiceSubtitle _instance;
    private TextMeshProUGUI _label;
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

        GameObject go = new GameObject("Subtitle");
        go.transform.SetParent(canvasObj.transform, false);
        TextMeshProUGUI label = go.AddComponent<TextMeshProUGUI>();
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.raycastTarget = false;
        label.color = new Color(1f, 1f, 1f, 0f);

        VoiceSubtitle s = canvasObj.AddComponent<VoiceSubtitle>();
        s._label = label;
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
        Color c = _label.color;
        c.a = a;
        _label.color = c;
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }
}
