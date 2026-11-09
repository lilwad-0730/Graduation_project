using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ★1110 救援提示面板（脫困、緊急回主選單共用）：畫面下方置中的半透明深色面板，
/// 左邊一顆按鍵標籤（Backspace）、右邊標題＋說明、底部一條細進度條（可標出「脫困」那個節點）。
/// 淡入淡出、輕輕上滑。全部程式生成，不用任何新美術，字體用遊戲選單同一套。
/// 用法：RescuePrompt.Show(標題, 說明, 進度 0~1（-1＝不顯示進度條）, 節點 0~1（-1＝無）);  RescuePrompt.Hide();
/// 按住流程（HangRescue）會把 HoldActive 設成 true，這時其他來源（脫困的小提示）先讓路，避免兩個面板搶同一個位置。
/// </summary>
public class RescuePrompt : MonoBehaviour
{
    /// <summary>按住流程正在使用面板（其他來源的 Show／Hide 會被忽略）。</summary>
    public static bool HoldActive;

    private static RescuePrompt s_i;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { s_i = null; HoldActive = false; }

    public static void Show(string title, string sub, float progress = -1f, float mark = -1f, bool fromHold = false)
    {
        if (HoldActive && !fromHold) return;
        Ensure();
        if (s_i == null) return;
        s_i._title.text = title ?? "";
        s_i._sub.text = sub ?? "";
        s_i._sub.gameObject.SetActive(!string.IsNullOrEmpty(sub));
        bool bar = progress >= 0f;
        s_i._track.gameObject.SetActive(bar);
        if (bar) s_i._fill.fillAmount = Mathf.Clamp01(progress);
        s_i._tick.gameObject.SetActive(bar && mark >= 0f);
        if (bar && mark >= 0f) s_i.PlaceTick(mark);
        s_i._want = 1f;
    }

    public static void Hide(bool fromHold = false)
    {
        if (HoldActive && !fromHold) return;
        if (s_i != null) s_i._want = 0f;
    }

    private static void Ensure()
    {
        if (s_i != null) return;
        GameObject go = new GameObject("[救援提示]");
        DontDestroyOnLoad(go);
        s_i = go.AddComponent<RescuePrompt>();
        s_i.Build();
    }

    // ───────────── 畫面 ─────────────
    private const float PanelW = 780f, PanelH = 132f, Pad = 30f;
    private CanvasGroup _group;
    private RectTransform _panel, _tick;
    private Text _title, _sub;
    private Image _track, _fill;
    private float _want, _cur;
    private static Sprite s_round, s_white;

    private static Font s_font;

    /// <summary>
    /// 救援提示是「出事時才用」的功能性提示，要的是清楚好讀、有點正式：用系統內建的微軟正黑體（Windows 一定有）。
    /// 沒有就退回遊戲選單那套手寫體，再不行用內建字體。
    /// </summary>
    private static Font GameFont()
    {
        if (s_font != null) return s_font;
        try { s_font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft JhengHei UI", "Microsoft JhengHei", "Noto Sans TC", "PingFang TC" }, 36); } catch { s_font = null; }
        if (s_font == null) s_font = Resources.Load<Font>("Fonts/ChenYuluoyan-2.0-Thin");
        if (s_font == null) s_font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        return s_font;
    }

    /// <summary>圓角矩形（九宮格切法，拉多大圓角都不變形）。</summary>
    private static Sprite Round()
    {
        if (s_round != null) return s_round;
        const int S = 64; const float R = 24f;
        Texture2D t = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float cx = Mathf.Clamp(x + 0.5f, R, S - R), cy = Mathf.Clamp(y + 0.5f, R, S - R);
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                float a = Mathf.Clamp01(R - d + 0.5f);
                t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        t.Apply();
        s_round = Sprite.Create(t, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(R, R, R, R));
        return s_round;
    }

    private static Sprite White()
    {
        if (s_white != null) return s_white;
        Texture2D t = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++) t.SetPixel(x, y, Color.white);
        t.Apply();
        s_white = Sprite.Create(t, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f);
        return s_white;
    }

    private static Image Img(Transform parent, string name, Sprite s, Color c, bool sliced)
    {
        GameObject g = new GameObject(name);
        g.transform.SetParent(parent, false);
        Image im = g.AddComponent<Image>();
        im.sprite = s; im.color = c; im.raycastTarget = false;
        if (sliced) im.type = Image.Type.Sliced;
        return im;
    }

    private static Text Txt(Transform parent, string name, int size, Color c, TextAnchor a)
    {
        GameObject g = new GameObject(name);
        g.transform.SetParent(parent, false);
        Text t = g.AddComponent<Text>();
        t.font = GameFont(); t.fontSize = size; t.color = c; t.alignment = a; t.raycastTarget = false;
        t.fontStyle = size >= 34 ? FontStyle.Bold : FontStyle.Normal;
        t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }

    private static void Rect(RectTransform rt, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = pivot; rt.anchoredPosition = pos; rt.sizeDelta = size;
    }

    private void Build()
    {
        GameObject cv = new GameObject("畫布");
        cv.transform.SetParent(transform, false);
        Canvas c = cv.AddComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = 20000;
        CanvasScaler cs = cv.AddComponent<CanvasScaler>();
        cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        cs.referenceResolution = new Vector2(1920f, 1080f);
        cs.matchWidthOrHeight = 0.5f;
        _group = cv.AddComponent<CanvasGroup>();
        _group.alpha = 0f; _group.interactable = false; _group.blocksRaycasts = false;

        // 外框（極淡的亮邊）＋ 底板
        Image edge = Img(cv.transform, "邊", Round(), new Color(1f, 1f, 1f, 0.10f), true);
        _panel = edge.rectTransform;
        Rect(_panel, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 78f), new Vector2(PanelW + 4f, PanelH + 4f));
        Image bg = Img(_panel, "底", Round(), new Color(0.04f, 0.05f, 0.08f, 0.93f), true);
        Rect(bg.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-4f, -4f));

        // 按鍵標籤
        Image key = Img(bg.transform, "鍵", Round(), new Color(1f, 1f, 1f, 0.13f), true);
        Rect(key.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(Pad, 10f), new Vector2(176f, 58f));
        Text kt = Txt(key.transform, "字", 24, new Color(0.96f, 0.93f, 0.86f, 0.95f), TextAnchor.MiddleCenter);
        Rect(kt.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        kt.text = "Backspace";

        // 標題＋說明
        _title = Txt(bg.transform, "標題", 36, new Color(0.97f, 0.94f, 0.88f, 1f), TextAnchor.MiddleLeft);
        Rect(_title.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(Pad + 176f + 28f, 32f), new Vector2(PanelW - Pad * 2f - 176f - 28f, 44f));
        _sub = Txt(bg.transform, "說明", 24, new Color(0.80f, 0.81f, 0.86f, 0.90f), TextAnchor.MiddleLeft);
        Rect(_sub.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(Pad + 176f + 28f, -10f), new Vector2(PanelW - Pad * 2f - 176f - 28f, 36f));

        // 進度條
        _track = Img(bg.transform, "軌", White(), new Color(1f, 1f, 1f, 0.16f), false);
        Rect(_track.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 22f), new Vector2(-Pad * 2f, 5f));
        _fill = Img(_track.transform, "填", White(), new Color(0.96f, 0.82f, 0.55f, 1f), false);
        Rect(_fill.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        _fill.type = Image.Type.Filled; _fill.fillMethod = Image.FillMethod.Horizontal; _fill.fillOrigin = 0;
        Image tick = Img(_track.transform, "節點", White(), new Color(1f, 1f, 1f, 0.85f), false);
        _tick = tick.rectTransform;
        Rect(_tick, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(3f, 15f));
        _tick.gameObject.SetActive(false);
    }

    private void PlaceTick(float mark)
    {
        _tick.anchorMin = _tick.anchorMax = new Vector2(Mathf.Clamp01(mark), 0.5f);
        _tick.anchoredPosition = Vector2.zero;
    }

    private void Update()
    {
        if (_group == null) return;
        _cur = Mathf.MoveTowards(_cur, _want, Time.unscaledDeltaTime * (_want > _cur ? 7f : 4f));
        float e = _cur * _cur * (3f - 2f * _cur);
        _group.alpha = e;
        if (_panel != null) _panel.anchoredPosition = new Vector2(0f, 78f - (1f - e) * 16f);   // 淡入時從下方輕輕滑上來
    }
}
