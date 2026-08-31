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

    /// <summary>
    /// 播放指定的關卡背景音樂，並自動執行淡入淡出
    /// </summary>
    /// <param name="newClip">你要播放的音樂檔案</param>
    public void PlayBGM(AudioClip newClip)
    {
        if (newClip == null) return;

        // 找出目前正在播放的 AudioSource，看看它播的音樂是不是跟現在要求的一樣
        AudioSource activeSource = _isUsingSource1 ? _bgmSource1 : _bgmSource2;
        if (activeSource.clip == newClip && activeSource.isPlaying)
        {
            // 如果音樂一樣且正在播放，就不需要重新播，直接返回
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
        newSource.volume = BgmVolume;

        // 切換主要來源
        _isUsingSource1 = !_isUsingSource1;
    }

    private IEnumerator FadeOutRoutine()
    {
        AudioSource activeSource = _isUsingSource1 ? _bgmSource1 : _bgmSource2;
        float startVolume = activeSource.volume;
        float timer = 0f;

        while (timer < crossfadeDuration)
        {
            timer += Time.deltaTime;
            float progress = timer / crossfadeDuration;
            activeSource.volume = Mathf.Lerp(startVolume, 0f, progress);
            yield return null;
        }

        activeSource.volume = 0f;
        activeSource.Stop();
        activeSource.clip = null; // 清除紀錄
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
