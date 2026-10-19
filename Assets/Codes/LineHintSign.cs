using UnityEngine;

/// <summary>
/// ★1019 M：「（教學字、文字卡）看能不能放到背景去，參考修毅設計的圖案表示進行內容的優化，
///          不要白底，我要去背，玩家經過亮起，離開後的時候黯淡消失」
///
/// 背景裡的一張線條畫提示，做法跟修毅的廢墟回頭圖示（RuinsLookBackHint）、荒原說明圖（DesertGuideHint）一樣：
///   ・圖是透明底的線條畫（Assets/Resources/Hints/*.png），固定擺在場景裡，不跟著玩家。
///   ・擺在她後面（z 比她大）：她走過的時候是她擋住圖，不是圖擋住她。
///   ・她走近：圖淡入，後面的香檳金柔光一起亮；走到圖前面最亮。
///   ・她走過之後：柔光先暗下去，圖再慢慢淡掉、消失。往回走會再亮回來。
///   ・上下差太多（例如她在上層城堡、圖在下層）就不出現。
///
/// 由 LineHintDirector 自動生成；也可以手動在場景裡建空物件掛這支、填圖的路徑。
/// </summary>
public class LineHintSign : MonoBehaviour
{
    [Header("圖")]
    [Tooltip("Resources 底下的路徑（不含 .png），例如 Hints/Tut_Move")]
    public string resourcePath = "Hints/Tut_Move";
    [Tooltip("圖在世界裡的寬度（公尺）。修毅的回頭圖示 820px＝8.5 公尺")]
    public float worldWidth = 11f;
    [Tooltip("跟修毅的圖示一樣 60：畫在背景之上；她是實心模型，站在圖前面會擋住圖")]
    public int sortingOrder = 60;
    [Tooltip("圖本身的顏色乘上這個（白＝原色）")]
    public Color tint = Color.white;

    [Header("出現／亮起／消失（玩家 X − 圖中心 X，公尺）")]
    [Tooltip("她在圖左邊這麼遠開始淡入")]
    public float appearFar = 17f;
    [Tooltip("她在圖左邊這麼近時完全出現")]
    public float appearFull = 8f;
    [Tooltip("她離圖中心這麼近（左右都算）時柔光最亮")]
    public float litRange = 4.5f;
    [Tooltip("柔光從最亮到最暗要再走幾公尺")]
    public float litFalloff = 7f;
    [Tooltip("她走過圖中心右邊這麼遠，圖開始變淡")]
    public float fadeOutStart = 5f;
    [Tooltip("她走過圖中心右邊這麼遠，圖完全消失")]
    public float fadeOutEnd = 14f;
    [Tooltip("她跟圖上下差超過這麼多公尺就不出現（不同層）")]
    public float verticalRange = 10f;
    [Tooltip("透明度追上目標的時間（秒）")]
    public float fadeSeconds = 0.6f;
    [Range(0f, 1f), Tooltip("還沒走到、或走過之後，線本身暗到多少（乘在出現程度上）")]
    public float lineDim = 0.7f;

    [Header("柔光（香檳金，跟光球同色系）")]
    public bool glow = true;
    public Color glowColor = new Color(1f, 0.82f, 0.5f, 1f);
    [Range(0f, 1f), Tooltip("還沒走到時的柔光")]
    public float glowIdle = 0.22f;
    [Range(0f, 1f), Tooltip("走到圖前面時的柔光")]
    public float glowLit = 0.75f;
    [Tooltip("柔光比圖大多少倍（寬、高）")]
    public Vector2 glowScale = new Vector2(1.15f, 1.9f);
    public float glowPulseSpeed = 2.2f;

    [Header("漂浮")]
    public float bobAmplitude = 0.12f;
    public float bobSpeed = 1.6f;

    public bool logEvents = true;

    private SpriteRenderer _sr;
    private SpriteRenderer _glowSr;
    private Transform _player;
    private float _nextPlayerLookup;
    private Vector3 _basePos;
    private float _presence;      // 0～1：圖出現的程度
    private float _lit;           // 0～1：亮的程度
    private float _phase;
    private bool _wasLit;

    private static Texture2D _glowTex;

    /// <summary>在 pos 生一張線條畫提示（z 由呼叫端決定，要比她大）。</summary>
    public static LineHintSign Create(string name, string resourcePath, Vector3 pos, float worldWidth)
    {
        GameObject go = new GameObject(name);
        go.transform.position = pos;
        LineHintSign s = go.AddComponent<LineHintSign>();
        s.resourcePath = resourcePath;
        s.worldWidth = worldWidth;
        return s;
    }

    private void Start()
    {
        Texture2D tex = Resources.Load<Texture2D>(resourcePath);
        if (tex == null)
        {
            Debug.LogWarning("【線條畫提示】找不到 Assets/Resources/" + resourcePath + ".png，不顯示。");
            enabled = false;
            return;
        }
        float ppu = tex.width / Mathf.Max(0.1f, worldWidth);
        _sr = gameObject.AddComponent<SpriteRenderer>();
        _sr.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), ppu);
        _sr.sortingOrder = sortingOrder;
        _sr.color = new Color(tint.r, tint.g, tint.b, 0f);
        _basePos = transform.position;
        _phase = Random.Range(0f, 6.28f);

        if (glow)
        {
            GameObject go = new GameObject("Glow");
            go.transform.SetParent(transform, false);
            _glowSr = go.AddComponent<SpriteRenderer>();
            _glowSr.sprite = Sprite.Create(GlowTexture(), new Rect(0, 0, 128, 128), new Vector2(0.5f, 0.5f), 128);
            _glowSr.sortingOrder = sortingOrder - 1;
            float h = worldWidth * tex.height / Mathf.Max(1, tex.width);
            go.transform.localScale = new Vector3(worldWidth * glowScale.x, h * glowScale.y, 1f);
            _glowSr.color = new Color(glowColor.r, glowColor.g, glowColor.b, 0f);
        }
        if (logEvents)
            Debug.Log("【線條畫提示】★1019 " + name + "：" + resourcePath + "（寬 " + worldWidth.ToString("F1") + " 公尺）放在 " + _basePos.ToString("F1"));
    }

    /// <summary>放射狀柔光：程式產生，不用額外素材（跟修毅的圖示同一種）。</summary>
    private static Texture2D GlowTexture()
    {
        if (_glowTex != null) return _glowTex;
        const int N = 128;
        _glowTex = new Texture2D(N, N, TextureFormat.RGBA32, false);
        _glowTex.wrapMode = TextureWrapMode.Clamp;
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                a = a * a * (3f - 2f * a);
                _glowTex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        _glowTex.Apply();
        return _glowTex;
    }

    private void Update()
    {
        if (_sr == null) return;
        if (_player == null && Time.unscaledTime >= _nextPlayerLookup)
        {
            _nextPlayerLookup = Time.unscaledTime + 0.5f;
            PlayerMovement pm = FindAnyObjectByType<PlayerMovement>();
            if (pm != null) _player = pm.transform;
        }

        float presenceTarget = 0f, litTarget = 0f;
        if (_player != null)
        {
            Vector3 p = _player.position;
            float dx = p.x - _basePos.x;                       // 負＝還沒走到
            float dy = Mathf.Abs(p.y - _basePos.y);
            if (dy <= verticalRange)
            {
                float inA = Mathf.InverseLerp(-Mathf.Abs(appearFar), -Mathf.Abs(appearFull), dx);
                float outA = 1f - Mathf.InverseLerp(fadeOutStart, fadeOutEnd, dx);
                presenceTarget = Mathf.Clamp01(Mathf.Min(inA, outA));
                litTarget = 1f - Mathf.InverseLerp(litRange, litRange + Mathf.Max(0.1f, litFalloff), Mathf.Abs(dx));
            }
        }

        float step = Time.deltaTime / Mathf.Max(0.05f, fadeSeconds);
        _presence = Mathf.MoveTowards(_presence, presenceTarget, step);
        _lit = Mathf.MoveTowards(_lit, litTarget, step);

        float lineA = _presence * Mathf.Lerp(lineDim, 1f, _lit);
        _sr.color = new Color(tint.r, tint.g, tint.b, tint.a * lineA);
        _sr.enabled = lineA > 0.002f;

        if (_glowSr != null)
        {
            float breathe = 0.7f + 0.3f * Mathf.Sin(Time.time * glowPulseSpeed + _phase);
            float ga = _presence * Mathf.Lerp(glowIdle, glowLit, _lit) * breathe;
            _glowSr.color = new Color(glowColor.r, glowColor.g, glowColor.b, ga);
            _glowSr.enabled = ga > 0.002f;
        }

        transform.position = _basePos + new Vector3(0f, bobAmplitude * Mathf.Sin(Time.time * bobSpeed + _phase), 0f);

        bool litNow = _lit > 0.6f;
        if (logEvents && litNow != _wasLit)
            Debug.Log("【線條畫提示】" + name + (litNow ? " 亮起（她走到圖前面）" : " 變暗"));
        _wasLit = litNow;
    }
}
