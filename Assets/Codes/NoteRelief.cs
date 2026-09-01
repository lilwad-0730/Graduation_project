using UnityEngine;

/// <summary>
/// 掛在 Note Paper 物件（或其 Parent）上
/// 當玩家接觸到此物件時：
/// 1. 觸發吸收動畫 (Animator 若存在)
/// 2. 通知 UnderwaterSuffocationEffect 執行 20% 緩解
/// 3. 禁用本物件 (被「吸收」消失)
/// </summary>
public class NoteRelief : MonoBehaviour
{
    [Header("【物件設定】")]
    [Tooltip("這張紙條的顯示名稱 (純描述用，方便辨識)")]
    public string noteName = "Note Paper";

    [Header("【緩解設定】")]
    [Tooltip("是否覆蓋主效果的 reliefAmount？關閉則使用主效果的預設值")]
    public bool overrideReliefAmount = false;

    [Tooltip("此特定紙條的緩解量 (overrideReliefAmount = true 時有效)")]
    [Range(0f, 1f)]
    public float customReliefAmount = 0.2f;

    [Header("【吸收動畫】")]
    [Tooltip("觸發吸收動畫的 Animator (留空則自動搜尋)")]
    public Animator noteAnimator;

    [Tooltip("吸收動畫的 Trigger 參數名稱")]
    public string absorbTriggerName = "Absorb";

    [Tooltip("動畫播完後幾秒物件消失 (秒)")]
    public float disappearDelay = 0.6f;

    [Header("【偵測設定】")]
    [Tooltip("偵測玩家的 Tag")]
    public string playerTag = "Player";

    [Header("🎵 音效設定")]
    [Tooltip("玩家接觸/吸收紙條時播放的音效 (例如 水下_日誌接觸_02.wav)")]
    public AudioClip collectSFX;
    [Tooltip("日誌發亮觸發音效 (進入發亮範圍瞬間播放一次，例如 水下_物件接觸_01.wav)")]
    public AudioClip glowSFX;
    [Tooltip("日誌發亮音效的感應距離 (公尺)")]
    public float glowHearDistance = 4.5f;
    [Tooltip("紙條出現/破隱時播放的音效 (例如 水下_日誌破障.wav)")]
    public AudioClip revealSFX;
    [Range(0f, 1f)] public float sfxVolume = 0.9f;

    private bool consumed = false;
    private bool hasPlayedGlowSFX = false;
    private AudioSource directAudioSource;
    private Transform playerTransform;

    private void PlayDirectSFX(AudioClip clip, float volume)
    {
        if (clip == null) return;
        if (directAudioSource == null)
        {
            directAudioSource = gameObject.AddComponent<AudioSource>();
            directAudioSource.playOnAwake = false;
            directAudioSource.spatialBlend = 0f; // 2D 零衰減直出，保證 100% 清晰響亮
        }
        directAudioSource.PlayOneShot(clip, AudioManager.ScaleSfx(volume));
    }

    private void Start()
    {
        if (noteAnimator == null)
            noteAnimator = GetComponentInChildren<Animator>();
        if (noteAnimator == null)
            noteAnimator = GetComponent<Animator>();

        if (revealSFX != null)
        {
            PlayDirectSFX(revealSFX, sfxVolume);
        }

        GameObject p = GameObject.FindGameObjectWithTag("Player");
        if (p != null) playerTransform = p.transform;
    }

    private void Update()
    {
        UpdateGlowAudio();
    }

    private void UpdateGlowAudio()
    {
        if (consumed || glowSFX == null || hasPlayedGlowSFX) return;

        if (playerTransform == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) playerTransform = p.transform;
            if (playerTransform == null) return;
        }

        float dist = Vector3.Distance(transform.position, playerTransform.position);
        if (dist <= glowHearDistance)
        {
            hasPlayedGlowSFX = true;
            PlayDirectSFX(glowSFX, sfxVolume);
            Debug.Log($"✨【日誌音效】主角靠近發亮區間，播放發亮音效 (水下_物件接觸_01)！");
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(playerTag) && other.GetComponentInParent<PlayerMovement>() == null) return;
        Absorb();
    }

    // 也支援 2D 碰撞
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag(playerTag) && other.GetComponentInParent<PlayerMovement>() == null) return;
        Absorb();
    }

    // 亦支援直接碰撞 (非 Trigger)
    private void OnCollisionEnter(Collision collision)
    {
        if (!collision.gameObject.CompareTag(playerTag) && collision.gameObject.GetComponentInParent<PlayerMovement>() == null) return;
        Absorb();
    }

    private void Absorb()
    {
        if (consumed) return;
        consumed = true;
        UnderwaterCheckpoint.MarkHere(this, "吸收紙條「" + noteName + "」");

        Debug.Log($"[NoteRelief] 玩家吸收了「{noteName}」！觸發窒息緩解效果。");

        // 播放日記接觸/吸收音效 (直出播放保證 100% 聽得到)
        if (collectSFX != null)
        {
            PlayDirectSFX(collectSFX, sfxVolume);
        }

        // 1. 播放吸收動畫
        if (noteAnimator != null)
        {
            // 動畫控制器沒有這個參數就不呼叫，免得每張紙條都噴「Parameter 'Absorb' does not exist」
            bool hasParam = false;
            foreach (var p in noteAnimator.parameters)
            {
                if (p.name == absorbTriggerName) { hasParam = true; break; }
            }
            if (hasParam) noteAnimator.SetTrigger(absorbTriggerName);
        }

        // 2. 通知窒息效果系統
        if (UnderwaterSuffocationEffect.Instance != null)
        {
            if (overrideReliefAmount)
            {
                // 暫時替換緩解量再呼叫
                float original = UnderwaterSuffocationEffect.Instance.reliefAmount;
                UnderwaterSuffocationEffect.Instance.reliefAmount = customReliefAmount;
                UnderwaterSuffocationEffect.Instance.TriggerRelief();
                UnderwaterSuffocationEffect.Instance.reliefAmount = original;
            }
            else
            {
                UnderwaterSuffocationEffect.Instance.TriggerRelief();
            }
        }
        else
        {
            Debug.LogWarning("[NoteRelief] 找不到 UnderwaterSuffocationEffect！請確認水下場景中有掛載此腳本。");
        }

        // 3. 延遲後隱藏/銷毀物件
        Destroy(gameObject, disappearDelay);
    }

    // Editor Gizmo：在 Scene 視窗顯示偵測範圍提示
    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.3f);
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            Gizmos.DrawWireCube(col.bounds.center, col.bounds.size);
        }
        else
        {
            Gizmos.DrawWireSphere(transform.position, 0.5f);
        }

        // 標示文字
        #if UNITY_EDITOR
        UnityEditor.Handles.Label(transform.position + Vector3.up * 0.7f, $"📄 {noteName}");
        #endif
    }
}
