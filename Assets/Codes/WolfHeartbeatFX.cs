using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ★1107 狼靠近的暗角心跳（廢墟追逐）：
///   有狼在追她、而且越靠越近 → 畫面邊緣隨心跳一縮一縮、越近越快越濃、顏色由深藍黑偏向暗紅；
///   狼被甩開或離開 → 慢慢退回（上升快、下降慢，避免閃爍）。被咬住（attached）時直接拉到最濃。
/// 只讀狼的狀態，不改狼的任何行為。關掉：WolfHeartbeatFX.Enabled = false。
/// </summary>
public class WolfHeartbeatFX : MonoBehaviour
{
    public static bool Enabled = true;

    [Tooltip("狼離主角多近（公尺）時最緊張")]
    public float nearDistance = 4f;
    [Tooltip("狼離主角多遠（公尺）以外完全沒感覺")]
    public float farDistance = 20f;

    private HeartbeatVignette _hv;
    private Transform _player;
    private float _level;           // 目前顯示的緊張度（有平滑）
    private float _nextScan;
    private float _targetLevel;
    private WolfEnemy[] _wolves = new WolfEnemy[0];

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
        if (FindFirstObjectByType<WolfHeartbeatFX>() != null) return;
        bool ruins = s.name.ToLowerInvariant().Contains("ruin");
        if (!ruins && FindFirstObjectByType<WolfEnemy>() == null) return;
        GameObject go = new GameObject("[狼靠近暗角心跳]");
        go.AddComponent<WolfHeartbeatFX>();
    }

    private void Awake()
    {
        if (s_chasing == null)
        {
            s_chasing = typeof(WolfEnemy).GetField("isChasing", BindingFlags.NonPublic | BindingFlags.Instance);
            s_attached = typeof(WolfEnemy).GetField("isAttached", BindingFlags.NonPublic | BindingFlags.Instance);
        }
    }

    private void Start()
    {
        _hv = HeartbeatVignette.Create(transform, "狼心跳暗角");
        _hv.MaxAlpha = 1f;
        _hv.Intensity = 0f;
    }

    private void Update()
    {
        if (_hv == null) return;
        float dt = Time.unscaledDeltaTime;

        if (Time.unscaledTime >= _nextScan)
        {
            _nextScan = Time.unscaledTime + 0.2f;
            _targetLevel = ScanThreat();
        }

        // 上升與下降都慢：黑邊要像「慢慢逼近」，不是切換
        _level = _targetLevel > _level
            ? Mathf.MoveTowards(_level, _targetLevel, dt * 0.4f)    // 約 2.5 秒從無到最濃：慢慢長出來
            : Mathf.MoveTowards(_level, _targetLevel, dt * 0.3f);   // 退得更慢（約 3 秒）

        // 兩層設計：
        //  底層＝深色黑邊，狼一盯上就從四個角冒出來（有感），越近越往中間擴大，黑的部分「不閃」；
        //  外圈＝比較淺的暗紅，等狼夠近（緊張度過半）才開始隨心跳一閃一閃，越近越快。
        float k = _level;
        _hv.PulseRingAlpha = 0.4f;
        _hv.SolidAt = 0.9f;
        _hv.Intensity = Mathf.Pow(k, 0.6f);        // 從 0 開始，前段長得比較快（實測 0.2 時幾乎看不到），但仍沒有「突然黑掉」的底值
        _hv.Spread = HeartbeatPulse.Smooth(0f, 0.95f, k);
        _hv.Pulse = HeartbeatPulse.Smooth(0.45f, 0.7f, k);
        _hv.Bps = HeartbeatPulse.Bps(k, 0.9f, 2.1f);
        _hv.Color = Color.Lerp(new Color(0.01f, 0.005f, 0.015f, 1f), new Color(0.03f, 0.008f, 0.015f, 1f), k);
        _hv.PulseColor = new Color(0.4f, 0.2f, 0.22f, 1f);      // 亮度跟之前差不多，但紅少一點（偏灰紫褐）
    }

    private float ScanThreat()
    {
        if (_player == null)
        {
            GameObject p = GameObject.FindWithTag("Player");
            if (p != null) _player = p.transform;
        }
        if (_player == null) return 0f;

        _wolves = FindObjectsByType<WolfEnemy>(FindObjectsSortMode.None);
        float best = 0f;
        Vector2 pp = _player.position;
        for (int i = 0; i < _wolves.Length; i++)
        {
            WolfEnemy w = _wolves[i];
            if (w == null || !w.isActiveAndEnabled) continue;
            bool attached = s_attached != null && (bool)s_attached.GetValue(w);
            bool chasing = s_chasing != null && (bool)s_chasing.GetValue(w);
            if (!attached && !chasing) continue;
            if (attached) return 1f;
            float d = Vector2.Distance(pp, w.transform.position);
            float t = 1f - Mathf.InverseLerp(nearDistance, farDistance, d);
            if (t > best) best = t;
        }
        return best;
    }
}
