using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// ★前導序列（0902 教授回饋：主畫面按「開始」直接進棉花堡太突兀、沒有代入感）。
///
/// 從主選單進到棉花堡（SampleScene）時：
///   全黑 → 幾張繪本頁（每頁慢慢推近、淡入淡出）→ 文字卡 M0「一直往前走／雲一直都在」→ 黑幕淡出，關卡開始。
///
/// 不用改主選單、不用改場景：靠 sceneLoaded 自己掛上；
/// 只有「上一個場景是 MainMenuScene」才播（死亡重生、從別關回來、在編輯器直接 Play 某關都不播）。
/// 想在編輯器測：Play 前把 ForceNextTime 設 true，或直接從 MainMenuScene 開始 Play。
///
/// 頁面清單改 IntroPages（Resources/Pages 底下的檔名，不含副檔名）。
/// 美術補好「序-1／序-2／序-3」（房間／影子／門外是雲）後，把它們插到 棉-1 前面就好。
/// 任意鍵可跳過整段（每頁至少看 0.6 秒）。
/// </summary>
public class OpeningSequence : MonoBehaviour
{
    // ── 設定 ──
    public static readonly string[] IntroPages =
    {
        // ★序-1～3 是美術要補的三張（房間／影子／門外是雲），放在 Assets/Picturebook/Resources/Intro/ 底下，
        //   檔名就叫 序-1.png、序-2.png、序-3.png。還沒畫好時找不到會自動跳過，不影響流程。
        //   ※不要放進 Resources/Pages：那個資料夾是繪本本體，多一張檔所有頁碼都會位移（掉落漫畫 41～47、結局 62 會全錯）。
        "Intro/序-1",       // 夜裡的房間，窗外是雲海；她坐在床沿，光在腳邊
        "Intro/序-2",       // 同一個房間，牆上她的影子比她大（「他」第一次出現，只是影子）
        "Intro/序-3",       // 她推開門，門外不是走廊，是雲
        "Pages/002_棉-1",   // 雲海上遠望白城：她要去一個好的地方
        "Pages/004_棉-2",   // 提著光往前走：走了很久
    };
    public const string GameplaySceneName = "SampleScene";
    public const string MenuSceneName = "MainMenuScene";
    public const string IntroCardId = "M0";
    public const float PageFadeIn = 0.9f;
    public const float PageHold = 3.4f;
    public const float PageFadeOut = 0.9f;
    public const float PageZoom = 0.06f;            // 每頁從 1.00 慢慢推到 1.06
    public const float MinSecondsBeforeSkip = 0.6f;
    public const int CanvasSortingOrder = 10001;    // 蓋過轉場特效 9999 與文字卡 10000（播卡前會把自己關掉）

    /// <summary>測試用：設 true 則下一次進 SampleScene 一定播。</summary>
    public static bool ForceNextTime;
    /// <summary>正在播前導（其他系統要判斷可用）。</summary>
    public static bool IsPlaying { get; private set; }

    private static bool _hooked;
    private static string _lastLoadedScene = "";

    // ── 自動掛載 ──
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (_hooked) return;
        _hooked = true;
        _lastLoadedScene = SceneManager.GetActiveScene().name;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single) return;
        string previous = _lastLoadedScene;
        _lastLoadedScene = scene.name;

        if (scene.name != GameplaySceneName) return;
        if (!ForceNextTime && previous != MenuSceneName) return;
        ForceNextTime = false;

        GameObject go = new GameObject("~OpeningSequence");
        go.AddComponent<OpeningSequence>();
    }

    // ── 畫面 ──
    private Canvas _canvas;
    private Image _bg;
    private RawImage _img;            // 繪本頁匯入成一般 Texture2D（不是 Sprite），所以用 RawImage
    private RectTransform _imgRect;
    private PlayerMovement _pm;
    private bool _skipAll;

    private void Start()
    {
        StartCoroutine(Run());
    }

    private IEnumerator Run()
    {
        IsPlaying = true;
        Build();

        // 凍住她：關卡在底下照常存在，只是被蓋住
        _pm = Object.FindFirstObjectByType<PlayerMovement>();
        if (_pm != null) _pm.isCutsceneFrozen = true;

        yield return null;   // 讓 Canvas 先畫一幀全黑

        for (int i = 0; i < IntroPages.Length && !_skipAll; i++)
        {
            Texture2D tex = Resources.Load<Texture2D>(IntroPages[i]);
            if (tex == null)
            {
                Debug.LogWarning("[OpeningSequence] 找不到 Resources/" + IntroPages[i] + "，跳過這一頁。");
                continue;
            }
            yield return ShowPage(tex);
        }

        // 全黑 → 文字卡（畫面已黑：不做黑幕淡入；播完維持黑，由我們接手淡出）
        StoryCardPlayer player = StoryCardPlayer.Instance;
        if (player != null && player.HasCard(IntroCardId))
        {
            _canvas.sortingOrder = 9998;          // 退到卡片（10000）底下：兩層都是全黑，換層看不出來，字才露得出來
            yield return player.Play(IntroCardId, false, false);
            _canvas.enabled = false;              // 卡片的黑幕還在，這一層可以收了
            player.ReleaseCurtainSmooth();        // 由卡片的黑幕淡出，露出關卡
        }
        else
        {
            yield return FadeImage(_bg, 1f, 0f, 0.8f);
            _canvas.enabled = false;
        }

        if (_pm != null) _pm.isCutsceneFrozen = false;
        IsPlaying = false;
        Destroy(gameObject);
    }

    private IEnumerator ShowPage(Texture2D tex)
    {
        _img.texture = tex;
        FitPage(tex);
        SetAlpha(_img, 0f);
        _imgRect.localScale = Vector3.one;

        float total = PageFadeIn + PageHold + PageFadeOut;
        float t = 0f;
        float shown = 0f;
        bool fadingOut = false;
        float fadeOutStart = PageFadeIn + PageHold;

        while (t < total)
        {
            float dt = Time.unscaledDeltaTime;
            t += dt; shown += dt;

            // 緩慢推近
            float z = 1f + PageZoom * Mathf.Clamp01(t / total);
            _imgRect.localScale = new Vector3(z, z, 1f);

            // 透明度：淡入 → 停留 → 淡出
            float a;
            if (t < PageFadeIn) a = t / PageFadeIn;
            else if (t < fadeOutStart) a = 1f;
            else { a = 1f - (t - fadeOutStart) / PageFadeOut; fadingOut = true; }
            SetAlpha(_img, Mathf.Clamp01(a));

#if ENABLE_LEGACY_INPUT_MANAGER
            // 任意鍵：這一頁提早進淡出；已在淡出中再按＝跳過整段
            if (shown >= MinSecondsBeforeSkip && Input.anyKeyDown)
            {
                if (fadingOut) { _skipAll = true; break; }
                fadeOutStart = Mathf.Min(fadeOutStart, t);
                total = fadeOutStart + PageFadeOut;
            }
#endif
            yield return null;
        }
        SetAlpha(_img, 0f);
    }

    private void Build()
    {
        GameObject canvasGo = new GameObject("OpeningCanvas");
        canvasGo.transform.SetParent(transform, false);
        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = CanvasSortingOrder;

        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        // 黑底（#0D1128 夜空色，與文字卡同色，銜接時看不出換層）
        GameObject bgGo = new GameObject("Black");
        bgGo.transform.SetParent(canvasGo.transform, false);
        _bg = bgGo.AddComponent<Image>();
        _bg.color = new Color(0.0510f, 0.0667f, 0.1569f, 1f);
        _bg.raycastTarget = true;
        Stretch(_bg.rectTransform);

        // 繪本頁
        GameObject imgGo = new GameObject("Page");
        imgGo.transform.SetParent(canvasGo.transform, false);
        _img = imgGo.AddComponent<RawImage>();
        _img.raycastTarget = false;
        _img.color = new Color(1f, 1f, 1f, 0f);
        _imgRect = _img.rectTransform;
        Stretch(_imgRect);
    }

    /// <summary>依貼圖長寬比在 1920×1080 畫布裡置中「內含」，不裁不變形。</summary>
    private void FitPage(Texture2D tex)
    {
        if (tex == null || _imgRect == null) return;
        const float W = 1920f, H = 1080f;
        float ar = (float)tex.width / Mathf.Max(1, tex.height);
        float w = W, h = W / ar;
        if (h > H) { h = H; w = H * ar; }
        _imgRect.anchorMin = _imgRect.anchorMax = new Vector2(0.5f, 0.5f);
        _imgRect.pivot = new Vector2(0.5f, 0.5f);
        _imgRect.sizeDelta = new Vector2(w, h);
        _imgRect.anchoredPosition = Vector2.zero;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
    }

    private static void SetAlpha(Graphic img, float a)
    {
        if (img == null) return;
        Color c = img.color; c.a = a; img.color = c;
    }

    private static IEnumerator FadeImage(Image img, float from, float to, float dur)
    {
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            SetAlpha(img, Mathf.Lerp(from, to, Mathf.Clamp01(t / dur)));
            yield return null;
        }
        SetAlpha(img, to);
    }

    private void OnDestroy()
    {
        if (IsPlaying)
        {
            IsPlaying = false;
            if (_pm != null) _pm.isCutsceneFrozen = false;
        }
    }
}
