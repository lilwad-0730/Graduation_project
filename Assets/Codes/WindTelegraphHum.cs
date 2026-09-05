using UnityEngine;

/// <summary>
/// ★0905 起風前兆的「聲音」：沙塵線出現的那一秒，一道低鳴從遠處堆起來，風一到就交給風聲。
/// Inside 的脈衝段：音樂跟波同步——閉著眼也知道下一道什麼時候來。
/// 聲音是程式合成的（低通過的棕噪音），不用素材；要換成真的錄音，把 clip 拖進 customClip。
/// 由 DesertBeatDirector 自動生成；音量走 AudioManager.SfxVolume。
/// </summary>
[DisallowMultipleComponent]
public class WindTelegraphHum : MonoBehaviour
{
    [Tooltip("最大音量（再乘 SfxVolume）")]
    public float volume = 0.55f;
    public float basePitch = 0.85f;
    [Tooltip("前兆從 0 到 1 時音高上升多少")]
    public float pitchRise = 0.25f;
    [Tooltip("留空＝用程式合成的低鳴")]
    public AudioClip customClip;

    private const int SampleRate = 22050;
    private const float ClipSeconds = 2.0f;

    private AudioSource _src;
    private AudioClip _generated;
    private float _level = 0f;

    public static WindTelegraphHum Install()
    {
        WindTelegraphHum existing = FindFirstObjectByType<WindTelegraphHum>();
        if (existing != null) return existing;
        GameObject go = new GameObject("WindTelegraphHum (自動生成)");
        return go.AddComponent<WindTelegraphHum>();
    }

    private void Awake()
    {
        _src = gameObject.AddComponent<AudioSource>();
        _src.playOnAwake = false;
        _src.loop = true;
        _src.spatialBlend = 0f;
        _src.volume = 0f;
        _src.clip = customClip != null ? customClip : BuildHumClip();
    }

    private void Update()
    {
        WindGustSystem w = WindGustSystem.Instance;
        float target = 0f;
        if (w != null)
        {
            if (w.IsTelegraphing) target = w.TelegraphProgress01;
            else if (w.IsPushActive) target = 1f - w.PushStrength01;   // 風一到，交棒給風聲（pushRampSeconds 內淡掉）
        }

        _level = Mathf.MoveTowards(_level, target, Time.deltaTime * 6f);

        if (_src == null || _src.clip == null) return;
        float vol = _level * _level * volume * AudioManager.SfxVolume;
        if (vol > 0.001f)
        {
            if (!_src.isPlaying) _src.Play();
            _src.volume = vol;
            _src.pitch = basePitch + pitchRise * _level;
        }
        else if (_src.isPlaying)
        {
            _src.Stop();
        }
    }

    /// <summary>棕噪音＋一階低通：遠處的風在堆起來的那種聲音。頭尾各 50ms 淡入淡出，循環不爆音。</summary>
    private AudioClip BuildHumClip()
    {
        int n = Mathf.RoundToInt(SampleRate * ClipSeconds);
        float[] data = new float[n];
        System.Random rng = new System.Random(20260905);
        double brown = 0.0;
        double lp = 0.0;
        float peak = 0.0001f;
        for (int i = 0; i < n; i++)
        {
            double white = rng.NextDouble() * 2.0 - 1.0;
            brown = (brown + 0.02 * white) / 1.02;          // 棕噪音（隨機漫步）
            lp += 0.06 * (brown * 3.5 - lp);                 // 低通：只留下 100Hz 以下那團
            float v = (float)lp;
            data[i] = v;
            float av = Mathf.Abs(v);
            if (av > peak) peak = av;
        }
        int fade = Mathf.RoundToInt(SampleRate * 0.05f);
        for (int i = 0; i < n; i++)
        {
            float g = 0.9f / peak;
            if (i < fade) g *= (float)i / fade;
            else if (i > n - fade) g *= (float)(n - i) / fade;
            data[i] = Mathf.Clamp(data[i] * g, -1f, 1f);
        }
        _generated = AudioClip.Create("Procedural_TelegraphHum", n, 1, SampleRate, false);
        _generated.SetData(data, 0);
        return _generated;
    }

    private void OnDestroy()
    {
        if (_generated != null) Destroy(_generated);
    }
}
