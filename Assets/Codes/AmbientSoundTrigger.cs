using UnityEngine;

/// <summary>
/// 環境與特定區域音效觸發器 (Ambient / Event Sound Trigger)
/// 可放置於地圖特定路段（如遠處狼嚎、群狼嚎叫、回聲洞窟等），踏入時觸發播放指定音效。
/// </summary>
[RequireComponent(typeof(Collider))]
public class AmbientSoundTrigger : MonoBehaviour
{
    [Header("🎵 音效設定")]
    [Tooltip("要播放的音效檔案 (例如 狼嚎_遠1, 狼嚎_群狼, 落下碎石等)")]
    public AudioClip soundClip;

    [Tooltip("音量大小 (0 ~ 1)")]
    [Range(0f, 1f)]
    public float volume = 0.9f;

    [Tooltip("是否只播放一次 (走過觸發一次後即失效)")]
    public bool playOnce = true;

    [Tooltip("是否為 3D 定點音效 (true = 在該觸發器位置發聲；false = 全螢幕 2D 廣播)")]
    public bool is3DSound = false;

    private bool hasPlayed = false;

    private void Start()
    {
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            col.isTrigger = true;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (hasPlayed && playOnce) return;

        if (other.CompareTag("Player") || other.name.ToLower().Contains("player") || other.GetComponentInParent<PlayerMovement>() != null)
        {
            PlaySound();
        }
    }

    public void PlaySound()
    {
        if (soundClip == null) return;
        hasPlayed = true;

        if (is3DSound)
        {
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlaySFXAt(soundClip, transform.position, volume);
            }
            else
            {
                AudioSource.PlayClipAtPoint(soundClip, transform.position, volume);
            }
        }
        else
        {
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlaySFX(soundClip, volume);
            }
            else
            {
                AudioSource.PlayClipAtPoint(soundClip, Camera.main != null ? Camera.main.transform.position : Vector3.zero, volume);
            }
        }

        Debug.Log($"🔊【環境音效】已觸發播放音效：'{soundClip.name}' (來自物件: {gameObject.name})");
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.2f, 0.9f, 0.4f, 0.35f);
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            Gizmos.DrawCube(col.bounds.center, col.bounds.size);
        }
    }
}
