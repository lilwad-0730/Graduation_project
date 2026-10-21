using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ★1021 玻璃館最底下的「深淵」提示：不畫一條黑邊，而是讓整個世界越往下越暗、越深，並有碎玻璃往下飄。
///
/// 為什麼這樣做（試玩回饋 #11＋這關的核心：碎裂的鏡子、掉進去就什麼都沒有）：
///   ・一條突然出現的黑邊很像「這裡是死亡區」的遊戲標記，會把人拉出情境。
///   ・改成「長長的暗度漸層」：從死亡區上方約 40 米開始慢慢變暗，到死亡區附近幾乎全黑——像光被鏡子吃掉，沒有邊界線。
///   ・再加幾片緩緩往下飄的碎玻璃（冷灰色、半透明、越深越淡），玩家看到東西一直往下掉，直覺就懂「下面是掉下去的地方」。
///   ・完全不發光；顏色近黑偏紫，跟玻璃館原本的暗一致。
///
/// 玻璃館是 3D 物理（死亡區是 3D BoxCollider），所以用 Collider 找，不是 Collider2D。
/// 全部執行時才生成，不改場景。要調整就改下面的數字（或在場景裡手動掛這支）。
/// </summary>
public class GlassHallAbyssHint : MonoBehaviour
{
    [Tooltip("要鋪在哪個死亡區上方（物件名稱）。")]
    public string zoneObjectName = "DeathZone_main";
    [Tooltip("漸層從死亡區往上延伸幾米（越長越沒有邊界感）。")]
    public float fadeHeight = 40f;
    [Tooltip("最暗處的不透明度（越接近 1 越黑）。")]
    [Range(0f, 1f)] public float maxAlpha = 0.92f;
    [Tooltip("漸層顏色：近黑偏紫，不要亮。")]
    public Color tint = new Color(0.03f, 0.02f, 0.07f, 1f);
    [Tooltip("漸層的彎曲程度：越大，上半段越淡、越靠近底部才快速變暗。")]
    public float curve = 1.8f;
    [Tooltip("圖層放在玩家後面幾米（z）。平台、玩家比它近，所以不會被蓋暗；背景比它遠，會被蓋暗。")]
    public float behindPlayer = 1.5f;
    [Tooltip("排序（Sorting Order）。蓋不到背景就調大，蓋到玩家就調小。")]
    public int sortingOrder = 40;

    [Header("碎玻璃")]
    public int shardCount = 16;
    public Color shardColor = new Color(0.55f, 0.6f, 0.72f, 1f);
    [Range(0f, 0.5f)] public float shardAlpha = 0.22f;
    public Vector2 shardFallSpeed = new Vector2(0.5f, 1.4f);
    public Vector2 shardSize = new Vector2(0.25f, 0.7f);
    [Tooltip("碎玻璃出現在鏡頭左右各幾米內。")]
    public float shardSpread = 22f;

    private Transform _cam;
    private Transform _gradient;
    private float _zoneY;
    private float _z;
    private Transform[] _shards;
    private SpriteRenderer[] _shardSr;
    private float[] _speed, _spin, _depthA;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        SceneManager.sceneLoaded -= OnSceneLoadedHook;
        SceneManager.sceneLoaded += OnSceneLoadedHook;
        Build(SceneManager.GetActiveScene());
    }

    private static void OnSceneLoadedHook(Scene s, LoadSceneMode m)
    {
        Build(s);
    }

    private static void Build(Scene s)
    {
        if (!s.name.ToLowerInvariant().Contains("glass")) return;
        if (FindAnyObjectByType<GlassHallAbyssHint>() != null) return;
        GameObject go = new GameObject("GlassHallAbyssHint");
        SceneManager.MoveGameObjectToScene(go, s);
        go.AddComponent<GlassHallAbyssHint>();
    }

    private void Start()
    {
        Collider zone = null;
        foreach (Collider c in FindObjectsByType<Collider>(FindObjectsInactive.Include))
        {
            if (c.gameObject.name == zoneObjectName) { zone = c; break; }
        }
        if (zone == null)
        {
            Debug.LogWarning($"【玻璃館深淵提示】找不到死亡區「{zoneObjectName}」（3D Collider），不生成。");
            enabled = false;
            return;
        }
        _zoneY = zone.bounds.center.y;
        PlayerMovement pm = FindAnyObjectByType<PlayerMovement>();
        _z = (pm != null ? pm.transform.position.z : 0f) + behindPlayer;
        if (Camera.main != null) _cam = Camera.main.transform;

        // ── 長漸層：上面全透明、下面最黑，用平滑曲線沒有邊 ──
        const int H = 256;
        Texture2D tex = new Texture2D(2, H, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        for (int y = 0; y < H; y++)
        {
            float t = y / (H - 1f);                       // 0＝最下，1＝最上
            float a = Mathf.Pow(1f - t, curve);
            a = a * a * (3f - 2f * a);                    // smoothstep，頭尾都柔
            Color c = new Color(tint.r, tint.g, tint.b, a * maxAlpha);
            tex.SetPixel(0, y, c);
            tex.SetPixel(1, y, c);
        }
        tex.Apply();
        Sprite sp = Sprite.Create(tex, new Rect(0, 0, 2, H), new Vector2(0.5f, 0f), 1f);

        GameObject g = new GameObject("AbyssGradient");
        g.transform.SetParent(transform, false);
        _gradient = g.transform;
        float below = 40f;                                // 死亡區下面再鋪一段純黑
        // 高度＝fadeHeight＋below；寬度先給 120 米，之後每幀跟著鏡頭 X 走（不會看到左右邊）
        g.transform.localScale = new Vector3(120f / 2f, (fadeHeight + below) / H, 1f);
        g.transform.position = new Vector3(0f, _zoneY - below, _z);
        SpriteRenderer sr = g.AddComponent<SpriteRenderer>();
        sr.sprite = sp;
        sr.sortingOrder = sortingOrder;

        Texture2D one = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        one.SetPixel(0, 0, new Color(tint.r, tint.g, tint.b, maxAlpha));
        one.Apply();
        GameObject solid = new GameObject("AbyssSolid");
        solid.transform.SetParent(transform, false);
        solid.transform.localScale = new Vector3(120f, 200f, 1f);
        solid.transform.position = new Vector3(0f, _zoneY - below, _z);
        SpriteRenderer ssr = solid.AddComponent<SpriteRenderer>();
        ssr.sprite = Sprite.Create(one, new Rect(0, 0, 1, 1), new Vector2(0.5f, 1f), 1f);
        ssr.sortingOrder = sortingOrder;

        BuildShards();
        Debug.Log($"【玻璃館深淵提示】死亡區 Y={_zoneY:F1}：往上 {fadeHeight:F0} 米漸層、碎玻璃 {shardCount} 片、圖層 z={_z:F1}、排序 {sortingOrder}");
    }

    private void BuildShards()
    {
        // 一張三角碎片貼圖
        const int N = 32;
        Texture2D t = new Texture2D(N, N, TextureFormat.RGBA32, false);
        t.wrapMode = TextureWrapMode.Clamp;
        for (int y = 0; y < N; y++)
        {
            for (int x = 0; x < N; x++)
            {
                float fy = (y + 0.5f) / N;
                float half = (1f - fy) * 0.5f;            // 上窄下寬的三角
                float dx = Mathf.Abs((x + 0.5f) / N - 0.5f);
                float edge = Mathf.Clamp01((half - dx) * N * 0.5f);
                t.SetPixel(x, y, new Color(1f, 1f, 1f, edge));
            }
        }
        t.Apply();
        Sprite sp = Sprite.Create(t, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), N);

        int n = Mathf.Max(0, shardCount);
        _shards = new Transform[n];
        _shardSr = new SpriteRenderer[n];
        _speed = new float[n];
        _spin = new float[n];
        _depthA = new float[n];
        for (int i = 0; i < n; i++)
        {
            GameObject s = new GameObject("Shard" + i);
            s.transform.SetParent(transform, false);
            float sz = Random.Range(shardSize.x, shardSize.y);
            s.transform.localScale = new Vector3(sz * Random.Range(0.6f, 1.2f), sz, 1f);
            SpriteRenderer r = s.AddComponent<SpriteRenderer>();
            r.sprite = sp;
            r.sortingOrder = sortingOrder + 1;
            _shards[i] = s.transform;
            _shardSr[i] = r;
            _speed[i] = Random.Range(shardFallSpeed.x, shardFallSpeed.y);
            _spin[i] = Random.Range(-40f, 40f);
            _depthA[i] = Random.Range(0.5f, 1f);
            Respawn(i, true);
        }
    }

    private void Respawn(int i, bool anywhere)
    {
        float cx = _cam != null ? _cam.position.x : 0f;
        float y = anywhere ? _zoneY + Random.Range(0f, fadeHeight) : _zoneY + fadeHeight;
        _shards[i].position = new Vector3(cx + Random.Range(-shardSpread, shardSpread), y, _z - 0.1f);
        _shards[i].rotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
    }

    private void LateUpdate()
    {
        if (_gradient == null) return;
        if (_cam == null && Camera.main != null) _cam = Camera.main.transform;
        float cx = _cam != null ? _cam.position.x : 0f;
        Vector3 gp = _gradient.position;
        gp.x = cx;
        _gradient.position = gp;
        Transform solid = transform.Find("AbyssSolid");
        if (solid != null) { Vector3 sp = solid.position; sp.x = cx; solid.position = sp; }

        if (_shards == null) return;
        float dt = Time.deltaTime;
        for (int i = 0; i < _shards.Length; i++)
        {
            Transform s = _shards[i];
            Vector3 p = s.position;
            p.y -= _speed[i] * dt;
            s.position = p;
            s.Rotate(0f, 0f, _spin[i] * dt);
            float depth = Mathf.Clamp01((p.y - _zoneY) / fadeHeight);      // 1＝很上面、0＝死亡區
            float a = shardAlpha * _depthA[i] * Mathf.Sin(depth * Mathf.PI);   // 兩頭都淡，中間最明顯
            _shardSr[i].color = new Color(shardColor.r, shardColor.g, shardColor.b, a);
            if (p.y < _zoneY - 2f || Mathf.Abs(p.x - cx) > shardSpread + 6f) Respawn(i, false);
        }
    }
}
