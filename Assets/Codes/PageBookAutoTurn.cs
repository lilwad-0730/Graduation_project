using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// ★1017 M：「加翻頁提示，同時在右下角進行內容倒計時，到計時結束自動翻頁」
///
/// 漫畫繪本（PageBook）在兩種情況下才管：
///   ・掉落漫畫（荒原 → 水下，BookTransitionManager 章節模式）
///   ・結局漫畫（玻璃館 → 結局，EndCredits.EndingMode）
/// 主選單「製作名單和來源」的自由翻閱不管（ExhibitMode 照舊）。
///
/// 每一頁：右下角一圈倒數（圈裡是剩幾秒），旁邊一行「→　翻頁」。倒數走完自動翻下一頁；
/// 玩家自己翻（→、D、空白鍵、點畫面、滾輪）就照玩家的，換頁後重新倒數。
/// 章節最後一頁：倒數走完自動進下一關（跟按右下角「進入下一關 ➔」一樣），圈移到那個按鈕上面，不擋它。
/// 結局最後一頁：EndCredits 本來就會 2.5 秒後自動捲名單，這裡不倒數。
///
/// 不改 PageBook／EndCredits，場景不動；繪本一出現就自己掛上。開關：PageBookAutoTurn.Enabled。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PageBook))]
public class PageBookAutoTurn : MonoBehaviour
{
    public static bool Enabled = true;

    [Header("倒數")]
    [Tooltip("每一頁停幾秒後自動翻")]
    public float secondsPerPage = 7f;
    [Tooltip("一打開的第一頁多停幾秒（玩家要先弄懂這是什麼）")]
    public float firstPageExtraSeconds = 3f;
    [Tooltip("翻到新的一頁後，過多久才出現倒數圈（先讓人看畫）")]
    public float showDelay = 0f;   // ★1021 原本 0.6：翻完頁還要再等才出現圈，看起來像「翻完才開始」。改成 0＝一翻到就在
    [Tooltip("★1021 翻頁翻到這個比例（0～1）就先把下一頁的倒數圈亮出來（滿的、還沒開始倒），翻完立刻接著倒數。")]
    [Range(0.1f, 1f)] public float showRingAtTurnProgress = 0.4f;
    public bool inChapterMode = true;
    public bool inEndingMode = true;

    [Header("提示")]
    public string hintText = "→　翻頁";
    public Color inkColor = new Color(0.9098f, 0.8784f, 0.8235f, 0.92f);
    public Color shadowColor = new Color(0.03f, 0.03f, 0.06f, 0.75f);
    [Tooltip("圈的直徑（1080 基準）")]
    public float ringSize = 58f;
    [Tooltip("離右下角多遠（1080 基準）")]
    public Vector2 cornerOffset = new Vector2(40f, 22f);

    private PageBook _book;
    private EndCredits _credits;
    private float _total = 7f;
    private float _remain = 7f;
    private float _shownFor;
    private int _lastIndex = -1;
    private bool _firstPage = true;
    private bool _done;
    private int _inactiveLogIndex = -1;

    private Canvas _canvas;
    private RectTransform _holder;
    private CanvasGroup _group;
    private Image _ring, _track, _disc;
    private TextMeshProUGUI _sec, _secShadow, _hint, _hintShadow;
    private float _alpha;

    // ══════════════════════════════════════════
    // 自己掛到繪本上
    // ══════════════════════════════════════════
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoadedStatic;
        SceneManager.sceneLoaded += OnSceneLoadedStatic;
        AttachAll();
    }

    private static void OnSceneLoadedStatic(Scene s, LoadSceneMode m) { AttachAll(); }

    private static void AttachAll()
    {
        PageBook[] books = Object.FindObjectsByType<PageBook>();
        for (int i = 0; i < books.Length; i++)
            if (books[i] != null && books[i].GetComponent<PageBookAutoTurn>() == null)
                books[i].gameObject.AddComponent<PageBookAutoTurn>();
    }

    private void Start()
    {
        _book = GetComponent<PageBook>();
        _credits = GetComponent<EndCredits>();
        if (_credits == null) _credits = Object.FindAnyObjectByType<EndCredits>();
        if (_book != null) _book.OnTurnStarted += OnTurnStarted;
    }

    private float _turnStart = -1f;
    private int _turnDir;

    private void OnTurnStarted(int dir)
    {
        _turnStart = Time.unscaledTime;
        _turnDir = dir;
    }

    private void OnDestroy()
    {
        if (_book != null) _book.OnTurnStarted -= OnTurnStarted;
        if (_canvas != null) Destroy(_canvas.gameObject);
    }

    // ══════════════════════════════════════════
    // 每幀
    // ══════════════════════════════════════════
    private void Update()
    {
        bool chapter = BookTransitionManager.isChapterMode;
        bool ending = EndCredits.EndingMode;
        bool active = Enabled && _book != null && !_done && _book.PageCount > 0
                      && ((chapter && inChapterMode) || (ending && inEndingMode));
        if (active && _credits != null && _credits.IsRolling) active = false;

        bool last = active && IsLastPage(chapter);
        if (active && ending && last) active = false;   // 結局最後一頁交給 EndCredits

        if (!active)
        {
            if (_book != null && _book.Index != _inactiveLogIndex && (chapter || ending))
            {
                _inactiveLogIndex = _book.Index;
                Debug.Log($"【漫畫倒數】第 {_book.Index + 1} 頁不倒數：Enabled={Enabled}、已完成={_done}、章節模式={chapter}、結局模式={ending}、名單捲動中={(_credits != null && _credits.IsRolling)}、結局最後一頁={(ending && last)}");
            }
            SetVisible(false);
            return;
        }

        EnsureBuilt();

        if (_book.Index != _lastIndex)
        {
            _firstPage = _lastIndex < 0;
            _lastIndex = _book.Index;
            _total = Mathf.Max(1f, secondsPerPage + (_firstPage ? firstPageExtraSeconds : 0f));
            _remain = _total;
            _shownFor = 0f;
            Debug.Log($"【漫畫倒數】第 {_book.Index + 1} 頁：倒數 {_total:F0} 秒（timeScale={Time.timeScale}、章節模式={chapter}、結局模式={ending}、最後一頁={last}）");
        }

        // 翻頁中、拖曳中：不算時間，圈先收起來
        if (_book.IsBusy)
        {
            _shownFor = 0f;
            // ★1021 往後翻到一定程度，就先把「下一頁」的倒數圈亮出來（滿的、還沒開始倒）：翻完立刻接著倒數，不用等翻完才冒出來
            bool nextIsLast = IsLastIndex(_book.Index + 1, chapter);
            bool showEarly = _turnDir > 0 && _turnStart >= 0f
                             && Time.unscaledTime - _turnStart >= _book.turnSeconds * showRingAtTurnProgress
                             && !(ending && nextIsLast);
            if (showEarly)
            {
                Layout(chapter && nextIsLast);
                _ring.fillAmount = 1f;
                string t = Mathf.CeilToInt(Mathf.Max(1f, secondsPerPage)).ToString();
                if (_sec.text != t) { _sec.text = t; _secShadow.text = t; }
                SetVisible(true);
            }
            else SetVisible(false);
            return;
        }

        // 設定選單打開（時間暫停）時不倒數。
        // ★1021 只有「時間暫停、而且設定選單真的開著」才停：轉場、文字卡有時會把 timeScale 留在 0，
        //   原本一看到 timeScale 是 0 就不倒數，翻了第一頁之後倒數就卡死（只有第一頁有倒數）。
        float dt = IsPausedBySettings() ? 0f : Time.unscaledDeltaTime;
        _shownFor += dt;
        if (_shownFor >= showDelay) _remain -= dt;

        if (_remain <= 0f)
        {
            _remain = 0f;
            if (chapter && last) _done = true;   // 這一下會進下一關
            _book.Next();
            SetVisible(false);
            return;
        }

        Layout(chapter && last);
        SetVisible(_shownFor >= showDelay);
        float fill = Mathf.Clamp01(_remain / _total);
        _ring.fillAmount = fill;
        string s = Mathf.CeilToInt(_remain).ToString();
        if (_sec.text != s) { _sec.text = s; _secShadow.text = s; }
    }

    private float _nextPauseCheck;
    private bool _pausedCached;

    private bool IsPausedBySettings()
    {
        if (Time.timeScale > 0f) return false;
        if (Time.unscaledTime >= _nextPauseCheck)
        {
            _nextPauseCheck = Time.unscaledTime + 0.25f;
            _pausedCached = Object.FindAnyObjectByType<SettingsPopupOverlayPresenter>() != null;   // 只找啟用中的＝選單開著
        }
        return _pausedCached;
    }

    private bool IsLastPage(bool chapter) { return IsLastIndex(_book.Index, chapter); }

    private bool IsLastIndex(int i, bool chapter)
    {
        if (chapter && BookTransitionManager.chapterEndPage >= 0 && i >= BookTransitionManager.chapterEndPage) return true;
        return i >= _book.PageCount - 1;
    }

    private void SetVisible(bool on)
    {
        if (_group == null) return;
        float target = on ? 1f : 0f;
        _alpha = Mathf.MoveTowards(_alpha, target, Time.unscaledDeltaTime / 0.3f);
        _group.alpha = _alpha;
        if (_canvas != null) _canvas.enabled = _alpha > 0.001f;
    }

    /// <summary>章節最後一頁：右下角有「進入下一關 ➔」按鈕（OnGUI，180×44、離邊 24 像素），圈放到它上面。</summary>
    private void Layout(bool lastChapterPage)
    {
        float scale = Mathf.Max(0.1f, Screen.height / 1080f);
        _canvas.GetComponent<CanvasScaler>().scaleFactor = scale;
        float y = cornerOffset.y;
        if (lastChapterPage) y = (24f + 44f + 14f) / scale;
        _holder.anchoredPosition = new Vector2(-cornerOffset.x, y);
        bool showHint = !lastChapterPage;
        _hint.enabled = showHint;
        _hintShadow.enabled = showHint;
    }

    // ══════════════════════════════════════════
    // 畫面（第一次要用才蓋）
    // ══════════════════════════════════════════
    private void EnsureBuilt()
    {
        if (_canvas != null) return;

        GameObject cgo = new GameObject("PageBookAutoTurn Canvas");
        _canvas = cgo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 600;   // 在繪本上面；名單（10000）、文字卡（10000）會蓋過它
        CanvasScaler sc = cgo.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        sc.scaleFactor = Mathf.Max(0.1f, Screen.height / 1080f);

        GameObject hgo = new GameObject("Holder");
        hgo.transform.SetParent(cgo.transform, false);
        _holder = hgo.AddComponent<RectTransform>();
        _holder.anchorMin = new Vector2(1f, 0f);
        _holder.anchorMax = new Vector2(1f, 0f);
        _holder.pivot = new Vector2(1f, 0f);
        _holder.sizeDelta = new Vector2(ringSize, ringSize);
        _group = hgo.AddComponent<CanvasGroup>();
        _group.alpha = 0f;
        _group.blocksRaycasts = false;
        _group.interactable = false;

        Sprite ringSprite = MakeRingSprite(128, 0.80f, 1.0f);
        Sprite discSprite = MakeRingSprite(128, 0f, 1.0f);

        _disc = MakeImage(hgo.transform, "Disc", discSprite, new Color(0.03f, 0.03f, 0.07f, 0.55f), ringSize + 10f);
        _track = MakeImage(hgo.transform, "Track", ringSprite, new Color(inkColor.r, inkColor.g, inkColor.b, 0.22f), ringSize);
        _ring = MakeImage(hgo.transform, "Ring", ringSprite, inkColor, ringSize);
        _ring.type = Image.Type.Filled;
        _ring.fillMethod = Image.FillMethod.Radial360;
        _ring.fillOrigin = (int)Image.Origin360.Top;
        _ring.fillClockwise = true;
        _ring.fillAmount = 1f;

        TMP_FontAsset font = ResolveFont();
        _secShadow = MakeLabel(hgo.transform, "SecondsShadow", font, shadowColor, 26f, TextAlignmentOptions.Center, new Vector2(1.5f, -1.5f), new Vector2(ringSize, ringSize), new Vector2(0.5f, 0.5f));
        _sec = MakeLabel(hgo.transform, "Seconds", font, inkColor, 26f, TextAlignmentOptions.Center, Vector2.zero, new Vector2(ringSize, ringSize), new Vector2(0.5f, 0.5f));

        // 提示字在圈的左邊，靠右對齊
        Vector2 hintSize = new Vector2(320f, ringSize);
        _hintShadow = MakeLabel(hgo.transform, "HintShadow", font, shadowColor, 28f, TextAlignmentOptions.MidlineRight, new Vector2(-ringSize * 0.5f - 18f + 1.5f, -1.5f), hintSize, new Vector2(1f, 0.5f));
        _hint = MakeLabel(hgo.transform, "Hint", font, inkColor, 28f, TextAlignmentOptions.MidlineRight, new Vector2(-ringSize * 0.5f - 18f, 0f), hintSize, new Vector2(1f, 0.5f));
        _hint.text = hintText;
        _hintShadow.text = hintText;

        _canvas.enabled = false;
    }

    private static Image MakeImage(Transform parent, string name, Sprite sprite, Color color, float size)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        Image img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false;
        RectTransform rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(size, size);
        rt.anchoredPosition = Vector2.zero;
        return img;
    }

    private static TextMeshProUGUI MakeLabel(Transform parent, string name, TMP_FontAsset font, Color color, float size,
                                             TextAlignmentOptions align, Vector2 pos, Vector2 box, Vector2 pivot)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.color = color;
        t.fontSize = size;
        t.alignment = align;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Overflow;
        t.raycastTarget = false;
        t.characterSpacing = 2f;
        t.text = "";
        RectTransform rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = pivot;
        rt.sizeDelta = box;
        rt.anchoredPosition = pos;
        return t;
    }

    /// <summary>跟文字卡、操作提示同一套字：StoryCardPlayer 的字 → InputHint 的字 → TMP 預設（全域備援是正黑體）。</summary>
    private static TMP_FontAsset ResolveFont()
    {
        StoryCardPlayer scp = Object.FindAnyObjectByType<StoryCardPlayer>();
        if (scp != null && scp.fontAsset != null) return scp.fontAsset;
        InputHint ih = Object.FindAnyObjectByType<InputHint>();
        if (ih != null && ih.fontAsset != null) return ih.fontAsset;
        return TMP_Settings.defaultFontAsset;
    }

    /// <summary>程式畫一個抗鋸齒的圓環（inner＝0 就是實心圓）。</summary>
    private static Sprite MakeRingSprite(int size, float inner01, float outer01)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        float c = (size - 1) * 0.5f;
        float rOut = c * outer01;
        float rIn = c * inner01;
        Color32[] px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                float a = Mathf.Clamp01(rOut - d + 0.5f);
                if (inner01 > 0f) a = Mathf.Min(a, Mathf.Clamp01(d - rIn + 0.5f));
                px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }
}
