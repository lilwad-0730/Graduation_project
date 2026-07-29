using UnityEngine;
using UnityEngine.UI;
using System.Collections;

/// <summary>
/// 水下窒息視覺效果系統
/// - 從螢幕外圍逐漸向中心漸黑，製造窒息感
/// - 最深暗化到 maxDarknessRadius（預設覆蓋螢幕 50%）
/// - 吃到 Note Paper 後暗化範圍縮回 20%，但仍會持續增加
/// - 所有參數均可在 Inspector 調整
/// </summary>
public class UnderwaterSuffocationEffect : MonoBehaviour
{
    // ==========================================
    // Inspector 可調參數
    // ==========================================

    [Header("【啟用控制】")]
    [Tooltip("是否啟用水下窒息效果 (可動態切換)")]
    public bool effectEnabled = true;

    [Tooltip("效果生效延遲 (進入水下場景後幾秒才開始暗化，給玩家緩衝期)")]
    public float startDelay = 3f;

    [Header("【暗化速度與面積】")]
    [Tooltip("暗化速度：每秒減少的透明圓心半徑量 (值越大越快變暗，預設 0.04)")]
    public float darknessIncreaseRate = 0.04f;

    [Tooltip("最大暗化半徑 (0=全黑, 1=全透明)。預設 0.4 表示外圍 50% 面積變暗後停止")]
    public float maxDarknessRadius = 0.4f;

    [Tooltip("起始透明圓心半徑 (遊戲剛開始時的初始狀態，1=完全無效果)")]
    public float startRadius = 1.0f;

    [Header("【Note Paper 緩解設定】")]
    [Tooltip("每次吃到 Note Paper 後，圓心半徑恢復的量 (預設 0.2 = 緩解 20%)")]
    public float reliefAmount = 0.2f;

    [Tooltip("Note Paper 緩解時的恢復速度 (每秒恢復量，預設 0.6，比暗化快)")]
    public float reliefSpeed = 0.6f;

    [Tooltip("緩解後暫停重新暗化的時間 (秒，預設 1.5)")]
    public float reliefPauseDuration = 1.5f;

    [Tooltip("吃到 Note Paper 緩解時螢幕閃光顏色")]
    public Color reliefFlashColor = new Color(0.4f, 0.8f, 1f, 0.3f);

    [Tooltip("閃光持續時間 (秒)")]
    public float reliefFlashDuration = 0.4f;

    [Header("【視覺調整】")]
    [Tooltip("暗化顏色 (預設純黑)")]
    public Color vignetteColor = Color.black;

    [Tooltip("邊緣柔化過渡寬度 (越大越柔和, 預設 0.25)")]
    public float edgeSoftness = 0.25f;

    [Tooltip("最大不透明度上限 (1=完全不透明的黑色, 0.9=略微透邊)")]
    [Range(0f, 1f)]
    public float maxDarknessIntensity = 1.0f;

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

    // 公開給外部腳本查詢用
    public float CurrentRadius => currentRadius;
    public bool IsActive => effectEnabled && currentRadius < startRadius;

    // ==========================================
    // Singleton (可選，方便 NoteRelief 腳本存取)
    // ==========================================
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

        if (effectEnabled && startDelay > 0f)
        {
            StartCoroutine(DelayedStart());
        }
    }

    private IEnumerator DelayedStart()
    {
        effectEnabled = false;
        yield return new WaitForSeconds(startDelay);
        effectEnabled = true;
    }

    private void Update()
    {
        if (vignetteMaterial == null) return;

        // 若效果開啟且尚未達最大暗化程度，持續推進暗化
        if (effectEnabled && !isPaused && !isRelieving)
        {
            float minRadius = maxDarknessRadius;
            if (currentRadius > minRadius)
            {
                currentRadius -= darknessIncreaseRate * Time.deltaTime;
                currentRadius = Mathf.Max(currentRadius, minRadius);
            }
        }

        // 更新 Shader 參數
        vignetteMaterial.SetFloat("_Radius", currentRadius);
        vignetteMaterial.SetFloat("_Softness", edgeSoftness);
        vignetteMaterial.SetFloat("_Intensity", maxDarknessIntensity);
        vignetteMaterial.SetColor("_Color", vignetteColor);
    }

    // ==========================================
    // 對外 API
    // ==========================================

    /// <summary>
    /// 由 NoteRelief 腳本呼叫：玩家吃到 Note Paper，執行緩解效果
    /// </summary>
    public void TriggerRelief()
    {
        if (reliefCoroutine != null) StopCoroutine(reliefCoroutine);
        reliefCoroutine = StartCoroutine(ReliefSequence());
    }

    /// <summary>
    /// 強制重置效果 (場景重開或玩家重生時使用)
    /// </summary>
    public void ResetEffect()
    {
        StopAllCoroutines();
        isRelieving = false;
        isPaused = false;
        currentRadius = startRadius;
    }

    /// <summary>
    /// 動態開關效果
    /// </summary>
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
        isPaused = false;

        // 觸發螢幕短暫閃光（氧氣補充感）
        if (flashImage != null) StartCoroutine(FlashEffect());

        // 圓心半徑快速擴大 (變亮)，但上限不超過 startRadius
        float targetRadius = Mathf.Min(currentRadius + reliefAmount, startRadius - 0.05f);

        while (currentRadius < targetRadius)
        {
            currentRadius += reliefSpeed * Time.deltaTime;
            currentRadius = Mathf.Min(currentRadius, targetRadius);
            yield return null;
        }

        isRelieving = false;

        // 緩解後暫停一小段時間，讓玩家喘口氣
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
            float alpha = Mathf.PingPong(timer / reliefFlashDuration, 0.5f) * 2f;
            Color c = reliefFlashColor;
            c.a = reliefFlashColor.a * (1f - (timer / reliefFlashDuration));
            flashImage.color = c;
            yield return null;
        }
        flashImage.enabled = false;
    }

    // ==========================================
    // Canvas 與 UI 初始化
    // ==========================================

    private void SetupOverlayCanvas()
    {
        // 嘗試找已存在的 Overlay Canvas，若無則創建
        Canvas[] allCanvas = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
        foreach (var c in allCanvas)
        {
            if (c.renderMode == RenderMode.ScreenSpaceOverlay && c.sortingOrder >= 90)
            {
                overlayCanvas = c;
                break;
            }
        }

        if (overlayCanvas == null)
        {
            GameObject canvasObj = new GameObject("SuffocationCanvas");
            overlayCanvas = canvasObj.AddComponent<Canvas>();
            overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            overlayCanvas.sortingOrder = 95;
            canvasObj.AddComponent<UnityEngine.UI.CanvasScaler>();
            canvasObj.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            DontDestroyOnLoad(canvasObj);
        }
    }

    private void CreateVignetteMaterial()
    {
        // 嘗試從 Resources 或 Shaders 資料夾載入 Shader
        Shader shader = Shader.Find("Custom/UnderwaterVignette");
        if (shader == null)
        {
            Debug.LogError("[SuffocationEffect] 找不到 Shader 'Custom/UnderwaterVignette'！" +
                           "請確認 Assets/Shaders/UnderwaterVignette.shader 已存在且已被 Unity 編譯。");
            enabled = false;
            return;
        }
        vignetteMaterial = new Material(shader);
    }

    private void CreateVignetteUI()
    {
        if (vignetteMaterial == null) return;

        // 建立全螢幕 RawImage 顯示 Vignette
        GameObject imgObj = new GameObject("SuffocationVignette");
        imgObj.transform.SetParent(overlayCanvas.transform, false);

        vignetteImage = imgObj.AddComponent<RawImage>();
        vignetteImage.material = vignetteMaterial;
        vignetteImage.color = Color.white;
        vignetteImage.raycastTarget = false;

        RectTransform rt = imgObj.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        // 建立閃光覆蓋層
        GameObject flashObj = new GameObject("SuffocationFlash");
        flashObj.transform.SetParent(overlayCanvas.transform, false);

        flashImage = flashObj.AddComponent<RawImage>();
        flashImage.color = new Color(0f, 0f, 0f, 0f);
        flashImage.raycastTarget = false;
        flashImage.enabled = false;

        RectTransform flashRt = flashObj.GetComponent<RectTransform>();
        flashRt.anchorMin = Vector2.zero;
        flashRt.anchorMax = Vector2.one;
        flashRt.offsetMin = Vector2.zero;
        flashRt.offsetMax = Vector2.zero;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (vignetteMaterial != null) Destroy(vignetteMaterial);
    }
}
