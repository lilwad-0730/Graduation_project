using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class PlayerPetrification : MonoBehaviour, IResettable
{
    [Header("石化設定")]
    [Tooltip("被石化幾次會觸發死亡重生？(預設 3)")]
    public int maxPetrifyCount = 3;

    [Tooltip("每次石化持續時間 (秒，預設 2.5)")]
    public float petrifyDuration = 2.5f;

    [Tooltip("石化時角色的變色 (預設全黑/深灰色)")]
    public Color petrifyColor = new Color(0.2f, 0.2f, 0.2f, 1f);

    [Header("開局與重生防護")]
    [Tooltip("開局或重生後，保護玩家免受石化的免疫時間 (秒，預設 5)")]
    public float respawnGracePeriod = 5.0f;

    [Header("狀態監控")]
    public int currentPetrifyCount = 0;
    public bool isPetrified = false;
    private float graceTimer = 0f;

    private PlayerMovement playerMovement;
    private PlayerRespawnSystem respawnSystem;
    private Rigidbody rb;
    private Animator animator;

    // 快取原本的 Renderer 與顏色，用於解除石化時復原
    private Dictionary<Renderer, Color> originalColors = new Dictionary<Renderer, Color>();

    private void Start()
    {
        playerMovement = GetComponent<PlayerMovement>();
        respawnSystem = GetComponent<PlayerRespawnSystem>();
        rb = GetComponent<Rigidbody>();
        
        if (playerMovement != null)
        {
            animator = playerMovement.animator;
        }
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        CacheOriginalRenderers();

        // 核心修復：開局給予 5 秒安全免疫時間，確保玩家順利落地並可移動！
        graceTimer = respawnGracePeriod;
        
        if (rb != null)
        {
            rb.isKinematic = false;
        }
    }

    private void Update()
    {
        if (graceTimer > 0f)
        {
            graceTimer -= Time.deltaTime;
        }
    }

    private void CacheOriginalRenderers()
    {
        originalColors.Clear();
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            if (r is SpriteRenderer sr)
            {
                originalColors[sr] = sr.color;
            }
            else
            {
                if (r.sharedMaterial != null && r.sharedMaterial.HasProperty("_Color"))
                {
                    originalColors[r] = r.sharedMaterial.color;
                }
                else
                {
                    originalColors[r] = Color.white;
                }
            }
        }
    }

    /// <summary>
    /// 觸發石化
    /// </summary>
    public void Petrify()
    {
        // 核心修複：若處於保護期內，不執行石化，防範開局卡死在半空！
        if (graceTimer > 0f) return;

        // 【修複重生後被石化卡死】：重生序列需要 5.5 秒，中途若風吹起再次石化，
        // 玩家在重生動畫結束時就已被石化鎖死，造成卡死。
        if (respawnSystem != null && respawnSystem.IsRespawning) return;

        if (isPetrified) return;
        
        isPetrified = true;
        currentPetrifyCount++;
        Debug.LogWarning($"【石化系統】玩家被石化！次數：{currentPetrifyCount}/{maxPetrifyCount}");

        // 停止角色動作與物理
        if (playerMovement != null) playerMovement.enabled = false;
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true; // 暫時鎖定物理
        }

        // 暫停動畫
        if (animator != null)
        {
            animator.speed = 0f;
        }

        // 視覺變色 (全黑/石化灰)
        ApplyPetrifyVisual(true);

        // 檢查是否達到 3 次
        if (currentPetrifyCount >= maxPetrifyCount)
        {
            StartCoroutine(DeathSequence());
        }
        else
        {
            StartCoroutine(UnpetrifySequence());
        }
    }

    private IEnumerator UnpetrifySequence()
    {
        yield return new WaitForSeconds(petrifyDuration);
        
        if (currentPetrifyCount >= maxPetrifyCount) yield break;

        Unpetrify();
    }

    public void Unpetrify()
    {
        if (!isPetrified) return;
        
        isPetrified = false;
        Debug.Log("【石化系統】石化解除，玩家恢復行動！");

        // 核心修復：解除石化後給予 3 秒免疫保護期，防止剛解除又被下一幀風吹秒石化！
        graceTimer = 3.0f;

        // 恢復物理與動作
        if (rb != null)
        {
            rb.isKinematic = false;
        }
        if (playerMovement != null) playerMovement.enabled = true;
        
        // 恢復動畫
        if (animator != null)
        {
            animator.speed = 1f;
        }

        // 恢復原色
        ApplyPetrifyVisual(false);
    }

    private void ApplyPetrifyVisual(bool petrified)
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            if (r is SpriteRenderer sr)
            {
                if (petrified)
                {
                    if (!originalColors.ContainsKey(sr)) originalColors[sr] = sr.color;
                    sr.color = petrifyColor;
                }
                else
                {
                    if (originalColors.TryGetValue(sr, out Color c)) sr.color = c;
                }
            }
            else
            {
                if (r.sharedMaterial != null && r.sharedMaterial.HasProperty("_Color"))
                {
                    if (petrified)
                    {
                        if (!originalColors.ContainsKey(r)) originalColors[r] = r.sharedMaterial.color;
                        r.material.color = petrifyColor;
                    }
                    else
                    {
                        if (originalColors.TryGetValue(r, out Color c)) r.material.color = c;
                    }
                }
            }
        }
    }

    private IEnumerator DeathSequence()
    {
        yield return new WaitForSeconds(0.5f);
        if (respawnSystem != null)
        {
            respawnSystem.TriggerRespawn();
        }
        else
        {
            Debug.LogError("【石化系統】找不到 PlayerRespawnSystem，無法觸發重生！");
        }
    }

    /// <summary>
    /// 【重生專用規則】清除玩家身上所有負面效果，使其恢復完全正常的可操作狀態。
    /// 此規則寫死：重生 = 完全乾淨的玩家。只給予 2 秒短暫保護防止落地瞬間被打，
    /// 不影響正常遊戲中的石化機制。
    /// </summary>
    public void ClearAllNegativeEffects()
    {
        StopAllCoroutines();
        isPetrified = false;
        currentPetrifyCount = 0;

        // 重生後只給 2 秒短暫保護，讓玩家安全落地即可，不長期免疫石化
        graceTimer = 2.0f;

        if (rb != null)
        {
            rb.isKinematic = false;
            rb.linearVelocity = Vector3.zero;
        }
        if (playerMovement != null) playerMovement.enabled = true;
        if (animator != null) animator.speed = 1f;

        // 清除石化視覺（恢復原本顏色）
        ApplyPetrifyVisual(false);

        Debug.Log("【石化系統】重生：已清除所有負面效果，玩家恢復完全正常狀態。");
    }

    // --- IResettable 實作 (場景重置用，非重生用) ---
    public void ResetToInitialState()
    {
        StopAllCoroutines();
        isPetrified = false;
        currentPetrifyCount = 0;
        graceTimer = respawnGracePeriod; // 場景整體重置時才給予完整保護期
        
        // 恢復物理與狀態
        if (rb != null)
        {
            rb.isKinematic = false;
        }
        if (playerMovement != null) playerMovement.enabled = true;
        if (animator != null) animator.speed = 1f;

        // 恢復原色
        ApplyPetrifyVisual(false);
    }
}
