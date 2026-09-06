using System.Collections;
using UnityEngine;

/// <summary>
/// 水下育兒物品專屬互動組件 (Underwater Nursery Collectible Item)
/// 支援 2.5D 平面距離偵測與多軌響度增益 (Layered Audio Gain Boost)，確保音效極致清晰響亮！
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class UnderwaterNurseryItem : MonoBehaviour, IResettable
{
    [Header("🎯 感應與接觸範圍 (可在 Scene 視窗即時預覽調整)")]
    [Tooltip("靠近觸發專屬記憶音效的 2D 感應半徑 (公尺)")]
    [Range(1f, 30f)]
    public float proximityRange = 6.0f;

    [Tooltip("實體接觸拾取 2D 半徑 (公尺)")]
    [Range(0.3f, 6f)]
    public float contactRadius = 1.5f;

    [Header("🎵 專屬音效配置")]
    [Tooltip("靠近範圍時播放的專屬回憶音效 (音樂盒/奶瓶/搖鈴)")]
    public AudioClip proximityClip;

    [Tooltip("實體碰觸時播放的接觸音效 (水下_物件接觸_01)")]
    public AudioClip contactClip;

    [Header("🔊 響度倍率增益 (支援 1.0 ~ 5.0 倍超清晰音量)")]
    [Range(0.5f, 5.0f)] public float proximityVolume = 2.5f;
    [Range(0.5f, 5.0f)] public float contactVolume = 3.0f;

    [Header("✨ 拾取動畫效果")]
    [Tooltip("拾取後是否播放輕微縮小淡出動畫")]
    public bool animateOnCollect = true;
    [Tooltip("淡出消失耗時 (秒)")]
    public float fadeDuration = 0.45f;

    private AudioSource audioSource;
    private Transform playerTrans;
    private Vector3 originalPosition;
    private Vector3 originalScale;
    private Quaternion originalRotation;
    private SpriteRenderer spriteRenderer;
    private Color originalColor = Color.white;

    private bool hasPlayedProximity = false;
    private bool isCollected = false;

    private void Awake()
    {
        EnsureAudioSource();
        originalPosition = transform.position;
        originalScale = transform.localScale != Vector3.zero ? transform.localScale : Vector3.one;
        originalRotation = transform.rotation;

        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            originalColor = spriteRenderer.color;
        }
    }

    private void Start()
    {
        EnsureAudioSource();
        EnsureTriggerCollider();
        FindPlayer();
    }

    private void EnsureAudioSource()
    {
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();

        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0.0f; // 2D 乾淨直出
        audioSource.volume = 1.0f;
    }

    private void EnsureTriggerCollider()
    {
        BoxCollider box = GetComponent<BoxCollider>();
        if (box == null) box = gameObject.AddComponent<BoxCollider>();

        box.isTrigger = true;
        float scaleX = Mathf.Abs(transform.lossyScale.x) > 0.001f ? Mathf.Abs(transform.lossyScale.x) : 1f;
        float scaleY = Mathf.Abs(transform.lossyScale.y) > 0.001f ? Mathf.Abs(transform.lossyScale.y) : 1f;
        float scaleZ = Mathf.Abs(transform.lossyScale.z) > 0.001f ? Mathf.Abs(transform.lossyScale.z) : 1f;

        box.size = new Vector3((contactRadius * 2f) / scaleX, (contactRadius * 2f) / scaleY, 20f / scaleZ);
    }

    private void FindPlayer()
    {
        if (playerTrans != null) return;

        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj == null)
        {
            PlayerMovement pm = Object.FindFirstObjectByType<PlayerMovement>();
            if (pm != null) playerObj = pm.gameObject;
        }

        if (playerObj != null)
        {
            playerTrans = playerObj.transform;
        }
    }

    private void Update()
    {
        if (isCollected) return;

        if (playerTrans == null)
        {
            FindPlayer();
            if (playerTrans == null) return;
        }

        // 2.5D 平面距離
        float dx = transform.position.x - playerTrans.position.x;
        float dy = transform.position.y - playerTrans.position.y;
        float dist2D = Mathf.Sqrt(dx * dx + dy * dy);

        // 1. 靠近感知範圍音效觸發 (靠近時只播一次，大音量清晰回響)
        if (!hasPlayedProximity && dist2D <= proximityRange)
        {
            PlayProximitySound();
        }

        // 2. 備用主動距離接觸偵測
        if (!isCollected && dist2D <= contactRadius)
        {
            Collect();
        }
    }

    private void PlayProximitySound()
    {
        hasPlayedProximity = true;
        if (proximityClip != null)
        {
            PlayBoostedAudio(proximityClip, proximityVolume);
            Debug.Log($"🎵【水下育兒物品】玩家靠近 [{gameObject.name}]，超大音量 (x{proximityVolume:F1}) 播放專屬回憶音效：{proximityClip.name}");
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (isCollected) return;

        if (other.CompareTag("Player") || other.name.ToLower().Contains("player") || other.GetComponentInParent<PlayerMovement>() != null)
        {
            Collect();
        }
    }

    /// <summary>
    /// 實體接觸拾取
    /// </summary>
    public void Collect()
    {
        if (isCollected) return;
        isCollected = true;

        Debug.Log($"✨【水下育兒物品】玩家接觸拾取 [{gameObject.name}]！");
        UnderwaterCheckpoint.MarkHere(this, "拾取育兒物品 " + gameObject.name);

        // 播放超清晰響亮接觸音效
        if (contactClip != null)
        {
            PlayBoostedAudio(contactClip, contactVolume);
        }

        if (animateOnCollect)
        {
            StartCoroutine(CollectAnimationRoutine());
        }
        else
        {
            gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// 多軌響度增益播放器 (Layered Audio Gain Booster)
    /// 突破 Unity 單軌 1.0 音量限制，實現 200% ~ 500% 超清晰飽滿的音效！
    /// </summary>
    public static void PlayBoostedAudio(AudioClip clip, float volumeMultiplier)
    {
        if (clip == null) return;

        GameObject sfxHost = new GameObject($"BoostedSFX_{clip.name}");
        if (Camera.main != null)
        {
            sfxHost.transform.position = Camera.main.transform.position;
        }

        float remainingVol = Mathf.Max(0f, AudioManager.ScaleSfx(volumeMultiplier));
        while (remainingVol > 0.01f)
        {
            float trackVol = Mathf.Min(1.0f, remainingVol);
            AudioSource src = sfxHost.AddComponent<AudioSource>();
            src.clip = clip;
            src.spatialBlend = 0.0f;
            src.volume = trackVol;
            src.Play();

            remainingVol -= 1.0f;
        }

        Destroy(sfxHost, clip.length + 0.3f);
    }

    private IEnumerator CollectAnimationRoutine()
    {
        float elapsed = 0f;
        Vector3 startScale = transform.localScale;
        Vector3 startPos = transform.position;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / fadeDuration);

            // 輕微上浮並縮小
            transform.localScale = Vector3.Lerp(startScale, Vector3.zero, t);
            transform.position = startPos + Vector3.up * (t * 0.6f);

            // 同步透明度淡出
            if (spriteRenderer != null)
            {
                Color c = originalColor;
                c.a = Mathf.Lerp(originalColor.a, 0f, t);
                spriteRenderer.color = c;
            }

            yield return null;
        }

        gameObject.SetActive(false);
        transform.localScale = originalScale;
        transform.position = originalPosition;
        if (spriteRenderer != null) spriteRenderer.color = originalColor;
    }

    // --- IResettable 重生刷新實作 ---
    public void ResetToInitialState()
    {
        StopAllCoroutines();

        // ★ 已經撿過的就維持消失。
        //   原本無條件 SetActive(true)，但 isCollected 不會被重置，
        //   於是撿過的物品重生後會復活成「看得到、撿不到、也不會再消失」的幽靈道具
        //   (所有靠近偵測與碰撞都會因為 isCollected == true 直接 return)。
        if (isCollected)
        {
            gameObject.SetActive(false);
            return;
        }

        gameObject.SetActive(true);

        transform.position = originalPosition;
        transform.rotation = originalRotation;
        transform.localScale = originalScale;

        if (spriteRenderer != null)
        {
            spriteRenderer.color = originalColor;
        }

        if (!isCollected)
        {
            hasPlayedProximity = false;
        }

        EnsureAudioSource();
        if (audioSource != null)
        {
            audioSource.Stop();
        }
    }

    private void OnDrawGizmosSelected()
    {
        // 靠近感知範圍圈 (黃色)
        Gizmos.color = new Color(1f, 0.92f, 0.016f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, proximityRange);

        // 實體接觸範圍圈 (綠色)
        Gizmos.color = new Color(0.1f, 1f, 0.1f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, contactRadius);
    }
}
