using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ★1116 B8 呼吸：她跑了一段路會喘，缺氧的時候呼吸又淺又急。只做畫面：整個畫面的亮度跟著呼吸極輕微地一明一暗（像胸口起伏），
/// 平常（沒在喘）完全是 0，什麼都不會發生。
///
///  做法：
///   ・「累積的喘」effort：貼地並用接近全速在跑的時候慢慢累積（約 6 秒跑滿），停下來後慢慢退（約 9 秒），所以停下來還會喘一陣子。
///   ・呼吸強度 level ＝ max(喘, 缺氧)。呼吸頻率從平常的 0.28 Hz（約 3.5 秒一次）隨 level 加快到 0.95 Hz；缺氧時更淺更急。
///   ・輸出一個曝光微動 EmotionColorGradeFX.ExposurePulse（單位：曝光檔位）。最大 ±0.07 檔，幾乎不會意識到，只會覺得「畫面在跟著喘」。
///   ・乘上 MotionComfort.Scale（動態強度）；調成 0 就完全不動。
///  只在廢墟與水下開（跟顏色就是心情同一批）。關掉：BreathPulseFX.Enabled = false。
/// </summary>
public class BreathPulseFX : MonoBehaviour
{
    public static bool Enabled = true;

    /// <summary>最大曝光微動（檔位，單邊）。</summary>
    public static float MaxAmplitude = 0.07f;
    public static float CalmHz = 0.28f;
    public static float WindedHz = 0.95f;
    /// <summary>跑到這個水平速度（占 baseSpeed 的比例）以上才算在用力。</summary>
    public static float EffortSpeedRatio = 0.7f;
    public static float EffortRisePerSecond = 1f / 6f;
    public static float EffortFallPerSecond = 1f / 9f;

    /// <summary>目前的喘（0～1，驗收／除錯用）。</summary>
    public static float Effort;

    private PlayerMovement _pm;
    private float _nextFind;
    private Vector3 _lastPos;
    private bool _hasLast;
    private float _phase;
    private float _amp;   // 平滑後的振幅比例，進出時不要啪一下

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        SceneManager.sceneLoaded -= OnLoaded;
        SceneManager.sceneLoaded += OnLoaded;
        Ensure(SceneManager.GetActiveScene());
    }

    private static void OnLoaded(Scene s, LoadSceneMode m) { Effort = 0f; Ensure(s); }

    private static void Ensure(Scene s)
    {
        if (!Enabled || !s.IsValid()) return;
        string n = s.name.ToLowerInvariant();
        if (!(n.Contains("ruin") || n.Contains("underwater"))) return;
        if (FindFirstObjectByType<BreathPulseFX>() != null) return;
        new GameObject("[呼吸]").AddComponent<BreathPulseFX>();
    }

    private void OnDisable() { EmotionColorGradeFX.ExposurePulse = 0f; Effort = 0f; }
    private void OnDestroy() { EmotionColorGradeFX.ExposurePulse = 0f; Effort = 0f; }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;
        if (_pm == null)
        {
            if (Time.unscaledTime >= _nextFind)
            {
                _nextFind = Time.unscaledTime + 1f;
                _pm = FindFirstObjectByType<PlayerMovement>();
                _hasLast = false;
            }
        }

        bool pushing = false;
        if (_pm != null)
        {
            Vector3 p = _pm.transform.position;
            if (_hasLast && dt > 0.0001f && (p - _lastPos).sqrMagnitude < 25f)
            {
                float vx = Mathf.Abs(p.x - _lastPos.x) / dt;
                pushing = _pm.isGrounded && !_pm.isUnderwater && vx >= _pm.BaseSpeed * EffortSpeedRatio;
            }
            _lastPos = p;
            _hasLast = true;
        }
        Effort = pushing
            ? Mathf.MoveTowards(Effort, 1f, EffortRisePerSecond * dt)
            : Mathf.MoveTowards(Effort, 0f, EffortFallPerSecond * dt);

        float suff = EmotionDirector.Enabled ? Mathf.SmoothStep(0f, 1f, EmotionDirector.Suffocation) : 0f;
        float level = Mathf.Max(Mathf.SmoothStep(0f, 1f, Effort), suff);
        if (!Enabled || Time.timeScale < 0.01f) level = 0f;

        // 缺氧時更急一點
        float hz = Mathf.Lerp(CalmHz, WindedHz, level) + 0.15f * suff;
        _phase += hz * dt * 6.2832f;
        if (_phase > 62.832f) _phase -= 62.832f;

        // 振幅：level 低時幾乎沒有；level 到 0.1 以上才開始淡入
        float ampT = Mathf.Clamp01((level - 0.08f) / 0.5f) * MotionComfort.Scale;
        _amp = Mathf.Lerp(_amp, ampT, 1f - Mathf.Exp(-1.5f * dt));
        if (_amp < 0.003f && ampT <= 0f) _amp = 0f;

        // 吸氣快、吐氣慢：用偏斜的波形，比單純正弦更像呼吸
        float s = Mathf.Sin(_phase);
        float wave = s + 0.25f * Mathf.Sin(2f * _phase + 0.6f);
        EmotionColorGradeFX.ExposurePulse = MaxAmplitude * _amp * wave * 0.8f;
    }
}
