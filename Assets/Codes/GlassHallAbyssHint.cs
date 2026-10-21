using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ★1021 玻璃館最底下的「深淵」提示：只鋪在死亡區頂邊「以下」，往下漸漸變暗到全黑。死亡區以上完全不動。
///
/// 範圍怎麼算（試玩回饋 #11）：
///   ・起點＝死亡區碰撞框的最上緣（玩家碰到那條線就重生），這條線以上什麼都沒有。
///   ・長度＝玩家掉到死亡區重生的那一刻，鏡頭「畫面最底邊」能到的位置：
///       鏡頭中心 ≈ 玩家 Y ＋ 跟隨偏移（CinemachineFollow 的 Y 偏移 2.2），
///       畫面最底 ＝ 鏡頭中心 － 鏡頭半高（正交大小 13.5）。
///       再加一點「重生淡出時還會多掉一小段」的餘裕，剛好到那裡變成全黑。
///     所以掉下去的時候，畫面是「從原本的樣子，一路暗下去，到畫面最底剛好黑透」，不會突然一條黑邊。
///   ・顏色：頂邊是很淡的冷紫（跟玻璃館的藍紫夜空接得起來），往下慢慢轉黑。不發光。
///   ・碎玻璃只在這一段裡往下飄（越深越淡）。
/// 玻璃館是 3D 物理（死亡區是 3D BoxCollider），用 Collider 找，不是 Collider2D。
/// 全部執行時才生成，不改場景。要調整就改下面的數字。
/// </summary>
public class GlassHallAbyssHint : MonoBehaviour
{
    [Tooltip("要鋪在哪個死亡區下方（物件名稱）。")]
    public string zoneObjectName = "DeathZone_main";
    [Tooltip("鏡頭 Y 跟隨偏移（場景裡 CinemachineFollow 的 FollowOffset.y）。")]
    public float cameraFollowYOffset = 2.2f;
    [Tooltip("重生淡出的短時間內玩家還會再多掉的距離（米）。")]
    public float fallAllowance = 3f;
    [Tooltip("漸層最黑處的不透明度。")]
    [Range(0f, 1f)] public float maxAlpha = 0.95f;
    [Tooltip("頂邊的顏色：很淡的冷紫（接得起來的夜空色），往下轉黑。")]
    public Color topColor = new Color(0.38f, 0.26f, 0.70f, 1f);
    [Tooltip("漸層曲線：越大，上半段越淡、越往下越快變黑。")]
    public float curve = 0.85f;
    [Tooltip("圖層放在玩家後面幾米（z）。平台、玩家比它近，所以不會被蓋暗；背景比它遠，會被蓋暗。")]
    public float behindPlayer = 1.5f;
    [Tooltip("排序（Sorting Order）。蓋不到背景就調大，蓋到玩家就調小。")]
    public int sortingOrder = 40;

    [Header("碎玻璃（只在漸層這一段）")]
    public int shardCount = 22;
    public Color shardColor = new Color(0.62f, 0.70f, 0.90f, 1f);
    [Range(0f, 1f)] public float shardAlpha = 0.45f;
    public Vector2 shardFallSpeed = new Vector2(0.5f, 1.4f);
    public Vector2 shardSize = new Vector2(0.45f, 1.3f);
    [Tooltip("碎玻璃出現在鏡頭左右各幾米內。")]
    public float shardSpread = 22f;

    private Transform _cam;
    private Transform _gradient, _solid;
    private float _top;       // 死亡區頂邊 Y（漸層起點）
    private float _depth;     // 漸層長度
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
        _top = zone.bounds.max.y;
        PlayerMovement pm = FindAnyObjectByType<PlayerMovement>();
        _z = (pm != null ? pm.transform.position.z : 0f) + behindPlayer;
        Camera cam = Camera.main;
        if (cam != null) _cam = cam.transform;
        float halfH = cam != null && cam.orthographic ? cam.orthographicSize : 13.5f;

        // 玩家在死亡區頂邊時的鏡頭畫面最底邊，再多一點餘裕 → 漸層長度
        _depth = Mathf.Max(4f, halfH - cameraFollowYOffset + fallAllowance);

        const int H = 256;
        Texture2D tex = new Texture2D(2, H, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        Color black = new Color(0.005f, 0.003f, 0.012f, 1f);
        for (int y = 0; y < H; y++)
        {
            float t = 1f - y / (H - 1f);                  // 0＝頂邊（死亡區最上緣）、1＝最底
            float a = Mathf.Pow(t, curve);
            a = a * a * (3f - 2f * a);                    // smoothstep：頭尾都柔，頂邊沒有線
            Color col = Color.Lerp(topColor, black, Mathf.Pow(Mathf.Clamp01(t), 1.8f));
            col.a = a * maxAlpha;
            tex.SetPixel(0, y, col);
            tex.SetPixel(1, y, col);
        }
        tex.Apply();
        Sprite sp = Sprite.Create(tex, new Rect(0, 0, 2, H), new Vector2(0.5f, 1f), 1f);   // 軸心在上緣

        GameObject g = new GameObject("AbyssGradient");
        g.transform.SetParent(transform, false);
        _gradient = g.transform;
        g.transform.localScale = new Vector3(120f / 2f, _depth / H, 1f);
        g.transform.position = new Vector3(0f, _top, _z);
        SpriteRenderer sr = g.AddComponent<SpriteRenderer>();
        sr.sprite = sp;
        sr.sortingOrder = sortingOrder;

        // 漸層最底之後（鏡頭再也拍不到的地方）用同一種黑補滿，避免掉下去看到底
        Texture2D one = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        one.SetPixel(0, 0, new Color(black.r, black.g, black.b, maxAlpha));
        one.Apply();
        GameObject solid = new GameObject("AbyssSolid");
        solid.transform.SetParent(transform, false);
        solid.transform.localScale = new Vector3(120f, 200f, 1f);
        solid.transform.position = new Vector3(0f, _top - _depth, _z);
        SpriteRenderer ssr = solid.AddComponent<SpriteRenderer>();
        ssr.sprite = Sprite.Create(one, new Rect(0, 0, 1, 1), new Vector2(0.5f, 1f), 1f);
        ssr.sortingOrder = sortingOrder;
        _solid = solid.transform;

        BuildShards();
        Debug.Log($"【玻璃館深淵提示】死亡區頂邊 Y={_top:F1}：往下 {_depth:F1} 米漸黑（鏡頭半高 {halfH:F1}、跟隨偏移 {cameraFollowYOffset:F1}、餘裕 {fallAllowance:F1}），圖層 z={_z:F1}、排序 {sortingOrder}");
    }

    private void BuildShards()
    {
        const int N = 32;
        Texture2D t = new Texture2D(N, N, TextureFormat.RGBA32, false);
        t.wrapMode = TextureWrapMode.Clamp;
        for (int y = 0; y < N; y++)
        {
            for (int x = 0; x < N; x++)
            {
                float fy = (y + 0.5f) / N;
                float half = (1f - fy) * 0.5f;
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
            r.color = new Color(shardColor.r, shardColor.g, shardColor.b, 0f);
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
        float y = anywhere ? _top - Random.Range(0f, _depth) : _top;
        _shards[i].position = new Vector3(cx + Random.Range(-shardSpread, shardSpread), y, _z - 0.1f);
        _shards[i].rotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
    }

    private void LateUpdate()
    {
        if (_gradient == null) return;
        if (_cam == null && Camera.main != null) _cam = Camera.main.transform;
        float cx = _cam != null ? _cam.position.x : 0f;
        Vector3 gp = _gradient.position; gp.x = cx; _gradient.position = gp;
        if (_solid != null) { Vector3 q = _solid.position; q.x = cx; _solid.position = q; }

        if (_shards == null) return;
        float dt = Time.deltaTime;
        for (int i = 0; i < _shards.Length; i++)
        {
            Transform s = _shards[i];
            Vector3 p = s.position;
            p.y -= _speed[i] * dt;
            s.position = p;
            s.Rotate(0f, 0f, _spin[i] * dt);
            float depth01 = Mathf.Clamp01((_top - p.y) / _depth);          // 0＝頂邊、1＝最底
            float a = shardAlpha * _depthA[i] * Mathf.Clamp01(Mathf.Sin(depth01 * Mathf.PI) * 1.6f) * (1f - depth01 * 0.5f);
            _shardSr[i].color = new Color(shardColor.r, shardColor.g, shardColor.b, a);
            if (p.y < _top - _depth || Mathf.Abs(p.x - cx) > shardSpread + 6f) Respawn(i, false);
        }
    }
}
