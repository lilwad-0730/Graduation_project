using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// 我你他　過場文字卡播放器
///
/// 規格出處：《我你他_文字卡_視覺與擺放_施工單 v1.1》
/// 文字出處：0826 週報附件《我，你，他　故事與全部文字》2026-08-25
///
/// 【怎麼用】
///   場景切換時（SceneTransitionZone 黑屏之後、LoadScene 之前）：
///       yield return StoryCardPlayer.Instance.Play("M4", true);
///   同場景播卡（M1 / M2，不換場景）：
///       yield return StoryCardPlayer.Instance.Play("M1");
///
/// 【不用做任何場景設定】
///   Canvas、黑幕、文字都在執行時自己建。26 頁內容已內建，
///   要改字就在 Inspector 改，或改 BuildDefaultCards()。
/// </summary>
[DisallowMultipleComponent]
public class StoryCardPlayer : MonoBehaviour
{
    // ══════════════════════════════════════════
    // 卡片資料
    // ══════════════════════════════════════════
    public enum CardStyle { Curtain, Paper }

    [System.Serializable]
    public class Card
    {
        public string cardId = "";
        [Tooltip("Curtain＝黑幕過場卡；Paper＝紙底日誌卡（水下 D1–D3）")]
        public CardStyle style = CardStyle.Curtain;
        [TextArea(2, 6)] public string[] pages = new string[0];
    }

    [Header("卡片內容（每一格＝一頁，頁內用換行分行）")]
    public List<Card> cards = new List<Card>();

    // ══════════════════════════════════════════
    // 版面（施工單第三節）
    // ══════════════════════════════════════════
    [Header("版面")]
    [Tooltip("#0D1128 近黑偏藍＝夜空色")]
    public Color bgColor = new Color(0.0510f, 0.0667f, 0.1569f, 1f);
    [Tooltip("#E8E0D2 骨白＝主角衣服色")]
    public Color textColor = new Color(0.9098f, 0.8784f, 0.8235f, 1f);
    [Tooltip("留空則用 TMP 預設字型。建議 Chinese_Dynamic_SDF（開發）／Chinese_Static_SDF（出貨）")]
    public TMP_FontAsset fontAsset;
    [Tooltip("fontAsset 沒指定時，從 Resources 這個路徑載字型（可留空）")]
    public string fontResourcePath = "";
    public float fontSize = 48f;
    [Tooltip("TMP lineSpacing。65 ＝ 1.65 倍行距")]
    public float lineSpacing = 65f;
    [Tooltip("TMP characterSpacing。2 ＝ +2%")]
    public float characterSpacing = 2f;
    [Tooltip("文字方塊寬度。1920 畫布左右各留 360 安全邊")]
    public float blockWidth = 1200f;
    [Range(0f, 1f), Tooltip("文字方塊中心在畫面高度的幾成。0.55 ＝ 略低於中線")]
    public float verticalPercent = 0.55f;
    [Tooltip("要比 SceneTransitionController 的 9999 大，否則轉場碎片會蓋住文字")]
    public int sortingOrder = 10000;

    [Header("紙卡樣式（日誌 D1–D3 用）")]
    [Tooltip("紙紋圖。留空就用下面的純色紙，一樣能跑")]
    public Sprite paperSprite;
    [Tooltip("沒有紙紋圖時的紙色")]
    public Color paperColor = new Color(0.937f, 0.898f, 0.808f, 1f);
    [Tooltip("紙上的墨色")]
    public Color paperInkColor = new Color(0.169f, 0.157f, 0.133f, 1f);

    // ══════════════════════════════════════════
    // 時序（施工單第四節）
    // ══════════════════════════════════════════
    [Header("時序　一頁＝淡入→停留→淡出→空白")]
    public float fadeIn = 0.5f;
    public float fadeOut = 0.5f;
    public float gap = 0.3f;

    [Header("停留　T ＝ holdBase ＋ holdPerChar × 該頁字數")]
    public float holdBase = 0.9f;
    public float holdPerChar = 0.15f;
    public float holdMin = 2.0f;
    public float holdMax = 6.5f;

    [Header("黑幕進出")]
    public float curtainIn = 0.8f;
    [Tooltip("最後一頁淡出後，全黑停多久")]
    public float curtainTailHold = 0.5f;
    public float curtainOut = 0.8f;

    // ══════════════════════════════════════════
    // 跳過（施工單第四節）
    // ══════════════════════════════════════════
    [Header("跳過")]
    public bool allowSkip = true;
    [Tooltip("單頁最短顯示秒數，這之內按鍵無效（防誤觸）")]
    public float minShowBeforeSkip = 1.0f;
    [Tooltip("長按這個鍵可跳過整段。給展場工作人員重置用，不對玩家說明")]
    public KeyCode skipAllKey = KeyCode.Escape;
    public float skipAllHoldSeconds = 1.5f;

    // ══════════════════════════════════════════
    // 內部狀態
    // ══════════════════════════════════════════
    private static StoryCardPlayer _instance;
    private Canvas _canvas;
    private Image _curtain;
    private TextMeshProUGUI _label;
    private CanvasGroup _labelGroup;
    private RectTransform _labelRect;
    private bool _built;
    private bool _playing;
    private bool _skipRest;
    private bool _paper;   // 這一次播放是不是紙卡

    /// <summary>正在播卡片。轉場程式可以用這個判斷要不要放行。</summary>
    public bool IsPlaying { get { return _playing; } }

    public static StoryCardPlayer Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = Object.FindObjectOfType<StoryCardPlayer>();
            }
            if (_instance == null)
            {
                GameObject go = new GameObject("StoryCardPlayer");
                _instance = go.AddComponent<StoryCardPlayer>();
            }
            return _instance;
        }
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Object.Destroy(gameObject);
            return;
        }
        _instance = this;
        Object.DontDestroyOnLoad(gameObject);

        if (cards == null || cards.Count == 0)
        {
            cards = BuildDefaultCards();
        }
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    /// <summary>
    /// ★關鍵：Play(cardId, true) 播完會維持全黑，把畫面交給呼叫端載入場景。
    /// 這支 Canvas 是 DontDestroyOnLoad，如果沒人收，新場景會一直黑著。
    /// 所以新場景載完就自己把黑幕淡掉，呼叫端不用做任何事。
    /// </summary>
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (_playing) return;
        if (!_built || _canvas == null || !_canvas.enabled) return;
        StartCoroutine(AutoReleaseRoutine());
    }

    private IEnumerator AutoReleaseRoutine()
    {
        yield return WaitUnscaled(curtainTailHold);
        yield return FadeCurtain(1f, 0f, curtainOut);
        SetCurtainAlpha(0f);
        if (_canvas != null) _canvas.enabled = false;
    }

    // ══════════════════════════════════════════
    // 對外唯一入口
    // ══════════════════════════════════════════

    /// <summary>
    /// 播一張卡。
    /// </summary>
    /// <param name="cardId">M1 / M2 / M3 / M4 / M5</param>
    /// <param name="screenAlreadyBlack">
    /// 呼叫端已經把畫面蓋黑了（例如 SceneTransitionZone 的 fadeImage）。
    /// true 時不做黑幕淡入淡出，播完維持全黑交還給呼叫端。
    /// </param>
    public IEnumerator Play(string cardId, bool screenAlreadyBlack)
    {
        // 畫面已經是黑的 → 兩頭都不做黑幕；否則兩頭都做
        yield return Play(cardId, !screenAlreadyBlack, !screenAlreadyBlack);
    }

    /// <summary>
    /// 完整版：分別控制黑幕的進與出。
    /// (true,  true )　同場景播卡：自己淡入、自己淡出　　　　　　StoryCardTrigger
    /// (false, false)　呼叫端已經黑屏：播完維持黑，交還呼叫端　 SceneTransitionZone 六行版
    /// (true,  false)　自己淡入黑幕，播完維持黑，接著載場景　　 StoryCardZoneHook 零改動版
    /// </summary>
    public IEnumerator Play(string cardId, bool curtainFadeIn, bool curtainFadeOut)
    {
        Card card = FindCard(cardId);
        if (card == null || card.pages == null || card.pages.Length == 0)
        {
            Debug.LogWarning("[StoryCardPlayer] 找不到卡片或卡片是空的：" + cardId);
            yield break;
        }
        _paper = card.style == CardStyle.Paper;
        yield return PlayPages(card.pages, curtainFadeIn, curtainFadeOut);
    }

    public IEnumerator Play(string cardId)
    {
        yield return Play(cardId, true, true);
    }

    /// <summary>
    /// ★給觸發器用的版本：協程掛在播放器（DontDestroyOnLoad）自己身上。
    /// 呼叫端的 GameObject 就算在換場景時被銷毀，卡片仍會播完、黑幕仍會收掉。
    /// 直接 yield return Instance.Play(...) 的話，宿主一死畫面會永遠卡黑。
    /// </summary>
    public Coroutine PlayDetached(string cardId, bool curtainFadeIn, bool curtainFadeOut)
    {
        return StartCoroutine(Play(cardId, curtainFadeIn, curtainFadeOut));
    }

    public IEnumerator PlayPages(string[] pages, bool curtainFadeIn, bool curtainFadeOut)
    {
        if (pages == null || pages.Length == 0) yield break;

        _playing = true;
        _skipRest = false;
        EnsureBuilt();

        _canvas.enabled = true;
        if (_curtain != null) _curtain.sprite = _paper ? paperSprite : null;
        if (_label != null) _label.color = _paper ? paperInkColor : textColor;
        SetCurtainAlpha(curtainFadeIn ? 0f : 1f);
        SetLabelAlpha(0f);
        _label.text = "";

        // 黑幕淡入
        if (curtainFadeIn && curtainIn > 0f)
        {
            yield return FadeCurtain(0f, 1f, curtainIn);
        }
        SetCurtainAlpha(1f);

        for (int i = 0; i < pages.Length; i++)
        {
            if (_skipRest) break;

            string page = pages[i] != null ? pages[i] : "";
            _label.text = page;
            _label.ForceMeshUpdate();

            yield return FadeLabel(0f, 1f, fadeIn);
            yield return Hold(HoldSecondsFor(page));
            yield return FadeLabel(1f, 0f, fadeOut);

            if (i < pages.Length - 1)
            {
                yield return WaitUnscaled(gap);
            }
        }

        SetLabelAlpha(0f);
        _label.text = "";

        // 交還畫面
        if (curtainFadeOut)
        {
            yield return WaitUnscaled(curtainTailHold);
            if (curtainOut > 0f)
            {
                yield return FadeCurtain(1f, 0f, curtainOut);
            }
            SetCurtainAlpha(0f);
            _canvas.enabled = false;
        }
        else
        {
            // 維持全黑，讓呼叫端接手載入場景。
            // 新場景載完後 OnSceneLoaded 會自己把黑幕淡掉。
            _canvas.enabled = true;
        }

        _playing = false;
    }

    /// <summary>呼叫端載入完場景後叫這個，把黑幕收掉。</summary>
    public void ReleaseCurtain()
    {
        if (_canvas != null)
        {
            SetCurtainAlpha(0f);
            SetLabelAlpha(0f);
            _canvas.enabled = false;
        }
    }

    public bool HasCard(string cardId)
    {
        return FindCard(cardId) != null;
    }

    private Card FindCard(string cardId)
    {
        if (string.IsNullOrEmpty(cardId) || cards == null) return null;
        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i] != null && cards[i].cardId == cardId) return cards[i];
        }
        return null;
    }

    // ══════════════════════════════════════════
    // 時序計算
    // ══════════════════════════════════════════
    public float HoldSecondsFor(string page)
    {
        int n = CountChars(page);
        float t = holdBase + holdPerChar * n;
        return Mathf.Clamp(t, holdMin, holdMax);
    }

    private static int CountChars(string page)
    {
        if (string.IsNullOrEmpty(page)) return 0;
        int n = 0;
        for (int i = 0; i < page.Length; i++)
        {
            char c = page[i];
            if (c != '\n' && c != '\r') n++;
        }
        return n;
    }

    // ══════════════════════════════════════════
    // 等待與淡入淡出　★全部用 unscaledDeltaTime
    //   轉場時 Time.timeScale 可能被設 0，用 deltaTime 會卡死在第一頁
    // ══════════════════════════════════════════
    private IEnumerator Hold(float seconds)
    {
        float t = 0f;
#if ENABLE_LEGACY_INPUT_MANAGER
        float holdKeyTime = 0f;
#endif
        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;

#if ENABLE_LEGACY_INPUT_MANAGER
            if (allowSkip)
            {
                if (Input.GetKey(skipAllKey))
                {
                    holdKeyTime += Time.unscaledDeltaTime;
                    if (holdKeyTime >= skipAllHoldSeconds)
                    {
                        _skipRest = true;
                        yield break;
                    }
                }
                else
                {
                    holdKeyTime = 0f;
                }

                if (t >= minShowBeforeSkip && Input.anyKeyDown)
                {
                    yield break;
                }
            }
#endif
            yield return null;
        }
    }

    private IEnumerator WaitUnscaled(float seconds)
    {
        float t = 0f;
        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    private IEnumerator FadeLabel(float from, float to, float duration)
    {
        if (duration <= 0f) { SetLabelAlpha(to); yield break; }
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            SetLabelAlpha(Mathf.Lerp(from, to, Mathf.Clamp01(t / duration)));
            yield return null;
        }
        SetLabelAlpha(to);
    }

    private IEnumerator FadeCurtain(float from, float to, float duration)
    {
        if (duration <= 0f) { SetCurtainAlpha(to); yield break; }
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            SetCurtainAlpha(Mathf.Lerp(from, to, Mathf.Clamp01(t / duration)));
            yield return null;
        }
        SetCurtainAlpha(to);
    }

    private void SetLabelAlpha(float a)
    {
        if (_labelGroup != null) _labelGroup.alpha = a;
    }

    private void SetCurtainAlpha(float a)
    {
        if (_curtain != null)
        {
            Color c = _paper
                ? (paperSprite != null ? Color.white : paperColor)
                : bgColor;
            _curtain.color = new Color(c.r, c.g, c.b, a);
        }
    }

    // ══════════════════════════════════════════
    // 建畫面（照 SceneTransitionController 的做法）
    // ══════════════════════════════════════════
    private void EnsureBuilt()
    {
        if (_built) return;

        GameObject canvasGo = new GameObject("StoryCardCanvas");
        canvasGo.transform.SetParent(transform, false);

        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = sortingOrder;

        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        // 黑幕：整面，吃掉點擊
        GameObject curtainGo = new GameObject("Curtain");
        curtainGo.transform.SetParent(canvasGo.transform, false);
        _curtain = curtainGo.AddComponent<Image>();
        _curtain.color = new Color(bgColor.r, bgColor.g, bgColor.b, 0f);
        _curtain.raycastTarget = true;
        RectTransform cr = curtainGo.AddComponent<RectTransform>();
        if (cr != null)
        {
            cr.anchorMin = new Vector2(0f, 0f);
            cr.anchorMax = new Vector2(1f, 1f);
            cr.offsetMin = new Vector2(0f, 0f);
            cr.offsetMax = new Vector2(0f, 0f);
        }

        // 文字
        GameObject labelGo = new GameObject("StoryCardText");
        labelGo.transform.SetParent(canvasGo.transform, false);
        _label = labelGo.AddComponent<TextMeshProUGUI>();
        _labelGroup = labelGo.AddComponent<CanvasGroup>();
        if (_labelGroup != null) _labelGroup.alpha = 0f;

        ApplyTextStyle();

        _labelRect = labelGo.AddComponent<RectTransform>();
        if (_labelRect != null)
        {
            _labelRect.anchorMin = new Vector2(0.5f, 0.5f);
            _labelRect.anchorMax = new Vector2(0.5f, 0.5f);
            _labelRect.pivot = new Vector2(0.5f, 0.5f);
            _labelRect.sizeDelta = new Vector2(blockWidth, 540f);
            // 0.55 ＝ 中心略低於中線
            _labelRect.anchoredPosition = new Vector2(0f, (0.5f - verticalPercent) * 1080f);
        }

        _canvas.enabled = false;
        _built = true;
    }

    private void ApplyTextStyle()
    {
        if (_label == null) return;

        TMP_FontAsset f = fontAsset;
        if (f == null && !string.IsNullOrEmpty(fontResourcePath))
        {
            f = Resources.Load<TMP_FontAsset>(fontResourcePath);
        }
        if (f != null) _label.font = f;

        _label.color = textColor;
        _label.fontSize = fontSize;
        _label.alignment = TextAlignmentOptions.Center;
        _label.lineSpacing = lineSpacing;
        _label.characterSpacing = characterSpacing;
        // ★分頁是手排的，開自動換行會把手排的結果拆掉
        _label.enableWordWrapping = false;
        _label.overflowMode = TextOverflowModes.Overflow;
        _label.raycastTarget = false;
    }

    // ══════════════════════════════════════════
    // 內建的 26 頁
    //   出處：0826 週報附件《故事與全部文字》
    //   分頁：施工單第五節，手排，不要改成自動斷行
    //   ★標「校對」的行是附件原文的疑似錯字，已按施工單第七節的建議修，
    //     若撰稿者不採用，改這裡即可
    // ══════════════════════════════════════════
    [ContextMenu("重新載入內建 26 頁")]
    private void ReloadDefaultCards()
    {
        cards = BuildDefaultCards();
    }

    public static List<Card> BuildDefaultCards()
    {
        List<Card> list = new List<Card>();

        // ── M1　棉花堡 → 廢墟　1 頁 / 39 字 ──
        list.Add(NewCard("M1", new string[] {
            "一切都是好的，\n在追尋親密關係的時候。\n而孩子通常會被視為是\n親密關係中愛情的結晶。"
        }));

        // ── M2　廢墟 → 荒原　5 頁 / 160 字 ──
        list.Add(NewCard("M2", new string[] {
            "但處在不斷需要重複勞動的時候，\n已經做了的東西，\n還需要日復一日地去再做，",
            // 校對 1：附件原文「她人」→「他人」
            "他人帶著好意的話語，\n都會被視作壓力，\n我不是做不好，我明白怎麼做，",
            "只是現在的我有點累了，\n我想要，也需要休息。\n但好像……沒有一個環境給我休息。",
            // 校對 2：附件原文「我應當行的」語法不通，改為「我應該可以的」
            "我只能不斷做著，\n沒有人可以代替我，我應該可以的。",
            "在這樣的情況下，我迎來了爆發，\n即使我知道這不對。\n可……\n真的不對嗎？"
        }));

        // ── M3　荒原 → 潛入水下　5 頁 / 114 字 ──
        list.Add(NewCard("M3", new string[] {
            "我已經累了，\n但是他們好像不允許我休息。",
            "我顯露出的疲態，疏忽，會被批評，\n而我過去的努力好像理所應當。",
            "我還在往前走，\n但我已經不知道當初的目標是什麼了。",
            "內心的麻木，情緒的咆哮，\n我已經聲嘶力竭了。",
            "我真的需要一個依靠。\n前面會是我的依靠嗎？"
        }));

        // ── M4　水下 → 星空玻璃館　10 頁 / 251 字 ──
        //    ★黑幕 59.4 秒。施工單第八節：建議拆成兩半或改用畫面上淡入
        list.Add(NewCard("M4", new string[] {
            "不知道什麼時候開始，\n我好像身處海面上。",
            "是我自己的選擇嗎？\n還是別人導致的？",
            // 校對 3：附件原文「我應該想的時候如何呼吸」疑似漏字
            "無所謂了，我已經在這裡，\n我應該想的是此時如何呼吸，\n而不是其他的。",
            "往後的生活，\n我不斷掙扎在窒息和嗆水當中，\n漸漸地，我開始沒了力氣。",
            // ★M4-05～07 移到水下撿日誌時播（D1–D3 紙卡），這裡不再重複
            // 校對 4：附件原文「帶來了恐懼，，頁打斷了」有贅字
            "就在我思索的時候，\n一團黑影籠罩著我，帶來了恐懼，\n打斷了我的思考，",
            // 校對 5：附件原文「就好跟著」→「只好跟著」
            "黑影很快就離去了，\n終於緩過氣的我也沒了思緒。\n只好跟著光球繼續往前走。",
            "奇怪，在最黑暗的地方，\n居然還有一道光芒？"
        }));

        // ── M5　星空玻璃館 → 結局（回到棉花堡）　5 頁 / 96 字 ──
        list.Add(NewCard("M5", new string[] {
            "情緒將我淹沒，\n黑暗將我籠罩，",
            // 校對 6：附件原文「我只能住前」→「往前」
            "我只能往前，不停的往前，\n好在有燭光，",
            "好奇怪，為什麼……\n為什麼這個時候會有光，",
            "原來我也可以求救。",
            // 校對 7、8：附件原文「在一次次的向我尋求幫助」語意不通；末句補句號
            "在一次次地向他人尋求幫助後，\n她離開了深不見底的黑暗，\n回到了她的棉花堡。"
        }));

        // ── D1–D3　水下日誌（紙底卡）＝原 M4-05～07 ──
        //    掛在撿日誌的位置播，撿到當下看，不進關底黑幕。
        //    ※目前的文字是她「當下的獨白」；若撰稿者要改成
        //      「日誌上實際寫的內容」，改這三張就好。
        list.Add(NewPaperCard("D1", new string[] {
            "光球……\n對！我還有光球，\n於是我朝著光球游去。"
        }));
        list.Add(NewPaperCard("D2", new string[] {
            "光球往下，我也往下。\n在這裏我看見一些東西，\n是以前我所記下的。"
        }));
        list.Add(NewPaperCard("D3", new string[] {
            "我的內心好像有什麼破開了。"
        }));

        return list;
    }

    private static Card NewPaperCard(string id, string[] pages)
    {
        Card c = NewCard(id, pages);
        c.style = CardStyle.Paper;
        return c;
    }

    private static Card NewCard(string id, string[] pages)
    {
        Card c = new Card();
        c.cardId = id;
        c.pages = pages;
        return c;
    }
}
