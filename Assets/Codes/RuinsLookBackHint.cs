using UnityEngine;

/// <summary>
/// ★1018 廢墟「回頭看狼＝狼退後」（123 木頭人）的一次性教學提示：場景裡固定位置的一個圖示（兩格小圖：狼貼著追 → 回頭之後狼退後、距離拉大，加 A／← 鍵），
/// 不跟著玩家。她走近時淡入、走過之後慢慢淡出；只要她成功回頭看過狼（WolfEnemy.PlayerHasLookedBack）就立刻淡出、本輪不再出現。
///
/// 不用手動擺：進場景時如果有狼、而且找得到廢墟的光球路徑點 P12，會自動在落地點旁生一個。
/// 想自己擺位置：在場景裡建一個空物件掛這支腳本（名字隨意），自動生成就會跳過；位置就是圖示的位置。
/// 圖示檔：Assets/Resources/Hints/LookBackKey.png（想換圖直接覆蓋同名檔）。
/// </summary>
public class RuinsLookBackHint : MonoBehaviour
{
    [Header("出現／消失（以玩家 X 對圖示 X 的差來算，單位公尺）")]
    [Tooltip("玩家在圖示左邊這麼遠開始淡入")]
    public float fadeInFarDistance = 14f;
    [Tooltip("玩家在圖示左邊這麼近時完全顯示")]
    public float fadeInFullDistance = 6f;
    [Tooltip("玩家走過圖示右邊這麼遠開始淡出")]
    public float fadeOutStartDistance = 3f;
    [Tooltip("玩家走過圖示右邊這麼遠時完全消失")]
    public float fadeOutEndDistance = 11f;
    [Tooltip("透明度追上目標的時間（秒）；成功回頭後用這個時間淡出")]
    public float fadeSeconds = 0.6f;

    [Header("外觀")]
    [Tooltip("圖示在世界裡的寬度（公尺）")]
    public float worldWidth = 8.5f;
    [Tooltip("上下輕輕漂浮的幅度（公尺）")]
    public float bobAmplitude = 0.18f;
    [Tooltip("圖示往左輕推的幅度（公尺），暗示『往左回頭』")]
    public float nudgeAmplitude = 0.12f;
    public int sortingOrder = 60;

    [Header("發光提示")]
    [Tooltip("圖示後面的柔光：香檳金，跟光球同色系，會呼吸")]
    public bool glow = true;
    public Color glowColor = new Color(1f, 0.82f, 0.5f, 1f);
    [Tooltip("柔光的最大透明度")]
    [Range(0f, 1f)] public float glowStrength = 0.75f;
    [Tooltip("柔光比圖示大多少倍（寬、高各自的倍率）")]
    public Vector2 glowScale = new Vector2(1.15f, 1.9f);
    [Tooltip("呼吸速度")]
    public float glowPulseSpeed = 2.2f;

    [Header("自動生成位置（沒有手動擺時）")]
    [Tooltip("相對光球路徑點 P12 的偏移（X 負＝在 P12 左邊／落地點那邊，Y 正＝往上）")]
    public Vector2 offsetFromP12 = new Vector2(-4f, 2f);

    private SpriteRenderer _sr;
    private SpriteRenderer _glowSr;
    private Transform _player;
    private Vector3 _basePos;
    private float _alpha;
    private bool _finished;

    // ★1019 AfterSceneLoad 只在「遊戲啟動時載入的第一個場景」跑一次；從主選單開始、之後換場景進來的關卡不會再呼叫。
    //   棉花堡獨立成場景後，廢墟／荒原都是換場景進來的，所以要掛 sceneLoaded，每次載入新場景都檢查一次。
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoadedHook;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoadedHook;
        AutoSpawn();
    }

    private static void OnSceneLoadedHook(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        if (mode != UnityEngine.SceneManagement.LoadSceneMode.Single) return;
        AutoSpawn();
    }

    private static void AutoSpawn()
    {
        if (FindAnyObjectByType<RuinsLookBackHint>() != null) return;       // 已經有手動擺的
        if (FindAnyObjectByType<WolfEnemy>(FindObjectsInactive.Include) == null && FindAnyObjectByType<WolfSpawner>(FindObjectsInactive.Include) == null) return;   // 沒有狼的場景不用
        GuidanceLight orb = FindAnyObjectByType<GuidanceLight>();
        if (orb == null || orb.waypoints == null) return;
        Transform p12 = null;
        foreach (Transform wp in orb.waypoints) if (wp != null && wp.name == "P12") { p12 = wp; break; }
        if (p12 == null) return;

        GameObject go = new GameObject("RuinsLookBackHint (自動生成)");
        RuinsLookBackHint h = go.AddComponent<RuinsLookBackHint>();
        go.transform.position = new Vector3(p12.position.x + h.offsetFromP12.x, p12.position.y + h.offsetFromP12.y, p12.position.z);
    }

    private void Start()
    {
        Texture2D tex = Resources.Load<Texture2D>("Hints/LookBackKey");
        if (tex == null) { Debug.LogWarning("【回頭教學提示】找不到 Assets/Resources/Hints/LookBackKey.png，不顯示。"); enabled = false; return; }
        Sprite sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), tex.width / Mathf.Max(0.1f, worldWidth));
        _sr = gameObject.AddComponent<SpriteRenderer>();
        _sr.sprite = sprite;
        _sr.sortingOrder = sortingOrder;
        _sr.color = new Color(1f, 1f, 1f, 0f);
        _basePos = transform.position;
        _alpha = 0f;

        if (glow)
        {
            // 柔光：程式產生一張放射狀漸層，不用額外素材
            const int N = 128;
            Texture2D g = new Texture2D(N, N, TextureFormat.RGBA32, false);
            g.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                    float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    a = a * a * (3f - 2f * a);   // smoothstep：邊緣自然淡出
                    g.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            g.Apply();
            GameObject go = new GameObject("Glow");
            go.transform.SetParent(transform, false);
            _glowSr = go.AddComponent<SpriteRenderer>();
            _glowSr.sprite = Sprite.Create(g, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), N);
            _glowSr.sortingOrder = sortingOrder - 1;
            float h = worldWidth * tex.height / Mathf.Max(1, tex.width);
            go.transform.localScale = new Vector3(worldWidth * glowScale.x, h * glowScale.y, 1f);
            _glowSr.color = new Color(glowColor.r, glowColor.g, glowColor.b, 0f);
        }
    }

    private void Update()
    {
        if (_sr == null || _finished) return;
        if (_player == null)
        {
            PlayerMovement pm = FindAnyObjectByType<PlayerMovement>();
            if (pm != null) _player = pm.transform;
            if (_player == null) return;
        }

        float target;
        if (WolfEnemy.PlayerHasLookedBack)
        {
            target = 0f;   // 她學會了：淡出，之後不再出現
        }
        else
        {
            float dx = _player.position.x - _basePos.x;   // 負＝還在圖示左邊（還沒走到）
            float inA = Mathf.InverseLerp(-Mathf.Abs(fadeInFarDistance), -Mathf.Abs(fadeInFullDistance), dx);
            float outA = 1f - Mathf.InverseLerp(fadeOutStartDistance, fadeOutEndDistance, dx);
            target = Mathf.Clamp01(Mathf.Min(inA, outA));
        }

        _alpha = Mathf.MoveTowards(_alpha, target, Time.deltaTime / Mathf.Max(0.05f, fadeSeconds));
        float pulse = 0.88f + 0.12f * Mathf.Sin(Time.time * 3f);
        Color c = _sr.color; c.a = _alpha * pulse; _sr.color = c;
        if (_glowSr != null)
        {
            float breathe = 0.65f + 0.35f * Mathf.Sin(Time.time * glowPulseSpeed);
            Color gc = glowColor; gc.a = _alpha * glowStrength * breathe; _glowSr.color = gc;
        }

        float t = Time.time;
        transform.position = _basePos + new Vector3(-nudgeAmplitude * (0.5f + 0.5f * Mathf.Sin(t * 2.4f)), bobAmplitude * Mathf.Sin(t * 1.6f), 0f);

        if (WolfEnemy.PlayerHasLookedBack && _alpha <= 0.001f) { _finished = true; _sr.enabled = false; if (_glowSr != null) _glowSr.enabled = false; }
    }
}
