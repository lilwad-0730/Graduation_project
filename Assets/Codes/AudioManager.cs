using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[DefaultExecutionOrder(10000)]   // 最後才跑：其他腳本在 LateUpdate 設的音量，壓低語音背景音時才能正確蓋過去
public class AudioManager : MonoBehaviour
{
    // 單例模式，讓全域都可以方便呼叫 AudioManager.Instance.PlayBGM(...)
    public static AudioManager Instance;

    public const string BgmVolumePrefsKey = "BGMVolume";
    private static float _bgmVolume = -1f;

    public static float BgmVolume
    {
        get
        {
            if (_bgmVolume < 0f)
                _bgmVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(BgmVolumePrefsKey, 0.75f));
            return _bgmVolume;
        }
    }

    public static void SetBgmVolume(float volume)
    {
        _bgmVolume = Mathf.Clamp01(volume);
        if (Instance != null && Instance._fadeCoroutine == null)
        {
            AudioSource activeSource = Instance._isUsingSource1 ? Instance._bgmSource1 : Instance._bgmSource2;
            if (activeSource != null && activeSource.isPlaying)
                activeSource.volume = _bgmVolume;
        }
    }

    public const string SfxVolumePrefsKey = "SFXVolume";
    private static float _sfxVolume = -1f;

    public static float SfxVolume
    {
        get
        {
            if (_sfxVolume < 0f)
                _sfxVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(SfxVolumePrefsKey, 0.75f));
            return _sfxVolume;
        }
    }

    public static void SetSfxVolume(float volume)
    {
        _sfxVolume = Mathf.Clamp01(volume);
    }

    public static float ScaleSfx(float volume)
    {
        return Mathf.Max(0f, volume) * SfxVolume;
    }

    [Header("音樂設定")]
    [Tooltip("淡入淡出需要的時間 (秒)")]
    public float crossfadeDuration = 1.5f;

    // 我們需要兩個 AudioSource 來做完美的交叉淡入淡出 (Crossfade)
    private AudioSource _bgmSource1;
    private AudioSource _bgmSource2;
    private AudioSource _sfxSource;
    private bool _isUsingSource1 = true;

    private Coroutine _fadeCoroutine;

    [Header("語音壓低背景音（播旁白時，其他聲音自動變小）")]
    [Tooltip("播語音時，其他所有聲音（背景音樂、狼、風、腳步、音效）壓到原本的幾倍")]
    [Range(0f, 1f)] public float voiceDuckLevel = 0.35f;
    [Tooltip("語音開始時，其他聲音降下去花多久（秒）")]
    public float voiceDuckDownSeconds = 0.35f;
    [Tooltip("語音結束後，其他聲音回來花多久（秒）")]
    public float voiceDuckUpSeconds = 0.9f;

    private AudioSource _voiceSource;
    private float _duck = 1f;            // 目前其他聲音的倍率（1＝沒壓）
    private float _duckRescanAt;
    private class DuckState { public float baseVolume; public float applied; }
    private readonly Dictionary<AudioSource, DuckState> _ducked = new Dictionary<AudioSource, DuckState>();
    private readonly List<AudioSource> _duckRemove = new List<AudioSource>();

    /// <summary>旁白語音是否正在播。</summary>
    public bool IsVoicePlaying => _voiceSource != null && _voiceSource.isPlaying;

    /// <summary>
    /// 播旁白語音（用自己的音軌，不受其他音效影響）；播放期間其他聲音自動壓低，播完再淡回來。
    /// 音量照音效音量設定（SFX）縮放。新的語音會蓋掉還沒播完的舊語音。
    /// </summary>
    public void PlayVoice(AudioClip clip, float volume = 1.0f)
    {
        if (clip == null) return;
        if (_voiceSource == null) return;
        _voiceSource.Stop();
        _voiceSource.clip = clip;
        _voiceSource.volume = ScaleSfx(volume);
        _voiceSource.Play();
    }

    public void StopVoice()
    {
        if (_voiceSource != null) _voiceSource.Stop();
    }

    /// <summary>
    /// 壓低其他聲音：每幀把場景裡所有 AudioSource（語音除外）乘上倍率。
    /// 其他程式中途自己改了音量（例如背景音樂交叉淡入、狼跑步聲忽大忽小），會被當成新的原始音量，所以不會打架；
    /// 壓低結束後全部還原成原始音量。
    /// </summary>
    private void LateUpdate()
    {
        float target = IsVoicePlaying ? voiceDuckLevel : 1f;
        float seconds = target < _duck ? voiceDuckDownSeconds : voiceDuckUpSeconds;
        float span = Mathf.Max(0.01f, 1f - voiceDuckLevel);
        _duck = Mathf.MoveTowards(_duck, target, Time.unscaledDeltaTime * span / Mathf.Max(0.01f, seconds));

        if (_duck >= 0.999f && _ducked.Count == 0) return;

        if (_duck < 0.999f && Time.unscaledTime >= _duckRescanAt)
        {
            _duckRescanAt = Time.unscaledTime + 0.25f;   // 新出現的聲音（例如 PlayClipAtPoint 的單次音效）每 0.25 秒補抓一次
            foreach (AudioSource s in FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
            {
                if (s == null || s == _voiceSource || _ducked.ContainsKey(s)) continue;
                _ducked[s] = new DuckState { baseVolume = s.volume, applied = s.volume };
            }
        }

        bool done = _duck >= 0.999f;
        _duckRemove.Clear();
        foreach (KeyValuePair<AudioSource, DuckState> kv in _ducked)
        {
            AudioSource s = kv.Key;
            DuckState st = kv.Value;
            if (s == null) { _duckRemove.Add(s); continue; }
            if (!Mathf.Approximately(s.volume, st.applied)) st.baseVolume = s.volume;   // 別的程式改過音量
            float v = done ? st.baseVolume : st.baseVolume * _duck;
            s.volume = v;
            st.applied = v;
        }
        foreach (AudioSource s in _duckRemove) _ducked.Remove(s);
        if (done) _ducked.Clear();
    }

    private void Awake()
    {
        // 確保整個遊戲中只有一個 AudioManager
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject); // 過關切換場景時不要銷毀，確保音樂不會斷掉
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        // 動態生成兩個 BGM AudioSource
        _bgmSource1 = gameObject.AddComponent<AudioSource>();
        _bgmSource1.loop = true;
        _bgmSource1.playOnAwake = false;

        _bgmSource2 = gameObject.AddComponent<AudioSource>();
        _bgmSource2.loop = true;
        _bgmSource2.playOnAwake = false;

        // 動態生成獨立的 SFX 音效 AudioSource (不受 BGM 淡入淡出影響)
        _sfxSource = gameObject.AddComponent<AudioSource>();
        _sfxSource.loop = false;
        _sfxSource.playOnAwake = false;
        _sfxSource.volume = 1f;

        _keepSfxSource = gameObject.AddComponent<AudioSource>();
        _keepSfxSource.loop = false;
        _keepSfxSource.playOnAwake = false;
        _keepSfxSource.volume = 1f;

        // 旁白語音專用（播的時候其他聲音會被壓低）
        _voiceSource = gameObject.AddComponent<AudioSource>();
        _voiceSource.loop = false;
        _voiceSource.playOnAwake = false;
        _voiceSource.spatialBlend = 0f;
    }

    private void OnEnable()
    {
        if (Instance == this)
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        }
    }

    private void OnDisable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    /// <summary>
    /// 場景切換自動重置與防漏檢測：
    /// 當載入新場景時，自動檢查新場景是否有任何啟用且開局播放的 BGMZone。
    /// 若新場景完全沒有任何開局 BGM (例如「dark glasses 玻璃館」)，上一關的音樂絕不該繼續殘留，自動淡出停止！
    /// </summary>
    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        if (mode != UnityEngine.SceneManagement.LoadSceneMode.Single) return;

        // ★1004 換場景時，上一關還沒播完的 SFX 一律停掉（例如荒原的石化長音效到下一關還在響）。
        //   原因：PlaySFX 用的是 DontDestroyOnLoad 的 _sfxSource.PlayOneShot，換場景不會被清掉。
        StopAllSfx();
        StopVoice();   // 上一關的旁白不帶到下一關

        // 檢查新載入的場景中是否有任何已啟用的 BGMZone 且設定為開局播放音樂
        bool hasActiveSceneBGM = false;
        BGMZone[] zones = Object.FindObjectsByType<BGMZone>(FindObjectsSortMode.None);
        foreach (var zone in zones)
        {
            if (zone != null && zone.enabled && zone.playOnStart && zone.levelMusic != null)
            {
                hasActiveSceneBGM = true;
                break;
            }
        }

        // 如果新場景完全沒有任何開局 BGMZone (例如玻璃館)，且非主選單，上一關的音樂立刻停止
        if (!hasActiveSceneBGM && scene.name != "MainMenuScene")
        {
            if (GetCurrentClip() != null)
            {
                Debug.Log($"🎵【AudioManager】新場景 [{scene.name}] 無開局 BGMZone，自動停止前一場景之背景音樂 ({GetCurrentClip().name})。");
                StopBGM();
            }
        }
    }

    /// <summary>
    /// 播放指定的關卡背景音樂，並自動執行淡入淡出
    /// </summary>
    /// <param name="newClip">你要播放的音樂檔案</param>
    public void PlayBGM(AudioClip newClip)
    {
        if (newClip == null) return;

        // 找出目前正在播放的 AudioSource，看看它播的音樂是不是跟現在要求的一樣
        AudioSource activeSource = _isUsingSource1 ? _bgmSource1 : _bgmSource2;
        AudioSource idleSource = _isUsingSource1 ? _bgmSource2 : _bgmSource1;

        if (activeSource.clip == newClip && activeSource.isPlaying)
        {
            // 音樂一樣且正在播放：不重播，但要順手清掉被中斷的淡入淡出留下的殘留音軌，
            // 否則那一軌會一直疊著播且永遠關不掉
            if (idleSource.isPlaying && _fadeCoroutine == null)
            {
                idleSource.Stop();
                idleSource.volume = 0f;
                idleSource.clip = null;
            }
            return;
        }

        if (_fadeCoroutine != null)
        {
            StopCoroutine(_fadeCoroutine);
        }

        // 開始執行淡入淡出
        _fadeCoroutine = StartCoroutine(CrossfadeRoutine(newClip));
    }

    /// <summary>
    /// 停止目前的背景音樂，並帶有淡出效果
    /// </summary>
    public void StopBGM()
    {
        if (_fadeCoroutine != null)
        {
            StopCoroutine(_fadeCoroutine);
        }
        _fadeCoroutine = StartCoroutine(FadeOutRoutine());
    }

    /// <summary>
    /// 取得目前正在播放的音樂
    /// </summary>
    public AudioClip GetCurrentClip()
    {
        AudioSource activeSource = _isUsingSource1 ? _bgmSource1 : _bgmSource2;
        return activeSource.clip;
    }

    private IEnumerator CrossfadeRoutine(AudioClip newClip)
    {
        AudioSource activeSource = _isUsingSource1 ? _bgmSource1 : _bgmSource2;
        AudioSource newSource = _isUsingSource1 ? _bgmSource2 : _bgmSource1;

        // ★ 修復「關不掉的 BGM」：
        //   上一次的 Crossfade 若在中途就被 StopCoroutine 打斷 (短時間內連續切換場景/區域音樂就會發生)，
        //   被打斷的那一輪不會執行到最後的 activeSource.Stop()，也不會翻轉 _isUsingSource1，
        //   於是兩個 AudioSource 同時在播，而之後的 StopBGM 只會關掉其中一個，
        //   另一個就永遠關不掉、還會一直 loop (使用者觀察到的「播兩輪、只有它關不掉」)。
        //   這裡在開始新的淡入淡出前，先把「不該再響的那一軌」硬關掉。
        if (newSource.isPlaying)
        {
            newSource.Stop();
            newSource.volume = 0f;
        }

        // 準備好新的音樂
        newSource.clip = newClip;
        newSource.volume = 0f;
        newSource.Play();

        float timer = 0f;
        float startingVolume = activeSource.volume;

        // 交叉淡出淡入：舊的變小聲，新的變大聲
        while (timer < crossfadeDuration)
        {
            timer += Time.deltaTime;
            float progress = timer / crossfadeDuration;

            activeSource.volume = Mathf.Lerp(startingVolume, 0f, progress);
            newSource.volume = Mathf.Lerp(0f, BgmVolume, progress);

            yield return null;
        }

        // 確保最終狀態
        activeSource.volume = 0f;
        activeSource.Stop();
        activeSource.clip = null;
        newSource.volume = BgmVolume;

        // 切換主要來源
        _isUsingSource1 = !_isUsingSource1;
        _fadeCoroutine = null;
    }

    private IEnumerator FadeOutRoutine()
    {
        // ★ 兩軌一起淡出並關閉：
        //   只關 activeSource 的話，若之前有被打斷的 Crossfade 留下另一軌還在播，就會關不掉。
        float startVolume1 = _bgmSource1.volume;
        float startVolume2 = _bgmSource2.volume;
        float timer = 0f;

        while (timer < crossfadeDuration)
        {
            timer += Time.deltaTime;
            float progress = timer / crossfadeDuration;
            _bgmSource1.volume = Mathf.Lerp(startVolume1, 0f, progress);
            _bgmSource2.volume = Mathf.Lerp(startVolume2, 0f, progress);
            yield return null;
        }

        StopAllBgmSourcesImmediate();
        _fadeCoroutine = null;
    }

    /// <summary>立即硬關掉所有 BGM 音軌 (含被中斷的淡入淡出殘留音軌)</summary>
    public void StopAllBgmSourcesImmediate()
    {
        // 先掐掉還在跑的淡入淡出，否則它下一幀又會把音量拉回來，等於白關
        if (_fadeCoroutine != null)
        {
            StopCoroutine(_fadeCoroutine);
            _fadeCoroutine = null;
        }

        if (_bgmSource1 != null)
        {
            _bgmSource1.volume = 0f;
            _bgmSource1.Stop();
            _bgmSource1.clip = null;
        }
        if (_bgmSource2 != null)
        {
            _bgmSource2.volume = 0f;
            _bgmSource2.Stop();
            _bgmSource2.clip = null;
        }
    }

    /// <summary>
    /// 全域 2D 音效播放 (支援多個音效同時重疊播放不被中斷，且不受 BGM 音量調整影響)
    /// </summary>
    public void PlaySFX(AudioClip clip, float volume = 1.0f)
    {
        if (clip == null) return;
        if (_keepSfxSource != null && KeepAcrossSceneClipNames.Contains(clip.name))
        {
            _keepSfxSource.PlayOneShot(clip, ScaleSfx(volume));   // 白名單：換場景不會被停
        }
        else if (_sfxSource != null)
        {
            _sfxSource.PlayOneShot(clip, ScaleSfx(volume));
        }
        else
        {
            AudioSource.PlayClipAtPoint(clip, Camera.main != null ? Camera.main.transform.position : Vector3.zero, ScaleSfx(volume));
        }
    }

    /// <summary>
    /// ★1004 換場景時不要被停掉的 SFX 白名單（用音檔名稱，不含副檔名）。
    /// 例如轉場本身的音效、要跨關延續的音效，加進來它就會走獨立的 AudioSource。預設空，代表全部都停。
    /// 用法：AudioManager.KeepAcrossSceneClipNames.Add("某音檔名");
    /// </summary>
    public static readonly System.Collections.Generic.HashSet<string> KeepAcrossSceneClipNames = new System.Collections.Generic.HashSet<string>();

    private AudioSource _keepSfxSource;

    /// <summary>停掉目前所有 PlaySFX 播出的音效（白名單走另一個 AudioSource，不受影響）。BGM 不動。</summary>
    public void StopAllSfx()
    {
        if (_sfxSource != null) _sfxSource.Stop();
    }

    /// <summary>
    /// 3D 空間定點音效播放 (例如推石、落石、狼嚎)
    /// </summary>
    public void PlaySFXAt(AudioClip clip, Vector3 position, float volume = 1.0f)
    {
        if (clip == null) return;
        AudioSource.PlayClipAtPoint(clip, position, ScaleSfx(volume));
    }
}
