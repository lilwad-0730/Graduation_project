using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ★1016 荒原進場守護（程式自動掛上，不用改場景；換到荒原以外的場景不會出現）。
///
/// 實玩時發現：一進荒原（還在看命運之書 I 頁、還沒操作）就開始起風，
/// 被動石化兩次、再被風一路推到最左邊，掉出地面死掉；重生後一樣。新玩家「剛進場、還沒搞懂」就會先死一次。
///
/// 1. 一進荒原、或在荒原重生完：先不起風（沒有前兆、沒有推力），等她開始左右移動，
///    再給 calmAfterMoveSeconds 秒，才開始原本的「停 → 吹」循環（之後完全照 WindGustSystem 原本的節奏）。
/// 2. 起點左邊的地面盡頭（再往左就沒有地、是 DeathZone）加一道看不見的牆，風再大也不會把她吹下去。
///
/// 兩項都可以在這個物件（★1016_DesertStartGuard）的 Inspector 關掉，或把 enableInBuild 改成 false 整個不裝。
/// </summary>
public class DesertStartGuard : MonoBehaviour
{
    /// <summary>總開關：false＝不自動安裝（整個功能關掉）。</summary>
    public static bool enableInBuild = true;

    [Header("進場／重生後先不起風")]
    [Tooltip("打開＝進荒原、在荒原重生完之後先不起風，等她開始左右移動才開始風的循環")]
    public bool calmUntilPlayerMoves = true;

    [Tooltip("她開始移動之後，再保持幾秒無風（之後才進入原本的「停 3.5 秒 → 吹」循環）")]
    public float calmAfterMoveSeconds = 2.5f;

    [Header("起點左邊的隱形牆")]
    [Tooltip("打開＝在起點左邊地面的盡頭加一道看不見的牆")]
    public bool addLeftWall = true;

    [Tooltip("用來找「最左邊那塊地面」的名稱開頭")]
    public string groundNamePrefix = "loopable ground";

    [Tooltip("牆的右側面離地面左緣往內幾公尺（她的身體約 2 公尺寬，留在地面上）")]
    public float wallInsetFromGroundEdge = 1.2f;

    [Tooltip("找不到地面時，牆的右側面放在這個 X")]
    public float fallbackWallRightX = -15.6f;

    private bool _waiting = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Install(SceneManager.GetActiveScene());
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Install(scene);
    }

    private static void Install(Scene scene)
    {
        if (!enableInBuild || !scene.IsValid()) return;
        string n = scene.name.ToLower();
        if (!(n.Contains("desert") || n.Contains("荒原") || n.Contains("荒漠"))) return;
        if (FindAnyObjectByType<DesertStartGuard>() != null) return;

        GameObject go = new GameObject("★1016_DesertStartGuard");
        SceneManager.MoveGameObjectToScene(go, scene);
        go.AddComponent<DesertStartGuard>();
        Debug.Log("【荒原進場守護】★1016 已安裝：進場／重生後等她開始走才起風；起點左邊加隱形牆。");
    }

    private void Start()
    {
        if (addLeftWall) CreateLeftWall();
    }

    private void CreateLeftWall()
    {
        float rightX = fallbackWallRightX;
        float yBottom = -12f;
        bool found = false;

        // 找名稱開頭是 groundNamePrefix、而且是實體（非觸發）的碰撞體裡，最左邊的那一塊
        float minX = float.PositiveInfinity;
        foreach (Collider c in FindObjectsByType<Collider>())
        {
            if (c == null || c.isTrigger || !c.enabled) continue;
            if (!c.gameObject.name.StartsWith(groundNamePrefix, System.StringComparison.OrdinalIgnoreCase)) continue;
            if (c.bounds.min.x < minX)
            {
                minX = c.bounds.min.x;
                yBottom = c.bounds.min.y;
                found = true;
            }
        }
        if (found) rightX = minX + wallInsetFromGroundEdge;

        float yTop = yBottom + 45f;   // 遠高於她跳得到的高度
        GameObject wall = new GameObject("★1016_DesertLeftWall");
        SceneManager.MoveGameObjectToScene(wall, gameObject.scene);
        wall.transform.position = new Vector3(rightX - 0.5f, (yBottom + yTop) * 0.5f, 0f);
        BoxCollider bc = wall.AddComponent<BoxCollider>();
        bc.size = new Vector3(1f, yTop - yBottom, 40f);

        Debug.Log($"【荒原進場守護】★1016 起點左邊隱形牆：右側面 x={rightX:F2}（{(found ? "地面左緣 " + minX.ToString("F2") + " 往內 " + wallInsetFromGroundEdge : "找不到地面，用預設值")}）");
    }

    private void Update()
    {
        if (!calmUntilPlayerMoves) return;
        WindGustSystem wind = WindGustSystem.Instance;
        if (wind == null || wind.IsStoppedForever) return;

        bool respawning = PlayerRespawnSystem.IsAnyRespawning;
        if (respawning) _waiting = true;   // 重生中：重生完再等她動一次
        if (!_waiting) return;

        wind.HoldCalm(0.25f);   // 每幀續住：計時凍在 0，沒有前兆、沒有推力

        if (respawning || PlayerMovement.IsHardCutsceneLocked) return;
        if (PlayerPressedMove())
        {
            _waiting = false;
            wind.HoldCalm(Mathf.Max(0f, calmAfterMoveSeconds));
            Debug.Log($"【荒原進場守護】她開始走了，{calmAfterMoveSeconds:F1} 秒後開始風的循環。");
        }
    }

    private static bool PlayerPressedMove()
    {
        return Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.D)
            || Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.RightArrow)
            || Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0.1f;
    }
}
