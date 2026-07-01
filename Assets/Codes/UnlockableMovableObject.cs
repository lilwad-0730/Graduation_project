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

    [Header("解鎖後物理煞車設定")]
    [Tooltip("解鎖後，是否鎖定巨石的旋轉？(打勾可避免巨石滾動或發瘋似地旋轉)")]
    public bool freezeRotationOnUnlock = true;

    [Tooltip("解鎖後，巨石的阻力/煞車力 (值越大越有沉重感，玩家推才動，一放手就會迅速停下，建議值：5 ~ 15)")]
    public float linearDampingOnUnlock = 10f;

    [Tooltip("解鎖後，巨石的質量 (Mass，重物建議設高一點)")]
    public float massOnUnlock = 50f;

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

        // 初始化時即套用物理煞車與旋轉鎖定，確保無論是否觸發解鎖事件，測試時皆能生效
        if (rb != null)
        {
            rb.mass = massOnUnlock;
            rb.linearDamping = linearDampingOnUnlock;

            if (freezeRotationOnUnlock)
            {
                // 鎖定旋轉與 Z 軸移動 (配合 2.5D)
                rb.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionZ;
            }
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

        if (rb != null)
        {
            if (disableKinematicOnUnlock)
            {
                rb.isKinematic = false;
            }

            // 設定剛體質量與空氣阻力（達成煞車與推力延遲效果）
            rb.mass = massOnUnlock;
            rb.linearDamping = linearDampingOnUnlock;

            if (freezeRotationOnUnlock)
            {
                // 鎖定旋轉與 Z 軸移動 (防止 3D 碰撞導致旋轉亂滾與偏離 2.5D 平面)
                rb.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionZ;
            }

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
