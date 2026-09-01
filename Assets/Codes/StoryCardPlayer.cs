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
///
/// 【日誌照片頁】
///   水下 D1–D3 撿日誌時，會先出一頁「夾在日誌裡的舊照片」
///   （照片＋日期＋筆刷符號），再出原句文字頁。
///   素材放 Assets/Resources/Diary/（diary_photo_1..3、diary_glyph_1..3、
///   diary_paper）。缺哪個檔就自動略過哪個，永遠不會卡住流程。
///   要換成 AI 生成的正式照片：同檔名覆蓋 PNG 即可，程式不用動。
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
    [Tooltip("紙卡沒指定紙紋圖時，自動從 Resources 這個路徑載（留空＝不載，用純色）")]
    public string paperResourcePath = "Diary/diary_paper";

    // ══════════════════════════════════════════
    // 日誌照片頁（水下 D1–D3：先照片，再原句）
    // ══════════════════════════════════════════
    [System.Serializable]
    public class DiaryPhotoPage
    {
        public string cardId = "";
        [Tooltip("Resources 路徑，不含副檔名。例：Diary/diary_photo_1")]
        public string photoResource = "";
        [Tooltip("Resources 路徑，頁面下方的筆刷符號（透明 PNG）。留空＝不顯示")]
        public string glyphResource = "";
        [Tooltip("寫在相紙白邊上的日期。由遊戲字型顯示，不烙進圖片，改這裡就好")]
        public string dateText = "";
        [Tooltip("整張相片的微傾角（度），像隨手夾進日誌的樣子")]
        public float tiltDegrees = 0f;
        [Tooltip("這一份日誌用的紙紋（Resources 路徑）。日誌隨閱讀順序老化：摺痕→狐斑→水漬與裂縫。留空＝用共用 paperResourcePath")]
        public string paperResource = "";
    }

    [Header("日誌照片頁（清單留空＝用內建的 D1–D3 三張）")]
    public bool diaryPhotoEnabled = true;
    [Tooltip("照片頁停留秒數（固定值，不吃字數公式；一樣可按鍵跳過）")]
    public float diaryPhotoHold = 4.2f;
    public List<DiaryPhotoPage> diaryPhotoPages = new List<DiaryPhotoPage>();

    [Header("演出（都可以歸零關掉）")]
    [Tooltip("照片頁進場的「放下照片」感：淡入時從下方這麼多像素落定，傾角同步回正")]
    public float photoSettlePixels = 26f;
    [Tooltip("筆刷符號比照片慢多少秒出現（先看照片，才看見她畫的記號）")]
    public float glyphDelay = 0.2f;
    [Tooltip("每一頁文字在顯示期間緩慢上浮的像素數。很小，像呼吸。0＝關")]
    public float pageDriftPixels = 4f;

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

    // 照片頁
    private DiaryPhotoPage _pendingPhoto;   // 這一次播放要先出的照片頁（沒有＝null）
    private CanvasGroup _photoGroup;
    private Image _photoImg;
    private Image _glyphImg;
    private TextMeshProUGUI _dateLabel;
    private bool _photoBuilt;
    private bool _paperLoadTried;
    private Sprite _activePaperSprite;   // 這一次播放實際用的紙紋（每份日誌可以不同）
    private int _driftToken;             // 換頁時遞增，讓上一頁的緩升協程自己停下
    private bool _pausedByMe;            // 這次的 Time.timeScale=0 是不是我設的
    private static readonly Dictionary<string, Sprite> _spriteCache = new Dictionary<string, Sprite>();

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
        if (diaryPhotoPages == null || diaryPhotoPages.Count == 0)
        {
            diaryPhotoPages = BuildDefaultDiaryPhotoPages();
        }
        AutoHookCollectibleNotes();
    }

    /// <summary>
    /// 幫場景裡每一張日誌紙（CollectibleNote）自動補上 StoryCardNoteHook：
    /// 撿到第 1／2／3 張 → D1／D2／D3。組員之後加紙也不用手掛。
    /// </summary>
    private static void AutoHookCollectibleNotes()
    {
        CollectibleNote[] notes = Object.FindObjectsByType<CollectibleNote>(FindObjectsSortMode.None);
        for (int i = 0; i < notes.Length; i++)
        {
            if (notes[i] != null && notes[i].GetComponent<StoryCardNoteHook>() == null)
            {
                notes[i].gameObject.AddComponent<StoryCardNoteHook>();
            }
        }
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        // ★播放旗標一定要歸零：PlayerMovement 現在會依這個旗標硬鎖玩家，
        //   協程被中途砍掉的話旗標會留在 true，玩家就永遠動不了。
        _playing = false;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        // ★安全網三：播放器被關掉或銷毀時，絕不把世界留在暫停狀態
        ReleaseTimePause(1f);
    }

    /// <summary>
    /// ★關鍵：Play(cardId, true) 播完會維持全黑，把畫面交給呼叫端載入場景。
    /// 這支 Canvas 是 DontDestroyOnLoad，如果沒人收，新場景會一直黑著。
    /// 所以新場景載完就自己把黑幕淡掉，呼叫端不用做任何事。
    /// </summary>
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // ★安全網二：換場景時若還停著卻沒在播卡，立刻解除（避免整個遊戲凍住）
        if (_pausedByMe && !_playing) ReleaseTimePause(1f);

        // 播放器是 DontDestroyOnLoad：新場景的日誌紙也要補掛鉤
        AutoHookCollectibleNotes();

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
        _pendingPhoto = (_paper && diaryPhotoEnabled) ? FindDiaryPhoto(cardId) : null;
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

    /// <summary>
    /// ★宿主可能當場被銷毀時用這個（例：撿到會消失的日誌紙）：
    /// 凍結與解凍都由播放器自己管，卡片播完一定會把玩家還回來。
    /// </summary>
    public Coroutine PlayFrozen(string cardId, PlayerMovement pm)
    {
        return StartCoroutine(PlayFrozenRoutine(cardId, pm));
    }

    private IEnumerator PlayFrozenRoutine(string cardId, PlayerMovement pm)
    {
        if (pm != null) pm.isCutsceneFrozen = true;
        // ★世界暫停：撿紙引發的其他演出（巨石消散特寫、窒息計時、紙的縮小動畫）
        //   全部等卡片播完才繼續，玩家一段都不會錯過。
        //   卡片本身全走 unscaledDeltaTime，暫停中照播。
        float prevTimeScale = Time.timeScale;
        if (prevTimeScale <= 0f) prevTimeScale = 1f;   // 別把別人設的 0 記成常態
        Time.timeScale = 0f;
        _pausedByMe = true;
        try
        {
            yield return Play(cardId, true, true);
        }
        finally
        {
            // ★安全網一：不管正常結束、被中止還是拋例外，一定還原
            ReleaseTimePause(prevTimeScale);
            if (pm != null) pm.isCutsceneFrozen = false;
        }
    }

    /// <summary>把暫停的世界還回去。重複呼叫安全。</summary>
    private void ReleaseTimePause(float restoreTo)
    {
        if (!_pausedByMe) return;
        _pausedByMe = false;
        Time.timeScale = restoreTo > 0f ? restoreTo : 1f;
    }

    public IEnumerator PlayPages(string[] pages, bool curtainFadeIn, bool curtainFadeOut)
    {
        if (pages == null || pages.Length == 0) yield break;

        _playing = true;
        _skipRest = false;
        EnsureBuilt();

        // 紙卡第一次播放時，若沒手動指定紙紋圖，試著從 Resources 載一張
        if (_paper && paperSprite == null && !_paperLoadTried && !string.IsNullOrEmpty(paperResourcePath))
        {
            _paperLoadTried = true;
            paperSprite = LoadSpriteFromResources(paperResourcePath);
        }
        // 這一份日誌若指定了自己的紙紋（日誌隨閱讀順序老化），這一次播放用它
        _activePaperSprite = _paper ? paperSprite : null;
        if (_paper && _pendingPhoto != null && !string.IsNullOrEmpty(_pendingPhoto.paperResource))
        {
            Sprite perCardPaper = LoadSpriteFromResources(_pendingPhoto.paperResource);
            if (perCardPaper != null) _activePaperSprite = perCardPaper;
        }

        _canvas.enabled = true;
        if (_curtain != null) _curtain.sprite = _paper ? _activePaperSprite : null;
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

        // ── 照片頁（只有日誌卡有；照片缺檔就跳過，只播文字，不卡流程）──
        DiaryPhotoPage photoCfg = _pendingPhoto;
        _pendingPhoto = null;
        if (photoCfg != null && !_skipRest)
        {
            Sprite photo = LoadSpriteFromResources(photoCfg.photoResource);
            if (photo == null)
            {
                Debug.LogWarning("[StoryCardPlayer] 找不到日誌照片資源 Resources/" + photoCfg.photoResource + "，這次只播文字。");
            }
            else
            {
                EnsurePhotoBuilt();
                _photoImg.sprite = photo;
                _photoImg.rectTransform.localRotation = Quaternion.Euler(0f, 0f, photoCfg.tiltDegrees);
                Sprite glyph = LoadSpriteFromResources(photoCfg.glyphResource);
                _glyphImg.sprite = glyph;
                _glyphImg.enabled = glyph != null;
                _dateLabel.text = photoCfg.dateText != null ? photoCfg.dateText : "";
                if (_label != null && _label.font != null) _dateLabel.font = _label.font;

                // 符號慢半拍：先看照片，才看見她畫的記號
                if (glyph != null && glyphDelay > 0.001f)
                {
                    Color gc = _glyphImg.color;
                    _glyphImg.color = new Color(gc.r, gc.g, gc.b, 0f);
                    StartCoroutine(FadeImageAlpha(_glyphImg, 1f, 0.5f, glyphDelay));
                }
                else if (glyph != null)
                {
                    Color gc = _glyphImg.color;
                    _glyphImg.color = new Color(gc.r, gc.g, gc.b, 1f);
                }

                yield return PhotoSettleIn(photoCfg.tiltDegrees);
                yield return Hold(diaryPhotoHold);
                yield return FadeGroup(_photoGroup, 1f, 0f, fadeOut);
                SetGroupAlpha(_photoGroup, 0f);
                if (!_skipRest) yield return WaitUnscaled(gap);
            }
        }

        for (int i = 0; i < pages.Length; i++)
        {
            if (_skipRest) break;

            string page = pages[i] != null ? pages[i] : "";
            _label.text = page;
            _label.ForceMeshUpdate();

            // 這一頁的緩升（換頁或跳過時 token 一變就自己停）
            _driftToken++;
            if (pageDriftPixels > 0.01f && _labelRect != null)
            {
                StartCoroutine(DriftLabel(_driftToken, fadeIn + HoldSecondsFor(page) + fadeOut));
            }

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
        _driftToken++;   // 停掉最後一頁的緩升
        if (_labelRect != null)
        {
            _labelRect.anchoredPosition = new Vector2(0f, (0.5f - verticalPercent) * 1080f);
        }

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
            SetGroupAlpha(_photoGroup, 0f);
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

    private DiaryPhotoPage FindDiaryPhoto(string cardId)
    {
        if (string.IsNullOrEmpty(cardId) || diaryPhotoPages == null) return null;
        for (int i = 0; i < diaryPhotoPages.Count; i++)
        {
            DiaryPhotoPage p = diaryPhotoPages[i];
            if (p != null && p.cardId == cardId && !string.IsNullOrEmpty(p.photoResource)) return p;
        }
        return null;
    }

    /// <summary>
    /// 從 Resources 載圖。先試 Sprite（Sprite 型匯入），
    /// 不行再載 Texture2D 執行時包成 Sprite——所以 PNG 用預設匯入設定就能用。
    /// 找不到會把 null 記進快取，之後不再重找。
    /// </summary>
    private static Sprite LoadSpriteFromResources(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        Sprite s;
        if (_spriteCache.TryGetValue(path, out s)) return s;
        s = Resources.Load<Sprite>(path);
        if (s == null)
        {
            Texture2D t = Resources.Load<Texture2D>(path);
            if (t != null)
            {
                s = Sprite.Create(t, new Rect(0f, 0f, t.width, t.height), new Vector2(0.5f, 0.5f), 100f);
            }
        }
        _spriteCache[path] = s;
        return s;
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

    /// <summary>
    /// 照片頁進場：「把照片放下來」。
    /// 淡入的同時，照片從下方 photoSettlePixels 落定、傾角從放大值回正（ease-out）。
    /// </summary>
    private IEnumerator PhotoSettleIn(float tiltDeg)
    {
        RectTransform pr = _photoImg != null ? _photoImg.rectTransform : null;
        float dur = fadeIn > 0f ? fadeIn : 0.001f;
        float drop = photoSettlePixels;
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / dur);
            float e = 1f - (1f - k) * (1f - k);   // ease-out
            if (_photoGroup != null) _photoGroup.alpha = k;
            if (pr != null && drop > 0.01f)
            {
                pr.anchoredPosition = new Vector2(0f, 150f - drop * (1f - e));
                pr.localRotation = Quaternion.Euler(0f, 0f, tiltDeg * (1f + 0.7f * (1f - e)));
            }
            yield return null;
        }
        if (_photoGroup != null) _photoGroup.alpha = 1f;
        if (pr != null)
        {
            pr.anchoredPosition = new Vector2(0f, 150f);
            pr.localRotation = Quaternion.Euler(0f, 0f, tiltDeg);
        }
    }

    /// <summary>把一張 Image 的 alpha 從 0 淡到 to（等 delay 秒才開始）。給符號慢半拍用。</summary>
    private IEnumerator FadeImageAlpha(Image img, float to, float duration, float delay)
    {
        if (img == null) yield break;
        Color c = img.color;
        float t = 0f;
        while (t < delay) { t += Time.unscaledDeltaTime; yield return null; }
        t = 0f;
        float dur = duration > 0f ? duration : 0.001f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            if (img == null) yield break;
            img.color = new Color(c.r, c.g, c.b, Mathf.Lerp(0f, to, Mathf.Clamp01(t / dur)));
            yield return null;
        }
        img.color = new Color(c.r, c.g, c.b, to);
    }

    /// <summary>一頁文字顯示期間的緩升：整段時間內勻速上浮 pageDriftPixels 像素，像呼吸。</summary>
    private IEnumerator DriftLabel(int token, float duration)
    {
        if (_labelRect == null || duration <= 0f) yield break;
        float baseY = (0.5f - verticalPercent) * 1080f;
        float t = 0f;
        while (t < duration && token == _driftToken)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / duration);
            _labelRect.anchoredPosition = new Vector2(0f, baseY - pageDriftPixels * 0.5f + pageDriftPixels * k);
            yield return null;
        }
    }

    private IEnumerator FadeGroup(CanvasGroup g, float from, float to, float duration)
    {
        if (g == null) yield break;
        if (duration <= 0f) { g.alpha = to; yield break; }
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            g.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t / duration));
            yield return null;
        }
        g.alpha = to;
    }

    private static void SetGroupAlpha(CanvasGroup g, float a)
    {
        if (g != null) g.alpha = a;
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
                ? (_activePaperSprite != null ? Color.white : paperColor)
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
        // ★ AddComponent<Image>() / AddComponent<TextMeshProUGUI>() 會自動把 Transform
        //   換成 RectTransform。這時再 AddComponent<RectTransform>() 會失敗並回傳 null，
        //   下面整段設定就被 if 擋掉——黑幕會停在預設的 100×100 置中小方塊，
        //   看起來就是「沒有蓋滿全螢幕、也看不出淡入淡出」。改用既有的 rectTransform。
        RectTransform cr = _curtain.rectTransform;
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

        // ★同上：TextMeshProUGUI 已經帶了 RectTransform，不能再 AddComponent。
        _labelRect = _label.rectTransform;
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

    /// <summary>
    /// 照片頁的三件東西：相片（含烙好的白邊）、白邊上的日期、頁面下方的筆刷符號。
    /// 位置全部照 1920×1080 參考解析度排，CanvasScaler 會自己縮。
    /// </summary>
    private void EnsurePhotoBuilt()
    {
        if (_photoBuilt) return;
        EnsureBuilt();

        GameObject rootGo = new GameObject("DiaryPhotoPage");
        rootGo.transform.SetParent(_canvas.transform, false);
        RectTransform rootRect = rootGo.AddComponent<RectTransform>();
        if (rootRect != null)
        {
            rootRect.anchorMin = new Vector2(0.5f, 0.5f);
            rootRect.anchorMax = new Vector2(0.5f, 0.5f);
            rootRect.pivot = new Vector2(0.5f, 0.5f);
            rootRect.sizeDelta = new Vector2(0f, 0f);
            rootRect.anchoredPosition = new Vector2(0f, 0f);
        }
        _photoGroup = rootGo.AddComponent<CanvasGroup>();
        if (_photoGroup != null) _photoGroup.alpha = 0f;

        // 相片（1024×1024 的圖裡已含相紙白邊與陰影），整張微傾
        GameObject photoGo = new GameObject("DiaryPhoto");
        photoGo.transform.SetParent(rootGo.transform, false);
        _photoImg = photoGo.AddComponent<Image>();
        _photoImg.raycastTarget = false;
        _photoImg.preserveAspect = true;
        RectTransform pr = _photoImg.rectTransform;
        if (pr != null)
        {
            pr.anchorMin = new Vector2(0.5f, 0.5f);
            pr.anchorMax = new Vector2(0.5f, 0.5f);
            pr.pivot = new Vector2(0.5f, 0.5f);
            pr.sizeDelta = new Vector2(720f, 720f);
            pr.anchoredPosition = new Vector2(0f, 150f);
        }

        // 日期：寫在相紙下緣白邊上，跟著相片一起微傾
        GameObject dateGo = new GameObject("DiaryDate");
        dateGo.transform.SetParent(photoGo.transform, false);
        _dateLabel = dateGo.AddComponent<TextMeshProUGUI>();
        _dateLabel.raycastTarget = false;
        _dateLabel.alignment = TextAlignmentOptions.Center;
        _dateLabel.fontSize = 34f;
        _dateLabel.characterSpacing = 6f;
        _dateLabel.color = new Color(paperInkColor.r, paperInkColor.g, paperInkColor.b, 0.88f);
        RectTransform dr = _dateLabel.rectTransform;
        if (dr != null)
        {
            dr.anchorMin = new Vector2(0.5f, 0.5f);
            dr.anchorMax = new Vector2(0.5f, 0.5f);
            dr.pivot = new Vector2(0.5f, 0.5f);
            dr.sizeDelta = new Vector2(620f, 70f);
            dr.anchoredPosition = new Vector2(0f, -241f);
        }

        // 筆刷符號：頁面下方置中，不跟相片傾斜
        GameObject glyphGo = new GameObject("DiaryGlyph");
        glyphGo.transform.SetParent(rootGo.transform, false);
        _glyphImg = glyphGo.AddComponent<Image>();
        _glyphImg.raycastTarget = false;
        _glyphImg.preserveAspect = true;
        RectTransform gr = _glyphImg.rectTransform;
        if (gr != null)
        {
            gr.anchorMin = new Vector2(0.5f, 0.5f);
            gr.anchorMax = new Vector2(0.5f, 0.5f);
            gr.pivot = new Vector2(0.5f, 0.5f);
            gr.sizeDelta = new Vector2(264f, 264f);
            gr.anchoredPosition = new Vector2(0f, -330f);
        }

        _photoBuilt = true;
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
    //   出處：0826 週報附件《故事與全部文字》——原句照登，一字未改（撰稿者指示）
    //   分頁：施工單第五節，手排，不要改成自動斷行
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
            "她人帶著好意的話語，\n都會被視作壓力，\n我不是做不好，我明白怎麼做，",
            "只是現在的我有點累了，\n我想要，也需要休息。\n但好像...沒有一個環境給我休息。",
            "我只能不斷做著，\n沒有人可以代替我，我應當行的。",
            "在這樣的情況下，我迎來了爆發，\n即使我知道這不對。\n可...\n真的不對嗎？"
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
            "無所謂了，我已經在這裡，\n我應該想的時候如何呼吸，\n而不是其他的。",
            "往後的生活，\n我不斷掙扎在窒息和嗆水當中，\n漸漸地，我開始沒了力氣。",
            // ★M4-05～07 移到水下撿日誌時播（D1–D3 紙卡），這裡不再重複
            "就在我思索的時候，\n一團黑影籠罩著我，帶來了恐懼，，\n頁打斷了我的思考，",
            "黑影很快就離去了，\n終於緩過氣的我也沒了思緒。\n就好跟著光球繼續往前走。",
            "奇怪，在最黑暗的地方，\n居然還有一道光芒？"
        }));

        // ── M5　星空玻璃館 → 結局（回到棉花堡）　5 頁 / 96 字 ──
        list.Add(NewCard("M5", new string[] {
            "情緒將我淹沒，\n黑暗將我籠罩，",
            "我只能住前，不停的往前，\n好在有燭光，",
            "好奇怪，為什麼...\n為什麼這個時候會有光，",
            "原來我也可以求救。",
            "在一次次的向我尋求幫助，\n她離開了深不見底的黑暗，\n回到了她的棉花堡"
        }));

        // ── D1–D3　水下日誌（紙底卡）＝原 M4-05～07 ──
        //    掛在撿日誌的位置播，撿到當下看，不進關底黑幕。
        //    ※目前的文字是她「當下的獨白」；若撰稿者要改成
        //      「日誌上實際寫的內容」，改這三張就好。
        list.Add(NewPaperCard("D1", new string[] {
            "光球...\n對！我還有光球，\n於是我朝著光球游去。"
        }));
        list.Add(NewPaperCard("D2", new string[] {
            "光球往下，我也往下。\n在這裏我看見一些東西，\n是以前我所記下的。"
        }));
        list.Add(NewPaperCard("D3", new string[] {
            "我的內心好像有什麼破開了。"
        }));

        return list;
    }

    /// <summary>
    /// 內建的三頁日誌照片設定。
    /// 出場順序照撰稿者指定：1 → 3 → 2
    /// （夕陽共舞 → 年節全家福 → 窗光裡抱著孩子——
    /// 　最後一份日誌「我的內心好像有什麼破開了。」配的是抱著孩子那張）。
    /// 日期是占位值；改日期或換圖直接改這裡，
    /// 或在 Inspector 的 Diary Photo Pages 清單裡改。
    /// </summary>
    public static List<DiaryPhotoPage> BuildDefaultDiaryPhotoPages()
    {
        List<DiaryPhotoPage> list = new List<DiaryPhotoPage>();

        DiaryPhotoPage d1 = new DiaryPhotoPage();
        d1.cardId = "D1";
        d1.photoResource = "Diary/diary_photo_1";   // 夕陽草原，兩個人在跳舞
        d1.glyphResource = "Diary/diary_glyph_1";   // 兩形依偎
        d1.dateText = "1998.6.21";
        d1.tiltDegrees = -2.4f;
        d1.paperResource = "Diary/diary_paper_1";   // 平整微黃，一道攤平的摺痕
        list.Add(d1);

        DiaryPhotoPage d2 = new DiaryPhotoPage();
        d2.cardId = "D2";
        d2.photoResource = "Diary/diary_photo_3";   // 全家福，光帶橫過整排臉
        d2.glyphResource = "Diary/diary_glyph_3";   // 一排形，唯獨她那形有鬆脫的線頭
        d2.dateText = "2004.1.22";
        d2.tiltDegrees = 1.8f;
        d2.paperResource = "Diary/diary_paper_2";   // 十字摺痕、狐斑增生
        list.Add(d2);

        DiaryPhotoPage d3 = new DiaryPhotoPage();
        d3.cardId = "D3";
        d3.photoResource = "Diary/diary_photo_2";   // 窗光裡的三個人（她抱著孩子）
        d3.glyphResource = "Diary/diary_glyph_2";   // 三形，小的被抱著
        d3.dateText = "2003.11.3";
        d3.tiltDegrees = -1.2f;
        d3.paperResource = "Diary/diary_paper_3";   // 乾水漬、邊角最深、右緣裂縫透光
        list.Add(d3);

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
