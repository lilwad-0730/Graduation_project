using UnityEngine;

public class UnlockableMovableObject : MonoBehaviour
{
    [Header("目標光絮與解鎖設定")]
    [Tooltip("要監聽的光絮球 (GuidanceLight)")]
    public GuidanceLight targetLight;

    [Tooltip("解鎖後，該物件的 Tag 會自動修改為此 Tag (預設為 Pushable 以利主角拉動)")]
    public string unlockedTag = "Pushable";

    [Tooltip("解鎖時，是否自動將 Rigidbody 的 isKinematic 設為 false，使其可受重力與推力影響？")]
    public bool disableKinematicOnUnlock = true;

    [Header("解鎖效果")]
    [Tooltip("解鎖時播放的特效 (可選)")]
    public GameObject unlockEffectPrefab;
    [Tooltip("解鎖時播放的音效 (可選)")]
    public AudioClip unlockSFX;

    // 用於記錄目前是否已被解鎖
    public bool IsUnlocked { get; private set; } = false;

    private Rigidbody rb;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = GetComponentInParent<Rigidbody>();
        }

        if (targetLight == null)
        {
            // 自動在場景中尋找第一個 GuidanceLight
            targetLight = FindFirstObjectByType<GuidanceLight>();
        }

        if (targetLight != null)
        {
            // 訂閱光絮球被吸收的事件
            targetLight.OnAbsorbed += HandleLightAbsorbed;
        }
        else
        {
            Debug.LogWarning($"【解鎖物件】在場景中找不到任何 GuidanceLight，將無法觸發解鎖機制。物件名稱: {gameObject.name}");
        }
    }

    void OnDestroy()
    {
        // 釋放事件訂閱避免記憶體殘留
        if (targetLight != null)
        {
            targetLight.OnAbsorbed -= HandleLightAbsorbed;
        }
    }

    private void HandleLightAbsorbed()
    {
        if (IsUnlocked) return; // 避免重複解鎖
        UnlockObject();
    }

    public void UnlockObject()
    {
        IsUnlocked = true;
        gameObject.tag = unlockedTag;

        if (rb != null && disableKinematicOnUnlock)
        {
            rb.isKinematic = false;
            rb.WakeUp(); // 喚醒物理碰撞
        }

        // 播放解鎖特效
        if (unlockEffectPrefab != null)
        {
            Instantiate(unlockEffectPrefab, transform.position, Quaternion.identity);
        }

        // 播放解鎖音效
        if (unlockSFX != null)
        {
            AudioSource.PlayClipAtPoint(unlockSFX, transform.position);
        }

        Debug.Log($"【解鎖物件】物件 {gameObject.name} 已成功解鎖！Tag 改為：{unlockedTag}，Rigidbody.isKinematic 設為：{(rb != null ? rb.isKinematic.ToString() : "無 Rigidbody")}");
    }
}
