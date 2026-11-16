using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ★1116 B7 足跡：廢墟的地上，她走過會留下一行淡淡的鞋底壓痕，風慢慢把它們抹掉。
/// 回頭看得到「我走了多遠」，狼在後面追時，那串腳印也是她身後的一條線。
///
///  做法：
///   ・鏡頭是純側視，腳印不能平貼在地上（側看是一條線），所以做成貼在地面表面、略靠鏡頭一側的小暗橢圓，
///     側看像壓進沙裡的鞋印。左右腳交替，前後錯開一點點。
///   ・只讀 PlayerMovement 的位置與落地狀態，不改它。每走 stepDistance 公尺留一個；最多同時 maxPrints 個（超過就回收最老的）。
///   ・風抹掉：每個腳印活 lifeSeconds 秒，後段淡出；風大（WindGustSystem 有在吹）時淡得更快一點。
///   ・程式生成的柔邊貼圖，不用美術。純視覺，不碰碰撞、不碰物理。
///  只在廢墟開。關掉：RuinsFootprintFX.Enabled = false（或把場上這個物件的 enabled 取消勾選）。
/// </summary>
public class RuinsFootprintFX : MonoBehaviour
{
    public static bool Enabled = true;

    [Header("腳印")]
    [Tooltip("每走多遠（公尺）留一個腳印")]
    public float stepDistance = 0.7f;
    [Tooltip("同時最多幾個腳印（超過就回收最老的）")]
    public int maxPrints = 28;
    [Tooltip("腳印寬度（公尺）")]
    public float printWidth = 0.55f;
    [Tooltip("腳印高度（公尺）：側視下看到的厚度，越小越像壓痕")]
    public float printHeight = 0.12f;
    [Tooltip("壓痕顏色（含透明度）")]
    public Color printColor = new Color(0.22f, 0.17f, 0.12f, 0.42f);
    [Tooltip("腳印存活秒數（含淡出）")]
    public float lifeSeconds = 22f;
    [Tooltip("多少比例的時間在淡入（剛踩下去時先淡淡浮現，不要啪一下出現）")]
    [Range(0f, 0.3f)] public float fadeInFraction = 0.04f;
    [Tooltip("腳印比腳底靠鏡頭多少公尺（避免被地面吃掉）")]
    public float towardCamera = 0.9f;

    private class Print
    {
        public Transform tf;
        public SpriteRenderer sr;
        public float born = -999f;
        public bool alive;
    }

    private Print[] _pool;
    private PlayerMovement _pm;
    private Collider _col;
    private Sprite _sprite;
    private Texture2D _tex;
    private Vector3 _lastPos;
    private bool _hasLast;
    private float _acc;
    private bool _flip;
    private int _next;
    private float _nextFind;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        SceneManager.sceneLoaded -= OnLoaded;
        SceneManager.sceneLoaded += OnLoaded;
        Ensure(SceneManager.GetActiveScene());
    }

    private static void OnLoaded(Scene s, LoadSceneMode m) { Ensure(s); }

    private static void Ensure(Scene s)
    {
        if (!Enabled || !s.IsValid()) return;
        string n = s.name.ToLowerInvariant();
        if (!(n.Contains("ruin") || n.Contains("desert"))) return;   // 廢墟與荒原（沙地）
        if (FindFirstObjectByType<RuinsFootprintFX>() != null) return;
        new GameObject("[足跡]").AddComponent<RuinsFootprintFX>();
    }

    private void Awake()
    {
        _tex = new Texture2D(32, 16, TextureFormat.RGBA32, false);
        _tex.wrapMode = TextureWrapMode.Clamp;
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 32; x++)
            {
                float dx = ((x + 0.5f) / 32f - 0.5f) * 2f;
                float dy = ((y + 0.5f) / 16f - 0.5f) * 2f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - d);
                _tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * Mathf.Sqrt(a)));
            }
        _tex.Apply(false, true);
        _sprite = Sprite.Create(_tex, new Rect(0, 0, 32, 16), new Vector2(0.5f, 0.5f), 32f);
        _sprite.hideFlags = HideFlags.HideAndDontSave;

        int n = Mathf.Max(4, maxPrints);
        _pool = new Print[n];
        Shader sh = Shader.Find("Sprites/Default");
        for (int i = 0; i < n; i++)
        {
            var go = new GameObject("腳印");
            go.transform.SetParent(transform, false);
            go.hideFlags = HideFlags.DontSave;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = _sprite;
            sr.sortingOrder = -50;
            if (sh != null) sr.sharedMaterial = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            sr.enabled = false;
            _pool[i] = new Print { tf = go.transform, sr = sr };
        }
    }

    private void OnDestroy()
    {
        if (_pool != null)
            for (int i = 0; i < _pool.Length; i++)
                if (_pool[i] != null && _pool[i].sr != null && _pool[i].sr.sharedMaterial != null) Destroy(_pool[i].sr.sharedMaterial);
        if (_sprite != null) Destroy(_sprite);
        if (_tex != null) Destroy(_tex);
    }

    private void Update()
    {
        UpdateFade();
        if (!Enabled) return;

        if (_pm == null)
        {
            if (Time.unscaledTime < _nextFind) return;
            _nextFind = Time.unscaledTime + 1f;
            _pm = FindFirstObjectByType<PlayerMovement>();
            if (_pm == null) return;
            _col = _pm.GetComponent<Collider>();
            _hasLast = false;
        }

        Vector3 p = _pm.transform.position;
        if (!_hasLast || (p - _lastPos).sqrMagnitude > 25f)   // 傳送、重生：不要算成走了一大段
        {
            _lastPos = p;
            _hasLast = true;
            return;
        }

        float dx = Mathf.Abs(p.x - _lastPos.x);
        _lastPos = p;

        bool can = _pm.isGrounded && !_pm.isUnderwater && !_pm.IsControlLocked && Time.timeScale > 0.01f;
        if (!can) { _acc = 0f; return; }

        _acc += dx;
        if (_acc < stepDistance) return;
        _acc = 0f;
        PlacePrint();
    }

    private void PlacePrint()
    {
        if (_col == null) return;
        Bounds b = _col.bounds;
        // 從腳邊往下打，找腳底下的地面（忽略觸發區、玩家自己）
        // ★實測：荒原的地面是 z=0 上很薄的一片（厚度約 0.13），角色在 z=-0.3，只打角色那條 z 會整個射偏。
        //   所以同時試「角色的 z」與「z=0」兩條線，取最近的一個。
        float[] zs = { _pm.transform.position.z, 0f };
        float bestD = float.MaxValue;
        RaycastHit best = default;
        bool found = false;
        for (int zi = 0; zi < zs.Length; zi++)
        {
            Vector3 from = new Vector3(b.center.x, b.min.y + 0.6f, zs[zi]);
            RaycastHit[] hits = Physics.RaycastAll(from, Vector3.down, 2.5f, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits.Length; i++)
            {
                Collider c = hits[i].collider;
                if (c == null || c.transform.IsChildOf(_pm.transform)) continue;
                if (hits[i].normal.y < 0.6f) continue;   // 牆壁不算
                if (hits[i].distance < bestD) { bestD = hits[i].distance; best = hits[i]; found = true; }
            }
        }
        if (!found) return;

        Print pr = _pool[_next];
        _next = (_next + 1) % _pool.Length;

        _flip = !_flip;
        float face = Mathf.Sign(_pm.FacingDirection.x == 0f ? 1f : _pm.FacingDirection.x);
        float slope = Mathf.Atan2(best.normal.x, best.normal.y) * Mathf.Rad2Deg;   // 貼著坡面
        pr.tf.position = new Vector3(best.point.x - face * (_flip ? 0.12f : -0.12f), best.point.y + 0.03f, _pm.transform.position.z - towardCamera - (_flip ? 0.08f : 0f));
        pr.tf.rotation = Quaternion.Euler(0f, 0f, -slope);
        pr.tf.localScale = new Vector3(printWidth, printHeight, 1f) * (_flip ? 1f : 0.92f);
        pr.born = Time.time;
        pr.alive = true;
        pr.sr.color = new Color(printColor.r, printColor.g, printColor.b, 0f);
        pr.sr.enabled = true;
    }

    private void UpdateFade()
    {
        if (_pool == null) return;
        // 風大時淡得快一點（WindGustSystem 在吹）
        float speed = 1f;
        if (WindGustSystem.Instance != null && WindGustSystem.Instance.IsPushActive) speed = 3f;
        for (int i = 0; i < _pool.Length; i++)
        {
            Print pr = _pool[i];
            if (!pr.alive) continue;
            float u = (Time.time - pr.born) * speed / Mathf.Max(1f, lifeSeconds);
            if (u >= 1f) { pr.alive = false; pr.sr.enabled = false; continue; }
            float a;
            if (u < fadeInFraction) a = u / Mathf.Max(0.001f, fadeInFraction);
            else a = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 1f, u));
            Color c = printColor;
            c.a = printColor.a * a;
            pr.sr.color = c;
        }
    }
}
