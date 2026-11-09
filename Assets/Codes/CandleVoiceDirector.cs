using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ★1109p 玻璃館：玩家「成功點亮」燭台的瞬間依序播人聲＋字幕。
/// 第 1 次點亮（不管哪一根）播 voices[0]，第 2 次播 voices[1]……每段整場只播一次。
/// 掛在場景裡任何物件上即可（會自己找場景裡所有 CandleAuraGlow 並訂閱 LightingCompleted，之後才生出來的燭台每秒補抓一次）。
///
/// 規則：
///   ‧ 用「已經觸發幾段」決定播哪一段，不是看哪根燭台；同一根燭台重生後再點不會再觸發（不重播、也不會把下一段提早播掉）。
///   ‧ 點燃時上一段還沒播完：排隊，等上一段播完（加一小段間隔）再播，不會把上一段切掉。
///   ‧ 字幕在這段語音「開始播」的時候才顯示，長度＝語音長度；字幕留空＝只播語音不顯示字幕。
///   ‧ 播放走 AudioManager.PlayVoice（語音專用音軌、播的時候其他聲音自動壓低，音量跟其他關的到點語音一致）。
/// </summary>
public class CandleVoiceDirector : MonoBehaviour
{
    [System.Serializable]
    public class CandleVoice
    {
        public AudioClip clip;
        [Range(0f, 1f)] public float volume = 1f;
        [Tooltip("這段語音播放時畫面下方顯示的字幕（留空＝不顯示）。可用換行分成兩行")]
        [TextArea(2, 4)] public string subtitle;
    }

    [Header("🎙 點亮燭台語音（依序：第 1 次點亮播第 1 段，以此類推）")]
    public CandleVoice[] voices = new CandleVoice[5];

    [Tooltip("字幕的字型、大小、顏色、光暈、位置（做法同荒原關 GuidanceLight 的 subtitleStyle，只有字色不同）")]
    public VoiceSubtitle.Style subtitleStyle = new VoiceSubtitle.Style();

    [Header("排隊")]
    [Tooltip("上一段語音播完後，隔多少秒才接著播排隊中的下一段")]
    [Range(0f, 3f)] public float queueGapSeconds = 0.5f;

    [Tooltip("每隔幾秒重新掃描一次場景，補抓後來才生出來的燭台")]
    public float rescanSeconds = 1f;

    private readonly HashSet<CandleAuraGlow> _subscribed = new HashSet<CandleAuraGlow>();
    private readonly HashSet<CandleAuraGlow> _fired = new HashSet<CandleAuraGlow>();   // 已經觸發過語音的燭台
    private readonly Queue<int> _pending = new Queue<int>();                            // 排隊中的語音編號
    private int _triggered;          // 已經觸發（播過＋排隊中）幾段
    private float _voiceEndTime;     // 目前這段語音預計播完的時間（Time.unscaledTime）
    private float _nextScan;

    /// <summary>已經觸發幾段語音（含排隊中的）。</summary>
    public int TriggeredCount { get { return _triggered; } }
    /// <summary>目前這段語音預計播完的時間（Time.unscaledTime）。沒在播就是過去的時間。</summary>
    public float VoiceEndTime { get { return _voiceEndTime; } }

    private void Start() { ScanCandles(); }

    private void OnDestroy()
    {
        foreach (CandleAuraGlow c in _subscribed)
            if (c != null) c.LightingCompleted -= OnLightingCompleted;
        _subscribed.Clear();
    }

    private void Update()
    {
        if (Time.unscaledTime >= _nextScan) { _nextScan = Time.unscaledTime + Mathf.Max(0.2f, rescanSeconds); ScanCandles(); }
        TryPlayNext();
    }

    private void ScanCandles()
    {
        CandleAuraGlow[] all = FindObjectsByType<CandleAuraGlow>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] == null || !_subscribed.Add(all[i])) continue;
            all[i].LightingCompleted += OnLightingCompleted;
        }
    }

    /// <summary>燭台點燃完成（CandleAuraGlow.LightingCompleted）：只有這一刻才觸發，開始點燃、中斷、之後撿起來都不算。</summary>
    private void OnLightingCompleted(CandleAuraGlow candle)
    {
        if (candle == null || !_fired.Add(candle)) return;   // 同一根燭台重生後再點，不重複觸發
        if (voices == null || _triggered >= voices.Length) return;
        _pending.Enqueue(_triggered++);
        TryPlayNext();   // 沒在播就立刻開始（不等下一幀）
    }

    private void TryPlayNext()
    {
        if (_pending.Count == 0) return;
        // 上一段還沒播完（含 GuidanceLight 等其他來源正在播的語音）就先排隊
        if (Time.unscaledTime < _voiceEndTime + queueGapSeconds) return;
        if (AudioManager.Instance != null && AudioManager.Instance.IsVoicePlaying) return;

        CandleVoice v = null;
        int idx = -1;
        while (_pending.Count > 0 && (v == null || v.clip == null)) { idx = _pending.Dequeue(); v = idx < voices.Length ? voices[idx] : null; }
        if (v == null || v.clip == null) return;   // 這段沒放語音檔就跳過

        if (AudioManager.Instance != null) AudioManager.Instance.PlayVoice(v.clip, v.volume);
        else AudioSource.PlayClipAtPoint(v.clip, Camera.main != null ? Camera.main.transform.position : transform.position, AudioManager.ScaleSfx(v.volume));
        VoiceSubtitle.Show(v.subtitle, v.clip.length, subtitleStyle);   // 字幕留空會直接略過
        _voiceEndTime = Time.unscaledTime + v.clip.length;
    }

    [ContextMenu("預覽字幕（Play 中）")]
    private void PreviewSubtitle()
    {
        if (!Application.isPlaying) { Debug.LogWarning("【字幕】請先按 Play 再預覽"); return; }
        if (voices == null) return;
        foreach (CandleVoice v in voices)
            if (v != null && !string.IsNullOrWhiteSpace(v.subtitle)) { VoiceSubtitle.Show(v.subtitle, 30f, subtitleStyle); return; }
    }
}
