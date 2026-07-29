using UnityEngine;
using UnityEngine.UI;
using System.Collections;

/// <summary>
/// 水下窒息視覺效果系統（含模糊效果版）
/// - 從螢幕外圍逐漸向中心漸黑＋模糊，製造窒息感
/// - startDelay 秒後才開始暗化
/// - 最深暗化到 maxDarknessRadius（外圍約 50% 面積）
/// - 吃到 Note Paper 後暗化範圍縮回 reliefAmount，但仍持續增加
/// - 所有參數均可在 Inspector 調整
/// </summary>
public class UnderwaterSuffocationEffect : MonoBehaviour
{
    // ==========================================
    // Inspector 可調參數
    // ==========================================

    [Header("【啟用控制】")]
    [Tooltip("是否啟用水下窒息效果")]
    public bool effectEnabled = true;

    [Tooltip("進入水下場景後幾秒才開始暗化（讓玩家先看清楚環境）")]
    public float startDelay = 4f;

    [Header("【暗化速度與面積】")]
    [Tooltip("暗化速度：每秒收縮的圓心半徑量（越大越快，預設 0.03）")]
    public float darknessIncreaseRate = 0.03f;

    [Tooltip("最大暗化半徑上限（0=全黑，1=完全透明）。預設 0.4 約等於外圍 50% 面積變暗後停止")]
    public float maxDarknessRadius = 0.4f;

    [Tooltip("起始圓心半徑（1=完全透明，無效果）")]
    public float startRadius = 1.0f;

    [Header("【Note Paper 緩解設定】")]
    [Tooltip("每次吃到 Note Paper，圓心半徑增加的量（預設 0.2 = 緩解 20%）")]
    public float reliefAmount = 0.2f;

    [Tooltip("緩解時半徑恢復的速度（每秒，預設 0.5）")]
    public float reliefSpeed = 0.5f;

    [Tooltip("緩解後暫停繼續暗化的時間（秒，預設 2.0）")]
    public float reliefPauseDuration = 2.0f;

    [Tooltip("吃到 Note Paper 觸發的閃光顏色（視覺回饋）")]
    public Color reliefFlashColor = new Color(0.4f, 0.85f, 1f, 0.35f);

    [Tooltip("閃光持續時間（秒）")]
    public float reliefFlashDuration = 0.5f;

    [Header("【視覺 - 暗化顏色】")]
    [Tooltip("暗化區域的顏色（預設純黑）")]
    public Color vignetteColor = Color.black;

    [Tooltip("邊緣柔化過渡寬度（越大越柔和，預設 0.28）")]
    [Range(0.01f, 1f)]
    public float edgeSoftness = 0.28f;

    [Tooltip("最大不透明度（1=完全不透明，0.85=稍微透出輪廓）")]
    [Range(0f, 1f)]
    public float maxDarknessIntensity = 1.0f;

    [Header("【視覺 - 模糊效果】")]
    [Tooltip("模糊強度（0=不模糊 純暗色, 30=極強模糊）")]
    [Range(0f, 30f)]
    public float blurStrength = 8f;

    [Tooltip("暗色混合比例（1=完全暗色，0=只模糊不加暗色）")]
    [Range(0f, 1f)]
    public float blurDarkMix = 0.7f;

    // ==========================================
    // 內部狀態
    // ==========================================
    private Material vignetteMaterial;
    private RawImage vignetteImage;
    private RawImage flashImage;
    private Canvas overlayCanvas;

    private float currentRadius;
    private bool isRelieving = false;
    private bool isPaused = false;
    private Coroutine reliefCoroutine;

    // 公開給外部腳本查詢
    public float CurrentRadius => currentRadius;
    public bool IsAtMaxDarkness => currentRadius <= maxDarknessRadius + 0.01f;

    // Singleton
    public static UnderwaterSuffocationEffect Instance { get; private set; }

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        currentRadius = startRadius;
        SetupOverlayCanvas();
        CreateVignetteMaterial();
        CreateVignetteUI();

        // 確認 effectEnabled 開始時為 true，但先等 startDelay 秒
        if (startDelay > 0f)
        {
            StartCoroutine(DelayedStart());
        }
    }

    private IEnumerator DelayedStart()
    {
        // 保持 currentRadius = startRadius（完全透明），只是靜候
        float timer = 0f;
        while (timer < startDelay)
        {
            if (!effectEnabled) { yield return null; continue; }
            timer += Time.deltaTime;
            yield return null;
        }
        // 開始正式暗化（什麼都不用做，Update 會自動接管）
    }

    private void Update()
    {
        if (vignetteMaterial == null) return;

        // 若效果開啟、不在緩解中、不在暫停中，且尚未達最大暗化程度
        if (effectEnabled && !isPaused && !isRelieving)
        {
            // 從 startDelay 結束後才開始縮小
            if (currentRadius > maxDarknessRadius)
            {
                currentRadius -= darknessIncreaseRate * Time.deltaTime;
                currentRadius = Mathf.Max(currentRadius, maxDarknessRadius);
            }
        }

        // 把所有參數推給 Shader
        vignetteMaterial.SetFloat("_Radius",       currentRadius);
        vignetteMaterial.SetFloat("_Softness",     edgeSoftness);
        vignetteMaterial.SetFloat("_Intensity",    maxDarknessIntensity);
        vignetteMaterial.SetColor("_Color",        vignetteColor);
        vignetteMaterial.SetFloat("_BlurAmount",   blurStrength);
        vignetteMaterial.SetFloat("_BlurDarkMix",  blurDarkMix);
    }

    // ==========================================
    // 對外 API
    // ==========================================

    /// <summary>
    /// 由 NoteRelief 呼叫：觸發緩解效果（縮小暗區約 reliefAmount）
    /// </summary>
    public void TriggerRelief()
    {
        if (reliefCoroutine != null) StopCoroutine(reliefCoroutine);
        reliefCoroutine = StartCoroutine(ReliefSequence());
    }

    /// <summary>強制重置效果（重生或場景重開用）</summary>
    public void ResetEffect()
    {
        StopAllCoroutines();
        isRelieving = false;
        isPaused    = false;
        currentRadius = startRadius;
    }

    /// <summary>動態開關效果</summary>
    public void SetEnabled(bool enabled)
    {
        effectEnabled = enabled;
        if (vignetteImage != null) vignetteImage.enabled = enabled;
        if (!enabled) currentRadius = startRadius;
    }

    // ==========================================
    // 緩解協程
    // ==========================================
    private IEnumerator ReliefSequence()
    {
        isRelieving = true;
        isPaused    = false;

        // 短暫螢幕閃光（氧氣補充感）
        if (flashImage != null)
            StartCoroutine(FlashEffect());

        // 目標：只恢復 reliefAmount（不超過起始值，也不完全清除）
        float targetRadius = Mathf.Clamp(
            currentRadius + reliefAmount,
            maxDarknessRadius,          // 不能比最大暗化還透明（下限）
            startRadius - 0.05f         // 不能完全恢復到起始（保持窒息感）
        );

        // 緩慢恢復到目標半徑
        while (currentRadius < targetRadius)
        {
            currentRadius += reliefSpeed * Time.deltaTime;
            currentRadius  = Mathf.Min(currentRadius, targetRadius);
            yield return null;
        }

        isRelieving = false;

        // 短暫暫停，讓玩家喘口氣，之後繼續變暗
        isPaused = true;
        yield return new WaitForSeconds(reliefPauseDuration);
        isPaused = false;
    }

    private IEnumerator FlashEffect()
    {
        if (flashImage == null) yield break;
        flashImage.enabled = true;
        float timer = 0f;
        while (timer < reliefFlashDuration)
        {
            timer += Time.deltaTime;
            float t = timer / reliefFlashDuration;
            Color c = reliefFlashColor;
            c.a = reliefFlashColor.a * (1f - t);   // 隨時間淡出
            flashImage.color = c;
            yield return null;
        }
        flashImage.color   = Color.clear;
        flashImage.enabled = false;
    }

    // ==========================================
    // Canvas 與 UI 初始化
    // ==========================================
    private void SetupOverlayCanvas()
    {
        // 嘗試找已存在高排序 Overlay Canvas
        foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (c.renderMode == RenderMode.ScreenSpaceOverlay && c.sortingOrder >= 90)
            {
                overlayCanvas = c;
                return;
            }
        }

        // 找不到就自己建
        GameObject canvasObj = new GameObject("SuffocationCanvas");
        overlayCanvas = canvasObj.AddComponent<Canvas>();
        overlayCanvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        overlayCanvas.sortingOrder = 95;
        canvasObj.AddComponent<UnityEngine.UI.CanvasScaler>();
        canvasObj.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        DontDestroyOnLoad(canvasObj);
    }

    private void CreateVignetteMaterial()
    {
        Shader shader = Shader.Find("Custom/UnderwaterVignette");
        if (shader == null)
        {
            Debug.LogError("[SuffocationEffect] 找不到 Shader 'Custom/UnderwaterVignette'！\n" +
                           "請確認 Assets/Shaders/UnderwaterVignette.shader 存在且已被 Unity 編譯。\n" +
                           "可嘗試在 Project 面板按 Ctrl+R 重新 Import 全部資產。");
            enabled = false;
            return;
        }
        vignetteMaterial = new Material(shader);
    }

    private void CreateVignetteUI()
    {
        if (vignetteMaterial == null) return;

        // --- Vignette 主層 ---
        GameObject imgObj = new GameObject("SuffocationVignette");
        imgObj.transform.SetParent(overlayCanvas.transform, false);

        vignetteImage              = imgObj.AddComponent<RawImage>();
        vignetteImage.material     = vignetteMaterial;
        vignetteImage.color        = Color.white;
        vignetteImage.raycastTarget = false;

        RectTransform rt = imgObj.GetComponent<RectTransform>();
        rt.anchorMin      = Vector2.zero;
        rt.anchorMax      = Vector2.one;
        rt.offsetMin      = Vector2.zero;
        rt.offsetMax      = Vector2.zero;

        // --- 閃光覆蓋層（吃 Note Paper 時短暫亮藍光） ---
        GameObject flashObj = new GameObject("SuffocationFlash");
        flashObj.transform.SetParent(overlayCanvas.transform, false);

        flashImage              = flashObj.AddComponent<RawImage>();
        flashImage.color        = Color.clear;
        flashImage.raycastTarget = false;
        flashImage.enabled      = false;

        RectTransform flashRt = flashObj.GetComponent<RectTransform>();
        flashRt.anchorMin      = Vector2.zero;
        flashRt.anchorMax      = Vector2.one;
        flashRt.offsetMin      = Vector2.zero;
        flashRt.offsetMax      = Vector2.zero;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (vignetteMaterial != null) Destroy(vignetteMaterial);
    }
}
