using UnityEngine;

/// <summary>
/// 綠洲 3D 空間距離水流環境音效 (Oasis 3D Proximity Water Ambient Sound)
/// 掛載於綠洲湖畔或出口轉換區 (例如 Transition_To_Underwater)。
/// 當玩家靠近綠洲時，水聲由遠漸近、清澈自然地浮現，具有極佳的環境沉浸感與出口導引效果。
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class OasisWaterAmbient : MonoBehaviour
{
    [Header("🎵 水流音效設定")]
    [Tooltip("綠洲水流音訊檔 (預設 水聲2（小）.mp3)")]
    public AudioClip waterClip;

    [Tooltip("最大音量 (0 ~ 1)")]
    [Range(0f, 1f)]
    public float volume = 0.6f;

    [Header("📐 3D 距離聽覺半徑")]
    [Tooltip("最小距離 (米，在此距離內音量為 100%)")]
    public float minDistance = 3.5f;

    [Tooltip("最大聽見距離 (米，超過此距離完全聽不見水聲)")]
    public float maxDistance = 16.0f;

    private AudioSource audioSource;

    private void Awake()
    {
        SetupAudioSource();
    }

    private void Start()
    {
        SetupAudioSource();

        if (audioSource != null && waterClip != null && !audioSource.isPlaying)
        {
            audioSource.Play();
        }
    }

    private void SetupAudioSource()
    {
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();

        audioSource.clip = waterClip;
        audioSource.volume = volume;
        audioSource.loop = true;
        audioSource.playOnAwake = true;

        // 核心：啟用 3D 空間衰減，讓聲音具備真實距離感
        audioSource.spatialBlend = 1.0f;
        audioSource.rolloffMode = AudioRolloffMode.Logarithmic;
        audioSource.minDistance = minDistance;
        audioSource.maxDistance = maxDistance;
        audioSource.dopplerLevel = 0f; // 避免 2D 橫向捲軸產生都卜勒音調偏差
    }

    private void OnValidate()
    {
        if (audioSource != null)
        {
            audioSource.clip = waterClip;
            audioSource.volume = volume;
            audioSource.minDistance = minDistance;
            audioSource.maxDistance = maxDistance;
        }
    }

    private void OnDrawGizmosSelected()
    {
        // 視覺化聽覺範圍球體
        Gizmos.color = new Color(0.2f, 0.8f, 1.0f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, minDistance);
        Gizmos.color = new Color(0.1f, 0.5f, 0.9f, 0.2f);
        Gizmos.DrawWireSphere(transform.position, maxDistance);
    }
}
