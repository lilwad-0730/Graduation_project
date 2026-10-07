using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// ★1001 會議・企劃第二種「玩家第一次被狼群追上後，用圖說明為什麼會重生」。
/// 三格圖：1 被狼群撕咬，推不動巨石 → 2 巨石滾下，她被砸暈，狼群被驅散 → 3 她醒來，回去推巨石。
/// 只在「第一次被狼咬滿而重生」時播，之後的重生直接黑幕。整個執行期間只播一次（ShownThisRun），換場景不重播。
///
/// 做法：不動 PlayerRespawnSystem。重生一開始就把它的 blackScreenTime 暫時拉長到三格圖的總長，
/// 黑幕蓋上後（fadeDuration 之後）在黑幕上面放一個 Canvas 一格一格淡入淡出，播完再把 blackScreenTime 改回去。
/// 圖從 Resources/RespawnStory/panel_0..3 讀（0 紙、1–3 格，同一頁的四層；或 Inspector 直接指定 panels）；找不到圖就不播、照原本重生。
/// 預設是「漫畫頁」：紙先出現，三格一格一格疊上去，最後整頁淡出；關掉 accumulateLayers 就回到一次一張。
/// 紅線：圖裡沒有字；她沒有臉；狼沒有眼睛牙齒；不畫傷口。
///
/// ★1003 五個版本（version）：A 頁面逐格（本檔）、B 導覽鏡頭、C 直式條漫、D 定格分格（第一格是實機畫面）、E 繪本翻頁；
/// B–E 的播放在 RespawnComicPlayer.cs，素材在 Resources/RespawnStory/B_guided、C_strip、D_freeze、E_book。
/// 某一版的素材不齊，就退回 A；A 也沒有就照原本重生。
///
/// ★1004 D 版（連續世界版，現行）：照 M 定的順序一頁七格——1 被狼群攻擊（被咬滿那一幀定格成格 1，像一張照片）→ 2 她倒下 → 3 巨石沒人撐、往崖邊滾回去
/// → 4 巨石落下、狼群四散 → 5 巨石停在山腳（她的光沿著巨石的路一盞一盞下來）→ 6 她又把巨石推起來 → 7 狼又跟在她身後，伺機而動。
/// 七格畫在同一張完整的頁面圖上（Resources/RespawnStory/D_freeze/page.png，3840×2160；光點位置在同資料夾的 page_layout.json），
/// 每一列是同一片連續的夜山；鏡頭只跟著會動的東西走（往左 → 往下 → 往右），最後拉回整頁（pageCamera，預設開；關掉改用聚光）。
/// 版面跟著頁面圖走：page_layout.json 的 layout（或光點名字）判斷是連續世界版（9 停，現行）還是完整頁版（12 停，Codex 10/04 交付的七格），各用各的鏡頭。
/// 沒有 page 就退回 1003 的兩格版（panel_2、panel_3），再不齊退回 A。
/// ★1004 C 版再設計：條漫 8 塊（1820×8192）、七拍、逐段緩動、光點層。場景裡若存著 1003 的舊數值（3 停），播放時自動換成新預設；
/// 素材若還是舊的 4 塊，就用舊的節奏。右鍵元件 →「C 版：套用 1004 再設計的預設節奏」可把 Inspector 的數值一起換掉。
///
/// ★1006 影片（M：「我想要將影片放到廢墟第一次死亡後，並且之後的死亡都不會觸發」）：
/// 有影片（Resources/RespawnStory/Video/first_death，預設是 D7 連續世界版的預覽影片 20.5 秒）就播影片，優先於上面的版本。
/// 廢墟第一次死亡＝被狼咬滿，或死的時候她在廢墟高度（y 低於 ruinsBelowY；棉花堡在天上，掉出雲的死亡不算）。
/// 一輪遊戲只播一次：之後的死亡直接黑幕；回到主選單（下一位玩家）才重設。找不到影片就照 version 播漫畫（只限被狼咬滿）。
///
/// ★1007 改成程式播 D 版漫畫（M：「然後加上廢墟第一次被狼群追擊死亡後出現漫畫」→ 選「改成程式播的 D 版漫畫」）：
/// useVideo 預設關、version 預設 D。影片檔還在 Resources，要換回影片就把 useVideo 預設改回 true。
/// 「被狼群追擊而死」三種都算：
///   1. 咬滿 PlayerMovement.wolvesToRespawn 隻。1001 起死亡門檻和減速分母 maxWolvesToStop 分開了；
///      原本拿 maxWolvesToStop（6）判斷，場上最多 5 隻狼，所以漫畫永遠不會播（影片靠「在廢墟死亡」那條才播得出來）。
///   2. 巨石挑戰中被第一隻狼咬到而失敗（BoulderChallengeController 接手：巨石下滾 → 重置）。
///   3. 在廢墟死掉時身上還咬著狼（例如被拖下坑）。
/// 巨石挑戰失敗時，重生要等巨石滾完才開始，所以格 1 用「被咬到那一刻」先截好的畫面，不是滾完之後的畫面。
/// 測試：Play 模式下，Player 身上這個元件右鍵 →「測試：現在播第一次死亡漫畫」。
///
/// 場景不用改：載入有 WolfEnemy 的場景時自動掛到 Player。
/// </summary>
[DisallowMultipleComponent]
public class FirstWolfDeathStory : MonoBehaviour
{
    public static bool Enabled = true;
    /// <summary>這一輪已經播過（跨場景保留；回到主選單＝新的一輪才重設）。</summary>
    public static bool ShownThisRun = false;

    public enum StoryVersion { A_PageBuild, B_GuidedView, C_VerticalStrip, D_FreezeFrame, E_Storybook }

    [Header("影片（1006；1007 起預設關）")]
    [Tooltip("★1007 預設關：M 改用程式播的 D 版漫畫（格 1 是死亡那一刻的實機畫面）。\n打開＝有影片就播影片，優先於下面的漫畫版本；找不到影片才照 version 播漫畫")]
    public bool useVideo = false;
    [Tooltip("Resources 裡的影片（不含副檔名）。預設是 D7 連續世界版的預覽影片（20.5 秒、1280×720、無聲）；換影片就用同名檔覆蓋")]
    public string videoResource = "RespawnStory/Video/first_death";
    [Tooltip("也可以直接指定影片（優先於 videoResource）")]
    public VideoClip videoClip;
    [Tooltip("廢墟裡任何一種死亡都算（被狼咬滿、掉進坑）。關掉＝只有被狼咬滿才播")]
    public bool videoOnAnyRuinsDeath = true;
    [Tooltip("死的時候她的 y 低於這個值就算在廢墟。SampleScene：棉花堡在天上（掉出雲的死亡區 y 約 -65～-95），廢墟地面約 -128、坑底死亡區約 -136")]
    public float ruinsBelowY = -100f;
    [Tooltip("影片淡入、淡出秒數（影片本身最後已經暗下來）")]
    public float videoFadeIn = 0.25f;
    public float videoFadeOut = 0.5f;
    [Tooltip("黑幕蓋上之後，影片還沒準備好最多再等幾秒；等不到就這次不播（不會卡住）")]
    public float videoPrepareTimeout = 2.0f;
    [Range(0f, 1f)] public float videoVolume = 1f;

    [Header("版本（1003）")]
    [Tooltip("A 頁面逐格：一頁三格一格一格疊上去\nB 導覽鏡頭：直式漫畫頁，鏡頭一格一格推進再拉回整頁\nC 直式條漫（1004 再設計）：七拍往下捲——光被夾住、同格反覆、滾、蝕、塵成星、醒、坡\nD 定格分格（1004 連續世界版）：被咬滿那一刻的實機畫面定格成格 1（一張照片），同一張頁面上接六格——倒下、往崖邊滾回、落下狼四散、山腳（她的光沿著巨石的路下來）、再推、狼又跟在身後；每一列是同一片連續的夜山，鏡頭跟著巨石與她的光走\nE 繪本翻頁：團隊繪本頁（廢-12 → 黑頁 → 廢-8）")]
    public StoryVersion version = StoryVersion.D_FreezeFrame;   // ★1007 預設 D（原本 A）
    [Tooltip("B：整頁停、移到格1、停、移到格2、停、移到格3、停、拉回整頁、停、淡出")]
    public float[] guidedTimes = new float[] { 0.6f, 0.9f, 2.0f, 0.8f, 2.6f, 0.9f, 2.6f, 1.0f, 1.0f, 0.6f };
    [Tooltip("C：每一停的視窗上緣（條漫像素，條寬 1820、每塊 1024、畫面一次看 1024）\n0 光被夾住｜1 同格反覆｜2 蝕｜3 塵成星・一點光｜4 醒｜5 山頂光球｜6 坡底・她又把手放上去")]
    public float[] stripTops = RespawnComicPlayer.C2Tops();
    [Tooltip("C：停與停之間捲動秒數（6 段）")]
    public float[] stripMoves = RespawnComicPlayer.C2Moves();
    [Tooltip("C：每一停停留秒數（7 停）")]
    public float[] stripHolds = RespawnComicPlayer.C2Holds();
    [Tooltip("C：每段捲動的緩動。0 慢進慢出、1 加速（第 2 段：滾下來，撞上「蝕」才停）、2 等速、3 減速")]
    public int[] stripEases = RespawnComicPlayer.C2Eases();
    [Tooltip("C：疊在條漫上的光點（跟著捲；到某一停亮起、會呼吸）")]
    public ComicGlow[] stripGlows = RespawnComicPlayer.C2Glows();
    [Range(0f, 1f)] public float stripGlowPulse = 0.25f;
    public float stripGlowPeriod = 2.6f;
    [Tooltip("C：整體速度（1＝約 17 秒；1.25 約 14 秒）")]
    [Range(0.5f, 2f)] public float stripSpeed = 1f;
    public float stripFade = 0.7f;
    [Tooltip("D（1004 連續世界版）：鏡頭偏移逐漸讀出整頁（照片 → 往左格 2、格 3 → 往下格 4、格 5 → 往右格 6、格 7 → 拉回整頁）。關掉＝鏡頭只拉遠一次，改由聚光照同一條路")]
    public bool pageCamera = true;
    [Tooltip("D 每個停點停多久（9 個）：S0 實機滿版、S1 格1 照片、S2 格2 倒下、S3 格3 往崖邊滾、S4 格4 落下狼散、S5 格5 山腳・她回來、S6 格6 再推、S7 格7 狼又跟著、S8 整頁\n長度和頁面版面的停點數不同時，自動用那個版面的預設值（完整頁版是 12 個）")]
    public float[] pageHolds = RespawnComicPlayer.DPageHolds();
    [Tooltip("D 從每個停點移到下一個的秒數（9 個，最後一個是 0）。預設值讓畫面每秒最多移動約 0.6 個畫面寬；調短會變快、比較不順")]
    public float[] pageMoves = RespawnComicPlayer.DPageMoves();
    [Tooltip("D 整體速度（1＝約 20 秒；1.2 約 17 秒但移動快 2 成）")]
    [Range(0.5f, 2f)] public float pageSpeed = 1f;
    [Tooltip("D（1003 兩格版，素材不齊時才用）：閃、轉紫、縮進格 1、停、格 2 進、停、格 3 進、停、淡出")]
    public float[] freezeTimes = new float[] { 0.12f, 0.35f, 0.6f, 1.6f, 0.3f, 3.0f, 0.3f, 3.2f, 0.6f };
    [Tooltip("D：定格畫面乘上的顏色（紫夜）")]
    public Color freezeTint = new Color(0.78f, 0.74f, 0.98f, 1f);
    [Range(0f, 1f)] public float freezeHalftoneAlpha = 0.28f;
    [Tooltip("E：頁 1 停、翻、頁 2 停、翻、頁 3 停、淡出")]
    public float[] bookTimes = new float[] { 3.2f, 0.7f, 1.8f, 0.7f, 3.6f, 0.6f };
    [Tooltip("各版素材的 Resources 資料夾")]
    public string folderB = "RespawnStory/B_guided", folderC = "RespawnStory/C_strip", folderD = "RespawnStory/D_freeze", folderE = "RespawnStory/E_book";

    [Header("圖（A 頁面逐格）")]
    [Tooltip("直接指定圖層；留空就讀 Resources 的 panelResources")]
    public Sprite[] panels;
    [Tooltip("漫畫頁的圖層：panel_0 是紙（整頁底），panel_1..3 是三格（格外透明，同一張頁面的座標）。一層一層疊上去，像漫畫格一格一格出現")]
    public string[] panelResources = new string[] { "RespawnStory/panel_0", "RespawnStory/panel_1", "RespawnStory/panel_2", "RespawnStory/panel_3" };
    [Tooltip("累積顯示：前面的層留著，下一層疊上去（漫畫頁）。關掉就回到一次只顯示一張、換張時淡出")]
    public bool accumulateLayers = true;

    [Header("節奏")]
    [Tooltip("每一格停留幾秒（含淡入淡出）。panelSecondsEach 有填的格以它為準")]
    public float panelSeconds = 3.2f;
    [Tooltip("逐層秒數（紙 0.6；格 1 煽り 3.0、格 2 俯瞰 3.6、格 3 平視・間 3.8）。留空或 0 就用 panelSeconds")]
    public float[] panelSecondsEach = new float[] { 0.6f, 3.0f, 3.6f, 3.8f };
    public float panelFade = 0.45f;
    [Tooltip("累積模式最後整頁一起淡出的秒數")]
    public float pageFadeOut = 0.6f;
    [Tooltip("畫面佔螢幕的比例（留邊）。漫畫頁圖層自己有邊，用 1")]
    [Range(0.5f, 1f)] public float screenFraction = 1.0f;
    [Tooltip("重生黑幕 Canvas 是 999、文字卡 10000；三格圖放在中間")]
    public int sortingOrder = 1200;

    [Header("規則")]
    [Tooltip("整個執行期間只播一次")]
    public bool onlyOnce = true;
    [Tooltip("允許按鍵跳到下一格（黑幕總長不會跟著縮短，預設關）")]
    public bool allowSkip = false;
    public float minSecondsBeforeSkip = 1.0f;

    [Header("除錯")]
    public bool logEvents = true;

    private PlayerMovement _pm;
    private PlayerRespawnSystem _rs;
    private bool _wasRespawning = false;
    private Canvas _canvas;
    private Image _image;
    private readonly System.Collections.Generic.List<Image> _layers = new System.Collections.Generic.List<Image>();
    private float _originalBlackTime = -1f;
    private Coroutine _routine;
    private readonly System.Collections.Generic.List<Texture2D> _loaded = new System.Collections.Generic.List<Texture2D>();   // B–E 播完要卸掉的貼圖
    private bool _wasFailing = false;      // ★1007 巨石挑戰失敗的開始（第一隻狼咬到）
    private Texture2D _failCapture;        // ★1007 失敗那一刻先截好的畫面（D 版格 1）
    private int _prevAttached = 0;         // ★1007 上一幀身上咬著幾隻狼（重生偵測晚一幀時用）
    private bool _testNext = false;        // ★1007 測試：下一次重生當成被狼群追擊而死
    private string _why = "";

    // ── 自動掛載 ─────────────────────────────────────────────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        AutoAttach();
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // 回到主選單＝這一輪結束（下一位玩家）：下一輪在廢墟第一次死亡會再播一次
        if (scene.name.IndexOf("MainMenu", System.StringComparison.OrdinalIgnoreCase) >= 0) ShownThisRun = false;
        AutoAttach();
    }

    public static bool AutoAttach()
    {
        if (!Enabled) return false;
        if (FindAnyObjectByType<WolfEnemy>(FindObjectsInactive.Include) == null && FindAnyObjectByType<WolfSpawner>(FindObjectsInactive.Include) == null) return false;
        PlayerMovement pm = FindAnyObjectByType<PlayerMovement>();
        if (pm == null) return false;
        if (pm.GetComponent<FirstWolfDeathStory>() != null) return false;
        pm.gameObject.AddComponent<FirstWolfDeathStory>();
        Debug.Log("[FirstWolfDeathStory] 已自動掛到 " + pm.gameObject.name);
        return true;
    }

    // ── 本體 ─────────────────────────────────────────────────
    private void Awake()
    {
        _pm = GetComponent<PlayerMovement>();
        if (_pm == null) _pm = GetComponentInParent<PlayerMovement>();
        _rs = GetComponent<PlayerRespawnSystem>();
        if (_rs == null) _rs = GetComponentInParent<PlayerRespawnSystem>();
        if (_rs == null) _rs = FindAnyObjectByType<PlayerRespawnSystem>();
    }

    private void Start()
    {
        PrewarmVideo();   // 進場景就先把影片準備好，死的那一刻不用等
    }

    private void Update()
    {
        // ★1007 巨石挑戰失敗的那一刻（第一隻狼咬到）先截好畫面：重生要等巨石滾完才開始，那時已經不是「被咬」的畫面
        bool failing = BoulderChallengeController.IsFailing;
        if (failing && !_wasFailing) OnBoulderFailStarted();
        _wasFailing = failing;

        bool now = PlayerRespawnSystem.IsAnyRespawning;
        if (now && !_wasRespawning) OnRespawnStarted();
        _wasRespawning = now;
        if (_pm != null) _prevAttached = _pm.attachedWolvesCount;
    }

    private void OnRespawnStarted()
    {
        if (!Enabled || _pm == null || _rs == null) { DropFailCapture(); return; }
        if (onlyOnce && ShownThisRun) { DropFailCapture(); return; }

        // ★1007 被狼群追擊而死：咬滿死亡門檻（wolvesToRespawn；原本拿減速分母 maxWolvesToStop＝6 判斷，永遠達不到）、
        //   巨石挑戰中被第一隻狼咬到而失敗、或在廢墟死掉時身上還咬著狼
        int attached = Mathf.Max(_pm.attachedWolvesCount, _prevAttached);
        int killW = Mathf.Max(1, _pm.wolvesToRespawn);
        bool inRuins = _pm.transform.position.y < ruinsBelowY;
        bool bitten = attached >= killW;
        bool boulderFail = BoulderChallengeController.IsFailing;
        bool dragged = inRuins && attached > 0;
        bool wolfDeath = _testNext || bitten || boulderFail || dragged;
        _why = _testNext ? "測試" : bitten ? "被狼咬滿 " + attached + " 隻" : boulderFail ? "巨石挑戰中被狼咬到" : dragged ? "死的時候身上咬著 " + attached + " 隻狼" : "";
        _testNext = false;

        // ★1006 影片：廢墟第一次死亡（被狼咬滿，或在廢墟高度死掉）
        if (useVideo)
        {
            if ((wolfDeath || (videoOnAnyRuinsDeath && inRuins)) && TryStartVideo(wolfDeath ? _why : "在廢墟死亡（y " + _pm.transform.position.y.ToString("F0") + "）")) { DropFailCapture(); return; }
        }

        if (!wolfDeath) { DropFailCapture(); return; }   // 漫畫只在被狼群追擊而死時播（鳥、石化、溺水、沒被狼咬的掉坑…都不播）

        if (version != StoryVersion.A_PageBuild && TryStartVersion()) return;
        DropFailCapture();

        Sprite[] sprites = LoadPanels();
        if (sprites == null || sprites.Length == 0)
        {
            if (logEvents) Debug.Log("[FirstWolfDeathStory] 沒有圖（Resources/RespawnStory/panel_0..3），這次不播");
            return;
        }
        ShownThisRun = true;
        FreezeWolves(true);
        float total = 0f;
        for (int i = 0; i < sprites.Length; i++) total += PanelDuration(i);
        if (accumulateLayers) total += Mathf.Max(0.05f, pageFadeOut);
        _originalBlackTime = _rs.blackScreenTime;
        _rs.blackScreenTime = Mathf.Max(_originalBlackTime, total + 0.3f);   // 重生協程在黑幕蓋上後才讀這個值
        if (logEvents) Debug.Log("[FirstWolfDeathStory] 廢墟第一次被狼群追擊而死（" + _why + "）：播 A 頁面逐格，黑幕拉長到 " + _rs.blackScreenTime.ToString("F1") + " 秒");
        _routine = StartCoroutine(PlayRoutine(sprites));
    }

    // ── 巨石挑戰失敗的畫面（1007） ─────────────────────────────
    private void OnBoulderFailStarted()
    {
        if (!Enabled || useVideo || (onlyOnce && ShownThisRun)) return;
        if (version != StoryVersion.D_FreezeFrame) return;   // 只有 D 版的格 1 用實機畫面
        StartCoroutine(CaptureFailFrame());
    }

    private IEnumerator CaptureFailFrame()
    {
        yield return new WaitForEndOfFrame();
        Texture2D cap = null;
        try { cap = ScreenCapture.CaptureScreenshotAsTexture(); }
        catch (System.Exception e) { Debug.LogWarning("[FirstWolfDeathStory] 巨石挑戰失敗那一刻截不到圖，格 1 改用重生那一刻：" + e.Message); }
        if (cap == null) yield break;
        DropFailCapture();
        _failCapture = cap;
        if (logEvents) Debug.Log("[FirstWolfDeathStory] 巨石挑戰失敗（第一隻狼咬到）：先截好漫畫格 1 的畫面");
    }

    private void DropFailCapture()
    {
        if (_failCapture != null) Destroy(_failCapture);
        _failCapture = null;
    }

    [ContextMenu("測試：現在播第一次死亡漫畫")]
    private void TestPlayNow()
    {
        if (!Application.isPlaying) { Debug.Log("[FirstWolfDeathStory] 要在 Play 模式下用"); return; }
        if (_rs == null || PlayerRespawnSystem.IsAnyRespawning) { Debug.Log("[FirstWolfDeathStory] 現在不能測：找不到重生系統，或正在重生"); return; }
        ShownThisRun = false;
        _testNext = true;
        _rs.TriggerRespawn();   // 跟真的死掉一樣走重生（回到目前的存檔點）
    }

    // ── 影片（1006） ─────────────────────────────────────────
    private VideoPlayer _vp;              // 進場景就準備好（放在自己的 active 物件上：Canvas 平常是關著的，關著的物件不能準備影片）
    private GameObject _videoGo;
    private RawImage _videoRaw;           // 畫在重生漫畫的 Canvas 上（在重生黑幕之上）
    private Image _videoBack;             // 影片後面一層全黑：螢幕不是 16:9 時，邊邊不會露出遊戲
    private RenderTexture _videoRT;
    private VideoClip _videoFromResources; // 從 Resources 讀到的影片
    private bool _videoBroken = false;     // 準備時就出錯：改播漫畫
    private readonly System.Collections.Generic.List<Behaviour> _frozenWolves = new System.Collections.Generic.List<Behaviour>();

    private VideoClip ResolveClip()
    {
        if (videoClip != null) return videoClip;
        if (string.IsNullOrEmpty(videoResource)) return null;
        if (_videoFromResources == null) _videoFromResources = Resources.Load<VideoClip>(videoResource);
        return _videoFromResources;
    }

    /// <summary>進場景先準備影片（還沒播過、有影片的時候）。</summary>
    private void PrewarmVideo()
    {
        if (!Enabled || !useVideo || _vp != null || _videoBroken) return;
        if (onlyOnce && ShownThisRun) return;
        VideoClip clip = ResolveClip();
        if (clip == null || clip.length < 0.1)
        {
            if (logEvents) Debug.Log("[FirstWolfDeathStory] 找不到影片（Resources/" + videoResource + "）；廢墟第一次被狼咬滿時照 version 播漫畫");
            return;
        }
        _videoRT = new RenderTexture((int)Mathf.Max(16, clip.width), (int)Mathf.Max(16, clip.height), 0, RenderTextureFormat.ARGB32);
        _videoRT.name = "FirstDeathVideo";
        _videoRT.Create();
        _videoGo = new GameObject("[FirstDeathVideo Player]");
        _vp = _videoGo.AddComponent<VideoPlayer>();
        _vp.playOnAwake = false;
        _vp.isLooping = false;
        _vp.source = VideoSource.VideoClip;
        _vp.clip = clip;
        _vp.renderMode = VideoRenderMode.RenderTexture;
        _vp.targetTexture = _videoRT;
        _vp.aspectRatio = VideoAspectRatio.FitInside;
        _vp.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;   // 重生用真實時間
        _vp.skipOnDrop = true;
        _vp.waitForFirstFrame = true;
        if (clip.audioTrackCount > 0)
        {
            _vp.audioOutputMode = VideoAudioOutputMode.Direct;
            for (ushort i = 0; i < clip.audioTrackCount; i++) { _vp.EnableAudioTrack(i, true); _vp.SetDirectAudioVolume(i, videoVolume); }
        }
        else _vp.audioOutputMode = VideoAudioOutputMode.None;
        _vp.errorReceived += OnVideoError;
        _vp.Prepare();
        if (logEvents) Debug.Log("[FirstWolfDeathStory] 影片準備中：" + clip.name + "（" + clip.length.ToString("F1") + " 秒，" + clip.width + "×" + clip.height + "）");
    }

    private void OnVideoError(VideoPlayer p, string msg)
    {
        Debug.LogWarning("[FirstWolfDeathStory] 影片錯誤：" + msg);
        _videoBroken = true;
    }

    /// <summary>有影片就拉長黑幕、播放，回傳 true；沒有影片或影片壞了回傳 false（呼叫端改播漫畫）。</summary>
    private bool TryStartVideo(string why)
    {
        if (_vp == null) PrewarmVideo();
        if (_vp == null || _videoBroken || _vp.clip == null)
        {
            if (logEvents && _videoBroken) Debug.Log("[FirstWolfDeathStory] 影片壞了，改照 version 播漫畫");
            CleanupVideo();
            return false;
        }
        VideoClip clip = _vp.clip;
        ShownThisRun = true;
        FreezeWolves(true);
        _originalBlackTime = _rs.blackScreenTime;
        // 黑幕在死後 fadeDuration 蓋滿，再過一點點（重置場景、8 個物理幀）才開始計黑幕時間；影片在黑幕蓋滿後開始
        float need = (float)clip.length + Mathf.Max(0f, videoFadeIn) + Mathf.Max(0f, videoFadeOut) + 0.6f;
        _rs.blackScreenTime = Mathf.Max(_originalBlackTime, need);
        if (logEvents) Debug.Log("[FirstWolfDeathStory] 廢墟第一次死亡（" + why + "）：播影片 " + clip.name + "（" + clip.length.ToString("F1") + " 秒），黑幕拉長到 " + _rs.blackScreenTime.ToString("F1") + " 秒；這一輪之後的死亡不再播");
        _routine = StartCoroutine(PlayVideoRoutine(clip));
        return true;
    }

    private IEnumerator PlayVideoRoutine(VideoClip clip)
    {
        EnsureUI();
        // 一層全黑（影片比例外的邊）＋影片（維持比例置中）
        GameObject back = new GameObject("Video Back");
        back.transform.SetParent(_canvas.transform, false);
        _videoBack = back.AddComponent<Image>();
        _videoBack.raycastTarget = false;
        _videoBack.color = new Color(0f, 0f, 0f, 0f);
        RectTransform brt = _videoBack.rectTransform;
        brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one; brt.offsetMin = Vector2.zero; brt.offsetMax = Vector2.zero;

        GameObject img = new GameObject("Video");
        img.transform.SetParent(_canvas.transform, false);
        _videoRaw = img.AddComponent<RawImage>();
        _videoRaw.raycastTarget = false;
        _videoRaw.color = new Color(1f, 1f, 1f, 0f);
        _videoRaw.texture = _videoRT;
        RectTransform rt = _videoRaw.rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        AspectRatioFitter fit = img.AddComponent<AspectRatioFitter>();
        fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        fit.aspectRatio = clip.height > 0 ? (float)clip.width / clip.height : 16f / 9f;

        bool ended = false;
        VideoPlayer.EventHandler onEnd = p => ended = true;
        _vp.loopPointReached += onEnd;
        if (!_vp.isPrepared) _vp.Prepare();

        // 等黑幕蓋滿（重生用 unscaled time）
        float t = 0f, wait = _rs.fadeDuration + 0.1f;
        while (t < wait) { t += Time.unscaledDeltaTime; yield return null; }
        t = 0f;
        while (!_vp.isPrepared && !_videoBroken && t < videoPrepareTimeout) { t += Time.unscaledDeltaTime; yield return null; }
        if (!_vp.isPrepared || _videoBroken)
        {
            // 黑幕已經拉長了（重生在這之前就讀走了黑幕時間）：照原本重生，只是黑久一點
            Debug.LogWarning("[FirstWolfDeathStory] 影片沒有準備好（" + (_videoBroken ? "播放錯誤" : "超過 " + videoPrepareTimeout + " 秒") + "），這次不播");
            FinishVideo();
            yield break;
        }

        _canvas.gameObject.SetActive(true);
        _videoBack.color = Color.black;
        _vp.Play();
        t = 0f;
        while (_vp.frame < 1 && !_videoBroken && t < 1.0f) { t += Time.unscaledDeltaTime; yield return null; }   // 第一幀出來再淡入

        double len = clip.length;
        float fi = Mathf.Max(0.01f, videoFadeIn), e = 0f;
        while (!ended && !_videoBroken)
        {
            e += Time.unscaledDeltaTime;
            float a = Mathf.Clamp01(e / fi);
            double remain = len - _vp.time;
            if (videoFadeOut > 0f && remain < videoFadeOut) a = Mathf.Min(a, Mathf.Clamp01((float)(remain / videoFadeOut)));
            _videoRaw.color = new Color(1f, 1f, 1f, a);
            if (_pm != null) _pm.isCutsceneFrozen = true;   // 影片播完前不能動（萬一黑幕先結束也一樣）
            if (e > len + 3.0) break;                        // 保險：影片卡住也不會一直蓋著
            yield return null;
        }
        if (_vp != null) _vp.loopPointReached -= onEnd;
        if (logEvents) Debug.Log("[FirstWolfDeathStory] 影片播完");
        FinishVideo();
    }

    private void FinishVideo()
    {
        if (_canvas != null) _canvas.gameObject.SetActive(false);
        FreezeWolves(false);
        if (_pm != null && !PlayerRespawnSystem.IsAnyRespawning) _pm.isCutsceneFrozen = false;   // 黑幕已經先結束：影片播完才放開
        if (_originalBlackTime >= 0f) _rs.blackScreenTime = _originalBlackTime;
        _originalBlackTime = -1f;
        CleanupVideo();
        _routine = null;
    }

    private void CleanupVideo()
    {
        if (_vp != null) _vp.errorReceived -= OnVideoError;
        _vp = null;
        if (_videoGo != null) Destroy(_videoGo);
        _videoGo = null;
        if (_videoRaw != null) Destroy(_videoRaw.gameObject);
        _videoRaw = null;
        if (_videoBack != null) Destroy(_videoBack.gameObject);
        _videoBack = null;
        if (_videoRT != null) { _videoRT.Release(); Destroy(_videoRT); }
        _videoRT = null;
        _videoFromResources = null;   // 不卸載：播放器在這一幀結束才真的銷毀，影片本身是串流讀檔，留著無妨
    }

    /// <summary>
    /// 播漫畫／影片的這段黑幕裡，狼群原地不動（重生會把狼放回原位；存檔點離狼很近時，黑幕一拉長狼就會在黑幕裡咬上來，
    /// 黑幕結束時她身上已經掛滿狼、走不動）。播完放開，照原本的規則追。
    /// </summary>
    private void FreezeWolves(bool freeze)
    {
        if (freeze)
        {
            _frozenWolves.Clear();
            foreach (WolfEnemy w in FindObjectsByType<WolfEnemy>(FindObjectsInactive.Exclude))
                if (w.enabled) { w.enabled = false; _frozenWolves.Add(w); }
            foreach (WolfSpawner s in FindObjectsByType<WolfSpawner>(FindObjectsInactive.Exclude))
                if (s.enabled) { s.enabled = false; _frozenWolves.Add(s); }
            if (logEvents && _frozenWolves.Count > 0) Debug.Log("[FirstWolfDeathStory] 黑幕裡狼群先停住（" + _frozenWolves.Count + "）");
        }
        else
        {
            for (int i = 0; i < _frozenWolves.Count; i++) if (_frozenWolves[i] != null) _frozenWolves[i].enabled = true;
            _frozenWolves.Clear();
        }
    }

    // ── B–E ─────────────────────────────────────────────────
    /// <summary>載入該版素材、拉長黑幕、開播。素材不齊回傳 false（呼叫端退回 A）。</summary>
    private bool TryStartVersion()
    {
        float total = 0f;
        bool startsAtDeath = false;
        IEnumerator play = null;
        switch (version)
        {
            case StoryVersion.B_GuidedView:
            {
                Texture2D[] t = LoadSeries(folderB + "/panel_", 1, 3);
                if (t == null) break;
                _loaded.AddRange(t);
                total = RespawnComicPlayer.DurationB(guidedTimes);
                play = AfterBlack(RespawnComicPlayer.PlayB(UIRoot(), t, guidedTimes));
                break;
            }
            case StoryVersion.C_VerticalStrip:
            {
                Texture2D[] t = LoadSeries(folderC + "/tile_", 0, 16, true);
                if (t == null) break;
                _loaded.AddRange(t);
                float[] tops = stripTops, moves = stripMoves, holds = stripHolds;
                int[] eases = stripEases;
                ComicGlow[] glows = stripGlows;
                bool oldNumbers = IsOldStripPreset(tops);
                if (t.Length >= 8 && oldNumbers)
                {
                    // 場景裡存的是 1003 舊版（3 停）的數值，素材已經是 1004 的 8 塊：用新預設
                    tops = RespawnComicPlayer.C2Tops(); moves = RespawnComicPlayer.C2Moves(); holds = RespawnComicPlayer.C2Holds();
                    eases = RespawnComicPlayer.C2Eases(); glows = RespawnComicPlayer.C2Glows();
                    if (logEvents) Debug.Log("[FirstWolfDeathStory] C：場景存的是 1003 的舊節奏，這次用 1004 再設計的預設（右鍵元件可套用到 Inspector）");
                }
                else if (t.Length < 8 && !oldNumbers)
                {
                    // 素材還是 1003 的 4 塊：用舊節奏，免得捲過頭
                    tops = new float[] { 0f, 1210f, 3024f }; moves = new float[] { 1.0f, 2.4f }; holds = new float[] { 2.4f, 2.8f, 3.0f };
                    eases = null; glows = null;
                    if (logEvents) Debug.Log("[FirstWolfDeathStory] C：素材只有 " + t.Length + " 塊（1003 舊版），用舊節奏");
                }
                float sp = Mathf.Max(0.1f, stripSpeed);
                moves = Scaled(moves, 1f / sp); holds = Scaled(holds, 1f / sp);
                total = RespawnComicPlayer.DurationC(moves, holds, stripFade);
                play = AfterBlack(RespawnComicPlayer.PlayC(UIRoot(), t, tops, moves, holds, stripFade, eases, glows, stripGlowPulse, stripGlowPeriod));
                break;
            }
            case StoryVersion.D_FreezeFrame:
            {
                RespawnComicPlayer.DPageSet set = RespawnComicPlayer.DPageSet.Load(folderD);
                if (set != null)
                {
                    // 1004 完整頁版：一張完整的頁面圖，鏡頭偏移逐漸讀出（鏡頭可開關）
                    foreach (Texture2D tx in set.All()) if (tx != null) _loaded.Add(tx);
                    int n = set.spec.Count;                                     // 版面跟著頁面圖（page_layout.json）：連續世界 9 停／完整頁 12 停
                    float[] ph = (pageHolds != null && pageHolds.Length == n) ? pageHolds : (float[])set.spec.holds.Clone();
                    float[] pm = (pageMoves != null && pageMoves.Length == n) ? pageMoves : (float[])set.spec.moves.Clone();
                    if (logEvents) Debug.Log("[FirstWolfDeathStory] D：頁面版面＝" + set.spec.label + ((ph != pageHolds || pm != pageMoves) ? "；Inspector 的停點數不是 " + n + "，用這個版面的預設節奏" : ""));
                    if (logEvents && set.page.width < 3000) Debug.LogWarning("[FirstWolfDeathStory] D：頁面圖只有 " + set.page.width + "×" + set.page.height + "，推近會糊。Project 視窗右鍵 page.png →「Reimport」（RespawnStoryImport 會設成原尺寸）");
                    bool camOn = pageCamera;
                    float sp = pageSpeed;
                    total = RespawnComicPlayer.DurationDPage(ph, pm, sp);
                    startsAtDeath = true;
                    play = FreezeRoutine(cap => RespawnComicPlayer.PlayDPage(UIRoot(), cap, set, ph, pm, sp, camOn, freezeTint, freezeHalftoneAlpha));
                    break;
                }
                // 1003 兩格版
                Texture2D ht = RespawnComicPlayer.Tex(folderD + "/halftone");
                Texture2D p2 = RespawnComicPlayer.Tex(folderD + "/panel_2");
                Texture2D p3 = RespawnComicPlayer.Tex(folderD + "/panel_3");
                if (p2 == null || p3 == null) break;
                _loaded.Add(p2); _loaded.Add(p3); if (ht != null) _loaded.Add(ht);
                total = RespawnComicPlayer.DurationD(freezeTimes);
                startsAtDeath = true;
                play = FreezeRoutine(cap => RespawnComicPlayer.PlayD(UIRoot(), cap, p2, p3, ht, freezeTimes, freezeTint, freezeHalftoneAlpha));
                break;
            }
            case StoryVersion.E_Storybook:
            {
                Texture2D[] t = LoadSeries(folderE + "/page_", 1, 3);
                if (t == null) break;
                _loaded.AddRange(t);
                total = RespawnComicPlayer.DurationE(bookTimes);
                play = AfterBlack(RespawnComicPlayer.PlayE(UIRoot(), t, bookTimes));
                break;
            }
        }
        if (play == null)
        {
            _loaded.Clear();
            if (logEvents) Debug.Log("[FirstWolfDeathStory] 版本 " + version + " 的素材不齊，退回 A");
            return false;
        }
        ShownThisRun = true;
        FreezeWolves(true);
        _originalBlackTime = _rs.blackScreenTime;
        // D 從死亡那一刻就蓋上（黑幕淡入的 fadeDuration 也算在裡面）；其他版等黑幕蓋上才開始
        float need = startsAtDeath ? total - _rs.fadeDuration + 0.2f : total + 0.3f;
        _rs.blackScreenTime = Mathf.Max(_originalBlackTime, need);
        if (logEvents) Debug.Log("[FirstWolfDeathStory] 廢墟第一次被狼群追擊而死（" + _why + "）：播 " + version + "（" + total.ToString("F1") + " 秒），黑幕拉長到 " + _rs.blackScreenTime.ToString("F1") + " 秒");
        _routine = StartCoroutine(Run(play));
        return true;
    }

    private static bool IsOldStripPreset(float[] tops)
    {
        return tops != null && tops.Length == 3 && Mathf.Approximately(tops[1], 1210f) && Mathf.Approximately(tops[2], 3024f);
    }

    private static float[] Scaled(float[] a, float s)
    {
        if (a == null) return null;
        float[] r = new float[a.Length];
        for (int i = 0; i < a.Length; i++) r[i] = a[i] * s;
        return r;
    }

    [ContextMenu("C 版：套用 1004 再設計的預設節奏")]
    private void ApplyC2Defaults()
    {
        stripTops = RespawnComicPlayer.C2Tops(); stripMoves = RespawnComicPlayer.C2Moves(); stripHolds = RespawnComicPlayer.C2Holds();
        stripEases = RespawnComicPlayer.C2Eases(); stripGlows = RespawnComicPlayer.C2Glows();
        stripGlowPulse = 0.25f; stripGlowPeriod = 2.6f; stripSpeed = 1f; stripFade = 0.7f;
        Debug.Log("[FirstWolfDeathStory] 已套用 C 版 1004 預設節奏（記得存場景）");
    }

    private Texture2D[] LoadSeries(string prefix, int from, int maxCount, bool untilMissing = false)
    {
        System.Collections.Generic.List<Texture2D> list = new System.Collections.Generic.List<Texture2D>();
        for (int i = from; i < from + maxCount; i++)
        {
            Texture2D t = RespawnComicPlayer.Tex(prefix + i);
            if (t == null)
            {
                if (untilMissing && list.Count > 0) break;
                return null;
            }
            list.Add(t);
        }
        return list.Count > 0 ? list.ToArray() : null;
    }

    private Transform UIRoot()
    {
        EnsureUI();
        return _canvas.transform;
    }

    private IEnumerator AfterBlack(IEnumerator inner)
    {
        float wait = _rs.fadeDuration + 0.1f, t = 0f;
        while (t < wait) { t += Time.unscaledDeltaTime; yield return null; }
        _canvas.gameObject.SetActive(true);
        yield return null;   // 讓 Canvas 先算好大小
        yield return inner;
    }

    private IEnumerator FreezeRoutine(System.Func<Texture2D, IEnumerator> makePlay)
    {
        Texture2D cap = null;
        if (_failCapture != null)
        {
            // ★1007 巨石挑戰失敗：格 1 用被咬到那一刻先截好的畫面
            cap = _failCapture;
            _failCapture = null;
        }
        else
        {
            yield return new WaitForEndOfFrame();   // 等這一幀畫完再截（黑幕這時幾乎還是透明的）
            try { cap = ScreenCapture.CaptureScreenshotAsTexture(); }
            catch (System.Exception e) { Debug.LogWarning("[FirstWolfDeathStory] 截圖失敗，第一格用黑畫面：" + e.Message); }
        }
        UIRoot();
        _canvas.gameObject.SetActive(true);
        yield return makePlay(cap);
        if (cap != null) Destroy(cap);
    }

    private IEnumerator Run(IEnumerator play)
    {
        yield return play;
        if (_canvas != null) _canvas.gameObject.SetActive(false);
        FreezeWolves(false);
        if (_originalBlackTime >= 0f) _rs.blackScreenTime = _originalBlackTime;
        _originalBlackTime = -1f;
        _routine = null;
        yield return null;   // 等播放層真的銷毀（Destroy 在這一幀結束才生效）
        for (int i = 0; i < _loaded.Count; i++) if (_loaded[i] != null) Resources.UnloadAsset(_loaded[i]);   // 條漫 8 塊 2048×1024，播完就卸
        _loaded.Clear();
    }

    private float PanelDuration(int i)
    {
        if (panelSecondsEach != null && i < panelSecondsEach.Length && panelSecondsEach[i] > 0.05f) return Mathf.Max(0.5f, panelSecondsEach[i]);
        return Mathf.Max(0.5f, panelSeconds);
    }

    private Sprite[] LoadPanels()
    {
        if (panels != null && panels.Length > 0)
        {
            int ok = 0;
            for (int i = 0; i < panels.Length; i++) if (panels[i] != null) ok++;
            if (ok > 0)
            {
                Sprite[] list = new Sprite[ok];
                int k = 0;
                for (int i = 0; i < panels.Length; i++) if (panels[i] != null) list[k++] = panels[i];
                return list;
            }
        }
        if (panelResources == null) return null;
        System.Collections.Generic.List<Sprite> found = new System.Collections.Generic.List<Sprite>();
        for (int i = 0; i < panelResources.Length; i++)
        {
            if (string.IsNullOrEmpty(panelResources[i])) continue;
            Sprite s = Resources.Load<Sprite>(panelResources[i]);
            if (s == null)
            {
                Texture2D t = Resources.Load<Texture2D>(panelResources[i]);
                if (t != null) s = Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f);
            }
            if (s != null) found.Add(s);
        }
        return found.ToArray();
    }

    private IEnumerator PlayRoutine(Sprite[] sprites)
    {
        // 等黑幕完全蓋上（重生用的是 unscaled time）
        float wait = _rs.fadeDuration + 0.1f;
        float t = 0f;
        while (t < wait) { t += Time.unscaledDeltaTime; yield return null; }

        EnsureUI();
        _canvas.gameObject.SetActive(true);
        if (accumulateLayers)
        {
            // 漫畫頁：一層疊一層，前面的留著
            EnsureLayerCount(sprites.Length);
            for (int i = 0; i < sprites.Length; i++)
            {
                Image img = _layers[i];
                img.sprite = sprites[i];
                img.preserveAspect = true;
                img.color = new Color(1f, 1f, 1f, 0f);
                img.gameObject.SetActive(true);
                float dur = PanelDuration(i);
                float fade = Mathf.Clamp(panelFade, 0.05f, dur * 0.6f);
                float e = 0f;
                while (e < dur)
                {
                    e += Time.unscaledDeltaTime;
                    img.color = new Color(1f, 1f, 1f, Mathf.Clamp01(e / fade));
                    if (allowSkip && e > minSecondsBeforeSkip && Input.anyKeyDown) break;
                    yield return null;
                }
                img.color = Color.white;
            }
            float f = 0f;
            float fo = Mathf.Max(0.05f, pageFadeOut);
            while (f < fo)
            {
                f += Time.unscaledDeltaTime;
                float a = 1f - Mathf.Clamp01(f / fo);
                for (int i = 0; i < sprites.Length; i++) _layers[i].color = new Color(1f, 1f, 1f, a);
                yield return null;
            }
            for (int i = 0; i < _layers.Count; i++) { _layers[i].color = new Color(1f, 1f, 1f, 0f); _layers[i].gameObject.SetActive(false); }
        }
        else
        {
            // 一次一張：淡入、停、淡出
            for (int i = 0; i < sprites.Length; i++)
            {
                _image.sprite = sprites[i];
                _image.preserveAspect = true;
                float dur = PanelDuration(i);
                float fade = Mathf.Clamp(panelFade, 0.05f, dur * 0.45f);
                float e = 0f;
                bool skipped = false;
                while (e < dur)
                {
                    e += Time.unscaledDeltaTime;
                    float a = 1f;
                    if (e < fade) a = e / fade;
                    else if (e > dur - fade) a = Mathf.Clamp01((dur - e) / fade);
                    _image.color = new Color(1f, 1f, 1f, a);
                    if (allowSkip && e > minSecondsBeforeSkip && Input.anyKeyDown) { skipped = true; break; }
                    yield return null;
                }
                if (skipped)
                {
                    float f = 0f;
                    while (f < 0.2f) { f += Time.unscaledDeltaTime; _image.color = new Color(1f, 1f, 1f, 1f - f / 0.2f); yield return null; }
                }
            }
            _image.color = new Color(1f, 1f, 1f, 0f);
        }
        _canvas.gameObject.SetActive(false);
        FreezeWolves(false);

        if (_originalBlackTime >= 0f) _rs.blackScreenTime = _originalBlackTime;
        _originalBlackTime = -1f;
        _routine = null;
    }

    private void EnsureLayerCount(int n)
    {
        while (_layers.Count < n)
        {
            GameObject img = new GameObject("Layer " + _layers.Count);
            img.transform.SetParent(_canvas.transform, false);
            Image im = img.AddComponent<Image>();
            im.raycastTarget = false;
            im.preserveAspect = true;
            RectTransform rt = im.rectTransform;
            float m = (1f - screenFraction) * 0.5f;
            rt.anchorMin = new Vector2(m, m);
            rt.anchorMax = new Vector2(1f - m, 1f - m);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            im.color = new Color(1f, 1f, 1f, 0f);
            img.SetActive(false);
            _layers.Add(im);
        }
    }

    private void EnsureUI()
    {
        if (_image != null) return;
        GameObject root = new GameObject("[RespawnStory Canvas]");
        DontDestroyOnLoad(root);   // 重生途中不會換場景，但保險
        _canvas = root.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = sortingOrder;
        CanvasScaler scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        GameObject img = new GameObject("Panel");
        img.transform.SetParent(root.transform, false);
        _image = img.AddComponent<Image>();
        _image.raycastTarget = false;
        _image.preserveAspect = true;
        RectTransform rt = _image.rectTransform;
        float m = (1f - screenFraction) * 0.5f;
        rt.anchorMin = new Vector2(m, m);
        rt.anchorMax = new Vector2(1f - m, 1f - m);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        _image.color = new Color(1f, 1f, 1f, 0f);
        root.SetActive(false);
    }

    private void OnDestroy()
    {
        FreezeWolves(false);
        CleanupVideo();
        DropFailCapture();
        if (_canvas != null) Destroy(_canvas.gameObject);
        if (_rs != null && _originalBlackTime >= 0f) _rs.blackScreenTime = _originalBlackTime;
    }
}
