using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ★1116 玻璃館最後一根燭火前的「熄燈」（提案 A：黑暗中逼近）。
///
/// 她往最後一根燭火跑的路上，整座玻璃館一段一段暗下去（每暗一段：閃兩下、一聲玻璃輕響），
/// 最後幾乎全黑，只剩：
///   ・她身邊一小圈（看得到腳下的玻璃，不會因為看不見而掉下去）；
///   ・遠處最後一根燭台的微光（唯一的方向）；
///   ・身後書頭怪眼罩透出的紅光（ShadowMonsterEyeGlow，熄燈時調亮）。
/// 追逐音樂同時退後，讓怪物的腳步、呼吸更清楚。
/// 點亮最後一根（或漏掉、她死掉回到前面）就亮回來。
///
/// 做法：畫面最上層蓋一張黑幕（Screen Space Overlay，在所有介面底下），黑幕是一張小貼圖（192×108），
///   每幀在她、燭台、紅光的位置挖出柔邊的洞。不改場景、不改燈光。
/// 由 ShadowMonsterController.EnsureInitialized 自動掛上。
/// </summary>
[DisallowMultipleComponent]
public class GlassFinaleBlackout : MonoBehaviour
{
    public static bool Enabled = true;

    [Header("什麼時候暗")]
    // ★1118b 玻璃橋拉長 2.5 倍（41.5 → 約 104 公尺）：開始暗的距離、全暗的距離一起 ×2.5
    [Tooltip("離最後一根燭火還有幾公尺時開始暗")]
    public float startBefore = 104f;
    [Tooltip("離最後一根燭火還有幾公尺時全暗")]
    public float fullBefore = 35f;
    [Tooltip("分幾段暗下去（一盞一盞熄）")]
    public int steps = 5;
    [Range(0f, 1f)] public float maxDarkness = 0.96f;
    [Tooltip("每熄一段，黑暗花幾秒壓下來")]
    public float stepFadeSeconds = 0.35f;
    [Tooltip("每熄一段前閃幾下的時間（秒）")]
    public float flickerSeconds = 0.4f;
    [Tooltip("點亮最後一根後亮回來要幾秒")]
    public float restoreSeconds = 1.2f;

    [Header("黑暗裡看得到的地方（公尺）")]
    [Tooltip("她身邊亮的半徑（看得到腳下）")]
    public float playerRadius = 4.5f;
    public float playerSoft = 4f;
    [Range(0f, 1f)] public float playerReveal = 0.9f;
    [Tooltip("燭台微光的半徑")]
    public float candleRadius = 2.2f;
    public float candleSoft = 4.5f;
    [Range(0f, 1f)] public float candleReveal = 0.85f;
    [Tooltip("紅光周圍透出來的半徑（怪物原始大小時）。★1118e 紅光縮成眼球大小的微光，洞跟著縮小（原本 1.6／3）")]
    public float eyeRadius = 0.7f;
    public float eyeSoft = 1.4f;
    [Range(0f, 1f)] public float eyeReveal = 1f;

    [Header("聲音與紅光")]
    [Tooltip("全暗時追逐音樂剩幾成")]
    [Range(0f, 1f)] public float bgmDuck = 0.35f;
    [Tooltip("全暗時紅光亮幾倍（★1118e 微光，原本 1.8）")]
    public float eyeBoost = 1.35f;
    [Tooltip("每熄一段的聲音（留空＝Resources/Sfx/Glass_LightOut）")]
    public AudioClip lightOutSfx;
    [Range(0f, 1f)] public float lightOutVolume = 0.7f;

    private const int W = 192, H = 108;

    private ShadowMonsterController _ctrl;
    private ShadowMonsterEyeGlow _eyes;
    private Transform _player;
    private CandleCollectible _last;

    private Canvas _canvas;
    private RawImage _img;
    private Texture2D _tex;
    private Color32[] _px;

    private int _stepNow;          // 目前熄到第幾段
    private float _dark;           // 目前黑暗程度 0～1（不含閃爍）
    private float _flickerT = -1f; // 正在閃的計時
    private bool _restoring;

    private struct Hole { public Vector2 c; public float r, soft, reveal; }
    private readonly Hole[] _holes = new Hole[3];

    private void Start()
    {
        _ctrl = GetComponent<ShadowMonsterController>();
        _eyes = GetComponent<ShadowMonsterEyeGlow>();
        if (lightOutSfx == null) lightOutSfx = Resources.Load<AudioClip>("Sfx/Glass_LightOut");
    }

    private void Update()
    {
        if (_ctrl == null) return;
        if (_player == null) _player = _ctrl.player;
        if (_last == null) _last = _ctrl.LastCandle;
        if (_eyes == null) _eyes = GetComponent<ShadowMonsterEyeGlow>();

        float target = 0f;
        bool finished = _last != null && (_last.isCollected || _last.missed);
        if (Enabled && _player != null && _last != null && !finished && IsChasing())
        {
            float lx = _last.transform.position.x;
            float p01 = Mathf.InverseLerp(lx - startBefore, lx - fullBefore, _player.position.x);
            int stepCount = Mathf.Max(1, steps);
            int step = Mathf.Clamp(Mathf.FloorToInt(p01 * stepCount + 0.0001f), 0, stepCount);
            if (step > _stepNow)
            {
                _flickerT = 0f;   // 熄下一段：先閃一下
                if (lightOutSfx != null)
                {
                    if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(lightOutSfx, lightOutVolume);
                    else AudioSource.PlayClipAtPoint(lightOutSfx, _player.position, lightOutVolume);
                }
                Debug.Log($"【熄燈】★1116 第 {step}/{stepCount} 段熄了（她 x={_player.position.x:F1}，最後一根燭火 x={lx:F1}）");
            }
            _stepNow = step;
            target = (float)step / stepCount;
            _restoring = false;
        }
        else
        {
            if (finished && _dark > 0.01f && !_restoring) { _restoring = true; Debug.Log("【熄燈】★1116 最後一根燭火定案，燈亮回來"); }
            _stepNow = 0;
            _flickerT = -1f;
        }

        // 往目標靠：熄的時候照每段的速度壓下來，亮回來照 restoreSeconds
        float speed = target > _dark ? 1f / Mathf.Max(0.05f, stepFadeSeconds * Mathf.Max(1, steps)) : 1f / Mathf.Max(0.05f, restoreSeconds);
        if (_flickerT >= 0f && _flickerT < flickerSeconds) speed = 0f;   // 閃完才壓下來
        _dark = Mathf.MoveTowards(_dark, target, speed * Time.deltaTime);

        // 閃爍：前一段的亮度 ↔ 更暗，快速兩下
        float shown = _dark;
        if (_flickerT >= 0f)
        {
            _flickerT += Time.deltaTime;
            if (_flickerT < flickerSeconds)
            {
                float f = _flickerT / Mathf.Max(0.05f, flickerSeconds);
                bool off = (f > 0.15f && f < 0.35f) || (f > 0.6f && f < 0.8f);
                if (off) shown = Mathf.Min(1f, _dark + 0.25f);
            }
            else _flickerT = -1f;
        }

        float a = shown * maxDarkness;
        if (_eyes != null) _eyes.boost = Mathf.Lerp(1f, eyeBoost, _dark);
        _ctrl.externalBgmDuck = Mathf.Lerp(1f, bgmDuck, _dark);
        Render(a);
    }

    private bool IsChasing()
    {
        var s = _ctrl.currentState;
        return s == ShadowMonsterController.MonsterState.Chasing
            || s == ShadowMonsterController.MonsterState.Appearing
            || s == ShadowMonsterController.MonsterState.Punishing;
    }

    // ───────── 黑幕 ─────────
    private void Render(float alpha)
    {
        if (alpha <= 0.002f)
        {
            if (_canvas != null && _canvas.enabled) _canvas.enabled = false;
            return;
        }
        Camera cam = Camera.main;
        if (cam == null) return;
        EnsureOverlay();
        _canvas.enabled = true;

        int n = 0;
        if (_player != null) n = AddHole(n, cam, _player.position + Vector3.up * 0.8f, playerRadius, playerSoft, playerReveal);
        if (_last != null) n = AddHole(n, cam, _last.transform.position + Vector3.up * 1.5f, candleRadius, candleSoft, candleReveal);
        if (_eyes != null && _eyes.Visible)
            n = AddHole(n, cam, _eyes.AnchorWorld, eyeRadius * _eyes.ScaleFactor, eyeSoft * _eyes.ScaleFactor, eyeReveal);

        float sx = Screen.width / (float)W, sy = Screen.height / (float)H;
        byte full = (byte)(Mathf.Clamp01(alpha) * 255f);
        for (int y = 0; y < H; y++)
        {
            float py = (y + 0.5f) * sy;
            for (int x = 0; x < W; x++)
            {
                float px = (x + 0.5f) * sx;
                float keep = 1f;
                for (int i = 0; i < n; i++)
                {
                    float dx = px - _holes[i].c.x, dy = py - _holes[i].c.y;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float o = 1f - Mathf.Clamp01((d - _holes[i].r) / Mathf.Max(1f, _holes[i].soft));   // 洞裡 1、外面 0
                    o = o * o * (3f - 2f * o);
                    keep *= 1f - _holes[i].reveal * o;
                }
                _px[y * W + x].a = (byte)(full * keep);
            }
        }
        _tex.SetPixels32(_px);
        _tex.Apply(false, false);
    }

    private int AddHole(int n, Camera cam, Vector3 world, float radius, float soft, float reveal)
    {
        Vector3 c = cam.WorldToScreenPoint(world);
        if (c.z < 0f) return n;
        // 世界公尺 → 螢幕像素（同一個深度往右量）
        Vector3 e = cam.WorldToScreenPoint(world + cam.transform.right * radius);
        Vector3 e2 = cam.WorldToScreenPoint(world + cam.transform.right * (radius + soft));
        _holes[n] = new Hole { c = new Vector2(c.x, c.y), r = Mathf.Abs(e.x - c.x), soft = Mathf.Abs(e2.x - e.x), reveal = reveal };
        return n + 1;
    }

    private void EnsureOverlay()
    {
        if (_canvas != null) return;
        GameObject go = new GameObject("玻璃館_熄燈黑幕（自動生成）");
        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = -110;   // 在所有介面（提示、文字卡）和黑影的窒息暗角（-100）底下

        _img = new GameObject("黑幕").AddComponent<RawImage>();
        _img.transform.SetParent(go.transform, false);
        _img.raycastTarget = false;
        _img.color = new Color(0.008f, 0.006f, 0.018f, 1f);
        RectTransform r = _img.rectTransform;
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;

        _tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        _tex.wrapMode = TextureWrapMode.Clamp;
        _tex.filterMode = FilterMode.Bilinear;
        _px = new Color32[W * H];
        for (int i = 0; i < _px.Length; i++) _px[i] = new Color32(255, 255, 255, 0);
        _img.texture = _tex;
    }

    private void OnDisable()
    {
        if (_canvas != null) _canvas.enabled = false;
        if (_ctrl != null) _ctrl.externalBgmDuck = 1f;
        if (_eyes != null) _eyes.boost = 1f;
        _dark = 0f;
        _stepNow = 0;
    }

    private void OnDestroy()
    {
        if (_canvas != null) Destroy(_canvas.gameObject);
        if (_tex != null) Destroy(_tex);
    }
}
