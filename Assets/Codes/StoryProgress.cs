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
/// ・滑鼠移到她身上：她背後亮起一圈冷藍光暈（★1112：只畫在她的剪影外面，不蓋在她身上，跟著影片裡的她動），頭上出現一行「翻開繪本　故事到目前為止（已開到第 N 頁）」；點下去開繪本。
/// ・鏡頭移到設定頁、名單頁（CameraFocusOnClick 會移動攝影機）或正在轉場時：不顯示提示、點了也不開。
/// ・提示只畫不擋點擊；主選單四個按鈕在畫面左邊，跟她的範圍不重疊。
/// ・找不到背景影片時，退回原本左下角的文字按鈕（StoryBookMenuButton），功能不會不見。
/// </summary>
public class MenuCharacterStoryBook : MonoBehaviour
{
    // 她在影片畫面裡的範圍（0～1，左下角為原點）
    private static readonly Rect CharacterUV = new Rect(0.57f, 0.08f, 0.23f, 0.67f);
    private const float FadeSeconds = 0.25f;
    // ★1112 M：「進入遊戲漫畫介面的光做冷藍光，放在扣除的人物後面，做光暈，而不是現在的覆蓋在人物上」
    //   她的剪影是從主選單影片（UI video source.mp4，1920×1080、24 秒循環）第 12 秒那格去背量出來的（166 點，影片像素、y 往下）。
    //   影片裡她會慢慢晃、遠近也變一點（左右約 29 像素、大小約 4%），所以每 0.5 秒量一次她相對第 12 秒那格的仿射變換（48 筆，
    //   對齊後剪影重疊率 ≥ 0.978、邊緣誤差 95% 在 1.4 像素內），執行時照影片播放時間內插，光暈跟著她動。
    //   光暈只畫在剪影外面（剪影裡挖空），看起來就是她背後透出來的光；腳邊淡出，不在地上發光。
    private const float VideoW = 1920f, VideoH = 1080f, VideoLoopSeconds = 24f, MotionStep = 0.5f;
    private const int HaloX0 = 1014, HaloX1 = 1600, HaloY0 = 172, HaloY1 = 1080;   // 光暈貼圖蓋住的影片範圍（像素，y 往下）
    private const int HaloScale = 2;                                               // 貼圖用半解析度（293×454）
    private static readonly Color HaloColor = new Color(0.55f, 0.80f, 1f, 1f);     // 冷藍
    private const float HaloMaxAlpha = 0.6f;   // 線性色彩空間混色；用錄影畫面模擬過（0.55～0.75 都看得清楚，又不搶）
    private static readonly Vector2 HaloPivotPx = new Vector2(1307f, 969f);        // 變換的錨點：她腳底中間（影片像素）

    // 第 12 秒那格她的剪影：x,y 交錯（影片像素，y 往下）
    private static readonly int[] SilhouetteRef =
    {
        1314,282, 1314,285, 1307,294, 1305,304, 1293,306, 1284,304, 1278,313, 1265,319, 1261,317, 1264,312, 1254,307,
        1257,312, 1257,316, 1244,328, 1237,325, 1232,325, 1227,328, 1199,328, 1188,334, 1181,335, 1178,338, 1179,345,
        1174,359, 1169,365, 1169,371, 1167,373, 1158,373, 1166,376, 1147,386, 1139,394, 1132,405, 1125,423, 1124,443,
        1129,463, 1138,480, 1138,485, 1135,492, 1134,538, 1139,553, 1157,571, 1184,582, 1187,590, 1197,595, 1200,602,
        1208,609, 1221,615, 1220,621, 1211,636, 1206,653, 1202,660, 1196,684, 1182,712, 1182,723, 1174,737, 1172,755,
        1165,763, 1166,771, 1171,777, 1171,782, 1165,796, 1166,823, 1168,830, 1175,840, 1191,845, 1199,857, 1207,861,
        1210,865, 1221,872, 1222,887, 1226,890, 1234,891, 1239,895, 1247,909, 1258,938, 1263,943, 1271,945, 1289,945,
        1304,950, 1311,948, 1318,926, 1319,913, 1322,909, 1323,902, 1326,899, 1337,917, 1347,940, 1347,945, 1342,950,
        1334,953, 1328,958, 1326,963, 1327,967, 1334,969, 1380,967, 1404,963, 1411,960, 1412,954, 1409,945, 1409,937,
        1417,901, 1424,896, 1446,890, 1447,888, 1447,873, 1451,863, 1459,856, 1463,849, 1466,834, 1471,826, 1472,813,
        1486,797, 1486,785, 1490,776, 1488,764, 1490,756, 1490,726, 1484,698, 1474,673, 1469,655, 1462,641, 1462,635,
        1469,626, 1468,608, 1471,600, 1471,584, 1466,572, 1459,563, 1460,551, 1454,532, 1453,518, 1443,506, 1438,487,
        1431,475, 1433,463, 1431,450, 1434,442, 1433,426, 1428,417, 1413,401, 1413,397, 1418,391, 1423,394, 1421,404,
        1425,400, 1425,393, 1419,387, 1421,379, 1420,360, 1417,357, 1415,357, 1414,361, 1400,360, 1397,353, 1392,350,
        1383,339, 1383,330, 1374,329, 1364,324, 1348,324, 1332,310, 1323,306, 1309,304, 1309,295, 1312,291, 1318,289, 1319,283
    };

    // 每 0.5 秒一筆（t = 0, 0.5, …, 23.5）：第 12 秒那格 → 那個時間的仿射 a11, a12, a21, a22, tx, ty（影片像素，y 往下）
    private static readonly float[] Motion =
    {
        1.00320f, -0.00020f, 0.00100f, 1.00280f, 11.45f, 4.76f,
        1.00620f, 0.00010f, 0.00040f, 1.00440f, 8.72f, 3.81f,
        1.00810f, 0.00060f, -0.00050f, 1.00630f, 7.29f, 3.21f,
        1.00990f, 0.00010f, -0.00070f, 1.00990f, 6.38f, 1.23f,
        1.01230f, 0.00070f, -0.00300f, 1.01110f, 3.91f, 2.63f,
        1.01360f, 0.00020f, -0.00120f, 1.01510f, 3.28f, -2.16f,
        1.01570f, 0.00060f, -0.00250f, 1.01600f, 0.78f, -1.80f,
        1.01690f, 0.00040f, -0.00200f, 1.01850f, -0.44f, -4.33f,
        1.01810f, 0.00020f, -0.00090f, 1.01990f, -1.78f, -7.19f,
        1.01950f, 0.00010f, -0.00120f, 1.02120f, -3.74f, -8.11f,
        1.02020f, 0.00000f, 0.00010f, 1.02160f, -5.01f, -10.66f,
        1.02090f, -0.00010f, -0.00030f, 1.02190f, -6.60f, -10.92f,
        1.02070f, 0.00010f, -0.00120f, 1.02100f, -7.28f, -9.90f,
        1.02030f, 0.00010f, -0.00130f, 1.02130f, -7.89f, -10.28f,
        1.01910f, -0.00020f, -0.00180f, 1.02080f, -7.41f, -9.67f,
        1.01830f, -0.00010f, -0.00120f, 1.01990f, -7.96f, -10.38f,
        1.01700f, -0.00000f, -0.00140f, 1.01880f, -7.84f, -9.75f,
        1.01560f, 0.00020f, -0.00150f, 1.01680f, -7.91f, -8.71f,
        1.01460f, 0.00000f, -0.00050f, 1.01440f, -8.34f, -8.91f,
        1.01220f, -0.00010f, -0.00130f, 1.01320f, -7.00f, -6.97f,
        1.00980f, 0.00020f, -0.00160f, 1.01140f, -5.98f, -5.39f,
        1.00740f, -0.00070f, -0.00050f, 1.00930f, -4.08f, -5.41f,
        1.00500f, 0.00010f, 0.00030f, 1.00600f, -3.23f, -4.53f,
        1.00250f, 0.00010f, 0.00080f, 1.00340f, -1.65f, -3.31f,
        1.00000f, -0.00000f, 0.00000f, 1.00000f, 0.00f, 0.00f,
        0.99740f, 0.00020f, -0.00030f, 0.99790f, 1.76f, 2.07f,
        0.99570f, -0.00000f, 0.00100f, 0.99430f, 2.74f, 2.81f,
        0.99290f, 0.00000f, 0.00000f, 0.99370f, 5.25f, 5.31f,
        0.99000f, 0.00010f, -0.00110f, 0.99240f, 8.03f, 8.20f,
        0.98830f, -0.00000f, -0.00060f, 0.99000f, 9.62f, 9.51f,
        0.98700f, 0.00020f, -0.00010f, 0.98790f, 10.61f, 10.71f,
        0.98540f, 0.00010f, 0.00010f, 0.98700f, 12.51f, 11.63f,
        0.98440f, 0.00020f, 0.00100f, 0.98420f, 13.69f, 12.57f,
        0.98290f, 0.00030f, 0.00070f, 0.98460f, 15.76f, 13.58f,
        0.98240f, 0.00040f, 0.00010f, 0.98350f, 16.78f, 15.57f,
        0.98250f, 0.00060f, -0.00020f, 0.98300f, 17.18f, 16.75f,
        0.98190f, 0.00050f, 0.00070f, 0.98190f, 19.01f, 16.50f,
        0.98230f, 0.00060f, -0.00020f, 0.98300f, 19.48f, 17.45f,
        0.98290f, 0.00080f, -0.00130f, 0.98440f, 19.89f, 18.56f,
        0.98380f, 0.00050f, -0.00110f, 0.98550f, 20.42f, 18.14f,
        0.98560f, 0.00040f, -0.00050f, 0.98640f, 19.72f, 17.09f,
        0.98680f, 0.00060f, -0.00140f, 0.98840f, 19.74f, 17.42f,
        0.98830f, 0.00130f, -0.00190f, 0.99010f, 19.21f, 17.08f,
        0.99080f, 0.00150f, -0.00370f, 0.99000f, 17.63f, 19.09f,
        0.99350f, 0.00130f, -0.00430f, 0.98780f, 16.16f, 20.29f,
        0.99560f, 0.00050f, 0.00060f, 0.99400f, 15.70f, 10.97f,
        0.99900f, 0.00040f, -0.00020f, 0.99540f, 13.13f, 10.78f,
        1.00090f, 0.00060f, -0.00050f, 0.99860f, 12.23f, 9.02f
    };
    // ★1111b 鏡頭回到主畫面後要先停穩這麼久，才接受點她。
    //   設定頁的「返回菜單」剛好在她的位置：按下去那一格鏡頭就跳回主畫面（CameraFocusOnClick 直接設位置），
    //   同一格的點擊會被當成點她、直接開繪本（11/11 實機錄影 1:16）。
    //   ★1111c 品管建議：0.25 → 0.4 秒（一般雙擊間隔 0.2～0.5 秒，連點兩下「返回菜單」的第二下不要變成點她），而且至少要連續 2 格。
    private const float HomeSettleSeconds = 0.4f;

    private RawImage _video;
    private Camera _cam;
    private Vector3 _homePos;
    private float _homeSize;

    private VideoPlayer _player;   // ★1112 讀影片播到哪，光暈跟著她動
    private RectTransform _haloRt;
    private RawImage _halo;
    private Texture2D _haloTex;
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
        while ((_video = FindMenuVideo(out _player)) == null && Time.realtimeSinceStartup < until) yield return null;
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

    private static RawImage FindMenuVideo(out VideoPlayer player)
    {
        player = null;
        // 播主選單影片的 VideoPlayer（輸出到 RenderTexture）→ 顯示那張 RenderTexture 的 RawImage；
        // 有好幾個時，優先選掛在名字含 MainMenuVideo 的 Canvas 底下的那個
        RawImage any = null;
        VideoPlayer anyPlayer = null;
        foreach (VideoPlayer vp in FindObjectsByType<VideoPlayer>(FindObjectsInactive.Exclude))
        {
            if (vp == null || vp.renderMode != VideoRenderMode.RenderTexture || vp.targetTexture == null) continue;
            foreach (RawImage ri in FindObjectsByType<RawImage>(FindObjectsInactive.Exclude))
            {
                if (ri == null || !ri.isActiveAndEnabled || ri.texture != vp.targetTexture) continue;
                Canvas root = ri.canvas != null ? ri.canvas.rootCanvas : null;
                if (root != null && root.name.Contains("MainMenuVideo")) { player = vp; return ri; }
                if (any == null) { any = ri; anyPlayer = vp; }
            }
        }
        player = anyPlayer;
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

        // ★1112 冷藍光暈：畫在她的剪影外面（剪影挖空），看起來是她背後透出來的光，不蓋在她身上
        GameObject haloGo = new GameObject("Halo");
        haloGo.transform.SetParent(canvasGo.transform, false);
        _haloRt = haloGo.AddComponent<RectTransform>();
        _haloRt.anchorMin = _haloRt.anchorMax = Vector2.zero;
        _halo = haloGo.AddComponent<RawImage>();
        _haloTex = BuildHaloTexture();
        _halo.texture = _haloTex;
        _halo.raycastTarget = false;
        _halo.color = new Color(HaloColor.r, HaloColor.g, HaloColor.b, 0f);

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

    /// <summary>★1112 冷藍光暈貼圖：她的剪影往外柔柔散開，剪影本身挖空（看起來是她背後的光）；腳邊淡出。程式畫的，不用新素材。</summary>
    private static Texture2D BuildHaloTexture()
    {
        int tw = (HaloX1 - HaloX0) / HaloScale, th = (HaloY1 - HaloY0) / HaloScale;
        float[] m = new float[tw * th];
        RasterizeSilhouette(m, tw, th);
        float[] core = Dilate(m, tw, th, 2);                 // 挖空的範圍比剪影大一點點（約 4 像素），她晃動時光也不會蓋到她
        BoxBlur(core, tw, th, 1);
        BoxBlur(core, tw, th, 1);
        float[] glow = (float[])m.Clone();                   // 3 次方框模糊 ≈ 高斯：從剪影邊緣往外散
        BoxBlur(glow, tw, th, 10);
        BoxBlur(glow, tw, th, 10);
        BoxBlur(glow, tw, th, 10);
        const float feet = 969f, top = 969f - 0.30f * (969f - 282f);   // 她腳底與往上 3 成身高（影片像素）
        Color32[] px = new Color32[tw * th];
        for (int y = 0; y < th; y++)
        {
            float vy = HaloY0 + (y + 0.5f) * HaloScale;
            float f = Mathf.Clamp01((feet - vy) / (feet - top));
            f = f * f * (3f - 2f * f);
            float fade = 0.15f + 0.85f * f;
            int row = (th - 1 - y) * tw;                     // Texture2D 第 0 列在最下面
            for (int x = 0; x < tw; x++)
            {
                int i = y * tw + x;
                float a = Mathf.Pow(Mathf.Clamp01(glow[i] * 1.9f), 1.25f) * (1f - Mathf.Clamp01(core[i])) * fade;
                px[row + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
            }
        }
        Texture2D tex = new Texture2D(tw, th, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return tex;
    }

    /// <summary>把剪影多邊形畫進貼圖（掃描線、奇偶規則；以像素中心判斷）。</summary>
    private static void RasterizeSilhouette(float[] m, int tw, int th)
    {
        int n = SilhouetteRef.Length / 2;
        float[] xs = new float[n], ys = new float[n], hits = new float[n];
        for (int k = 0; k < n; k++)
        {
            xs[k] = (SilhouetteRef[2 * k] - HaloX0) / (float)HaloScale;
            ys[k] = (SilhouetteRef[2 * k + 1] - HaloY0) / (float)HaloScale;
        }
        for (int y = 0; y < th; y++)
        {
            float sy = y + 0.5f;
            int c = 0;
            for (int k = 0, j = n - 1; k < n; j = k++)
            {
                float y0 = ys[j], y1 = ys[k];
                if ((y0 <= sy && y1 > sy) || (y1 <= sy && y0 > sy))
                    hits[c++] = xs[j] + (sy - y0) / (y1 - y0) * (xs[k] - xs[j]);
            }
            System.Array.Sort(hits, 0, c);
            for (int h = 0; h + 1 < c; h += 2)
            {
                int xa = Mathf.Max(0, Mathf.CeilToInt(hits[h] - 0.5f));
                int xb = Mathf.Min(tw - 1, Mathf.CeilToInt(hits[h + 1] - 0.5f) - 1);
                for (int x = xa; x <= xb; x++) m[y * tw + x] = 1f;
            }
        }
    }

    /// <summary>方形範圍取最大值（半徑 r）。</summary>
    private static float[] Dilate(float[] src, int w, int h, int r)
    {
        float[] tmp = new float[src.Length], dst = new float[src.Length];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float v = 0f;
                for (int k = Mathf.Max(0, x - r); k <= Mathf.Min(w - 1, x + r); k++) v = Mathf.Max(v, src[y * w + k]);
                tmp[y * w + x] = v;
            }
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float v = 0f;
                for (int k = Mathf.Max(0, y - r); k <= Mathf.Min(h - 1, y + r); k++) v = Mathf.Max(v, tmp[k * w + x]);
                dst[y * w + x] = v;
            }
        return dst;
    }

    /// <summary>方框模糊（半徑 r，邊界外當 0），先橫後直，就地改。</summary>
    private static void BoxBlur(float[] a, int w, int h, int r)
    {
        float inv = 1f / (2 * r + 1);
        float[] line = new float[Mathf.Max(w, h)];
        for (int y = 0; y < h; y++)
        {
            int o = y * w;
            float s = 0f;
            for (int k = 0; k <= r && k < w; k++) s += a[o + k];
            for (int x = 0; x < w; x++)
            {
                line[x] = s * inv;
                int add = x + r + 1, sub = x - r;
                if (add < w) s += a[o + add];
                if (sub >= 0) s -= a[o + sub];
            }
            for (int x = 0; x < w; x++) a[o + x] = line[x];
        }
        for (int x = 0; x < w; x++)
        {
            float s = 0f;
            for (int k = 0; k <= r && k < h; k++) s += a[k * w + x];
            for (int y = 0; y < h; y++)
            {
                line[y] = s * inv;
                int add = y + r + 1, sub = y - r;
                if (add < h) s += a[add * w + x];
                if (sub >= 0) s -= a[sub * w + x];
            }
            for (int y = 0; y < h; y++) a[y * w + x] = line[y];
        }
    }

    /// <summary>★1112 影片像素（左上角為原點、y 往下）→ 螢幕像素：screen = origin + (X·perPx.x, Y·perPx.y)。</summary>
    private bool VideoToScreen(out Vector2 origin, out Vector2 perPx)
    {
        origin = perPx = Vector2.zero;
        if (_video == null || !_video.isActiveAndEnabled) return false;
        _video.rectTransform.GetWorldCorners(_corners);
        Canvas c = _video.canvas;
        Camera cc = (c != null && c.renderMode != RenderMode.ScreenSpaceOverlay) ? c.worldCamera : null;
        Vector2 bl = RectTransformUtility.WorldToScreenPoint(cc, _corners[0]);
        Vector2 tr = RectTransformUtility.WorldToScreenPoint(cc, _corners[2]);
        Rect uv = _video.uvRect;
        if (uv.width <= 0f || uv.height <= 0f) return false;
        float sx = (tr.x - bl.x) / uv.width, sy = (tr.y - bl.y) / uv.height;
        perPx = new Vector2(sx / VideoW, -sy / VideoH);
        origin = new Vector2(bl.x - uv.x * sx, bl.y + (1f - uv.y) * sy);
        return true;
    }

    /// <summary>★1112 照影片現在播到的時間，把光暈貼到她身後（她晃、遠近變，光暈跟著）。</summary>
    private void PlaceHalo()
    {
        if (_haloRt == null || !VideoToScreen(out Vector2 o, out Vector2 k)) return;
        float a11 = 1f, a12 = 0f, a21 = 0f, a22 = 1f, tx = 0f, ty = 0f;
        int n = Motion.Length / 6;
        if (_player != null && n > 1)
        {
            double len = _player.length > 0.1 ? _player.length : VideoLoopSeconds;
            double tt = _player.time % len;
            if (tt < 0) tt += len;
            float f = (float)(tt * (VideoLoopSeconds / len)) / MotionStep;
            int i0 = Mathf.FloorToInt(f) % n;
            if (i0 < 0) i0 += n;
            int i1 = (i0 + 1) % n;
            float u = Mathf.Clamp01(f - Mathf.Floor(f));
            a11 = M(i0, i1, 0, u); a12 = M(i0, i1, 1, u); a21 = M(i0, i1, 2, u);
            a22 = M(i0, i1, 3, u); tx = M(i0, i1, 4, u); ty = M(i0, i1, 5, u);
        }
        Vector2 p = HaloPivotPx;
        Vector2 q = new Vector2(a11 * p.x + a12 * p.y + tx, a21 * p.x + a22 * p.y + ty);   // 錨點跟著她移到哪
        float sx = Mathf.Sqrt(a11 * a11 + a21 * a21);
        float sy = sx > 1e-4f ? (a11 * a22 - a12 * a21) / sx : 1f;
        float rot = Mathf.Atan2(a21, a11) * Mathf.Rad2Deg;   // 影片座標 y 往下，螢幕 y 往上：角度要反過來
        float w = HaloX1 - HaloX0, h = HaloY1 - HaloY0;
        _haloRt.pivot = new Vector2((p.x - HaloX0) / w, 1f - (p.y - HaloY0) / h);
        _haloRt.sizeDelta = new Vector2(w * k.x, -h * k.y);
        _haloRt.anchoredPosition = o + new Vector2(q.x * k.x, q.y * k.y);
        _haloRt.localScale = new Vector3(sx, sy, 1f);
        _haloRt.localEulerAngles = new Vector3(0f, 0f, -rot);
    }

    private static float M(int i0, int i1, int c, float u)
    {
        return Mathf.LerpUnclamped(Motion[i0 * 6 + c], Motion[i1 * 6 + c], u);
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
        if (!hasRect || _halo == null) return;
        if (_hint <= 0f && !hover)
        {
            if (_halo.color.a > 0f) { Color g0 = _halo.color; g0.a = 0f; _halo.color = g0; Color l0 = _label.color; l0.a = 0f; _label.color = l0; }
            return;   // 沒顯示時不用每幀搬位置
        }

        PlaceHalo();
        Color g = HaloColor; g.a = HaloMaxAlpha * _hint; _halo.color = g;

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
        // ★1112 光暈是程式畫的貼圖：離開主選單時一起清掉
        if (_haloTex != null) Destroy(_haloTex);
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
