using UnityEngine;

[RequireComponent(typeof(Collider))]
public class JumpBoostZone : MonoBehaviour, IResettable
{
    [Header("目標光絮與加成設定")]
    [Tooltip("要監聽的光絮球 (GuidanceLight)")]
    public GuidanceLight targetLight;

    [Tooltip("吸收光絮後額外增加的跳躍力（主角預設 jumpForce 為 5）")]
    public float additionalJumpForce = 3.0f;

    [Tooltip("是否限制在此 Trigger 區域內才享有加成？（勾選代表離開區域就失效，否則代表永久享有加成）")]
    public bool isZoneRestricted = true;

    [Header("加成特效")]
    [Tooltip("啟用加成時在玩家身上生成的特效 (可選，例如發光特效)")]
    public GameObject boostEffectPrefab;

    // 狀態記錄
    public bool IsLightAbsorbed { get; private set; } = false;
    private bool isPlayerInside = false;
    private bool isBoostApplied = false;
    private PlayerMovement activePlayer;
    private float originalJumpForce = 5f;
    private GameObject activeEffectInstance;

    void Start()
    {
        // 確保碰撞體設為 Trigger
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            col.isTrigger = true;
        }

        if (targetLight == null)
        {
            targetLight = FindFirstObjectByType<GuidanceLight>();
        }

        if (targetLight != null)
        {
            targetLight.OnAbsorbed += HandleLightAbsorbed;
        }
        else
        {
            Debug.LogWarning($"【跳躍加成區】在場景中找不到任何 GuidanceLight。物件名稱: {gameObject.name}");
        }
    }

    void OnDestroy()
    {
        if (targetLight != null)
        {
            targetLight.OnAbsorbed -= HandleLightAbsorbed;
        }
        // 如果被銷毀時加成還在，主動恢復玩家數值
        if (isBoostApplied && activePlayer != null)
        {
            RemoveBoost(activePlayer);
        }
    }

    private void HandleLightAbsorbed()
    {
        if (IsLightAbsorbed) return;
        IsLightAbsorbed = true;
        Debug.Log($"【跳躍加成區】偵測到光絮球已被吸收。");

        // 如果玩家已經在區域內，或者非區域限制，立即給予加成
        if (!isZoneRestricted)
        {
            // 非區域限制，尋找場景中玩家並給予永久加成
            PlayerMovement player = FindFirstObjectByType<PlayerMovement>();
            if (player != null)
            {
                ApplyBoost(player);
            }
        }
        else if (isPlayerInside && activePlayer != null)
        {
            ApplyBoost(activePlayer);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        PlayerMovement player = other.GetComponent<PlayerMovement>();
        if (player == null)
        {
            player = other.GetComponentInParent<PlayerMovement>();
        }

        if (player != null)
        {
            isPlayerInside = true;
            activePlayer = player;

            if (IsLightAbsorbed)
            {
                ApplyBoost(player);
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        PlayerMovement player = other.GetComponent<PlayerMovement>();
        if (player == null)
        {
            player = other.GetComponentInParent<PlayerMovement>();
        }

        if (player != null && player == activePlayer)
        {
            isPlayerInside = false;

            if (isZoneRestricted)
            {
                RemoveBoost(player);
            }
            activePlayer = null;
        }
    }

    private void ApplyBoost(PlayerMovement player)
    {
        if (isBoostApplied) return;

        originalJumpForce = player.jumpForce;
        player.jumpForce = originalJumpForce + additionalJumpForce;
        isBoostApplied = true;

        // 生成特效並跟隨玩家
        if (boostEffectPrefab != null)
        {
            activeEffectInstance = Instantiate(boostEffectPrefab, player.transform.position, Quaternion.identity);
            activeEffectInstance.transform.SetParent(player.transform);
        }

        Debug.Log($"【跳躍加成區】已套用跳躍加成！原始跳躍力: {originalJumpForce} -> 目前跳躍力: {player.jumpForce}");
    }

    private void RemoveBoost(PlayerMovement player)
    {
        if (!isBoostApplied) return;

        player.jumpForce = originalJumpForce;
        isBoostApplied = false;

        if (activeEffectInstance != null)
        {
            Destroy(activeEffectInstance);
        }

        Debug.Log($"【跳躍加成區】已移除跳躍加成，恢復原始跳躍力: {player.jumpForce}");
    }

    // --- IResettable 實作 ---
    public void ResetToInitialState()
    {
        if (isBoostApplied && activePlayer != null)
        {
            RemoveBoost(activePlayer);
        }
        else if (isBoostApplied)
        {
            PlayerMovement p = FindFirstObjectByType<PlayerMovement>();
            if (p != null) RemoveBoost(p);
        }
        IsLightAbsorbed = false;
        isPlayerInside = false;
        isBoostApplied = false;
        if (activeEffectInstance != null)
        {
            Destroy(activeEffectInstance);
        }
    }
}
