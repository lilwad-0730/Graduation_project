using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// 全域選單/場景切換控制器
/// 實現「畫面碎裂後重新組合 (Shattered & Reassembled Transition)」
/// 細緻多塊小碎片散開 -> 非同步載入 Scene -> 碎片反向聚合重組 -> 銷毀
/// 100% 避免白屏、黑屏閃爍與重複觸發
/// </summary>
public class SceneTransitionController : MonoBehaviour
{
    public static SceneTransitionController Instance;

    [Header("轉轉時間與網格設定")]
    public float shatterDuration = 1.2f;
    public float reassembleDuration = 1.0f;
    public int gridRows = 8;
    public int gridCols = 12;

    private Canvas _transitionCanvas;
    private RectTransform _transitionContainer;
    private List<RectTransform> _shards = new List<RectTransform>();
    private List<Vector2> _originalPositions = new List<Vector2>();
    private List<Vector2> _shatteredOffsets = new List<Vector2>();
    private List<float> _shatteredRotations = new List<float>();
    private bool _isTransitioning;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            BuildTransitionCanvas();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void BuildTransitionCanvas()
    {
        GameObject canvasGo = new GameObject("TransitionCanvas");
        canvasGo.transform.SetParent(transform);

        _transitionCanvas = canvasGo.AddComponent<Canvas>();
        _transitionCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _transitionCanvas.sortingOrder = 9999; // 確保在最頂層

        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        canvasGo.AddComponent<GraphicRaycaster>();

        GameObject containerGo = new GameObject("ShardContainer");
        containerGo.transform.SetParent(canvasGo.transform, false);
        _transitionContainer = containerGo.AddComponent<RectTransform>();
        _transitionContainer.anchorMin = Vector2.zero;
        _transitionContainer.anchorMax = Vector2.one;
        _transitionContainer.sizeDelta = Vector2.zero;

        // 建立 12x8 = 96 個小型碎裂遮罩區塊
        float shardWidth = 1920f / gridCols;
        float shardHeight = 1080f / gridRows;

        for (int r = 0; r < gridRows; r++)
        {
            for (int c = 0; c < gridCols; c++)
            {
                GameObject shardGo = new GameObject($"Shard_{r}_{c}");
                shardGo.transform.SetParent(_transitionContainer, false);

                Image img = shardGo.AddComponent<Image>();
                // 深炭灰與暗紅邊界碎塊風格
                img.color = ( (r + c) % 2 == 0 ) ? new Color(0.05f, 0.05f, 0.07f, 1f) : new Color(0.08f, 0.03f, 0.04f, 1f);

                RectTransform rt = shardGo.GetComponent<RectTransform>();
                rt.sizeDelta = new Vector2(shardWidth + 2f, shardHeight + 2f); // 輕微 Overlap 避免露縫

                float posX = -960f + shardWidth * c + shardWidth * 0.5f;
                float posY = -540f + shardHeight * r + shardHeight * 0.5f;
                Vector2 origPos = new Vector2(posX, posY);
                rt.anchoredPosition = origPos;

                _shards.Add(rt);
                _originalPositions.Add(origPos);

                // 隨機爆裂位移與旋轉角度
                float randomAngle = Random.Range(-180f, 180f);
                float dist = Random.Range(300f, 800f);
                Vector2 dir = new Vector2(Mathf.Cos(randomAngle * Mathf.Deg2Rad), Mathf.Sin(randomAngle * Mathf.Deg2Rad));

                _shatteredOffsets.Add(origPos + dir * dist);
                _shatteredRotations.Add(Random.Range(-45f, 45f));

                shardGo.SetActive(false); // 預設隱藏
            }
        }
    }

    public void TransitionToScene(string targetSceneName)
    {
        if (_isTransitioning) return;

        if (string.IsNullOrWhiteSpace(targetSceneName))
        {
            Debug.LogError("[SceneTransitionController] Target scene name is empty.");
            return;
        }

        StartCoroutine(TransitionRoutine(targetSceneName.Trim()));
    }

    private IEnumerator TransitionRoutine(string targetSceneName)
    {
        _isTransitioning = true;

        // 1. 開啟所有碎裂 Shards
        ResetAndShowShards();

        // 2. 播放「畫面碎裂與散開」動畫
        float elapsed = 0f;
        while (elapsed < shatterDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / shatterDuration;
            float smoothT = Mathf.SmoothStep(0f, 1f, t);

            for (int i = 0; i < _shards.Count; i++)
            {
                _shards[i].anchoredPosition = Vector2.Lerp(_originalPositions[i], _shatteredOffsets[i], smoothT);
                _shards[i].localRotation = Quaternion.Euler(0f, 0f, _shatteredRotations[i] * smoothT);
                _shards[i].localScale = Vector3.Lerp(Vector3.one, new Vector3(0.2f, 0.2f, 1f), smoothT);
            }
            yield return null;
        }

        // 3. 非同步載入目標 Scene
        AsyncOperation asyncLoad = null;
        try
        {
            asyncLoad = SceneManager.LoadSceneAsync(targetSceneName);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[SceneTransitionController] Failed to load scene '{targetSceneName}': {ex.Message}");
        }

        if (asyncLoad == null)
        {
            HideShards();
            _isTransitioning = false;
            yield break;
        }

        asyncLoad.allowSceneActivation = false;

        while (asyncLoad.progress < 0.9f)
        {
            yield return null;
        }

        // 允許切換場景
        asyncLoad.allowSceneActivation = true;

        // 等待新場景加載完畢
        while (!asyncLoad.isDone)
        {
            yield return null;
        }

        // 4. 播放「碎片反向聚合重組」動畫 (Reassemble)
        elapsed = 0f;
        while (elapsed < reassembleDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / reassembleDuration;
            float smoothT = Mathf.SmoothStep(0f, 1f, t);

            for (int i = 0; i < _shards.Count; i++)
            {
                _shards[i].anchoredPosition = Vector2.Lerp(_shatteredOffsets[i], _originalPositions[i], smoothT);
                _shards[i].localRotation = Quaternion.Euler(0f, 0f, _shatteredRotations[i] * (1f - smoothT));
                _shards[i].localScale = Vector3.Lerp(new Vector3(0.2f, 0.2f, 1f), Vector3.one, smoothT);
            }
            yield return null;
        }

        // 隱藏 Shards
        HideShards();

        // 銷毀轉場控制器
        _isTransitioning = false;
        Destroy(gameObject);
    }

    private void ResetAndShowShards()
    {
        for (int i = 0; i < _shards.Count; i++)
        {
            _shards[i].gameObject.SetActive(true);
            _shards[i].anchoredPosition = _originalPositions[i];
            _shards[i].localRotation = Quaternion.identity;
            _shards[i].localScale = Vector3.one;
        }
    }

    private void HideShards()
    {
        for (int i = 0; i < _shards.Count; i++)
        {
            if (_shards[i] != null)
            {
                _shards[i].gameObject.SetActive(false);
            }
        }
    }
}
