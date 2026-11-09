using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ★1109 存檔點暖光：踩到新的存檔點時，腳邊「亮一下」——一團暖色柔光綻開，幾顆小光點往上飄，然後慢慢退掉。
///   意思是「這裡暫時安全了」，是整段緊張節奏裡難得的一口氣。
/// 觸發來源：PlayerRespawnSystem.OnCheckpointChanged（唯讀訂閱）。
/// 不亮的情況：場景剛開始 2.5 秒內（起點、續玩點的第一次通知）、離玩家太遠（程式強制改的）、功能被關掉。
/// 緊張時（狼在追、缺氧）亮度會壓低但不會完全消失；安心感高時（EmotionDirector.Calm）稍微亮一點。
/// 全部用程式生成的柔邊貼圖，沒有任何新美術。關掉：CheckpointWarmGlowFX.Enabled = false。
/// </summary>
public class CheckpointWarmGlowFX : MonoBehaviour
{
    public static bool Enabled = true;

    // 企劃可調
    public static Color Tint = new Color(1f, 0.8f, 0.48f, 1f);
    public static float HaloSize = 7f;        // 外圈柔光寬度（世界單位）
    public static float CoreSize = 2.4f;      // 中心亮核寬度
    public static float MaxAlpha = 0.55f;
    public static float BloomSeconds = 0.45f, FadeSeconds = 4.2f;
    public static int SortingOrder = 34;      // 疊層：低於暗角 40、缺氧 30 以外的畫面 UI 都在它之上（Canvas 本來就在世界物件之上）
    /// <summary>已經放過幾次（驗收／除錯用）。</summary>
    public static int PlayCount;

    private static float s_sceneStart;
    private static Sprite s_soft;
    private static Texture2D s_tex;
    private static Material s_mat;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        s_sceneStart = Time.unscaledTime;
        SceneManager.sceneLoaded -= OnLoaded;
        SceneManager.sceneLoaded += OnLoaded;
        PlayerRespawnSystem.OnCheckpointChanged -= OnCheckpoint;
        PlayerRespawnSystem.OnCheckpointChanged += OnCheckpoint;
    }

    private static void OnLoaded(Scene s, LoadSceneMode m) { s_sceneStart = Time.unscaledTime; }

    private static void OnCheckpoint(Vector3 pos)
    {
        if (!Enabled) return;
        if (Time.unscaledTime - s_sceneStart < 2.5f) return;            // 場景起點、續玩點的第一次通知
        GameObject p = GameObject.FindWithTag("Player");
        if (p == null) return;
        Vector3 pp = p.transform.position;
        if (Vector2.Distance(pos, pp) > 14f) return;                    // 離玩家很遠＝程式強制改的，不演
        PlayerRespawnSystem rs = PlayerRespawnSystem.Instance;
        if (rs != null && rs.IsRespawning) return;

        // 光放在「玩家腳邊」：存檔點座標有時在地板中間，放腳邊最自然
        Collider2D c2 = p.GetComponent<Collider2D>();
        Collider c3 = p.GetComponent<Collider>();
        float feetY = c2 != null ? c2.bounds.min.y : (c3 != null ? c3.bounds.min.y : pp.y - 0.5f);
        GameObject go = new GameObject("[存檔點暖光]");
        go.transform.position = new Vector3(pp.x, feetY + 0.15f, pp.z - 0.4f);
        go.AddComponent<CheckpointWarmGlowFX>();
        PlayCount++;
    }

    // ─────────────── 單次演出 ───────────────
    private struct Mote { public Transform t; public SpriteRenderer sr; public Vector2 v; public float delay, life, size; }
    private SpriteRenderer _halo, _core;
    private readonly List<Mote> _motes = new List<Mote>();
    private float _t;
    private float _gain;

    private void Awake()
    {
        EnsureAssets();
        _halo = MakeSr("光暈", HaloSize, 0.55f);
        _core = MakeSr("亮核", CoreSize, 1f);

        // 緊張時壓低、安心時稍亮；最低留 35%，不會完全看不到
        float calmBoost = 1f + 0.25f * EmotionDirector.Calm;
        _gain = Mathf.Clamp(Mathf.Lerp(0.35f, 1f, EmotionDirector.Quiet), 0.35f, 1f) * calmBoost;

        int n = 7;
        for (int i = 0; i < n; i++)
        {
            SpriteRenderer sr = MakeSr("光點" + i, 1f, 1f);
            float size = Random.Range(0.18f, 0.38f);
            sr.transform.localScale = Vector3.one * size;
            sr.transform.localPosition = new Vector3(Random.Range(-1.4f, 1.4f), Random.Range(0f, 0.5f), 0f);
            _motes.Add(new Mote
            {
                t = sr.transform, sr = sr,
                v = new Vector2(Random.Range(-0.12f, 0.12f), Random.Range(0.55f, 1.1f)),
                delay = Random.Range(0.05f, 0.9f), life = Random.Range(2.2f, 3.4f), size = size
            });
        }
    }

    private SpriteRenderer MakeSr(string name, float size, float a)
    {
        GameObject g = new GameObject(name);
        g.transform.SetParent(transform, false);
        SpriteRenderer sr = g.AddComponent<SpriteRenderer>();
        sr.sprite = s_soft;
        sr.sharedMaterial = s_mat;
        sr.sortingOrder = SortingOrder;
        sr.color = new Color(Tint.r, Tint.g, Tint.b, 0f);
        g.transform.localScale = new Vector3(size, size * 0.8f, 1f);
        return sr;
    }

    private void Update()
    {
        _t += Time.unscaledDeltaTime;
        float bloom = Mathf.SmoothStep(0f, 1f, _t / BloomSeconds);
        float fade = 1f - Mathf.SmoothStep(0f, 1f, (_t - BloomSeconds) / FadeSeconds);
        float k = bloom * fade * _gain;
        float breathe = 1f + 0.04f * Mathf.Sin(_t * 3f);

        if (_halo != null)
        {
            _halo.color = new Color(Tint.r, Tint.g, Tint.b, MaxAlpha * 0.55f * k);
            float s = HaloSize * Mathf.Lerp(0.55f, 1f, bloom) * breathe;
            _halo.transform.localScale = new Vector3(s, s * 0.8f, 1f);
        }
        if (_core != null)
        {
            _core.color = new Color(1f, 0.93f, 0.75f, MaxAlpha * k);
            float s = CoreSize * Mathf.Lerp(0.4f, 1f, bloom) * breathe;
            _core.transform.localScale = new Vector3(s, s * 0.8f, 1f);
        }
        for (int i = 0; i < _motes.Count; i++)
        {
            Mote m = _motes[i];
            if (m.t == null) continue;
            float lt = (_t - m.delay) / m.life;
            if (lt < 0f || lt > 1f) { m.sr.color = new Color(1f, 0.92f, 0.7f, 0f); continue; }
            m.t.localPosition += new Vector3(m.v.x, m.v.y, 0f) * Time.unscaledDeltaTime;
            float a = Mathf.Sin(lt * Mathf.PI);                       // 慢慢浮現、慢慢消失
            m.sr.color = new Color(1f, 0.92f, 0.7f, a * 0.85f * _gain);
        }
        if (_t > BloomSeconds + FadeSeconds + 0.3f) Destroy(gameObject);
    }

    // ─────────────── 共用資源（程式生成的柔邊圓） ───────────────
    private static void EnsureAssets()
    {
        if (s_soft != null) return;
        const int N = 64;
        s_tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
        s_tex.wrapMode = TextureWrapMode.Clamp;
        s_tex.filterMode = FilterMode.Bilinear;
        float c = (N - 1) * 0.5f;
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float r = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                float a = Mathf.Clamp01(1f - r);
                a = a * a * a * (a * (a * 6f - 15f) + 10f);        // 比 smoothstep 更柔的邊
                s_tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        s_tex.Apply();
        s_soft = Sprite.Create(s_tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), N);   // 寬 1 單位
        Shader sh = Shader.Find("Sprites/Default");
        if (sh != null) s_mat = new Material(sh) { name = "Mat_CheckpointGlow (runtime)" };
    }
}
