using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// ★1010 風暴的畫面和聲音加強（1008 會議 荒原 c「風暴襲擊的效果並不明確，視覺和聽覺都要強化」；修毅清單待完成 8）。
///
/// 改動前：起風時只有風線、細沙粒子（DesertWindDustFX）和一條風聲，跟風停差別不大；
///         廢墟龍捲風掃過時畫面本身沒有變化。
/// 改動後（疊在原本的效果上，不改場景、不改 WindGustSystem／DesertWindDustFX）：
///   前兆（遠處沙塵線）：天色先暗一點，低沉風吼從小聲開始
///   起風那一下：沙色一閃、鏡頭一震、一聲呼嘯
///   吹風期間：沙霧罩住畫面、兩層風紋往風向掃、四邊變暗、鏡頭微震、低沉風吼＋壓低音高的原本風聲、風沙粒子加密
///   風停：不到一秒散掉
/// 荒原（有 WindGustSystem）自動生成，照風的狀態走；廢墟由 RuinsOrbDirector 叫風暴時呼叫 SetManual／Hit。
/// 聲音是程式合成的（棕噪音風吼、掃頻呼嘯），不用素材；音量走 AudioManager。
/// 整包關掉：StormBoostFX.Enabled = false。
/// </summary>
[DefaultExecutionOrder(99990)]   // 在 Cinemachine 算完鏡頭之後才加震動（跟 ScreenFeedbackManager 同一招）
[DisallowMultipleComponent]
public class StormBoostFX : MonoBehaviour
{
    public static bool Enabled = true;
    public static StormBoostFX Instance { get; private set; }

    public static readonly Color DesertDustColor = new Color(0.82f, 0.64f, 0.40f, 1f);
    public static readonly Color RuinsDustColor = new Color(0.60f, 0.57f, 0.53f, 1f);

    [Header("畫面")]
    [Range(0f, 1f)] public float hazeMaxAlpha = 0.24f;
    [Range(0f, 1f)] public float streakMaxAlpha = 0.38f;
    [Range(0f, 1f)] public float vignetteMaxAlpha = 0.5f;
    [Range(0f, 1f)] public float hitFlashAlpha = 0.3f;
    [Tooltip("風紋每秒捲過幾個畫面寬")]
    public float streakScroll = 1.4f;
    [Tooltip("吹風時風沙粒子（DesertWindDustFX）加密幾倍")]
    public float dustEmissionBoost = 1.8f;

    [Header("鏡頭震動（世界單位）")]
    public float shakeSustain = 0.06f;
    public float shakeHit = 0.3f;
    public float shakeHitSeconds = 0.5f;

    [Header("聲音")]
    [Range(0f, 1f)] public float roarVolume = 0.65f;
    [Tooltip("原本的風聲再疊一層、音高壓低（場景沒有風聲就略過）")]
    [Range(0f, 1f)] public float layerVolume = 0.45f;
    [Range(0f, 1f)] public float whooshVolume = 0.9f;

    [Header("時間")]
    [Tooltip("每秒升多少（0→1 約 0.25 秒）")]
    public float riseSpeed = 4f;
    [Tooltip("每秒降多少（1→0 約 0.8 秒）")]
    public float fallSpeed = 1.25f;
    [Tooltip("前兆期間最多到多少（0～1）")]
    [Range(0f, 1f)] public float telegraphLevel = 0.3f;
    public bool logEvents = true;

    private Canvas _canvas;
    private RawImage _haze, _streakA, _streakB, _vignette, _flash;
    private Texture2D _texStreak, _texVignette;
    private AudioSource _roar, _layer, _oneShot;
    private AudioClip _roarClip, _whooshClip;

    private float _level;
    private float _hitT = 99f, _hitStrength;
    private float _dir = -1f;
    private Color _color = DesertDustColor;
    private float _manualLevel, _manualDir = -1f, _manualUntil = -1f;
    private Color _manualColor = DesertDustColor;
    private bool _wasBlowing;
    private float _uvA, _uvB;

    private Camera _cam;
    private Vector3 _lastOffset, _lastCamPos;
    private bool _offsetApplied;

    private ParticleSystem[] _dust = new ParticleSystem[0];
    private float[] _dustBase = new float[0];
    private bool _dustCaptured;

    private const int SampleRate = 22050;

    // ── 自動生成（荒原）──────────────────────────────────────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        SceneManager.sceneLoaded -= OnLoaded;
        SceneManager.sceneLoaded += OnLoaded;
        TryInstallForWind();
    }

    private static void OnLoaded(Scene s, LoadSceneMode m) { TryInstallForWind(); }

    private static void TryInstallForWind()
    {
        if (!Enabled) return;
        string sn = SceneManager.GetActiveScene().name.ToLower();
        bool desert = sn.Contains("desert") || sn.Contains("荒漠") || sn.Contains("荒原");
        if (!desert && WindGustSystem.Instance == null && FindAnyObjectByType<WindGustSystem>() == null) return;
        Ensure();
    }

    public static StormBoostFX Ensure()
    {
        if (Instance != null) return Instance;
        StormBoostFX existing = FindAnyObjectByType<StormBoostFX>();
        if (existing != null) return existing;
        return new GameObject("[風暴加強 StormBoostFX]").AddComponent<StormBoostFX>();
    }

    // ── 對外 ─────────────────────────────────────────────────
    /// <summary>手動指定強度（廢墟龍捲風用）。seconds 內有效；期間取「自動」與「手動」較大的那個。windDir：+1＝往右吹。</summary>
    public void SetManual(float level, float windDir, Color color, float seconds = 0.3f)
    {
        _manualLevel = Mathf.Clamp01(level);
        _manualDir = windDir >= 0f ? 1f : -1f;
        _manualColor = color;
        _manualUntil = Time.time + Mathf.Max(0.05f, seconds);
    }

    /// <summary>風打到的那一下：沙色一閃、鏡頭一震、一聲呼嘯。</summary>
    public void Hit(float strength = 1f)
    {
        _hitT = 0f;
        _hitStrength = Mathf.Clamp01(strength);
        if (_oneShot != null && _whooshClip != null)
            _oneShot.PlayOneShot(_whooshClip, whooshVolume * _hitStrength * AudioManager.SfxVolume);
    }

    // ── 初始 ─────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        BuildOverlay();
        BuildAudio();
        if (logEvents) Debug.Log("【風暴加強】已生成（" + SceneManager.GetActiveScene().name + "）：起風時沙霧、風紋、鏡頭震、風吼、呼嘯");
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        RemoveCameraOffset();
        RestoreDust();
        if (_canvas != null) Destroy(_canvas.gameObject);
        if (_texStreak != null) Destroy(_texStreak);
        if (_texVignette != null) Destroy(_texVignette);
        if (_roarClip != null) Destroy(_roarClip);
        if (_whooshClip != null) Destroy(_whooshClip);
    }

    private void BuildOverlay()
    {
        GameObject c = new GameObject("[風暴加強 畫面]");
        c.transform.SetParent(transform, false);
        _canvas = c.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = -100;   // 疊在遊戲畫面上、所有 UI（文字卡、選單、黑幕）底下
        _canvas.enabled = false;

        _texStreak = MakeStreakTexture();
        _texVignette = MakeVignetteTexture();

        _haze = MakeLayer(c.transform, "沙霧", null);
        _streakA = MakeLayer(c.transform, "風紋粗", _texStreak);
        _streakB = MakeLayer(c.transform, "風紋細", _texStreak);
        _vignette = MakeLayer(c.transform, "四邊變暗", _texVignette);
        _flash = MakeLayer(c.transform, "風打到", null);
        _streakB.uvRect = new Rect(0f, 0.37f, 1.8f, 0.6f);
        _streakA.uvRect = new Rect(0f, 0f, 0.8f, 1f);
    }

    private static RawImage MakeLayer(Transform parent, string name, Texture tex)
    {
        GameObject g = new GameObject(name);
        g.transform.SetParent(parent, false);
        RawImage img = g.AddComponent<RawImage>();
        img.texture = tex;
        img.raycastTarget = false;
        img.color = new Color(1f, 1f, 1f, 0f);
        RectTransform rt = img.rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        return img;
    }

    private void BuildAudio()
    {
        _roar = gameObject.AddComponent<AudioSource>();
        _roar.playOnAwake = false; _roar.loop = true; _roar.spatialBlend = 0f; _roar.volume = 0f;
        _layer = gameObject.AddComponent<AudioSource>();
        _layer.playOnAwake = false; _layer.loop = true; _layer.spatialBlend = 0f; _layer.volume = 0f; _layer.pitch = 0.7f;
        _oneShot = gameObject.AddComponent<AudioSource>();
        _oneShot.playOnAwake = false; _oneShot.loop = false; _oneShot.spatialBlend = 0f;
        _roarClip = MakeRoarClip();
        _whooshClip = MakeWhooshClip();
        _roar.clip = _roarClip;
    }

    // ── 每幀 ─────────────────────────────────────────────────
    private void Update()
    {
        float dt = Time.deltaTime;
        float auto = 0f;
        bool blowing = false;
        WindGustSystem w = WindGustSystem.Instance;
        if (Enabled && w != null && w.isActiveAndEnabled && !w.IsStoppedForever)
        {
            if (w.CurrentState == WindState.Blowing) { blowing = true; auto = Mathf.Max(0.35f, w.PushStrength01); }
            else if (w.IsTelegraphing) auto = telegraphLevel * w.TelegraphProgress01;
            _dir = w.WindDirectionX;
            _color = DesertDustColor;
            if (!_dustCaptured) CaptureDust(w);
            if (_layer.clip == null && w.windSoundClip != null) _layer.clip = w.windSoundClip;
        }
        if (blowing && !_wasBlowing)
        {
            Hit(1f);
            if (logEvents) Debug.Log("【風暴加強】起風：沙色一閃、鏡頭一震、呼嘯");
        }
        _wasBlowing = blowing;

        float target = auto;
        bool manual = Enabled && Time.time < _manualUntil;
        if (manual)
        {
            if (_manualLevel >= target) { _dir = _manualDir; _color = _manualColor; }
            target = Mathf.Max(target, _manualLevel);
        }
        else if (PlayerMovement.IsHardCutsceneLocked)
        {
            target = 0f;   // 文字卡、重生黑幕時收掉（廢墟風暴手動驅動時不收）
        }

        _level = Mathf.MoveTowards(_level, target, (target > _level ? riseSpeed : fallSpeed) * dt);
        _hitT += dt;
        float hitEnv = _hitT < shakeHitSeconds ? (1f - _hitT / shakeHitSeconds) * _hitStrength : 0f;

        ApplyOverlay(dt, hitEnv);
        ApplyAudio();
        ApplyDust();
    }

    private void ApplyOverlay(float dt, float hitEnv)
    {
        bool show = _level > 0.002f || hitEnv > 0.002f;
        if (_canvas.enabled != show) _canvas.enabled = show;
        if (!show) return;

        _uvA += -_dir * streakScroll * dt;
        _uvB += -_dir * streakScroll * 1.9f * dt;
        _uvA -= Mathf.Floor(_uvA); _uvB -= Mathf.Floor(_uvB);
        Rect ra = _streakA.uvRect; ra.x = _uvA; _streakA.uvRect = ra;
        Rect rb = _streakB.uvRect; rb.x = _uvB; _streakB.uvRect = rb;

        Color haze = _color; haze.a = hazeMaxAlpha * _level;
        _haze.color = haze;
        Color streak = Color.Lerp(_color, Color.white, 0.45f);
        streak.a = streakMaxAlpha * _level;
        _streakA.color = streak;
        streak.a *= 0.6f;
        _streakB.color = streak;
        _vignette.color = new Color(0.16f, 0.11f, 0.07f, vignetteMaxAlpha * _level);
        Color flash = Color.Lerp(_color, Color.white, 0.6f);
        flash.a = hitFlashAlpha * hitEnv * hitEnv;
        _flash.color = flash;
    }

    private void ApplyAudio()
    {
        float sfx = AudioManager.SfxVolume;
        float v = roarVolume * _level * sfx;
        if (v > 0.001f)
        {
            _roar.volume = v;
            _roar.pitch = 0.78f + 0.22f * _level;
            if (!_roar.isPlaying) _roar.Play();
        }
        else if (_roar.isPlaying) _roar.Stop();

        float lv = layerVolume * _level * sfx;
        if (_layer.clip != null && lv > 0.001f)
        {
            _layer.volume = lv;
            if (!_layer.isPlaying) _layer.Play();
        }
        else if (_layer.isPlaying) _layer.Stop();
    }

    // ── 風沙粒子加密 ─────────────────────────────────────────
    private void CaptureDust(WindGustSystem w)
    {
        if (w.windParticles == null) return;
        _dust = w.windParticles.GetComponentsInChildren<ParticleSystem>(true);
        _dustBase = new float[_dust.Length];
        for (int i = 0; i < _dust.Length; i++) _dustBase[i] = _dust[i] != null ? _dust[i].emission.rateOverTimeMultiplier : 0f;
        _dustCaptured = true;
    }

    private void ApplyDust()
    {
        if (!_dustCaptured) return;
        float m = Mathf.Lerp(1f, Mathf.Max(1f, dustEmissionBoost), _level);
        for (int i = 0; i < _dust.Length; i++)
        {
            if (_dust[i] == null) continue;
            var em = _dust[i].emission;
            em.rateOverTimeMultiplier = _dustBase[i] * m;
        }
    }

    private void RestoreDust()
    {
        if (!_dustCaptured) return;
        for (int i = 0; i < _dust.Length; i++)
        {
            if (_dust[i] == null) continue;
            var em = _dust[i].emission;
            em.rateOverTimeMultiplier = _dustBase[i];
        }
    }

    // ── 鏡頭震動 ─────────────────────────────────────────────
    private void LateUpdate()
    {
        if (_cam == null) _cam = Camera.main;
        if (_cam == null) return;
        RemoveCameraOffset();

        float hitEnv = _hitT < shakeHitSeconds ? (1f - _hitT / shakeHitSeconds) * _hitStrength : 0f;
        float amp = shakeSustain * _level + shakeHit * hitEnv;
        if (amp < 0.0005f || Time.timeScale <= 0f) return;
        float t = Time.time * 17f;
        Vector3 off = new Vector3((Mathf.PerlinNoise(t, 0.37f) - 0.5f) * 2f, (Mathf.PerlinNoise(0.71f, t) - 0.5f) * 2f, 0f) * amp;
        Transform ct = _cam.transform;
        ct.position += off;
        _lastOffset = off;
        _lastCamPos = ct.position;
        _offsetApplied = true;
    }

    /// <summary>上一幀加的震動，如果鏡頭這一幀沒被 Cinemachine 重寫（還停在加完的位置），先扣回去，免得越震越偏。</summary>
    private void RemoveCameraOffset()
    {
        if (!_offsetApplied || _cam == null) return;
        Transform ct = _cam.transform;
        if ((ct.position - _lastCamPos).sqrMagnitude < 1e-8f) ct.position -= _lastOffset;
        _offsetApplied = false;
    }

    // ── 程式畫的貼圖 ─────────────────────────────────────────
    /// <summary>★1010 實機：原本 512×128、風紋 40～280 格，拉滿畫面變成又粗又長的橫線（像掃描線）；
    /// 改 1024×512、風紋 16～96 格（畫面上約 30～180 像素），細而短、數量多，看起來才像一道道被吹過去的沙。</summary>
    private static Texture2D MakeStreakTexture()
    {
        const int W = 1024, H = 512;
        float[] a = new float[W * H];
        System.Random r = new System.Random(1010);
        for (int s = 0; s < 650; s++)
        {
            int y = r.Next(H);
            int len = 16 + r.Next(80);
            int x0 = r.Next(W);
            float k = 0.2f + 0.8f * (float)r.NextDouble();
            int thick = r.NextDouble() < 0.2 ? 2 : 1;
            for (int i = 0; i < len; i++)
            {
                float u = i / (float)len;
                float v = Mathf.Sin(u * Mathf.PI) * k;
                int x = (x0 + i) % W;
                for (int dy = 0; dy < thick; dy++)
                {
                    int yy = (y + dy) % H;
                    int idx = yy * W + x;
                    a[idx] = Mathf.Max(a[idx], v * (dy == 0 ? 1f : 0.5f));
                }
            }
        }
        Texture2D tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Bilinear;
        Color32[] px = new Color32[W * H];
        for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a[i]) * 255f));
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return tex;
    }

    private static Texture2D MakeVignetteTexture()
    {
        const int S = 128;
        Texture2D tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Color32[] px = new Color32[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float nx = (x + 0.5f) / S * 2f - 1f, ny = (y + 0.5f) / S * 2f - 1f;
                float d = Mathf.Sqrt(nx * nx + ny * ny) / 1.4142f;
                float al = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.42f, 1f, d));
                px[y * S + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(al * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return tex;
    }

    // ── 程式合成的聲音 ───────────────────────────────────────
    /// <summary>低沉風吼：棕噪音再低通，三次起伏（一陣一陣），頭尾交叉淡接成無縫循環。</summary>
    private static AudioClip MakeRoarClip()
    {
        int n = SampleRate * 4;
        float[] d = new float[n];
        System.Random r = new System.Random(2210);
        float b = 0f, lp = 0f, peak = 0.0001f;
        for (int i = 0; i < n; i++)
        {
            float white = (float)(r.NextDouble() * 2.0 - 1.0);
            b = (b + 0.02f * white) / 1.02f;
            lp += 0.12f * (b - lp);
            float mod = 0.72f + 0.28f * Mathf.Sin(2f * Mathf.PI * 3f * i / n);
            d[i] = lp * mod;
            peak = Mathf.Max(peak, Mathf.Abs(d[i]));
        }
        int m = SampleRate / 3;
        for (int k = 0; k < m; k++)
        {
            float t = k / (float)m;
            d[n - m + k] = Mathf.Lerp(d[n - m + k], d[k], t);
        }
        float g = 0.8f / peak;
        for (int i = 0; i < n; i++) d[i] *= g;
        AudioClip c = AudioClip.Create("Procedural_StormRoar", n, 1, SampleRate, false);
        c.SetData(d, 0);
        return c;
    }

    /// <summary>呼嘯：白噪音過一道從亮到暗的低通（掃頻），快起慢收，底下墊一點棕噪音。</summary>
    private static AudioClip MakeWhooshClip()
    {
        float secs = 1.5f;
        int n = Mathf.RoundToInt(SampleRate * secs);
        float[] d = new float[n];
        System.Random r = new System.Random(77);
        float lp = 0f, b = 0f, peak = 0.0001f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)n;
            float white = (float)(r.NextDouble() * 2.0 - 1.0);
            float a = Mathf.Lerp(0.45f, 0.025f, Mathf.Sqrt(t));
            lp += a * (white - lp);
            b = (b + 0.02f * white) / 1.02f;
            float env = t < 0.05f ? t / 0.05f : Mathf.Exp(-(t - 0.05f) * 3.2f);
            d[i] = (lp * 0.8f + b * 2.5f) * env;
            peak = Mathf.Max(peak, Mathf.Abs(d[i]));
        }
        float g = 0.9f / peak;
        for (int i = 0; i < n; i++) d[i] *= g;
        AudioClip c = AudioClip.Create("Procedural_StormWhoosh", n, 1, SampleRate, false);
        c.SetData(d, 0);
        return c;
    }
}
