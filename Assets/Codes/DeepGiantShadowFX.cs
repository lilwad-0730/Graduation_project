using UnityEngine;

/// <summary>
/// ★1107 水下遠景巨大影子：很遠、很慢、看不清全貌的巨大輪廓從深處經過，不攻擊、沒有碰撞。
///   只在她潛得夠深的時候才會出現（淺處完全不出現），一次橫過整個畫面約 55 秒，
///   淡進淡出像從黑暗裡浮現又沉回去。畫在最後面，前面的石頭會把它擋住。
///   位置只跟著鏡頭走一部分（視差），所以鏡頭移動時它看起來在很遠的地方。
/// 由 EmotionAtmosphereFX 在水下場景建立；關掉：DeepGiantShadowFX.Enabled = false。
/// </summary>
public class DeepGiantShadowFX : MonoBehaviour
{
    public static bool Enabled = true;

    [Header("出現條件（玩家 Y）")]
    [Tooltip("比這個淺：完全不出現")]
    public float startY = -22f;
    [Tooltip("到這個深度：濃度達到最大")]
    public float fullY = -80f;

    [Header("節奏")]
    [Tooltip("第一次出現：潛到夠深之後再等幾秒")]
    public float firstDelay = 30f;
    [Tooltip("之後每次間隔（最短; 最長，只在夠深時計時）")]
    public Vector2 interval = new Vector2(60f, 110f);
    [Tooltip("橫過畫面花幾秒")]
    public float crossSeconds = 55f;

    [Header("外觀")]
    [Tooltip("身長（公尺）")]
    public float length = 34f;
    public Color color = new Color(0.02f, 0.06f, 0.12f, 1f);
    [Tooltip("最濃時的不透明度")]
    [Range(0f, 0.6f)] public float maxAlpha = 0.24f;
    [Tooltip("跟著鏡頭的比例（1＝黏在畫面上，越小越像固定在遠處的世界）")]
    [Range(0f, 1f)] public float follow = 0.85f;

    private SpriteRenderer _sr;
    private Texture2D _tex;
    private Sprite _sprite;
    private Material _mat;
    private Transform _player;
    private Camera _cam;

    private float _depth = -1f;
    private float _timer;
    private bool _active;
    private float _t, _dir, _yOff, _anchorY, _seed;

    private void Start()
    {
        _timer = firstDelay;
        BuildSprite();
    }

    private void BuildSprite()
    {
        const int W = 192, H = 64;
        _tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        _tex.wrapMode = TextureWrapMode.Clamp;
        _tex.filterMode = FilterMode.Bilinear;
        float[] a = new float[W * H];
        for (int py = 0; py < H; py++)
        {
            float y = (py / (float)(H - 1) - 0.5f);           // -0.5～0.5（高度，換算成「相對寬度」前的比例）
            for (int px = 0; px < W; px++)
            {
                float x = px / (float)(W - 1);                 // 0＝尾 1＝頭
                float yy = y * (H / (float)W);                 // 換成跟身長同單位的高度（圖寬＝1 單位）
                float hBody = BodyHalfHeight(x);
                float hFluke = FlukeHalfHeight(x);
                float hFin = 0f;
                float dy = yy;
                float d = Mathf.Max(hBody - Mathf.Abs(dy) , 0f);
                float dBody = (dy > 0f) ? (hBody * 0.88f - dy) : (hBody - (-dy));   // 背高一點點低、肚子圓一點
                float dFluke = hFluke - Mathf.Abs(dy);
                if (x > 0.5f && x < 0.7f && dy < 0f)                                 // 胸鰭：往下垂的一片
                {
                    float u = (x - 0.5f) / 0.2f;
                    hFin = 0.07f * Mathf.Sin(u * Mathf.PI);
                    float dFin = (hBody + hFin) - (-dy);
                    d = Mathf.Max(d, dFin);
                }
                d = Mathf.Max(d, Mathf.Max(dBody, dFluke));
                a[py * W + px] = Mathf.Clamp01(d / 0.012f);
            }
        }
        // 3×3 模糊兩次：邊緣糊掉，看起來是遠處水裡的東西
        for (int pass = 0; pass < 2; pass++)
        {
            float[] b = new float[a.Length];
            for (int py = 0; py < H; py++)
                for (int px = 0; px < W; px++)
                {
                    float s = 0f; int c = 0;
                    for (int oy = -1; oy <= 1; oy++)
                        for (int ox = -1; ox <= 1; ox++)
                        {
                            int qx = px + ox, qy = py + oy;
                            if (qx < 0 || qx >= W || qy < 0 || qy >= H) continue;
                            s += a[qy * W + qx]; c++;
                        }
                    b[py * W + px] = s / c;
                }
            a = b;
        }
        Color[] px32 = new Color[a.Length];
        for (int i = 0; i < a.Length; i++) px32[i] = new Color(1f, 1f, 1f, a[i]);
        _tex.SetPixels(px32);
        _tex.Apply();
        _sprite = Sprite.Create(_tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), W);   // 寬剛好 1 單位，用 localScale 放大到身長

        GameObject go = new GameObject("巨影");
        go.transform.SetParent(transform, false);
        _sr = go.AddComponent<SpriteRenderer>();
        _sr.sprite = _sprite;
        Shader sh = Shader.Find("Sprites/Default");
        if (sh != null) { _mat = new Material(sh) { name = "Mat_DeepGiantShadow (runtime)" }; _sr.sharedMaterial = _mat; }
        _sr.sortingOrder = 16;     // 水色濾鏡（water）是 15，畫在它上面才不會被整片水色蓋掉；Z 在最遠處，前面的石頭與主角（不透明）照樣擋住它
        _sr.color = new Color(color.r, color.g, color.b, 0f);
        _sr.enabled = false;
    }

    // 鯨形輪廓（x：0＝尾、1＝頭；回傳半高，單位＝身長）
    private static float BodyHalfHeight(float x)
    {
        if (x < 0.2f)
        {
            // 尾柄：由細慢慢變粗
            return Mathf.Lerp(0.012f, 0.048f, Mathf.SmoothStep(0f, 1f, x / 0.2f));
        }
        float u = (x - 0.2f) / 0.8f;                       // 0～1（身體到頭）
        float body = Mathf.Sin(Mathf.Pow(u, 0.62f) * Mathf.PI);   // 頭尖處回到 0
        return Mathf.Lerp(0.048f, 0.115f, Mathf.SmoothStep(0f, 0.5f, u)) * Mathf.Pow(Mathf.Max(body, 0f), 0.55f);
    }

    // 尾鰭：兩片橫向的橢圓
    private static float FlukeHalfHeight(float x)
    {
        float u = (x - 0.045f) / 0.055f;
        if (u < -1f || u > 1f) return 0f;
        return 0.1f * Mathf.Sqrt(1f - u * u);
    }

    private void Update()
    {
        if (_sr == null) return;
        if (_cam == null) _cam = Camera.main;
        if (_player == null)
        {
            GameObject p = GameObject.FindWithTag("Player");
            if (p != null) _player = p.transform;
        }
        if (_cam == null || _player == null) return;

        float target = Mathf.InverseLerp(startY, fullY, _player.position.y);
        _depth = _depth < 0f ? target : Mathf.Lerp(_depth, target, 1f - Mathf.Exp(-1.2f * Time.unscaledDeltaTime));

        if (!Enabled) { _sr.enabled = false; _active = false; return; }

        if (!_active)
        {
            _sr.enabled = false;
            if (_depth > 0.15f)                // 夠深才倒數
            {
                _timer -= Time.deltaTime;
                if (_timer <= 0f) StartPass();
            }
            return;
        }

        _t += Time.deltaTime / Mathf.Max(5f, crossSeconds);
        if (_t >= 1f) { _active = false; _timer = Random.Range(interval.x, interval.y); _sr.enabled = false; return; }

        Vector3 c = _cam.transform.position;
        float viewH = _cam.orthographic ? _cam.orthographicSize * 2f : 14f;
        float viewW = viewH * _cam.aspect;
        float travel = viewW * 0.5f + length * 0.5f + 2f;
        float xRel = Mathf.Lerp(-travel, travel, Mathf.SmoothStep(0f, 1f, _t * 0.5f + 0.5f * _t)) * _dir;
        float x = c.x * follow + xRel;
        float yBase = c.y;                  // 上下跟著鏡頭（潛得很深時它不會飄出畫面），只有左右有視差
        float y = yBase + _yOff + Mathf.Sin(Time.time * 0.35f + _seed) * 0.8f;
        transform.position = new Vector3(x, y, 40f);

        Transform m = _sr.transform;
        m.localScale = new Vector3(length * _dir, length, 1f);
        m.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * 0.25f + _seed) * 2.5f * _dir);

        float fade = HeartbeatPulse.Smooth(0f, 0.22f, _t) * HeartbeatPulse.Smooth(1f, 0.78f, _t);
        float a = maxAlpha * fade * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_depth * 1.4f));
        _sr.color = new Color(color.r, color.g, color.b, a);
        _sr.enabled = a > 0.002f;
    }

    private void StartPass()
    {
        _active = true;
        _t = 0f;
        _dir = Random.value < 0.5f ? 1f : -1f;
        _yOff = Random.Range(-2.5f, 3f);
        _anchorY = _cam != null ? _cam.transform.position.y : 0f;
        _seed = Random.Range(0f, 100f);
    }

    private void OnDestroy()
    {
        if (_tex != null) Destroy(_tex);
        if (_sprite != null) Destroy(_sprite);
        if (_mat != null) Destroy(_mat);
    }
}
