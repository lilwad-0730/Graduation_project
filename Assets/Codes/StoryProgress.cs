using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

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
/// 主選單多一個入口「故事到目前為止」（左下角，程式長出來的按鈕，美術之後可以換圖）：
///   開 Book 場景自由翻閱，只能翻到開到的那一頁，Esc 回主選單。沒人碰會自己翻（ExhibitMode），也只翻到那一頁。
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
            GameObject host = new GameObject("[StoryBookMenuButton]");
            SceneManager.MoveGameObjectToScene(host, scene);
            host.AddComponent<StoryBookMenuButton>();
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
    public static void OpenReaderFromMenu()
    {
        if (!Application.CanStreamedLevelBeLoaded(BookSceneName)) { Debug.LogWarning("【故事進度】Build Settings 裡沒有 Book 場景，開不了繪本"); return; }
        ReaderMode = true;
        EndCredits.EndingMode = false;
        BookTransitionManager.ResetTransientState();
        Time.timeScale = 1f;
        Debug.Log("【故事進度】★1023 主選單開繪本：翻到第 " + (UnlockedThrough + 1) + " 頁為止");
        SceneManager.LoadScene(BookSceneName);
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

/// <summary>主選單左下角的「故事到目前為止」按鈕（程式長出來的，美術之後可以換成圖）。</summary>
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
