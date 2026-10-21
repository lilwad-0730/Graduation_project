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
/// ★1020 M：「場景中插畫的出現，如果不採用線稿的方式而是使用翻頁的方式」→ 多一種出現方式（style）：
///   ・Fade（原本）：線條畫直接淡入淡出。
///   ・PageTurn：背景裡一本攤開的書（兩頁骨白紙），她走近時翻過一頁，圖印在翻開的那一頁上；
///     走過之後再翻一頁蓋掉、書淡掉。圖一律印成深色線（水下、玻璃館的淺色線圖也是），紙是亮的，所以哪一關都看得清楚。
///   預設照 LineHintSign.DefaultStyle（LineHintDirector 依測試檔 hints=page 設定；沒寫就是 Fade）。
/// ★1020 radial：用距離（上下左右都算）決定出現，不管她走過了沒——水下往下游、從上面接近的地方用。
/// ★1020 hideWhen：條件成立就收起來（例如水下的通關巨石已經散掉）。
///
/// 由 LineHintDirector 自動生成；也可以手動在場景裡建空物件掛這支、填圖的路徑。
/// </summary>
public class LineHintSign : MonoBehaviour
{
    public enum Style { Fade, PageTurn }

    /// <summary>★1020 新生成的提示用哪一種出現方式（LineHintDirector 開場時依測試檔設定）。</summary>
    public static Style DefaultStyle = Style.Fade;

    [Header("圖")]
    [Tooltip("Resources 底下的路徑（不含 .png），例如 Hints/Tut_Move")]
    public string resourcePath = "Hints/Tut_Move";
    [Tooltip("圖在世界裡的寬度（公尺）。修毅的回頭圖示 820px＝8.5 公尺")]
    public float worldWidth = 11f;
    [Tooltip("跟修毅的圖示一樣 60：畫在背景之上；她是實心模型，站在圖前面會擋住圖")]
    public int sortingOrder = 60;
    [Tooltip("圖本身的顏色乘上這個（白＝原色）")]
    public Color tint = Color.white;

    [Header("★1020 出現方式")]
    public Style style = Style.Fade;
    [Tooltip("用距離（上下左右都算）決定出現：appearFar 公尺內開始出現、appearFull 公尺內全出現（不看她走過了沒）")]
    public bool radial = false;
    /// <summary>★1020 回傳 true 時收起來（程式設定，Inspector 看不到）。</summary>
    public System.Func<bool> hideWhen;

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

    [Header("★1020 翻頁（style＝PageTurn 時）")]
    [Tooltip("翻一頁要幾秒")]
    public float pageTurnSeconds = 0.75f;
    [Tooltip("紙的顏色")]
    public Color paperColor = new Color(0.955f, 0.935f, 0.89f, 1f);
    [Tooltip("印在紙上的線色")]
    public Color inkColor = new Color(0.157f, 0.133f, 0.196f, 1f);

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
    private float _drawW, _drawH; // 圖在世界裡的寬高

    // ── 翻頁用 ──
    private SpriteRenderer _pageL, _pageR, _artL, _artR, _spineL, _spineR, _edge, _shadow;
    private Transform _turn;
    private SpriteRenderer _turnFront, _turnFrontArt, _turnBack, _turnBackArt;
    private bool _revealed;       // 圖目前印在攤開的兩頁上
    private int _turnKind;        // 0＝沒在翻；1＝翻開（露出圖）；-1＝翻走（蓋掉圖）
    private float _turnU;
    private Color _artTint;

    private static Texture2D _glowTex, _paperTex, _spineTex, _whiteTex;

    /// <summary>在 pos 生一張線條畫提示（z 由呼叫端決定，要比她大）。</summary>
    public static LineHintSign Create(string name, string resourcePath, Vector3 pos, float worldWidth)
    {
        GameObject go = new GameObject(name);
        go.transform.position = pos;
        LineHintSign s = go.AddComponent<LineHintSign>();
        s.resourcePath = resourcePath;
        s.worldWidth = worldWidth;
        s.style = DefaultStyle;
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
        _drawW = worldWidth;
        _drawH = worldWidth * tex.height / Mathf.Max(1, tex.width);
        _basePos = transform.position;
        _phase = Random.Range(0f, 6.28f);

        if (style == Style.PageTurn) BuildBook(tex);
        else
        {
            float ppu = tex.width / Mathf.Max(0.1f, worldWidth);
            _sr = gameObject.AddComponent<SpriteRenderer>();
            _sr.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), ppu);
            _sr.sortingOrder = sortingOrder;
            _sr.color = new Color(tint.r, tint.g, tint.b, 0f);
        }

        if (glow)
        {
            GameObject go = new GameObject("Glow");
            go.transform.SetParent(transform, false);
            _glowSr = go.AddComponent<SpriteRenderer>();
            _glowSr.sprite = Sprite.Create(GlowTexture(), new Rect(0, 0, 128, 128), new Vector2(0.5f, 0.5f), 128);
            _glowSr.sortingOrder = style == Style.PageTurn ? sortingOrder - 9 : sortingOrder - 1;
            float gw = style == Style.PageTurn ? _drawW * 1.2f : _drawW;
            float gh = style == Style.PageTurn ? _drawH * 1.35f : _drawH;
            go.transform.localScale = new Vector3(gw * glowScale.x, gh * glowScale.y, 1f);
            _glowSr.color = new Color(glowColor.r, glowColor.g, glowColor.b, 0f);
        }
        if (logEvents)
            Debug.Log("【線條畫提示】★1019 " + name + "：" + resourcePath + "（寬 " + worldWidth.ToString("F1") + " 公尺" +
                      (style == Style.PageTurn ? "，翻頁" : "") + (radial ? "，依距離" : "") + "）放在 " + _basePos.ToString("F1"));
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
        if (_sr == null && _pageL == null) return;
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
            if (radial)
            {
                float d = new Vector2(dx, p.y - _basePos.y).magnitude;
                presenceTarget = 1f - Mathf.InverseLerp(Mathf.Abs(appearFull), Mathf.Abs(appearFar), d);
                litTarget = 1f - Mathf.InverseLerp(litRange, litRange + Mathf.Max(0.1f, litFalloff), d);
            }
            else if (dy <= verticalRange)
            {
                float inA = Mathf.InverseLerp(-Mathf.Abs(appearFar), -Mathf.Abs(appearFull), dx);
                float outA = 1f - Mathf.InverseLerp(fadeOutStart, fadeOutEnd, dx);
                presenceTarget = Mathf.Clamp01(Mathf.Min(inA, outA));
                litTarget = 1f - Mathf.InverseLerp(litRange, litRange + Mathf.Max(0.1f, litFalloff), Mathf.Abs(dx));
            }
        }
        if (hideWhen != null)
        {
            bool hide = false;
            try { hide = hideWhen(); } catch { hide = false; }
            if (hide) { presenceTarget = 0f; litTarget = 0f; }
        }

        float step = Time.deltaTime / Mathf.Max(0.05f, fadeSeconds);
        _presence = Mathf.MoveTowards(_presence, presenceTarget, step);
        _lit = Mathf.MoveTowards(_lit, litTarget, step);

        if (style == Style.PageTurn) UpdateBook(presenceTarget);
        else
        {
            float lineA = _presence * Mathf.Lerp(lineDim, 1f, _lit);
            _sr.color = new Color(tint.r, tint.g, tint.b, tint.a * lineA);
            _sr.enabled = lineA > 0.002f;
        }

        if (_glowSr != null)
        {
            float breathe = 0.7f + 0.3f * Mathf.Sin(Time.time * glowPulseSpeed + _phase);
            float shown = style == Style.PageTurn ? _presence * (_revealed || _turnKind != 0 ? 1f : 0.5f) : _presence;
            float ga = shown * Mathf.Lerp(glowIdle, glowLit, _lit) * breathe;
            _glowSr.color = new Color(glowColor.r, glowColor.g, glowColor.b, ga);
            _glowSr.enabled = ga > 0.002f;
        }

        float bob = style == Style.PageTurn ? 0f : bobAmplitude;
        transform.position = _basePos + new Vector3(0f, bob * Mathf.Sin(Time.time * bobSpeed + _phase), 0f);

        bool litNow = _lit > 0.6f;
        if (logEvents && litNow != _wasLit)
            Debug.Log("【線條畫提示】" + name + (litNow ? " 亮起（她走到圖前面）" : " 變暗"));
        _wasLit = litNow;
    }

    // ═══════════════════════════════ ★1020 翻頁 ═══════════════════════════════
    //   一本攤開的書：左頁、右頁（骨白紙），中間書脊一道淡淡的陰影，底下一圈頁緣與影子。
    //   圖切成左右兩半，各印在一頁上。翻開＝右邊一頁（空白）從右往左翻過來，翻過去的那一面印著圖的左半、
    //   底下的右頁印著圖的右半 → 翻完就是整張圖。翻走＝右頁（印著圖的右半）再翻過去，背面空白，蓋掉圖。

    private void BuildBook(Texture2D tex)
    {
        bool lightArt = resourcePath.EndsWith("_light");
        // 淺色線的圖（水下、玻璃館）印到紙上要變深色：顏色相乘，骨白 × 這個值 ≈ 深色線
        _artTint = lightArt ? new Color(inkColor.r / 0.91f, inkColor.g / 0.88f, inkColor.b / 0.82f, 1f) : Color.white;
        float W = _drawW, H = _drawH;
        float m = W * 0.06f;
        float pageW = W * 0.5f + m;
        float pageH = H * 1.22f;
        float ppu = tex.width / Mathf.Max(0.1f, W);

        Transform root = new GameObject("Book").transform;
        root.SetParent(transform, false);

        _shadow = MakeQuad(root, "Shadow", WhiteSprite(), new Vector3(0.12f, -0.14f, 0.02f), new Vector2(pageW * 2f + 0.25f, pageH + 0.25f), sortingOrder - 8);
        _edge = MakeQuad(root, "PageEdge", WhiteSprite(), new Vector3(0f, -0.07f, 0.01f), new Vector2(pageW * 2f + 0.12f, pageH + 0.12f), sortingOrder - 7);
        _pageL = MakeQuad(root, "PageL", PaperSprite(), new Vector3(-pageW * 0.5f, 0f, 0f), new Vector2(pageW, pageH), sortingOrder - 6);
        _pageR = MakeQuad(root, "PageR", PaperSprite(), new Vector3(pageW * 0.5f, 0f, 0f), new Vector2(pageW, pageH), sortingOrder - 6);
        _spineL = MakeQuad(root, "SpineL", SpineSprite(), new Vector3(-pageW * 0.09f, 0f, -0.001f), new Vector2(pageW * 0.18f, pageH), sortingOrder - 5);
        _spineR = MakeQuad(root, "SpineR", SpineSprite(), new Vector3(pageW * 0.09f, 0f, -0.001f), new Vector2(-pageW * 0.18f, pageH), sortingOrder - 5);

        Sprite left = Sprite.Create(tex, new Rect(0, 0, tex.width / 2, tex.height), new Vector2(0.5f, 0.5f), ppu);
        Sprite right = Sprite.Create(tex, new Rect(tex.width / 2, 0, tex.width - tex.width / 2, tex.height), new Vector2(0.5f, 0.5f), ppu);
        _artL = MakeSprite(root, "ArtL", left, new Vector3(-W * 0.25f, 0f, -0.002f), sortingOrder - 4);
        _artR = MakeSprite(root, "ArtR", right, new Vector3(W * 0.25f, 0f, -0.002f), sortingOrder - 4);

        // 翻動的那一頁：軸在書脊（x＝0），x 方向的縮放＝cos(角度)
        _turn = new GameObject("TurningPage").transform;
        _turn.SetParent(root, false);
        _turn.localPosition = new Vector3(0f, 0f, -0.004f);
        _turnFront = MakeQuad(_turn, "Front", PaperSprite(), new Vector3(pageW * 0.5f, 0f, 0f), new Vector2(pageW, pageH), sortingOrder - 3);
        _turnFrontArt = MakeSprite(_turn, "FrontArt", right, new Vector3(W * 0.25f, 0f, -0.001f), sortingOrder - 2);
        _turnBack = MakeQuad(_turn, "Back", PaperSprite(), new Vector3(pageW * 0.5f, 0f, 0f), new Vector2(-pageW, pageH), sortingOrder - 3);
        _turnBackArt = MakeSprite(_turn, "BackArt", left, new Vector3(W * 0.25f, 0f, -0.001f), sortingOrder - 2);
        _turnBackArt.transform.localScale = new Vector3(-1f, 1f, 1f);
        _turn.gameObject.SetActive(false);

        _revealed = false;
        _turnKind = 0;
        ApplyBookAlpha(0f);
    }

    private SpriteRenderer MakeQuad(Transform parent, string n, Sprite sp, Vector3 localPos, Vector2 size, int order)
    {
        SpriteRenderer r = MakeSprite(parent, n, sp, localPos, order);
        r.transform.localScale = new Vector3(size.x / sp.bounds.size.x, size.y / sp.bounds.size.y, 1f);
        return r;
    }

    private SpriteRenderer MakeSprite(Transform parent, string n, Sprite sp, Vector3 localPos, int order)
    {
        GameObject go = new GameObject(n);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        SpriteRenderer r = go.AddComponent<SpriteRenderer>();
        r.sprite = sp;
        r.sortingOrder = order;
        return r;
    }

    private void UpdateBook(float presenceTarget)
    {
        // 書本身：照出現程度淡入淡出
        float bookA = _presence;
        // 要不要翻開：出現夠多就翻開；離開（出現程度掉下去）就翻走。中間留一段不動，免得在邊界來回翻
        bool want = presenceTarget > 0.6f;
        bool unwant = presenceTarget < 0.35f;
        if (_turnKind == 0)
        {
            if (want && !_revealed && _presence > 0.45f) StartTurn(1);
            else if (unwant && _revealed) StartTurn(-1);
        }
        if (_turnKind != 0)
        {
            _turnU += Time.deltaTime / Mathf.Max(0.1f, pageTurnSeconds);
            float u = Mathf.Clamp01(_turnU);
            float e = u * u * (3f - 2f * u);
            float th = e * Mathf.PI;
            float c = Mathf.Cos(th);
            _turn.localScale = new Vector3(Mathf.Abs(c) < 0.02f ? 0.02f * Mathf.Sign(c == 0f ? 1f : c) : c, 1f + 0.05f * Mathf.Sin(th), 1f);
            bool front = c >= 0f;
            float shade = 0.70f + 0.30f * Mathf.Abs(c);
            _turnFront.enabled = front;
            _turnFrontArt.enabled = front && _turnKind < 0;
            _turnBack.enabled = !front;
            _turnBackArt.enabled = !front && _turnKind > 0;
            SetRgb(_turnFront, paperColor * shade);
            SetRgb(_turnBack, paperColor * shade);
            SetRgb(_turnFrontArt, _artTint * shade);
            SetRgb(_turnBackArt, _artTint * shade);
            if (u >= 1f) EndTurn();
        }
        ApplyBookAlpha(bookA);
    }

    private void StartTurn(int kind)
    {
        _turnKind = kind;
        _turnU = 0f;
        _turn.gameObject.SetActive(true);
        _turn.localScale = Vector3.one;
        if (kind > 0)
        {
            // 翻開：右頁先印上圖的右半（翻起來就看得到），左頁還空白，等翻過來的那一面（圖的左半）蓋上
            _artR.enabled = true;
            _artL.enabled = false;
        }
        else
        {
            // 翻走：右頁底下換成空白，翻動的那頁正面印著圖的右半，背面空白落到左頁上蓋掉圖的左半
            _artR.enabled = false;
            _artL.enabled = true;
        }
        if (logEvents) Debug.Log("【線條畫提示】" + name + (kind > 0 ? " 翻開一頁" : " 翻過一頁（收起來）"));
    }

    private void EndTurn()
    {
        if (_turnKind > 0) { _revealed = true; _artL.enabled = true; _artR.enabled = true; }
        else { _revealed = false; _artL.enabled = false; _artR.enabled = false; }
        _turnKind = 0;
        _turn.gameObject.SetActive(false);
    }

    private void ApplyBookAlpha(float a)
    {
        bool on = a > 0.002f;
        float lineA = Mathf.Lerp(lineDim, 1f, _lit);
        SetA(_shadow, new Color(0f, 0f, 0f, 0.22f), a, on);
        SetA(_edge, new Color(paperColor.r * 0.78f, paperColor.g * 0.76f, paperColor.b * 0.72f, 1f), a, on);
        SetA(_pageL, paperColor, a, on);
        SetA(_pageR, paperColor, a, on);
        SetA(_spineL, new Color(0.25f, 0.2f, 0.15f, 0.35f), a, on);
        SetA(_spineR, new Color(0.25f, 0.2f, 0.15f, 0.35f), a, on);
        SetA(_artL, _artTint, a * lineA, on && _artL.enabled);
        SetA(_artR, _artTint, a * lineA, on && _artR.enabled);
        if (_turnKind != 0)
        {
            SetAlphaOnly(_turnFront, a); SetAlphaOnly(_turnBack, a);
            SetAlphaOnly(_turnFrontArt, a * lineA); SetAlphaOnly(_turnBackArt, a * lineA);
        }
        if (!on && _turnKind == 0 && !_revealed) { _artL.enabled = false; _artR.enabled = false; }
    }

    private static void SetA(SpriteRenderer r, Color c, float a, bool visible)
    {
        if (r == null) return;
        r.color = new Color(c.r, c.g, c.b, c.a * a);
        if (r.gameObject.name != "ArtL" && r.gameObject.name != "ArtR") r.enabled = visible;
        else if (!visible) r.color = new Color(c.r, c.g, c.b, 0f);
    }

    private static void SetRgb(SpriteRenderer r, Color c)
    {
        if (r == null) return;
        r.color = new Color(c.r, c.g, c.b, r.color.a);
    }

    private static void SetAlphaOnly(SpriteRenderer r, float a)
    {
        if (r == null) return;
        Color c = r.color;
        r.color = new Color(c.r, c.g, c.b, a);
    }

    private static Sprite WhiteSprite()
    {
        if (_whiteTex == null)
        {
            _whiteTex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            Color[] px = new Color[16];
            for (int i = 0; i < 16; i++) px[i] = Color.white;
            _whiteTex.SetPixels(px);
            _whiteTex.Apply();
        }
        return Sprite.Create(_whiteTex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4);
    }

    /// <summary>紙：白底、極淡的紙紋、四邊稍暗（程式產生，顏色由 SpriteRenderer.color 決定）。</summary>
    private static Sprite PaperSprite()
    {
        if (_paperTex == null)
        {
            const int N = 128;
            _paperTex = new Texture2D(N, N, TextureFormat.RGBA32, false);
            _paperTex.wrapMode = TextureWrapMode.Clamp;
            System.Random rnd = new System.Random(20261020);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float ex = Mathf.Min(x, N - 1 - x) / (N * 0.5f), ey = Mathf.Min(y, N - 1 - y) / (N * 0.5f);
                    float edge = Mathf.Clamp01(Mathf.Min(ex, ey) * 9f);
                    float v = 0.93f + 0.07f * edge + ((float)rnd.NextDouble() - 0.5f) * 0.03f;
                    _paperTex.SetPixel(x, y, new Color(v, v, v, 1f));
                }
            _paperTex.Apply();
        }
        return Sprite.Create(_paperTex, new Rect(0, 0, 128, 128), new Vector2(0.5f, 0.5f), 128);
    }

    /// <summary>書脊的陰影：左邊最暗、往右淡掉（右頁用負的寬度翻過來）。</summary>
    private static Sprite SpineSprite()
    {
        if (_spineTex == null)
        {
            const int N = 64;
            _spineTex = new Texture2D(N, 4, TextureFormat.RGBA32, false);
            _spineTex.wrapMode = TextureWrapMode.Clamp;
            for (int x = 0; x < N; x++)
            {
                float t = 1f - x / (float)(N - 1);       // 右邊（書脊）最暗
                float a = Mathf.Pow(Mathf.Clamp01(t), 2.2f);
                for (int y = 0; y < 4; y++) _spineTex.SetPixel(N - 1 - x, y, new Color(1f, 1f, 1f, a));
            }
            _spineTex.Apply();
        }
        return Sprite.Create(_spineTex, new Rect(0, 0, 64, 4), new Vector2(0.5f, 0.5f), 64);
    }
}
