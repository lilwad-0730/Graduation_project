using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// ★1023 故事進度（M 10/23：「玩家玩到哪裡，獲取到什麼，我們的故事就同步到哪裡」）。
///
/// 記「繪本開到第幾頁」（0 起算、含）。她走到哪一關、翻過哪一段漫畫、拿到什麼，就把書多開幾頁：
///   棉花堡 001–008 ／ 墜落＋廢墟 009–027 ／ 進荒原 028–035 ／ 荒原走到鳥群開始追蹤（x≥45）036–038 ／
///   荒原拿到光球 039 ／ 掉落漫畫 040–048 ／ 水下 049–056 ／ 穿水幕漫畫 057–059 ／ 玻璃館鏡牆碎掉 060–067 ／ 結局 068–078。
/// ★1023c 荒原、玻璃館改成「發生了才開」：進荒原只開到 035，鳥影三頁等鳥開始追她，鏡牆那幾頁等鏡子碎掉（之前一進場就開，會先劇透）。
/// ★1023d 繪本在 061 之後插了 4 頁「鏡中的她變成黑影」（062、064、065、066），之後的頁碼往後移 4：共 78 頁。
/// 按「開始遊戲」新的一輪就從頭（只開棉花堡那幾頁）；存在 PlayerPrefs，關掉遊戲再開還在。
///
/// 主選單多一個入口「故事到目前為止」：開 Book 場景自由翻閱，只能翻到開到的那一頁，Esc 回主選單。沒人碰會自己翻（ExhibitMode），也只翻到那一頁。
/// ★1111 入口從左下角的文字按鈕改成「點主選單背景影片裡的她」（MenuCharacterStoryBook）；找不到背景影片時才退回左下角按鈕。
/// </summary>
public static class StoryProgress
{
    public const string PrefKey = "StoryProgress.UnlockedThrough";
    public const int FirstChapterEnd = 7;      // 008_棉-4
    public const int LastPage = 77;            // 078_結-10（★1023d 插入鏡-1～4 之後共 78 頁）
    public const int MirrorPagesEnd = 66;      // 067_結-7（黑影追她）：鏡牆碎掉之後開到這一頁
    public const string BookSceneName = "Book";

    /// <summary>開到第幾頁（0 起算、含）。</summary>
    public static int UnlockedThrough { get; private set; } = FirstChapterEnd;
    /// <summary>正在用主選單的「故事到目前為止」翻書（Book 場景不是結局、不是章節轉場）。</summary>
    public static bool ReaderMode { get; private set; }

    private static bool _hooked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        ReaderMode = false;
        _hooked = false;
        UnlockedThrough = Mathf.Clamp(PlayerPrefs.GetInt(PrefKey, FirstChapterEnd), 0, LastPage);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (!_hooked) { _hooked = true; SceneManager.sceneLoaded += OnSceneLoaded; }
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single || !scene.IsValid()) return;
        string n = scene.name;
        string l = n.ToLower();
        if (l == "cottoncastle") Unlock(7, "進棉花堡");
        else if (l == "samplescene" || l.Contains("ruined")) Unlock(26, "墜落、廢墟");
        else if (l == "desert") Unlock(34, "進荒原");                 // 036–038 鳥影：DesertOrbDirector（x≥45）；039：拿到光球
        else if (l == "underwater") Unlock(55, "水下");
        else if (l.Contains("dark glasses")) Unlock(58, "玻璃館");  // 060–063：MirrorWallAbsorbCutscene 鏡牆碎掉
        else if (n == BookSceneName)
        {
            if (EndCredits.EndingMode) Unlock(LastPage, "結局");
            else if (ReaderMode)
            {
                GameObject host = new GameObject("[StoryBookReader]");
                SceneManager.MoveGameObjectToScene(host, scene);
                host.AddComponent<StoryBookReader>();
            }
        }
        else if (n == GameFlow.MenuSceneName)
        {
            ReaderMode = false;
            GameObject host = new GameObject("[StoryBookMenuCharacter]");
            SceneManager.MoveGameObjectToScene(host, scene);
            host.AddComponent<MenuCharacterStoryBook>();   // ★1111 點她開繪本；找不到背景影片會自己退回左下角按鈕
        }
    }

    /// <summary>把書開到第 index 頁（0 起算、含）；只會往後開，不會收回。</summary>
    public static void Unlock(int index, string why)
    {
        index = Mathf.Clamp(index, 0, LastPage);
        if (index <= UnlockedThrough) return;
        UnlockedThrough = index;
        PlayerPrefs.SetInt(PrefKey, UnlockedThrough);
        PlayerPrefs.Save();
        Debug.Log($"【故事進度】★1023 {why}：繪本開到第 {UnlockedThrough + 1} 頁");
    }

    /// <summary>新的一輪（按「開始遊戲」）：從頭來，只開棉花堡那幾頁。</summary>
    public static void ResetForNewRun()
    {
        UnlockedThrough = FirstChapterEnd;
        PlayerPrefs.SetInt(PrefKey, UnlockedThrough);
        PlayerPrefs.Save();
        Debug.Log("【故事進度】★1023 新的一輪：繪本回到只開第 1～" + (FirstChapterEnd + 1) + " 頁");
    }

    /// <summary>主選單按「故事到目前為止」：開 Book 場景自由翻閱（只到開到的那一頁）。</summary>
    public static void OpenReaderFromMenu() { TryOpenReaderFromMenu(); }

    /// <summary>★1111 同上；真的開始載入繪本才回 true（Build Settings 裡沒有 Book 場景時回 false）。</summary>
    public static bool TryOpenReaderFromMenu()
    {
        if (!Application.CanStreamedLevelBeLoaded(BookSceneName)) { Debug.LogWarning("【故事進度】Build Settings 裡沒有 Book 場景，開不了繪本"); return false; }
        ReaderMode = true;
        EndCredits.EndingMode = false;
        BookTransitionManager.ResetTransientState();
        Time.timeScale = 1f;
        Debug.Log("【故事進度】★1023 主選單開繪本：翻到第 " + (UnlockedThrough + 1) + " 頁為止");
        SceneManager.LoadScene(BookSceneName);
        return true;
    }

    /// <summary>從繪本回主選單。</summary>
    public static void CloseReader()
    {
        ReaderMode = false;
        GameFlow.ReturnToMainMenu("故事到目前為止");
    }
}

/// <summary>Book 場景（主選單進來的自由翻閱）：只翻到開到的那一頁、Esc 回主選單、角落一行字。</summary>
public class StoryBookReader : MonoBehaviour
{
    private PageBook _book;
    private GUIStyle _style;
    private float _t;

    private IEnumerator Start()
    {
        yield return null;   // 等 PageBook Awake 把頁面載好
        _book = FindAnyObjectByType<PageBook>();
        if (_book == null) { Debug.LogWarning("【故事進度】Book 場景裡找不到 PageBook"); yield break; }
        _book.minPage = 0;
        _book.maxPage = Mathf.Min(StoryProgress.UnlockedThrough, _book.PageCount - 1);
        _book.GoTo(0, false);
        ExhibitMode ex = _book.GetComponent<ExhibitMode>();
        if (ex != null) { ex.enabled = true; ex.enableAutoPlay = true; }
        EndCredits ec = FindAnyObjectByType<EndCredits>();
        if (ec != null) ec.enabled = false;   // 名單不要在這裡捲
        Debug.Log("【故事進度】★1023 繪本開到第 " + (_book.maxPage + 1) + " 頁（共 " + _book.PageCount + " 頁）");
    }

    private void Update()
    {
        _t += Time.unscaledDeltaTime;
#if ENABLE_LEGACY_INPUT_MANAGER
        if (_t > 0.5f && Input.GetKeyDown(KeyCode.Escape)) StoryProgress.CloseReader();
#endif
    }

    private void OnGUI()
    {
        if (_book == null) return;
        if (_style == null)
        {
            _style = new GUIStyle(GUI.skin.label);
            _style.fontSize = Mathf.RoundToInt(Screen.height * 0.022f);
            _style.alignment = TextAnchor.LowerLeft;
        }
        _style.normal.textColor = new Color(0.91f, 0.88f, 0.82f, 0.55f);
        float pad = Screen.height * 0.03f;
        string s = "故事到目前為止：第 " + (_book.Index + 1) + " ／ " + (_book.maxPage + 1) + " 頁　　Esc 回主選單";
        GUI.Label(new Rect(pad, 0, Screen.width - pad, Screen.height - pad), s, _style);
    }

    private void OnDestroy()
    {
        if (_book != null) _book.maxPage = -1;
    }
}

/// <summary>
/// ★1111 主選單：點背景影片裡的她，打開「故事到目前為止」（M 11/11：「之前放在遊戲初始介面的可查看漫畫選項做在人物，點擊人物就可以看漫畫」）。
/// 她是畫在主選單背景影片裡的（MainMenuVideoCanvas 的 RawImage 播 UI video source），所以點擊範圍＝影片畫面上她站的位置
/// （24 秒影片逐段抽格量的聯集：x 0.585–0.785、從上量 0.27–0.905，四邊各留一點），跟著 RawImage 在螢幕上的範圍換算，
/// 不同解析度、視窗大小都對得上。
/// ・滑鼠移到她身上：她身上淡淡亮起一層暖光，頭上出現一行「翻開繪本　故事到目前為止（已開到第 N 頁）」；點下去開繪本。
/// ・鏡頭移到設定頁、名單頁（CameraFocusOnClick 會移動攝影機）或正在轉場時：不顯示提示、點了也不開。
/// ・提示只畫不擋點擊；主選單四個按鈕在畫面左邊，跟她的範圍不重疊。
/// ・找不到背景影片時，退回原本左下角的文字按鈕（StoryBookMenuButton），功能不會不見。
/// </summary>
public class MenuCharacterStoryBook : MonoBehaviour
{
    // 她在影片畫面裡的範圍（0～1，左下角為原點）
    private static readonly Rect CharacterUV = new Rect(0.57f, 0.08f, 0.23f, 0.67f);
    private const float FadeSeconds = 0.25f;
    // ★1111b 專案是線性色彩空間，UI 的半透明在線性空間混色：0.22 在暗背景上會變成一團霧、蓋掉她（11/11 實機錄影），改成 0.08 才是「淡淡的暖光」
    private const float GlowMaxAlpha = 0.08f;
    // ★1111b 鏡頭回到主畫面後要先停穩這麼久，才接受點她。
    //   設定頁的「返回菜單」剛好在她的位置：按下去那一格鏡頭就跳回主畫面（CameraFocusOnClick 直接設位置），
    //   同一格的點擊會被當成點她、直接開繪本（11/11 實機錄影 1:16）。
    //   ★1111c 品管建議：0.25 → 0.4 秒（一般雙擊間隔 0.2～0.5 秒，連點兩下「返回菜單」的第二下不要變成點她），而且至少要連續 2 格。
    private const float HomeSettleSeconds = 0.4f;

    private RawImage _video;
    private Camera _cam;
    private Vector3 _homePos;
    private float _homeSize;

    private RectTransform _glowRt;
    private Image _glow;
    private RectTransform _labelRt;
    private Text _label;
    private float _hint;          // 0～1，提示現在的顯示程度
    private bool _opening;
    private bool _ready;
    private bool _loggedHover;
    private float _homeFor;       // ★1111b 鏡頭連續停在主畫面多久了（LateUpdate 量，下一格才用）
    private int _homeFrames;      // ★1111c 連續幾格（防單格卡頓一次就累滿）
    private readonly Vector3[] _corners = new Vector3[4];

    private IEnumerator Start()
    {
        // 影片的 RenderTexture 可能晚一點才接上：最多等 1.5 秒，都找不到才退回左下角按鈕
        float until = Time.realtimeSinceStartup + 1.5f;
        while ((_video = FindMenuVideo()) == null && Time.realtimeSinceStartup < until) yield return null;
        if (_video == null)
        {
            Debug.LogWarning("【主選單】★1111 找不到主選單背景影片（RawImage），繪本入口退回左下角按鈕");
            gameObject.AddComponent<StoryBookMenuButton>();
            enabled = false;
            yield break;
        }
        GrabCamera();
        BuildHint();
        _ready = true;
        Debug.Log("【主選單】★1111 繪本入口改成點她（背景影片「" + _video.name + "」上 x " + CharacterUV.xMin.ToString("F2") + "–" + CharacterUV.xMax.ToString("F2")
                  + "、y " + CharacterUV.yMin.ToString("F2") + "–" + CharacterUV.yMax.ToString("F2") + "），繪本開到第 " + (StoryProgress.UnlockedThrough + 1) + " 頁");
    }

    private static RawImage FindMenuVideo()
    {
        // 播主選單影片的 VideoPlayer（輸出到 RenderTexture）→ 顯示那張 RenderTexture 的 RawImage；
        // 有好幾個時，優先選掛在名字含 MainMenuVideo 的 Canvas 底下的那個
        RawImage any = null;
        foreach (VideoPlayer vp in FindObjectsByType<VideoPlayer>(FindObjectsInactive.Exclude))
        {
            if (vp == null || vp.renderMode != VideoRenderMode.RenderTexture || vp.targetTexture == null) continue;
            foreach (RawImage ri in FindObjectsByType<RawImage>(FindObjectsInactive.Exclude))
            {
                if (ri == null || !ri.isActiveAndEnabled || ri.texture != vp.targetTexture) continue;
                Canvas root = ri.canvas != null ? ri.canvas.rootCanvas : null;
                if (root != null && root.name.Contains("MainMenuVideo")) return ri;
                if (any == null) any = ri;
            }
        }
        return any;
    }

    /// <summary>主畫面的攝影機與它的「主畫面位置」（設定頁、名單頁會把它移走）。</summary>
    private void GrabCamera()
    {
        _cam = Camera.main;
        if (_cam != null) { _homePos = _cam.transform.position; _homeSize = _cam.orthographicSize; }
    }

    private void BuildHint()
    {
        GameObject canvasGo = new GameObject("StoryBookCharacterHint");
        canvasGo.transform.SetParent(transform, false);
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;   // 不加 CanvasScaler：座標就是螢幕像素，直接用影片換算出來的位置
        canvas.sortingOrder = 120;                           // 畫在影片與選單上面；全部不接點擊（raycastTarget 關掉、也沒有 GraphicRaycaster）

        GameObject glowGo = new GameObject("Glow");
        glowGo.transform.SetParent(canvasGo.transform, false);
        _glowRt = glowGo.AddComponent<RectTransform>();
        _glowRt.anchorMin = _glowRt.anchorMax = Vector2.zero;
        _glowRt.pivot = new Vector2(0.5f, 0.5f);
        _glow = glowGo.AddComponent<Image>();
        _glow.sprite = MakeGlowSprite();
        _glow.raycastTarget = false;
        _glow.color = new Color(1f, 0.86f, 0.6f, 0f);

        GameObject labelGo = new GameObject("Label");
        labelGo.transform.SetParent(canvasGo.transform, false);
        _labelRt = labelGo.AddComponent<RectTransform>();
        _labelRt.anchorMin = _labelRt.anchorMax = Vector2.zero;
        _labelRt.pivot = new Vector2(0.5f, 0f);
        _label = labelGo.AddComponent<Text>();
        Font f = Resources.Load<Font>("Fonts/ChenYuluoyan-2.0-Thin");
        if (f == null) f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _label.font = f;
        _label.alignment = TextAnchor.LowerCenter;
        _label.horizontalOverflow = HorizontalWrapMode.Overflow;
        _label.verticalOverflow = VerticalWrapMode.Overflow;
        _label.raycastTarget = false;
        _label.text = "翻開繪本　故事到目前為止（已開到第 " + (StoryProgress.UnlockedThrough + 1) + " 頁）";
        Outline ol = labelGo.AddComponent<Outline>();   // ★1111b 細字在暗背景上加一圈黑邊比較好讀
        ol.effectColor = new Color(0f, 0f, 0f, 0.65f);
        ol.effectDistance = new Vector2(1.2f, -1.2f);
        _label.color = new Color(0.96f, 0.93f, 0.87f, 0f);
    }

    /// <summary>暖光：中間亮、往外柔柔淡掉的圓（程式畫的，不用新素材）。</summary>
    private static Sprite MakeGlowSprite()
    {
        const int N = 128;
        Texture2D tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Color32[] px = new Color32[N * N];
        float c = (N - 1) * 0.5f;
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                float a = Mathf.Clamp01(1f - d);
                a = a * a * (3f - 2f * a);
                px[y * N + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
    }

    /// <summary>她在螢幕上的範圍（像素，左下角為原點）。</summary>
    private bool TryGetCharacterScreenRect(out Rect r)
    {
        r = default;
        if (_video == null || !_video.isActiveAndEnabled) return false;
        _video.rectTransform.GetWorldCorners(_corners);
        Canvas c = _video.canvas;
        Camera cc = (c != null && c.renderMode != RenderMode.ScreenSpaceOverlay) ? c.worldCamera : null;
        Vector2 bl = RectTransformUtility.WorldToScreenPoint(cc, _corners[0]);
        Vector2 tr = RectTransformUtility.WorldToScreenPoint(cc, _corners[2]);
        Rect uv = _video.uvRect;   // 影片的哪一塊貼在 RawImage 上（預設整張）
        if (uv.width <= 0f || uv.height <= 0f) return false;
        float u0 = (CharacterUV.xMin - uv.x) / uv.width, u1 = (CharacterUV.xMax - uv.x) / uv.width;
        float v0 = (CharacterUV.yMin - uv.y) / uv.height, v1 = (CharacterUV.yMax - uv.y) / uv.height;
        r = Rect.MinMaxRect(Mathf.LerpUnclamped(bl.x, tr.x, u0), Mathf.LerpUnclamped(bl.y, tr.y, v0),
                            Mathf.LerpUnclamped(bl.x, tr.x, u1), Mathf.LerpUnclamped(bl.y, tr.y, v1));
        return r.width > 1f && r.height > 1f;
    }

    /// <summary>鏡頭還在主畫面（沒有移到設定頁、名單頁）。</summary>
    private bool CameraAtHome()
    {
        if (_cam == null) return true;
        return (_cam.transform.position - _homePos).sqrMagnitude < 0.25f && Mathf.Abs(_cam.orthographicSize - _homeSize) < 0.25f;
    }

    private static bool Transitioning()
    {
        PixeLadder.EasyTransition.SceneTransitioner st = PixeLadder.EasyTransition.SceneTransitioner.Instance;
        return st != null && st.IsTransitioning;
    }

    private void Update()
    {
        if (!_ready) return;
        if (_cam == null) GrabCamera();   // 攝影機換過或一開始還沒好：補抓一次

#if ENABLE_LEGACY_INPUT_MANAGER
        Vector3 mouse = Input.mousePosition;
        // ★1111c 同一格也按了 Enter／Space（主選單的鍵盤確認）就不算點她，免得跟「開始遊戲」同時載入兩個場景
        bool pressed = Input.GetMouseButtonDown(0) && !Input.GetKeyDown(KeyCode.Return) && !Input.GetKeyDown(KeyCode.KeypadEnter) && !Input.GetKeyDown(KeyCode.Space);
#else
        Vector3 mouse = new Vector3(-1f, -1f, 0f);   // 只開新輸入系統時這裡讀不到滑鼠（主選單其他地方也是用舊輸入）
        bool pressed = false;
#endif
        bool hasRect = TryGetCharacterScreenRect(out Rect r);
        bool usable = hasRect && !_opening && CameraAtHome() && !Transitioning() && _homeFor >= HomeSettleSeconds && _homeFrames >= 2;
        bool hover = usable && r.Contains(mouse);

        if (hover && !_loggedHover) { _loggedHover = true; Debug.Log("【主選單】★1111 滑鼠移到她身上，顯示「翻開繪本」提示"); }

        if (hover && pressed)
        {
            Debug.Log("【主選單】★1111 點她 → 開繪本「故事到目前為止」");
            if (StoryProgress.TryOpenReaderFromMenu()) { _opening = true; return; }
        }

        float target = hover ? 1f : 0f;
        _hint = Mathf.MoveTowards(_hint, target, Time.unscaledDeltaTime / FadeSeconds);
        if (!hasRect || _glow == null) return;
        if (_hint <= 0f && !hover)
        {
            if (_glow.color.a > 0f) { Color g0 = _glow.color; g0.a = 0f; _glow.color = g0; Color l0 = _label.color; l0.a = 0f; _label.color = l0; }
            return;   // 沒顯示時不用每幀搬位置
        }

        _glowRt.anchoredPosition = r.center;
        _glowRt.sizeDelta = new Vector2(r.width * 1.5f, r.height * 1.15f);
        Color g = _glow.color; g.a = GlowMaxAlpha * _hint; _glow.color = g;

        _label.fontSize = Mathf.Max(14, Mathf.RoundToInt(Screen.height * 0.033f));   // ★1111b 0.028 太小、太細
        // ★1111b 往右挪一點、貼近她的頭：不要跟影片裡的副標「一直都在」擠在一起
        _labelRt.anchoredPosition = new Vector2(r.center.x + r.width * 0.08f, r.yMax + Screen.height * 0.004f);
        _labelRt.sizeDelta = new Vector2(r.width * 3f, _label.fontSize * 1.6f);
        Color lc = _label.color; lc.a = _hint; _label.color = lc;
    }

    private void LateUpdate()
    {
        // ★1111b 等這一格所有 Update 跑完（包括主選單把鏡頭跳回來）才記；下一格的點擊才看得到
        if (!_ready) return;
        if (CameraAtHome() && !Transitioning()) { _homeFor += Time.unscaledDeltaTime; _homeFrames++; }
        else { _homeFor = 0f; _homeFrames = 0; }
    }

    private void OnDestroy()
    {
        // 暖光是程式畫的貼圖：離開主選單時一起清掉
        if (_glow != null && _glow.sprite != null)
        {
            Texture2D t = _glow.sprite.texture;
            Destroy(_glow.sprite);
            if (t != null) Destroy(t);
        }
    }
}

/// <summary>主選單左下角的「故事到目前為止」按鈕（程式長出來的，美術之後可以換成圖）。★1111 只在找不到背景影片時當備用。</summary>
public class StoryBookMenuButton : MonoBehaviour
{
    private Text _label;

    private void Start()
    {
        GameObject canvasGo = new GameObject("StoryBookButtonCanvas");
        canvasGo.transform.SetParent(transform, false);
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;
        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();
        if (FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            GameObject es = new GameObject("EventSystem");
            es.transform.SetParent(transform, false);
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }

        GameObject btnGo = new GameObject("StoryBookButton");
        btnGo.transform.SetParent(canvasGo.transform, false);
        RectTransform rt = btnGo.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(0f, 0f);
        rt.pivot = new Vector2(0f, 0f);
        rt.anchoredPosition = new Vector2(150f, 14f);      // ★1023 跟主選單那排按鈕的左緣大致對齊；壓低一點，不碰「離開遊戲」的框
        rt.sizeDelta = new Vector2(560f, 64f);
        Image bg = btnGo.AddComponent<Image>();
        bg.color = new Color(0.05f, 0.07f, 0.16f, 0.55f);
        Button btn = btnGo.AddComponent<Button>();
        ColorBlock cb = btn.colors;
        cb.highlightedColor = new Color(0.25f, 0.27f, 0.4f, 0.8f);
        cb.pressedColor = new Color(0.4f, 0.4f, 0.55f, 0.9f);
        btn.colors = cb;
        btn.onClick.AddListener(StoryProgress.OpenReaderFromMenu);

        GameObject txtGo = new GameObject("Label");
        txtGo.transform.SetParent(btnGo.transform, false);
        RectTransform trt = txtGo.AddComponent<RectTransform>();
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(18f, 6f); trt.offsetMax = new Vector2(-18f, -6f);
        _label = txtGo.AddComponent<Text>();
        Font f = Resources.Load<Font>("Fonts/ChenYuluoyan-2.0-Thin");
        if (f == null) f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _label.font = f;
        _label.fontSize = 30;
        _label.alignment = TextAnchor.MiddleLeft;
        _label.horizontalOverflow = HorizontalWrapMode.Overflow;   // 字不要被切掉
        _label.verticalOverflow = VerticalWrapMode.Overflow;
        _label.color = new Color(0.91f, 0.88f, 0.82f, 0.95f);
        _label.raycastTarget = false;
        _label.text = "繪本　故事到目前為止（第 " + (StoryProgress.UnlockedThrough + 1) + " 頁）";
    }
}
