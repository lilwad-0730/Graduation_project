using UnityEngine;

/// <summary>
/// 掛在「自己播背景音樂」的 AudioSource 上（沒有透過 AudioManager.PlayBGM 播的，例如主選單的 UI BGM），
/// 讓它的音量跟著 BGM 音量滑桿走：實際音量 = Inspector 上設定的音量 × BGM 音量。
/// 播放方式（Play On Awake、Loop、換場景就停）都不變，只管音量。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
public class BgmVolumeFollower : MonoBehaviour
{
    [Tooltip("BGM 音量 100% 時的音量。留 -1 會自動用 AudioSource 原本設定的音量")]
    [SerializeField] private float baseVolume = -1f;

    private AudioSource _source;

    private void Awake()
    {
        _source = GetComponent<AudioSource>();
        if (baseVolume < 0f) baseVolume = _source.volume;
        Apply();   // Play On Awake 的第一幀就用對的音量，不會先大聲一下
    }

    private void Update()
    {
        Apply();
    }

    private void Apply()
    {
        if (_source != null) _source.volume = baseVolume * AudioManager.BgmVolume;
    }
}
