using UnityEngine;
using System.Collections;

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
        if (_sfxSource != null)
        {
            _sfxSource.PlayOneShot(clip, ScaleSfx(volume));
        }
        else
        {
            AudioSource.PlayClipAtPoint(clip, Camera.main != null ? Camera.main.transform.position : Vector3.zero, ScaleSfx(volume));
        }
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
