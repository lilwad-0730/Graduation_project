using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class PlayerPetrification : MonoBehaviour, IResettable
{
    [Header("🛡️ 測試與無敵模式 (Debug God Mode)")]
    [Tooltip("【測試專用無敵模式】：開啟後免疫所有鳥怪攻擊致死、免疫風暴石化與倒數死亡，遊戲機制與動畫正常運行。")]
    public bool godMode = false;

    [Tooltip("是否允許在測試時按 F10 鍵或 Ctrl+G 快速切換無敵模式 (方便在 Editor 或遊玩中即時測試)")]
    public bool allowKeyboardToggle = true;

    [Tooltip("無敵模式開啟時，是否在螢幕角落顯示提示浮水印標籤")]
    public bool showOnScreenIndicator = true;

    private static bool _globalGodMode = false;
    private static PlayerPetrification _instance;

    /// <summary>
    /// 【全域無敵模式屬性】任何腳本、UI 或後台設定皆可直接讀取與切換。
    /// 開啟時自動洗清所有殘留石化與負面狀態。
    /// </summary>
    public static bool IsGodMode
    {
        get
        {
            if (_instance != null) return _instance.godMode;
            return _globalGodMode;
        }
        set
        {
            _globalGodMode = value;
            if (_instance != null)
            {
                _instance.godMode = value;
                if (value)
                {
                    _instance.ClearAllNegativeEffects();
                }
            }
        }
    }

    [Header("🎨 石化外觀與顏色微調 (可在 Inspector 自由自訂)")]
    [Tooltip("石化時的主體顏色 (點擊色盤即可自由調整深灰、淺灰、石青或岩石白)")]
    public Color petrifyColor = new Color(0.72f, 0.72f, 0.72f, 1f);

    [Tooltip("石雕立體輪廓微光強度 (0 為純平光，0.2 ~ 0.8 為立體紋理自發光，數值越大在荒原越醒目)")]
    [Range(0f, 2f)]
    public float stoneGlowIntensity = 0.4f;

    [Header("⏱️ 石化機制數值調整")]
    [Tooltip("被石化幾次會觸發死亡重生？(預設 3)")]
    [Range(1, 10)]
    public int maxPetrifyCount = 3;

    [Tooltip("每次石化持續時間 (秒，預設 2.5)")]
    [Range(0.5f, 10f)]
    public float petrifyDuration = 2.5f;

    [Tooltip("解除石化後，給予玩家移動避難的免疫時間 (秒，預設 5)")]
    [Range(0f, 10f)]
    public float unpetrifyGraceDuration = 5.0f;

    [Header("🧎 主動石化（企劃定案：按住不放＝抗風抗鳥，但不能動）")]
    [Tooltip("按住 ⬇ 或 S 主動石化硬撐：風暴吹不動、鳥啄不死，但完全不能動；放開立刻解除")]
    public bool holdToPetrify = true;

    [Tooltip("主動石化的按鍵（↓ 方向鍵恆定支援，這裡設第二顆鍵）")]
    public KeyCode braceKey = KeyCode.S;

    [Header("🎵 石化與解石音效 (Petrification SFX)")]
    [Tooltip("主角被石化時播放的音效 (例如 石化.mp3)")]
    public AudioClip petrifySFX;
    [Tooltip("主角解除石化恢復行動時播放的音效 (例如 解除石化.mp3)")]
    public AudioClip unpetrifySFX;
    [Range(0f, 1f)]
    public float sfxVolume = 0.9f;

    [Header("狀態監控")]
    public int currentPetrifyCount = 0;
    public bool isPetrified = false;
    private float graceTimer = 0f;
    private bool _bracing = false;   // 目前的石化是玩家主動按出來的
    /// <summary>目前的石化是玩家自己按住 ⬇/S 硬撐出來的（重生守護不該把它清掉）。</summary>
    public bool IsBracing => _bracing;

    private PlayerMovement playerMovement;
    private PlayerRespawnSystem respawnSystem;
    private Rigidbody rb;
    private Animator animator;

    // 快取原本的 Renderer 與顏色，用於解除石化時復原
    private Dictionary<Renderer, Color> originalColors = new Dictionary<Renderer, Color>();

    private void Awake()
    {
        _instance = this;
        if (_globalGodMode) godMode = true;
    }

    private void OnEnable()
    {
        _instance = this;
        if (godMode) _globalGodMode = true;
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    private void Start()
    {
        EnsureComponents();
        CacheOriginalRenderers();

        // 開局強制完全清除任何殘留的石化、動作停用與動畫凍結，並給予 5 秒開局免疫保護
        ClearAllNegativeEffects();
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

#if UNITY_EDITOR
        if (petrifySFX == null)
        {
            petrifySFX = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Music/荒漠/石化.mp3");
        }
        if (unpetrifySFX == null)
        {
            unpetrifySFX = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Music/荒漠/解除石化.mp3");
        }
#endif
    }

    private void Update()
    {
        // 1. 快捷鍵切換無敵模式 (F10 或 Ctrl+G)
        if (allowKeyboardToggle && (Input.GetKeyDown(KeyCode.F10) || (Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(KeyCode.G))))
        {
            IsGodMode = !IsGodMode;
            Debug.LogWarning($"🛡️【無敵模式】已{(IsGodMode ? "<color=green>【開啟】</color>" : "<color=red>【關閉】</color>")}！(測試專用：免疫鳥怪撞擊與風暴石化，關卡機制正常運作)");
        }

        // 2. 同步 Inspector 開關與全域狀態
        if (godMode != _globalGodMode)
        {
            _globalGodMode = godMode;
            if (godMode) ClearAllNegativeEffects();
        }

        if (graceTimer > 0f)
        {
            graceTimer -= Time.deltaTime;
        }

        // 3. 主動石化：按住 ⬇/S 變成石頭（抗風抗鳥、不能動）；放開立刻解除
        if (holdToPetrify)
        {
            bool holding = Input.GetKey(braceKey) || Input.GetKey(KeyCode.DownArrow);
            if (holding && !isPetrified)
            {
                EnsureComponents();
                bool canBrace = playerMovement != null
                    && playerMovement.enabled
                    && playerMovement.isGrounded
                    && !playerMovement.isUnderwater
                    && !playerMovement.isCutsceneFrozen
                    && !PlayerMovement.IsHardCutsceneLocked
                    && !PlayerRespawnSystem.IsAnyRespawning;
                if (canBrace) BeginBrace();
            }
            else if (!holding && isPetrified && _bracing)
            {
                EndBrace();
            }
        }
    }

    /// <summary>玩家按住 ⬇/S：主動石化硬撐（不計次、不會因此死亡）。</summary>
    private void BeginBrace()
    {
        if (isPetrified) return;
        _bracing = true;
        isPetrified = true;
        Debug.Log("🪨【石化系統】主動石化：她把自己變成石頭，硬撐過去（放開 ⬇/S 解除）。");

        if (playerMovement != null) playerMovement.enabled = false;
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }
        if (animator != null) animator.speed = 0f;
        ApplyPetrifyVisual(true);

        if (petrifySFX != null)
        {
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(petrifySFX, sfxVolume);
            else AudioSource.PlayClipAtPoint(petrifySFX, transform.position, AudioManager.ScaleSfx(sfxVolume));
        }
    }

    /// <summary>放開 ⬇/S：解除主動石化，立即恢復行動。</summary>
    private void EndBrace()
    {
        if (!isPetrified || !_bracing) return;
        _bracing = false;
        isPetrified = false;
        graceTimer = 0.3f;

        if (unpetrifySFX != null)
        {
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(unpetrifySFX, sfxVolume);
            else AudioSource.PlayClipAtPoint(unpetrifySFX, transform.position, AudioManager.ScaleSfx(sfxVolume));
        }

        if (rb != null) rb.isKinematic = false;
        if (playerMovement != null) playerMovement.enabled = true;
        if (animator != null) animator.speed = 1f;
        ApplyPetrifyVisual(false);
    }

    private void OnGUI()
    {
        if (showOnScreenIndicator && IsGodMode)
        {
            GUIStyle style = new GUIStyle(GUI.skin.box);
            style.fontSize = 13;
            style.fontStyle = FontStyle.Bold;
            style.normal.textColor = Color.yellow;
            style.alignment = TextAnchor.MiddleCenter;
            
            // 螢幕右上角半透明浮水印標籤
            GUI.Box(new Rect(Screen.width - 240, 10, 230, 32), "🛡️ GOD MODE: ON (F10 切換)", style);
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
    /// 觸發石化 (嚴格單次觸發，免疫期或已石化狀態下絕不重複播放音效)
    /// </summary>
    public void Petrify()
    {
        EnsureComponents();

        // ★【無敵模式】：免疫石化與倒數死亡，機制正常運行
        if (IsGodMode)
        {
            Debug.Log("🛡️【無敵模式】玩家受到石化判定，但因處於無敵模式，免疫石化與死亡！");
            return;
        }

        // 若處於免疫保護期內，不執行石化
        if (graceTimer > 0f) return;

        // 全域重生防護：重生期間不允許重複石化
        if (PlayerRespawnSystem.IsAnyRespawning) return;

        if (isPetrified) return;
        
        isPetrified = true;
        currentPetrifyCount++;
        Debug.LogWarning($"🔊【石化系統】主角被石化！播放石化音效 (次數：{currentPetrifyCount}/{maxPetrifyCount})");

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

        // 僅在進入石化瞬間播放一次石化音效
        if (petrifySFX != null)
        {
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(petrifySFX, sfxVolume);
            else AudioSource.PlayClipAtPoint(petrifySFX, transform.position, AudioManager.ScaleSfx(sfxVolume));
        }

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

    /// <summary>
    /// 解除石化 (嚴格單次觸發，並給予 5 秒充足的移動避難緩衝期)
    /// </summary>
    public void Unpetrify()
    {
        if (!isPetrified) return;
        
        EnsureComponents();
        isPetrified = false;
        Debug.Log("🔊【石化系統】石化解除！播放解除石化音效，並給予 5 秒避難免疫期。");

        // 僅在解除石化瞬間播放一次音效
        if (unpetrifySFX != null)
        {
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(unpetrifySFX, sfxVolume);
            else AudioSource.PlayClipAtPoint(unpetrifySFX, transform.position, AudioManager.ScaleSfx(sfxVolume));
        }

        // 給予 5 秒免疫緩衝期，讓玩家有充裕時間跑進掩體
        graceTimer = 5.0f;

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
            if (r == null) continue;

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
                    else sr.color = Color.white;
                }
            }
            else
            {
                // 3D 角色模型 (SkinnedMeshRenderer / MeshRenderer)
                Material[] mats = r.materials;
                for (int i = 0; i < mats.Length; i++)
                {
                    Material mat = mats[i];
                    if (mat == null) continue;

                    if (petrified)
                    {
                        if (mat.HasProperty("_Color"))
                        {
                            if (!originalColors.ContainsKey(r)) originalColors[r] = mat.color;
                            mat.color = petrifyColor;
                        }
                        else if (mat.HasProperty("_BaseColor"))
                        {
                            if (!originalColors.ContainsKey(r)) originalColors[r] = mat.GetColor("_BaseColor");
                            mat.SetColor("_BaseColor", petrifyColor);
                        }

                        // 同步開啟石化微光/紋理對比，讓淺灰色在荒原中極其顯眼！
                        if (mat.HasProperty("_EmissionColor"))
                        {
                            if (stoneGlowIntensity > 0.01f)
                            {
                                mat.EnableKeyword("_EMISSION");
                                mat.SetColor("_EmissionColor", petrifyColor * stoneGlowIntensity);
                            }
                            else
                            {
                                mat.SetColor("_EmissionColor", Color.black);
                            }
                        }
                    }
                    else
                    {
                        Color orig = Color.white;
                        if (originalColors.TryGetValue(r, out Color c)) orig = c;

                        if (mat.HasProperty("_Color")) mat.color = orig;
                        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", orig);
                        if (mat.HasProperty("_EmissionColor"))
                        {
                            mat.SetColor("_EmissionColor", Color.black);
                        }
                    }
                }
            }
        }
    }

    private void OnValidate()
    {
        // 若在運行或預覽中石化，即時同步 Inspector 調整的顏色與微光強度
        if (isPetrified)
        {
            ApplyPetrifyVisual(true);
        }
    }

    [ContextMenu("🎨 即時預覽石化外觀 (Preview Petrify)")]
    public void EditorPreviewPetrify()
    {
        CacheOriginalRenderers();
        ApplyPetrifyVisual(true);
    }

    [ContextMenu("🔄 恢復正常外觀 (Preview Normal)")]
    public void EditorPreviewNormal()
    {
        ApplyPetrifyVisual(false);
    }

    private IEnumerator DeathSequence()
    {
        if (IsGodMode)
        {
            Debug.Log("🛡️【無敵模式】已阻擋石化死亡序列 (DeathSequence)。");
            yield break;
        }

        Debug.LogWarning("【石化系統】玩家達到最大石化次數 (3/3)，開始啟動 0.5 秒 DeathSequence...");
        yield return new WaitForSeconds(0.5f);
        EnsureComponents();

        if (IsGodMode) yield break;

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
        _bracing = false;

        // 給予短暫 0.5 秒保護，避免重生瞬間與畫面切換穿幫
        graceTimer = 0.5f;

        // 1. 物理強制解鎖
        if (rb != null)
        {
            rb.isKinematic = false;
            rb.useGravity = true;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        // 2. 移動腳本與標記強制解鎖 (重生過場期間保持 isCutsceneFrozen)
        if (playerMovement != null)
        {
            playerMovement.freezeHorizontal = false;
            if (!PlayerRespawnSystem.IsAnyRespawning)
            {
                playerMovement.enabled = true;
                playerMovement.isCutsceneFrozen = false;
            }
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
