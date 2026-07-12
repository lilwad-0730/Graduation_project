using UnityEngine;
using UnityEngine.UI;
using System.Collections;

/// <summary>
/// 螢幕受傷回饋管理器。
/// 當玩家受傷時，提供相機震動與螢幕邊緣閃爍紅色的漸層效果。
/// 自動生成，無須手動在場景中放置物件。
/// </summary>
[DefaultExecutionOrder(99999)] // 確保在 Cinemachine 計算完位置後執行，強行抖動相機
public class ScreenFeedbackManager : MonoBehaviour
{
    public static ScreenFeedbackManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<ScreenFeedbackManager>();
                if (_instance == null)
                {
                    GameObject go = new GameObject("ScreenFeedbackManager_AutoCreated");
                    _instance = go.AddComponent<ScreenFeedbackManager>();
                }
            }
            return _instance;
        }
    }
    private static ScreenFeedbackManager _instance;

    [Header("UI 設定")]
    [Tooltip("受傷紅邊的最高透明度 (0 ~ 1)")]
    [Range(0f, 1f)]
    public float maxRedVignetteAlpha = 0.5f;

    private Canvas feedbackCanvas;
    private Image vignetteImage;
    private Texture2D vignetteTexture;

    // 相機震動變數
    private float shakeDuration = 0f;
    private float shakePower = 0f;
    private float shakeFade = 0f;
    private Vector3 currentShakeOffset = Vector3.zero;

    private Coroutine flashCoroutine;

    private void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(gameObject);
            CreateFeedbackUI();
        }
        else if (_instance != this)
        {
            Destroy(gameObject);
        }
    }

    private void OnEnable()
    {
        // 換場景時重新檢查並生成 UI
        if (feedbackCanvas == null)
        {
            CreateFeedbackUI();
        }
    }

    private void CreateFeedbackUI()
    {
        // 1. 建立 Canvas
        GameObject canvasObj = new GameObject("ScreenFeedbackCanvas_System");
        feedbackCanvas = canvasObj.AddComponent<Canvas>();
        feedbackCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        feedbackCanvas.sortingOrder = 998; // 比漸黑轉場 (999) 略低，防止擋住黑屏
        
        canvasObj.AddComponent<CanvasScaler>();
        
        // 2. 建立漸層紅邊 Image
        GameObject vignetteObj = new GameObject("RedVignette");
        vignetteObj.transform.SetParent(canvasObj.transform, false);
        vignetteImage = vignetteObj.AddComponent<Image>();
        
        // 動態產生漸層貼圖
        vignetteTexture = CreateVignetteTexture();
        vignetteImage.sprite = Sprite.Create(vignetteTexture, new Rect(0, 0, vignetteTexture.width, vignetteTexture.height), new Vector2(0.5f, 0.5f));
        
        // 延展填滿整個螢幕
        RectTransform rectTrans = vignetteImage.GetComponent<RectTransform>();
        rectTrans.anchorMin = Vector2.zero;
        rectTrans.anchorMax = Vector2.one;
        rectTrans.sizeDelta = Vector2.zero;
        rectTrans.anchoredPosition = Vector2.zero;
        
        // 關閉點擊射線阻擋，以免擋住遊戲 UI
        vignetteImage.raycastTarget = false;
        
        // 預設設為全透明
        vignetteImage.color = new Color(1f, 0f, 0f, 0f);
        
        DontDestroyOnLoad(canvasObj);
    }

    // 動態生成 2D 漸層紅邊貼圖 (無需外部 PNG，全自動畫出)
    private Texture2D CreateVignetteTexture()
    {
        int size = 128;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] colors = new Color[size * size];
        
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // 標準化座標為 -1 到 1
                float nx = (x / (float)size) * 2f - 1f;
                float ny = (y / (float)size) * 2f - 1f;
                
                // 方形漸層公式，使紅邊沿著螢幕四個邊緣往內淡入
                float dist = Mathf.Max(Mathf.Abs(nx), Mathf.Abs(ny));
                
                // 從距離中心 0.45 處開始漸層
                float alpha = Mathf.Clamp01((dist - 0.45f) / 0.55f);
                
                // 紅色底，透明度漸變
                colors[y * size + x] = new Color(1f, 0f, 0f, alpha);
            }
        }
        
        tex.SetPixels(colors);
        tex.Apply();
        return tex;
    }

    /// <summary>
    /// 觸發受傷反饋：螢幕震動，並且螢幕紅邊閃爍 3 下
    /// </summary>
    public void TriggerHitFeedback()
    {
        // 1. 啟動震動：持續 0.4 秒，強度 0.3f
        TriggerCameraShake(0.4f, 0.3f);

        // 2. 啟動紅邊閃爍：閃爍 3 下，每半個週期 0.12 秒
        if (flashCoroutine != null) StopCoroutine(flashCoroutine);
        flashCoroutine = StartCoroutine(FlashRedBorderCoroutine(3, 0.12f));
    }

    private void TriggerCameraShake(float duration, float power)
    {
        shakeDuration = duration;
        shakePower = power;
        shakeFade = power / duration;
    }

    private IEnumerator FlashRedBorderCoroutine(int flashCount, float halfCycleDuration)
    {
        if (vignetteImage == null) yield break;

        for (int i = 0; i < flashCount; i++)
        {
            // A. 漸顯紅邊
            float timer = 0f;
            while (timer < halfCycleDuration)
            {
                timer += Time.deltaTime;
                float alpha = Mathf.Lerp(0f, maxRedVignetteAlpha, timer / halfCycleDuration);
                vignetteImage.color = new Color(1f, 0f, 0f, alpha);
                yield return null;
            }

            // B. 漸隱紅邊
            timer = 0f;
            while (timer < halfCycleDuration)
            {
                timer += Time.deltaTime;
                float alpha = Mathf.Lerp(maxRedVignetteAlpha, 0f, timer / halfCycleDuration);
                vignetteImage.color = new Color(1f, 0f, 0f, alpha);
                yield return null;
            }
        }

        // 確保結束時完全透明
        vignetteImage.color = new Color(1f, 0f, 0f, 0f);
    }

    private void LateUpdate()
    {
        if (shakeDuration > 0f)
        {
            shakeDuration -= Time.deltaTime;
            
            // 隨機計算抖動偏移量
            float rx = Random.Range(-1f, 1f) * shakePower;
            float ry = Random.Range(-1f, 1f) * shakePower;
            currentShakeOffset = new Vector3(rx, ry, 0f);
            
            // 漸減抖動能量
            shakePower = Mathf.MoveTowards(shakePower, 0f, shakeFade * Time.deltaTime);
            
            // 套用到主相機
            if (Camera.main != null)
            {
                Camera.main.transform.position += currentShakeOffset;
            }
        }
    }
}
