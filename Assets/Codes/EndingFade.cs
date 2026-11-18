using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// ★1118d 結局轉場：畫面慢慢變全黑 → 在黑幕下載入結局繪本 → 黑幕慢慢淡掉，露出漫畫。
/// 呼叫 EndingFade.Begin(場景, 淡出秒, 全黑停留秒, 淡入秒) 即可；黑幕是 DontDestroyOnLoad 的 Overlay Canvas，
/// 淡入結束後自己銷毀。全程用不受 timeScale 影響的時間。遊戲聲音（AudioListener.volume）跟著畫面一起淡出、淡入，最後還原成玩家設定的音量。
/// </summary>
public class EndingFade : MonoBehaviour
{
    /// <summary>比文字卡（10000）、救援提示（20000）都高。</summary>
    public const int SortingOrder = 32000;

    public static EndingFade Instance { get; private set; }

    /// <summary>目前黑幕的不透明度（測試／驗證用）。</summary>
    public float Alpha => _group != null ? _group.alpha : 0f;
    /// <summary>0＝淡出　1＝載入中／黑屏停留　2＝淡入　3＝結束</summary>
    public int Phase { get; private set; }

    private CanvasGroup _group;
    private float _origVolume = 1f;
    private bool _volumeTouched;

    public static void Begin(string scene, float fadeOut, float blackHold, float fadeIn)
    {
        if (Instance != null) return;   // 防重入
        var go = new GameObject("EndingFade（自動生成）");
        DontDestroyOnLoad(go);
        var ef = go.AddComponent<EndingFade>();
        Instance = ef;
        ef.BuildCanvas();
        ef.StartCoroutine(ef.Run(scene, Mathf.Max(0f, fadeOut), Mathf.Max(0f, blackHold), Mathf.Max(0f, fadeIn)));
    }

    private void BuildCanvas()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;
        _group = gameObject.AddComponent<CanvasGroup>();
        _group.alpha = 0f;
        _group.blocksRaycasts = false;
        _group.interactable = false;

        var imgGo = new GameObject("Black", typeof(RectTransform));
        imgGo.transform.SetParent(transform, false);
        var rt = (RectTransform)imgGo.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        var img = imgGo.AddComponent<Image>();
        img.color = Color.black;
        img.raycastTarget = true;   // 黑幕可見時擋住點擊（CanvasGroup.blocksRaycasts 控制開關）
    }

    private void SetAlpha(float a)
    {
        _group.alpha = a;
        _group.blocksRaycasts = a > 0.001f;
        if (_volumeTouched) AudioListener.volume = _origVolume * (1f - a);
    }

    private IEnumerator Run(string scene, float fadeOut, float blackHold, float fadeIn)
    {
        _origVolume = AudioListener.volume;
        _volumeTouched = true;
        Debug.Log($"【結局】★1118d 淡出 {fadeOut:F1}s → 載入 {scene} → 黑屏 {blackHold:F1}s → 淡入 {fadeIn:F1}s");

        Phase = 0;
        for (float t = 0f; t < fadeOut; t += Time.unscaledDeltaTime)
        {
            SetAlpha(Mathf.SmoothStep(0f, 1f, t / fadeOut));
            yield return null;
        }
        SetAlpha(1f);

        Phase = 1;
        if (!string.IsNullOrEmpty(scene))
        {
            AsyncOperation op = SceneLoader.Load(scene);
            if (op != null) { while (!op.isDone) yield return null; }
            // SceneLoader 在「已經在載入別的場景」時回傳 null：等到真的進了目標場景（最多 15 秒）
            float guard = 0f;
            while (SceneManager.GetActiveScene().name != scene && guard < 15f) { guard += Time.unscaledDeltaTime; yield return null; }
        }
        yield return null; yield return null; yield return null;   // 新場景的 Awake／Start 跑完、第一個畫面畫好
        for (float t = 0f; t < blackHold; t += Time.unscaledDeltaTime) yield return null;

        Phase = 2;
        for (float t = 0f; t < fadeIn; t += Time.unscaledDeltaTime)
        {
            SetAlpha(Mathf.SmoothStep(1f, 0f, t / fadeIn));
            yield return null;
        }
        SetAlpha(0f);
        Phase = 3;
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (_volumeTouched) AudioListener.volume = _origVolume;
        if (Instance == this) Instance = null;
    }
}
