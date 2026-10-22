using System.Collections;   // ★1021 結局插卡的協程
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
    public float secondsPerPage = 5f;      // ★1021 7 → 5（43 章）
    [Tooltip("一打開的第一頁多停幾秒（玩家要先弄懂這是什麼）")]
    public float firstPageExtraSeconds = 2f;   // ★1021 3 → 2
    [Tooltip("翻到新的一頁後，過多久才出現倒數圈（先讓人看畫）")]
    public float showDelay = 0.6f;
    public bool inChapterMode = true;
    public bool inEndingMode = true;

    [Header("結局插卡（★1021 M：E1 拆兩張）")]
    [Tooltip("結局繪本翻離這一頁（0 起算；69＝第 070 頁「光做的孩子」）之後、看下一頁之前，先播一張黑幕卡；-1＝不插")]
    public int endingCardAfterIndex = 69;
    [Tooltip("要插的卡（StoryCardPlayer 的 id）。E1a＝「那是我的形狀」；E1 剩下標題頁「我　你　他」，仍由 EndCredits 在名單前播")]
    public string endingCardId = "E1a";

    [Header("往回翻（★1021 M：隨著玩家推進，繪本緊隨開放）")]
    [Tooltip("掉落漫畫、結局繪本可以往回翻到第 1 頁（玩過的章節都開著）；往前仍只到本段最後一頁")]
    public bool allowFlipBackToStart = true;
    [Tooltip("翻回到本段之前的頁時，自動翻頁改用這個秒數追回來")]
    public float catchUpSecondsPerPage = 2.5f;

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
    private bool _cardPlaying, _cardDone;   // ★1021 結局插卡

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
    }

    private void OnDestroy()
    {
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

        if (!active) { SetVisible(false); return; }

        EnsureBuilt();

        // ★1021 往回翻：章節模式由 PageBook 讀這個旗標；結局模式 EndCredits 把 minPage 設成結局第一頁，這裡放開
        BookTransitionManager.allowFlipBackBeforeChapter = allowFlipBackToStart;
        if (allowFlipBackToStart && ending && _book.minPage != 0) _book.minPage = 0;

        if (_cardPlaying) { SetVisible(false); return; }   // ★1021 卡播的時候不倒數

        if (_book.Index != _lastIndex)
        {
            int prev = _lastIndex;
            _firstPage = _lastIndex < 0;
            _lastIndex = _book.Index;
            int segStart = chapter ? BookTransitionManager.chapterStartPage : (_credits != null ? _credits.endingStartPage : 0);
            bool behind = allowFlipBackToStart && _book.Index < segStart;   // 翻回本段之前：用追回來的秒數
            _total = behind ? Mathf.Max(1f, catchUpSecondsPerPage) : Mathf.Max(1f, secondsPerPage + (_firstPage ? firstPageExtraSeconds : 0f));
            _remain = _total;
            _shownFor = 0f;
            // ★1021 結局插卡：翻離第 070 頁那一刻（手翻或自動翻都算），先播「那是我的形狀」再看 071
            if (ending && !_cardDone && endingCardAfterIndex >= 0 && prev == endingCardAfterIndex && _book.Index == endingCardAfterIndex + 1)
            {
                StartCoroutine(PlayEndingCard());
                SetVisible(false);
                return;
            }
        }

        // 翻頁中、拖曳中：不算時間，圈先收起來
        if (_book.IsBusy) { SetVisible(false); _shownFor = 0f; return; }

        // 設定選單打開（時間暫停）時不倒數
        float dt = Time.timeScale > 0f ? Time.unscaledDeltaTime : 0f;
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

    // ★1021 結局插卡
    private IEnumerator PlayEndingCard()
    {
        _cardPlaying = true; _cardDone = true;
        StoryCardPlayer scp = StoryCardPlayer.Instance;
        bool wasEnabled = _book.enabled;
        _book.enabled = false;   // 卡播的時候不吃翻頁的按鍵、滑鼠
        if (scp != null && scp.HasCard(endingCardId))
        {
            scp.SetSortingOrder(10001);   // 跟 EndCredits 一樣，蓋過名單與這裡的倒數圈
            yield return scp.Play(endingCardId);   // 自己淡入黑幕、播完淡出，底下就是下一頁
        }
        else Debug.LogWarning("[PageBookAutoTurn] 找不到結局插卡 " + endingCardId + "，略過");
        _book.enabled = wasEnabled;
        _remain = _total; _shownFor = 0f;
        _cardPlaying = false;
    }

    private bool IsLastPage(bool chapter)
    {
        int i = _book.Index;
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
