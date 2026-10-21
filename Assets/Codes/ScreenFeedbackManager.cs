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
                _instance = FindAnyObjectByType<ScreenFeedbackManager>();
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
    public float maxRedVignetteAlpha = 0.8f;

    [Tooltip("受傷紅邊的顏色（偏暗的血紅，不是螢光紅）")]
    public Color hurtColor = new Color(0.85f, 0.05f, 0.07f, 1f);

    [Tooltip("受傷紅邊單次閃爍的半週期時間 (秒，設定為 0.25f 到 0.35f 會讓頻率變慢顯得更柔和)")]
    public float flashHalfCycleDuration = 0.3f;

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
        vignetteImage.color = new Color(hurtColor.r, hurtColor.g, hurtColor.b, 0f);
        
        DontDestroyOnLoad(canvasObj);
    }

    // 動態生成 2D 漸層紅邊貼圖 (無需外部 PNG，全自動畫出)
    // ★1021 原本用「方形」距離，紅框四個角是硬轉角、看起來方方的；改成橢圓（貼合螢幕）＋平滑曲線，
    //   中間完全不紅、邊緣漸漸變濃、四個角最濃但沒有稜角。貼圖只畫透明度，顏色由 Image.color 決定。
    private Texture2D CreateVignetteTexture()
    {
        int size = 256;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        Color[] colors = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nx = ((x + 0.5f) / size) * 2f - 1f;
                float ny = ((y + 0.5f) / size) * 2f - 1f;
                float r = Mathf.Sqrt(nx * nx + ny * ny);                 // 圓形距離：貼圖拉滿螢幕就變成橢圓
                float t = Mathf.Clamp01((r - 0.3f) / 0.85f);
                float alpha = t * t * (3f - 2f * t);                     // smoothstep：沒有一圈明顯的邊
                alpha = Mathf.Pow(alpha, 0.9f);
                colors[y * size + x] = new Color(1f, 1f, 1f, alpha);
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

        // 2. 啟動紅邊閃爍：閃爍 3 下，時間由變數控制以減慢頻率
        if (flashCoroutine != null) StopCoroutine(flashCoroutine);
        flashCoroutine = StartCoroutine(FlashRedBorderCoroutine(3, flashHalfCycleDuration));
    }

    private void TriggerCameraShake(float duration, float power)
    {
        shakeDuration = duration;
        shakePower = power;
        shakeFade = power / duration;
    }

    // ★1021 原本是「慢慢亮、慢慢暗」重複 3 次（約 1.8 秒），像警示燈。
    //   改成像被打到：一下子衝到最紅（0.07 秒）→ 停一下（0.12 秒，讓人看得到）→ 退（0.75 秒）→ 一次小一點的餘波。共約 1.3 秒。
    private IEnumerator FlashRedBorderCoroutine(int flashCount, float halfCycleDuration)
    {
        if (vignetteImage == null) yield break;

        yield return HitPulse(0.07f, 0.12f, 0.75f, maxRedVignetteAlpha);
        yield return HitPulse(0.10f, 0.08f, 0.55f, maxRedVignetteAlpha * 0.6f);

        SetVignette(0f);
    }

    private IEnumerator HitPulse(float rise, float hold, float fall, float peak)
    {
        float timer = 0f;
        while (timer < rise)
        {
            timer += Time.unscaledDeltaTime;
            SetVignette(Mathf.Lerp(0f, peak, Mathf.Clamp01(timer / rise)));
            yield return null;
        }
        SetVignette(peak);
        yield return new WaitForSecondsRealtime(hold);
        timer = 0f;
        while (timer < fall)
        {
            timer += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(timer / fall);
            SetVignette(peak * (1f - u) * (1f - u));                     // 先快後慢地退
            yield return null;
        }
    }

    private void SetVignette(float alpha)
    {
        if (vignetteImage == null) return;
        vignetteImage.color = new Color(hurtColor.r, hurtColor.g, hurtColor.b, Mathf.Clamp01(alpha));
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
