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
        EnsureComponents();
        CacheOriginalRenderers();

        // 開局給予免疫保護時間
        graceTimer = respawnGracePeriod;
        
        if (rb != null)
        {
            rb.isKinematic = false;
        }
    }

    /// <summary>
    /// 安全檢索所有核心組件 (相容跨層級與 Prefab 子物件結構)
    /// </summary>
    private void EnsureComponents()
    {
        if (playerMovement == null)
        {
            playerMovement = GetComponent<PlayerMovement>();
            if (playerMovement == null) playerMovement = GetComponentInParent<PlayerMovement>();
            if (playerMovement == null) playerMovement = GetComponentInChildren<PlayerMovement>();
        }

        if (respawnSystem == null)
        {
            respawnSystem = GetComponent<PlayerRespawnSystem>();
            if (respawnSystem == null) respawnSystem = GetComponentInParent<PlayerRespawnSystem>();
            if (respawnSystem == null) respawnSystem = FindFirstObjectByType<PlayerRespawnSystem>();
        }

        if (rb == null)
        {
            rb = GetComponent<Rigidbody>();
            if (rb == null) rb = GetComponentInParent<Rigidbody>();
            if (rb == null) rb = GetComponentInChildren<Rigidbody>();
        }

        if (animator == null && playerMovement != null)
        {
            animator = playerMovement.animator;
        }
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
            if (animator == null) animator = GetComponentInParent<Animator>();
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
        EnsureComponents();

        // 若處於免疫保護期內，不執行石化
        if (graceTimer > 0f) return;

        // 全域重生防護：重生期間不允許重複石化
        if (PlayerRespawnSystem.IsAnyRespawning) return;

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
            rb.isKinematic = true;
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
        
        EnsureComponents();
        isPetrified = false;
        Debug.Log("【石化系統】石化解除，玩家恢復行動！");

        graceTimer = 3.0f;

        if (rb != null)
        {
            rb.isKinematic = false;
        }
        if (playerMovement != null) playerMovement.enabled = true;
        
        if (animator != null)
        {
            animator.speed = 1f;
        }

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
                    if (originalColors.TryGetValue(sr, out Color c))
                    {
                        sr.color = c;
                    }
                    else
                    {
                        sr.color = Color.white; // 備用方案：若快取失敗則強制還原為白色
                    }
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
                        if (originalColors.TryGetValue(r, out Color c))
                        {
                            r.material.color = c;
                        }
                        else
                        {
                            r.material.color = Color.white;
                        }
                    }
                }
            }
        }
    }

    private IEnumerator DeathSequence()
    {
        Debug.LogWarning("【石化系統】玩家達到最大石化次數 (3/3)，開始啟動 0.5 秒 DeathSequence...");
        yield return new WaitForSeconds(0.5f);
        EnsureComponents();

        if (respawnSystem != null)
        {
            Debug.Log("【石化系統】已找到 PlayerRespawnSystem，強制啟動並觸發 TriggerRespawn()...");
            respawnSystem.enabled = true;
            respawnSystem.TriggerRespawn();
        }
        else
        {
            Debug.LogError("【石化系統】找不到本地 PlayerRespawnSystem，嘗試全域搜尋...");
            PlayerRespawnSystem sys = FindFirstObjectByType<PlayerRespawnSystem>();
            if (sys != null)
            {
                sys.enabled = true;
                sys.TriggerRespawn();
            }
            else
            {
                Debug.LogError("【石化系統致命錯誤】場景中「完全沒有」PlayerRespawnSystem，請確認是否有掛載該腳本！");
            }
        }
    }

    /// <summary>
    /// 【重生專用寫死規則】完全清除玩家身上的所有負面狀態與石化效果。
    /// 包含：物理解鎖 (isKinematic=false)、動作恢復 (PlayerMovement=true)、
    /// 動畫恢復 (animator.speed=1.0)、顏色刷回原本貼圖、給予 5 秒免疫。
    /// </summary>
    public void ClearAllNegativeEffects()
    {
        StopAllCoroutines();
        EnsureComponents();

        isPetrified = false;
        currentPetrifyCount = 0;

        // 給予 5 秒免疫防護，涵蓋重生過場全過程
        graceTimer = 5.0f;

        // 1. 物理強制解鎖
        if (rb != null)
        {
            rb.isKinematic = false;
            rb.useGravity = true;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        // 2. 移動腳本與標記強制解鎖
        if (playerMovement != null)
        {
            playerMovement.enabled = true;
            playerMovement.freezeHorizontal = false;
            playerMovement.isCutsceneFrozen = false;
            playerMovement.isStrictLockingX = false;
            playerMovement.attachedWolvesCount = 0;
        }

        // 3. 動畫播放速度強制恢復
        if (animator != null)
        {
            animator.speed = 1.0f;
        }

        // 4. 視覺強制還原正常貼圖顏色 (刷洗掉黑色)
        ApplyPetrifyVisual(false);

        Debug.Log($"【石化診斷 LOG】ClearAllNegativeEffects() 執行完成！\n" +
                  $" - isPetrified: {isPetrified}\n" +
                  $" - currentPetrifyCount: {currentPetrifyCount}\n" +
                  $" - graceTimer: {graceTimer}\n" +
                  $" - Rigidbody.isKinematic: {(rb != null ? rb.isKinematic.ToString() : "NULL")}\n" +
                  $" - PlayerMovement.enabled: {(playerMovement != null ? playerMovement.enabled.ToString() : "NULL")}");
    }

    // --- IResettable 實作 (場景重置用) ---
    public void ResetToInitialState()
    {
        ClearAllNegativeEffects();
    }
}
