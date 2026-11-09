using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Reflection;

/// <summary>
/// ★1109 狼先有影子：狼還沒追她、也還在畫面外的時候，只要在附近，那一側的畫面邊緣會慢慢浮出一片淡淡的暗影，
/// 還飄著幾顆灰塵——「有東西在那邊」，但看不到、也還沒開始追。越近越濃。
/// 狼一進畫面、或開始追她，影子就淡出，交給原本的心跳暗角接手（不重複疊兩層黑邊）。
/// 只讀狼的狀態（反射），畫在 Canvas 疊層 36（暗角 40 之下、缺氧 30 之上）。
/// 全部程式生成的柔邊貼圖，沒有新美術。關掉：WolfShadowForewarnFX.Enabled = false。
/// </summary>
public class WolfShadowForewarnFX : MonoBehaviour
{
    public static bool Enabled = true;

    // 企劃可調
    public static float FadeOutRange = 16f;     // 狼離畫面邊緣多少公尺以外，影子完全沒有（剛好在邊緣外最濃）
    public static float MaxAlpha = 0.5f;
    public static float EdgeWidth01 = 0.24f;    // 影子佔畫面寬度的比例
    public static Color ShadowColor = new Color(0.03f, 0.02f, 0.05f, 1f);
    public static int SortingOrder = 36;
    /// <summary>左右兩側目前的濃度（驗收／除錯用）。</summary>
    public static float LeftLevel, RightLevel;

    private static FieldInfo s_chasing, s_attached;

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
        if (FindFirstObjectByType<WolfShadowForewarnFX>() != null) return;
        if (FindObjectsByType<WolfEnemy>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length == 0) return;
        new GameObject("[狼先有影子]").AddComponent<WolfShadowForewarnFX>();
    }

    private Image _left, _right;
    private Texture2D _tex, _dotTex;
    private Sprite _sprite, _dotSprite;
    private float _lv, _rv;
    private Camera _cam;
    private Transform _player;
    private float _nextScan;
    private WolfEnemy[] _wolves = new WolfEnemy[0];

    private struct Dust { public Image img; public float x, y, vx, vy, life, age, size; public bool left; }
    private Dust[] _dust;
    private RectTransform _canvasRt;

    private void Awake()
    {
        if (s_chasing == null)
        {
            s_chasing = typeof(WolfEnemy).GetField("isChasing", BindingFlags.NonPublic | BindingFlags.Instance);
            s_attached = typeof(WolfEnemy).GetField("isAttached", BindingFlags.NonPublic | BindingFlags.Instance);
        }
        BuildUi();
    }

    private void BuildUi()
    {
        GameObject cv = new GameObject("影子畫布");
        cv.transform.SetParent(transform, false);
        Canvas c = cv.AddComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = SortingOrder;
        _canvasRt = cv.GetComponent<RectTransform>();

        // 柔邊影子貼圖：由邊緣往內漸淡，中間高度最濃
        const int W = 64, H = 64;
        _tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        _tex.wrapMode = TextureWrapMode.Clamp;
        _tex.filterMode = FilterMode.Bilinear;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float u = x / (float)(W - 1);                  // 0＝畫面邊緣，1＝往內
                float v = y / (float)(H - 1);
                float a = Mathf.Pow(1f - u, 2.2f);
                a *= 0.4f + 0.6f * Mathf.Sin(v * Mathf.PI);
                _tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(a)));
            }
        _tex.Apply();
        _sprite = Sprite.Create(_tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f);

        _left = MakeEdge(cv.transform, "左影", true);
        _right = MakeEdge(cv.transform, "右影", false);

        // 灰塵：柔邊小點
        const int D = 16;
        _dotTex = new Texture2D(D, D, TextureFormat.RGBA32, false);
        _dotTex.wrapMode = TextureWrapMode.Clamp;
        float ctr = (D - 1) * 0.5f;
        for (int y = 0; y < D; y++)
            for (int x = 0; x < D; x++)
            {
                float r = Mathf.Sqrt((x - ctr) * (x - ctr) + (y - ctr) * (y - ctr)) / ctr;
                float a = Mathf.Clamp01(1f - r); a = a * a * (3f - 2f * a);
                _dotTex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        _dotTex.Apply();
        _dotSprite = Sprite.Create(_dotTex, new Rect(0, 0, D, D), new Vector2(0.5f, 0.5f), 100f);

        _dust = new Dust[14];
        for (int i = 0; i < _dust.Length; i++)
        {
            GameObject g = new GameObject("灰塵" + i);
            g.transform.SetParent(cv.transform, false);
            Image im = g.AddComponent<Image>();
            im.sprite = _dotSprite;
            im.raycastTarget = false;
            im.color = new Color(0.55f, 0.52f, 0.5f, 0f);
            _dust[i].img = im;
            _dust[i].left = i % 2 == 0;
            _dust[i].age = Random.value * 5f; _dust[i].life = 0f;   // 先錯開
        }
    }

    private Image MakeEdge(Transform parent, string name, bool left)
    {
        GameObject g = new GameObject(name);
        g.transform.SetParent(parent, false);
        Image im = g.AddComponent<Image>();
        im.sprite = _sprite;
        im.raycastTarget = false;
        im.color = new Color(ShadowColor.r, ShadowColor.g, ShadowColor.b, 0f);
        RectTransform rt = im.rectTransform;
        rt.anchorMin = new Vector2(left ? 0f : 1f - EdgeWidth01, 0f);
        rt.anchorMax = new Vector2(left ? EdgeWidth01 : 1f, 1f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        if (!left) rt.localScale = new Vector3(-1f, 1f, 1f);      // 右邊把貼圖翻過來，邊緣在右
        return im;
    }

    private void Update()
    {
        if (!Enabled) { SetAlpha(0f, 0f); return; }
        float dt = Time.unscaledDeltaTime;
        if (_cam == null) _cam = Camera.main;
        if (_player == null) { GameObject p = GameObject.FindWithTag("Player"); if (p != null) _player = p.transform; }
        if (_cam == null || _player == null) return;

        if (Time.unscaledTime >= _nextScan)
        {
            _nextScan = Time.unscaledTime + 0.25f;
            _wolves = FindObjectsByType<WolfEnemy>(FindObjectsSortMode.None);
        }

        float lt = 0f, rt = 0f;
        for (int i = 0; i < _wolves.Length; i++)
        {
            WolfEnemy w = _wolves[i];
            if (w == null || !w.isActiveAndEnabled) continue;
            bool chasing = s_chasing != null && (bool)s_chasing.GetValue(w);
            bool attached = s_attached != null && (bool)s_attached.GetValue(w);
            if (chasing || attached) continue;                       // 開始追了：交給心跳暗角

            Vector3 wp = w.transform.position;
            float dx = wp.x - _player.position.x;
            float dy = Mathf.Abs(wp.y - _player.position.y);
            if (dy > 10f) continue;                                  // 不在同一層高度：感覺不到
            Vector3 vp = _cam.WorldToViewportPoint(wp);
            bool offscreenSide = vp.x < 0f || vp.x > 1f;             // 在畫面外（左或右）才有影子
            if (!offscreenSide) continue;
            // 以「離畫面邊緣多遠」算：剛好在邊緣外最濃，往外 FadeOutRange 公尺漸淡到沒有
            // （這台鏡頭半個畫面寬約 25 公尺，比狼的發現距離 22 還大：狼會先進畫面才開始追，所以不能用發現距離）
            float halfW = Mathf.Abs(_cam.ViewportToWorldPoint(new Vector3(1f, 0.5f, vp.z)).x - _cam.ViewportToWorldPoint(new Vector3(0.5f, 0.5f, vp.z)).x);
            float outside = Mathf.Max(0f, Mathf.Abs(wp.x - _cam.transform.position.x) - halfW);
            float t = 1f - Mathf.InverseLerp(0f, FadeOutRange, outside);
            t = t * t * (3f - 2f * t);
            if (vp.x < 0.5f) lt = Mathf.Max(lt, t); else rt = Mathf.Max(rt, t);
        }

        // 已經有別的恐懼在畫面上時（狼追著、缺氧），影子退讓，不再疊黑
        float quiet = EmotionDirector.Enabled ? Mathf.Clamp01(1f - 1.6f * EmotionDirector.Tension) : 1f;
        lt *= quiet; rt *= quiet;

        _lv = Mathf.MoveTowards(_lv, lt, dt * (lt > _lv ? 0.5f : 1.0f));   // 慢慢浮出來、退得快一點
        _rv = Mathf.MoveTowards(_rv, rt, dt * (rt > _rv ? 0.5f : 1.0f));
        SetAlpha(_lv, _rv);
        UpdateDust(dt);
    }

    private void SetAlpha(float l, float r)
    {
        LeftLevel = l; RightLevel = r;
        if (_left == null) return;
        _left.color = new Color(ShadowColor.r, ShadowColor.g, ShadowColor.b, MaxAlpha * l);
        _right.color = new Color(ShadowColor.r, ShadowColor.g, ShadowColor.b, MaxAlpha * r);
        _left.enabled = l > 0.003f;
        _right.enabled = r > 0.003f;
    }

    private void UpdateDust(float dt)
    {
        float W = Screen.width, H = Screen.height;
        for (int i = 0; i < _dust.Length; i++)
        {
            Dust d = _dust[i];
            float level = d.left ? _lv : _rv;
            d.age += dt;
            if (d.age >= d.life)
            {
                if (level < 0.05f) { d.img.color = new Color(0.55f, 0.52f, 0.5f, 0f); d.age = 0f; d.life = 0.0001f; _dust[i] = d; continue; }   // 影子不在時不出新灰塵
                d.age = 0f; d.life = Random.Range(2.5f, 4.5f);
                d.size = Random.Range(0.006f, 0.014f) * H;
                d.x = (d.left ? Random.Range(0.0f, 0.1f) : Random.Range(0.9f, 1.0f)) * W;
                d.y = Random.Range(0.1f, 0.75f) * H;
                d.vx = (d.left ? 1f : -1f) * Random.Range(0.012f, 0.03f) * W;      // 往畫面內慢慢飄
                d.vy = Random.Range(-0.01f, 0.012f) * H;
                d.img.rectTransform.sizeDelta = new Vector2(d.size, d.size);
            }
            d.x += d.vx * dt; d.y += d.vy * dt;
            float k = Mathf.Sin(Mathf.Clamp01(d.age / d.life) * Mathf.PI);
            d.img.rectTransform.anchorMin = d.img.rectTransform.anchorMax = Vector2.zero;
            d.img.rectTransform.anchoredPosition = new Vector2(d.x, d.y);
            d.img.color = new Color(0.58f, 0.55f, 0.52f, 0.5f * k * Mathf.Clamp01(level * 1.5f));
            _dust[i] = d;
        }
    }

    private void OnDestroy()
    {
        LeftLevel = RightLevel = 0f;
        if (_tex != null) Destroy(_tex);
        if (_sprite != null) Destroy(_sprite);
        if (_dotTex != null) Destroy(_dotTex);
        if (_dotSprite != null) Destroy(_dotSprite);
    }
}
