using UnityEngine;

/// <summary>
/// 沙漠植物/沙丘微沙吹拂音效 (Desert Sand Rustle Sound)
/// 可掛載於仙人掌 (Cactus) 或特定沙丘物件上。
/// 具備【隨機音調 (Pitch Variation)】與【防規律冷卻 (Cooldown)】機制，
/// 確保玩家經過時營造出細緻有機的環境流沙聲，絕不產生機械式的重複規律感。
/// </summary>
[RequireComponent(typeof(Collider))]
public class DesertSandRustle : MonoBehaviour
{
    [Header("🎵 沙聲音效設定")]
    [Tooltip("要播放的沙聲音訊檔 (例如 沙聲2.mp3 或 沙聲1.mp3)")]
    public AudioClip sandClip;

    [Tooltip("音量大小 (0 ~ 1)")]
    [Range(0f, 1f)]
    public float volume = 0.55f;

    [Header("🎲 防規律機制")]
    [Tooltip("觸發冷卻時間 (秒，預設 8 秒內重複經過不重響)")]
    public float cooldown = 8.0f;

    [Tooltip("是否啟用隨機音調 (讓每次風吹過沙粒的聲音都有微妙變化)")]
    public bool randomizePitch = true;

    [Tooltip("隨機音調範圍 (預設 0.9 ~ 1.12)")]
    public Vector2 pitchRange = new Vector2(0.9f, 1.12f);

    [Tooltip("是否採用 3D 定點音效 (true = 在該植物位置發聲)")]
    public bool is3DSound = true;

    private float lastPlayTime = -999f;

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
        if (Time.time - lastPlayTime < cooldown) return;

        if (other.CompareTag("Player") || other.name.ToLower().Contains("player") || other.GetComponentInParent<PlayerMovement>() != null)
        {
            PlaySandSound();
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (Time.time - lastPlayTime < cooldown) return;

        if (other.CompareTag("Player") || other.name.ToLower().Contains("player") || other.GetComponentInParent<PlayerMovement>() != null)
        {
            PlaySandSound();
        }
    }

    public void PlaySandSound()
    {
        if (sandClip == null) return;

        lastPlayTime = Time.time;

        float targetPitch = randomizePitch ? Random.Range(pitchRange.x, pitchRange.y) : 1.0f;

        if (is3DSound)
        {
            GameObject tempAudio = new GameObject("Temp_SandAudio_" + gameObject.name);
            tempAudio.transform.position = transform.position;
            AudioSource src = tempAudio.AddComponent<AudioSource>();
            src.clip = sandClip;
            src.volume = volume;
            src.pitch = targetPitch;
            src.spatialBlend = 1.0f;
            src.minDistance = 2.0f;
            src.maxDistance = 12.0f;
            src.Play();
            Destroy(tempAudio, sandClip.length + 0.2f);
        }
        else
        {
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlaySFX(sandClip, volume);
            }
            else
            {
                AudioSource.PlayClipAtPoint(sandClip, transform.position, volume);
            }
        }

        Debug.Log($"🌾【沙漠微沙聲】已觸發植物流沙聲：'{sandClip.name}' (物件: {gameObject.name}, Pitch: {targetPitch:F2})");
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.9f, 0.75f, 0.3f, 0.3f);
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            Gizmos.DrawWireCube(col.bounds.center, col.bounds.size);
        }
    }
}
