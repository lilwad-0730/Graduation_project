using UnityEngine;
using UnityEngine.UI;
using System.Collections;

/// <summary>
/// 水下窒息視覺效果系統（URP 相容版）
/// 
/// 模糊視覺模擬原理：
///   建立三個疊加的 UI 圓形暗化層，半徑略有差異、透明度遞減，
///   視覺上產生「外圍暗且模糊、邊緣帶暈散」的窒息效果。
///   完全不使用 GrabPass，URP 與 Built-In RP 都能正常運作。
/// </summary>
public class UnderwaterSuffocationEffect : MonoBehaviour, IResettable
{
    // ==========================================
    // Inspector 可調參數
    // ==========================================

    [Header("【啟用控制】")]
    [Tooltip("是否啟用效果")]
    public bool effectEnabled = true;

    [Tooltip("進入場景後幾秒才開始暗化（讓玩家先看清楚環境）")]
    public float startDelay = 4f;

    [Header("【暗化速度與面積】")]
    [Tooltip("暗化速度：每秒縮減的圓心半徑（越大越快，建議 0.02~0.08）")]
    public float darknessIncreaseRate = 0.03f;

    [Tooltip("最大暗化半徑（外圍停止點，0=全黑，1=完全透明）。預設 0.4 約等於外圍 50% 面積變暗")]
    public float maxDarknessRadius = 0.4f;

    [Tooltip("起始透明圓心半徑（1=一開始完全看不到效果）")]
    public float startRadius = 1.0f;

    [Header("【Note Paper 緩解設定】")]
    [Tooltip("每次吃到 Note Paper，圓心半徑恢復量（0.2 = 緩解約 20% 的暗化面積）")]
    public float reliefAmount = 0.2f;

    [Tooltip("緩解時半徑恢復速度（每秒，預設 0.5）")]
    public float reliefSpeed = 0.5f;

    [Tooltip("緩解後暫停再次暗化的時間（秒）")]
    public float reliefPauseDuration = 2.0f;

    [Tooltip("吃到 Note Paper 時的螢幕閃光顏色")]
    public Color reliefFlashColor = new Color(0.4f, 0.85f, 1f, 0.35f);

    [Tooltip("閃光持續時間（秒）")]
    public float reliefFlashDuration = 0.5f;

    [Header("【呼吸機制（企劃正典：光圈＝她的一口氣）】")]
    [Tooltip("開啟後光圈會一路閉合到 deathRadius，見底撐過 deathGraceSeconds 就溺斃重生；關閉則回到舊行為（只暗到 maxDarknessRadius，永不死）")]
    public bool enableSuffocationDeath = true;

    [Tooltip("溺斃半徑：光圈縮到這裡＝畫面幾乎閉合（建議 0.10 ~ 0.15）")]
    public float deathRadius = 0.12f;

    [Tooltip("光圈見底之後，再撐幾秒才溺斃（留給玩家最後找光的機會）")]
    public float deathGraceSeconds = 1.5f;

    [Header("🎵 緩解音效 (Relief SFX)")]
    [Tooltip("吃到日記紙條緩解窒息時播放的音效 (例如 水下_日誌接觸_02.wav)")]
    public AudioClip reliefSFX;
    [Range(0f, 1f)] public float sfxVolume = 0.9f;

    [Header("【視覺 - 暗化顏色】")]
    [Tooltip("暗化顏色（預設純黑）")]
    public Color vignetteColor = Color.black;

    [Tooltip("最大不透明度（1=完全不透明）")]
    [Range(0f, 1f)]
    public float maxDarknessIntensity = 1.0f;

    [Header("【視覺 - 模糊邊緣模擬（多層疊加）】")]
    [Tooltip("邊緣柔化寬度 - 主層（越大越柔和，預設 0.22）")]
    [Range(0.01f, 1f)]
    public float mainSoftness = 0.22f;

    [Tooltip("模糊暈散範圍：外層多延伸多少（模擬模糊向外擴散，預設 0.18）")]
    [Range(0f, 0.5f)]
    public float blurSpread = 0.18f;

    [Tooltip("外暈層不透明度（模糊感的關鍵，預設 0.55）")]
    [Range(0f, 1f)]
    public float outerGlowOpacity = 0.55f;

    [Tooltip("中暈層不透明度（預設 0.38）")]
    [Range(0f, 1f)]
    public float midGlowOpacity = 0.38f;

    // ==========================================
    // 內部狀態
    // ==========================================
    private Shader vignetteShader;

    // 三層 + 閃光層
    private Material matMain;    // 主暗化層（最內，最不透明）
    private Material matMid;     // 中暈層
    private Material matOuter;   // 外暈層（最外，最透明，延伸最廣）
    private RawImage imgMain;
    private RawImage imgMid;
    private RawImage imgOuter;
    private RawImage imgFlash;

    private Canvas overlayCanvas;

    private float currentRadius;
    private bool isRelieving = false;
    private bool isPaused    = false;
    private float delayTimer = 0f;
    private float _deathTimer = 0f;
    private bool delayDone   = false;
    private Coroutine reliefCoroutine;

    // 公開查詢
    public float CurrentRadius        => currentRadius;
    public bool  IsAtMaxDarkness      => currentRadius <= maxDarknessRadius + 0.01f;

    // Singleton
    public static UnderwaterSuffocationEffect Instance { get; private set; }

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        currentRadius = startRadius;
        delayDone     = (startDelay <= 0f);

        AdjustSceneWaterSortingOrder();
        SetupOverlayCanvas();
        LoadShader();
        CreateAllLayers();
    }

    private void AdjustSceneWaterSortingOrder()
    {
        GameObject waterObj = GameObject.Find("water");
        if (waterObj != null)
        {
            SpriteRenderer sr = waterObj.GetComponent<SpriteRenderer>();
            if (sr != null && sr.sortingOrder >= 30)
            {
                sr.sortingOrder = 15; // 確保水色濾鏡在場景(0)之上，但在黑框(30)之下
            }
        }
    }

    private void Update()
    {
        if (overlayCanvas != null && overlayCanvas.renderMode == RenderMode.ScreenSpaceCamera && overlayCanvas.worldCamera == null)
        {
            overlayCanvas.worldCamera = Camera.main;
        }

        if (vignetteShader == null || imgMain == null) return;

        // 等候 startDelay
        if (!delayDone)
        {
            delayTimer += Time.deltaTime;
            if (delayTimer >= startDelay) delayDone = true;
        }

        // 暗化推進（呼吸機制開啟時，光圈可一路閉合到 deathRadius；否則停在 maxDarknessRadius）
        float radiusFloor = enableSuffocationDeath ? Mathf.Min(maxDarknessRadius, deathRadius) : maxDarknessRadius;
        if (effectEnabled && delayDone && !isPaused && !isRelieving)
        {
            if (currentRadius > radiusFloor)
            {
                currentRadius -= darknessIncreaseRate * Time.deltaTime;
                currentRadius  = Mathf.Max(currentRadius, radiusFloor);
            }
        }

        // 呼吸機制：一口氣用完 → 短暫倒數 → 溺斃重生
        if (enableSuffocationDeath && effectEnabled && delayDone && !PlayerRespawnSystem.IsAnyRespawning)
        {
            if (currentRadius <= deathRadius + 0.005f)
            {
                _deathTimer += Time.deltaTime;
                if (_deathTimer >= deathGraceSeconds)
                {
                    _deathTimer = 0f;
                    Debug.LogWarning("🫁【呼吸機制】一口氣用完了，光圈閉合——觸發溺斃重生。");
                    PlayerRespawnSystem sys = FindFirstObjectByType<PlayerRespawnSystem>();
                    if (sys != null)
                    {
                        sys.enabled = true;
                        sys.TriggerRespawn();
                    }
                    ResetEffect();   // 重生＝重新呼吸
                }
            }
            else
            {
                _deathTimer = 0f;
            }
        }

        // 將目前半徑推給三層 Shader
        UpdateLayerMaterials();
    }

    // ==========================================
    // 對外 API
    // ==========================================

    /// <summary>
    /// 由 NoteRelief 呼叫：觸發緩解效果（只縮回約 reliefAmount，不完全清除）
    /// </summary>
    public void TriggerRelief()
    {
        RestoreBreath(reliefAmount);
    }

    /// <summary>光球＝氧氣（0805 企劃定案）：把呼吸光圈補回指定量。由光絮吸收、日誌緩解等呼叫。</summary>
    public void RestoreBreath(float amount)
    {
        if (reliefCoroutine != null) StopCoroutine(reliefCoroutine);
        reliefCoroutine = StartCoroutine(ReliefSequence(amount));
    }

    /// <summary>強制重置（重生或場景重開用）</summary>
    public void ResetEffect()
    {
        StopAllCoroutines();
        isRelieving   = false;
        isPaused      = false;
        delayTimer    = 0f;
        _deathTimer   = 0f;
        delayDone     = (startDelay <= 0f);
        currentRadius = startRadius;
    }

    // --- IResettable：任何一種死法重生後，都重新呼吸 ---
    public void ResetToInitialState()
    {
        ResetEffect();
    }

    /// <summary>動態開關效果</summary>
    public void SetEnabled(bool enabled)
    {
        effectEnabled = enabled;
        if (imgMain  != null) imgMain.enabled  = enabled;
        if (imgMid   != null) imgMid.enabled   = enabled;
        if (imgOuter != null) imgOuter.enabled = enabled;
        if (!enabled) currentRadius = startRadius;
    }

    // ==========================================
    // 緩解協程
    // ==========================================
    private IEnumerator ReliefSequence(float amount)
    {
        isRelieving = true;
        isPaused    = false;

        if (reliefSFX != null)
        {
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(reliefSFX, sfxVolume);
            else AudioSource.PlayClipAtPoint(reliefSFX, Camera.main != null ? Camera.main.transform.position : Vector3.zero, AudioManager.ScaleSfx(sfxVolume));
        }

        if (imgFlash != null) StartCoroutine(FlashEffect());

        // 只恢復 reliefAmount，不超過 startRadius - 0.05（保持部分窒息感）
        float targetRadius = Mathf.Clamp(
            currentRadius + amount,
            Mathf.Min(maxDarknessRadius, deathRadius),
            startRadius - 0.05f
        );

        while (currentRadius < targetRadius)
        {
            currentRadius += reliefSpeed * Time.deltaTime;
            currentRadius  = Mathf.Min(currentRadius, targetRadius);
            yield return null;
        }

        isRelieving = false;

        // 暫停後繼續暗化
        isPaused = true;
        yield return new WaitForSeconds(reliefPauseDuration);
        isPaused = false;
    }

    private IEnumerator FlashEffect()
    {
        if (imgFlash == null) yield break;
        imgFlash.enabled = true;
        float timer = 0f;
        while (timer < reliefFlashDuration)
        {
            timer += Time.deltaTime;
            Color c = reliefFlashColor;
            c.a = reliefFlashColor.a * (1f - timer / reliefFlashDuration);
            imgFlash.color = c;
            yield return null;
        }
        imgFlash.color   = Color.clear;
        imgFlash.enabled = false;
    }

    // ==========================================
    // Shader 材質更新
    // ==========================================
    private void UpdateLayerMaterials()
    {
        if (matMain == null) return;

        float r = currentRadius;

        // 主層：核心暗區（緊貼 currentRadius）
        matMain.SetFloat("_Radius",    r);
        matMain.SetFloat("_Softness",  mainSoftness);
        matMain.SetFloat("_Intensity", maxDarknessIntensity);
        matMain.SetColor("_Color",     vignetteColor);

        // 中暈層：半徑多加 blurSpread * 0.5，軟化寬度更大
        if (matMid != null)
        {
            matMid.SetFloat("_Radius",    r + blurSpread * 0.5f);
            matMid.SetFloat("_Softness",  mainSoftness + 0.12f);
            matMid.SetFloat("_Intensity", midGlowOpacity * maxDarknessIntensity);
            matMid.SetColor("_Color",     vignetteColor);
        }

        // 外暈層：半徑多加 blurSpread，非常軟的寬過渡，最低不透明度
        if (matOuter != null)
        {
            matOuter.SetFloat("_Radius",    r + blurSpread);
            matOuter.SetFloat("_Softness",  mainSoftness + 0.28f);
            matOuter.SetFloat("_Intensity", outerGlowOpacity * maxDarknessIntensity);
            matOuter.SetColor("_Color",     vignetteColor);
        }
    }

    // ==========================================
    // 初始化
    // ==========================================
    private void LoadShader()
    {
        vignetteShader = Shader.Find("Custom/UnderwaterVignette");
        if (vignetteShader == null)
        {
            Debug.LogError("[SuffocationEffect] 找不到 Shader 'Custom/UnderwaterVignette'！\n" +
                           "請確認 Assets/Shaders/UnderwaterVignette.shader 存在且已被 Unity 編譯（Project 面板按 Ctrl+R）。");
            enabled = false;
        }
    }

    private void SetupOverlayCanvas()
    {
        Camera mainCam = Camera.main;

        GameObject canvasObj = new GameObject("SuffocationCanvas");
        canvasObj.transform.SetParent(transform, false);
        overlayCanvas = canvasObj.AddComponent<Canvas>();

        if (mainCam != null)
        {
            // 改為相機渲染模式，使黑框能依照 Sorting Order 排序
            overlayCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            overlayCanvas.worldCamera = mainCam;
            overlayCanvas.planeDistance = 5f;
            overlayCanvas.sortingLayerName = "Default";
            overlayCanvas.sortingOrder = 30; // 高於背景與水下世界 (0)，但低於設定 UI (48~60)
        }
        else
        {
            overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            overlayCanvas.sortingOrder = 30;
        }

        canvasObj.AddComponent<UnityEngine.UI.CanvasScaler>();
        canvasObj.AddComponent<UnityEngine.UI.GraphicRaycaster>();
    }

    private void CreateAllLayers()
    {
        if (vignetteShader == null) return;

        // 依序建立：外暈 → 中暈 → 主層（越後建立越在上面）
        matOuter  = new Material(vignetteShader);
        imgOuter  = CreateFullscreenLayer("Vignette_Outer", matOuter);

        matMid    = new Material(vignetteShader);
        imgMid    = CreateFullscreenLayer("Vignette_Mid",   matMid);

        matMain   = new Material(vignetteShader);
        imgMain   = CreateFullscreenLayer("Vignette_Main",  matMain);

        // 閃光層（最上）
        GameObject flashObj = new GameObject("Vignette_Flash");
        flashObj.transform.SetParent(overlayCanvas.transform, false);
        imgFlash              = flashObj.AddComponent<RawImage>();
        imgFlash.color        = Color.clear;
        imgFlash.raycastTarget = false;
        imgFlash.enabled      = false;
        FullscreenRect(imgFlash.GetComponent<RectTransform>());

        // 立刻初始化材質
        UpdateLayerMaterials();
    }

    private RawImage CreateFullscreenLayer(string name, Material mat)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(overlayCanvas.transform, false);
        RawImage img       = obj.AddComponent<RawImage>();
        img.material       = mat;
        img.color          = Color.white;
        img.raycastTarget  = false;
        FullscreenRect(img.GetComponent<RectTransform>());
        return img;
    }

    private void FullscreenRect(RectTransform rt)
    {
        rt.anchorMin  = Vector2.zero;
        rt.anchorMax  = Vector2.one;
        rt.offsetMin  = Vector2.zero;
        rt.offsetMax  = Vector2.zero;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (matMain  != null) Destroy(matMain);
        if (matMid   != null) Destroy(matMid);
        if (matOuter != null) Destroy(matOuter);
        if (overlayCanvas != null && overlayCanvas.gameObject != null)
        {
            Destroy(overlayCanvas.gameObject);
        }
    }
}
