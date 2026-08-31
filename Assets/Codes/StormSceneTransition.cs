using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

/// <summary>
/// 廢墟風暴吸入式關卡切換傳送門 (StormSceneTransition)。
/// 獨立掛載於世界地面的固定 Trigger 碰撞框上（不隨相機移動）：
/// 1. 【地面物理 100% 完整保留】：主角始終穩踏在地面碰撞體上，絕對不竄改 Y 座標，杜絕任何跌出界外問題！
/// 2. 【僅平滑拉引 X 軸至中心】：進入邊界後，將主角水平 X 軸以指數平滑拉向 Trigger 中心的 X。
/// 3. 【原地掙扎奔跑】：玩家按方向鍵時動作依然奔跑，呈現頂風掙扎但被風暴吸入的電影感。
/// 4. 【聯動背景龍捲風】：進入時可通知背景龍捲風啟動跟隨相機，隨後黑屏淡出載入 desert！
/// </summary>
[RequireComponent(typeof(Collider))]
public class StormSceneTransition : MonoBehaviour
{

    [Header("📜 過場文字卡")]
    [Tooltip("黑幕全黑後、載入下一關前要播的文字卡。留空＝不播")]
    public string storyCardId = "M2";
    [Header("🎯 場景切換設定")]
    [Tooltip("目標切換的關卡場景名稱 (預設為 desert)")]
    public string nextSceneName = "desert";

    [Tooltip("進入目標場景 (desert) 後，要指定重生的隱形物件/重生點名稱 (若留空則使用該場景預設位置)")]
    public string targetSpawnPointName = "SpawnPoint_FromSampleScene";

    [Header("🌪️ 風暴吸入與演出設定 (僅平滑拉引 X 軸，絕不改變地面 Y 軸)")]
    [Tooltip("水平吸入中心 X 座標所需時間 (秒，數值越大吸入速度越慢、掙扎時間越長，建議 1.5 ~ 3.5)")]
    [Range(0.5f, 6.0f)]
    public float suctionDuration = 2.0f;

    [Tooltip("黑屏淡出時間 (秒，預設 1.2 秒)")]
    public float fadeDuration = 1.2f;

    [Tooltip("關聯的背景龍捲風視覺物件 (可拖入 1 個或多個龍捲風，踏入時會一併啟動相機跟隨)")]
    public TornadoFollowCamera[] backgroundTornadoes;

    [Tooltip("進入風暴時播放的狂風暴風咆哮音效 (選填)")]
    public AudioClip stormVortexSFX;
    [Range(0f, 1f)] public float sfxVolume = 0.95f;

    private bool isTransitioning = false;
    private UnityEngine.UI.Image fadeImage;

    private void Awake()
    {
        EnsureColliderSetup();
    }

    private void Start()
    {
        if (string.IsNullOrEmpty(nextSceneName) || nextSceneName.Equals("underwater", System.StringComparison.OrdinalIgnoreCase))
        {
            nextSceneName = "desert";
        }

        EnsureColliderSetup();
    }

    private void EnsureColliderSetup()
    {
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            col.isTrigger = true;
            if (col is BoxCollider box)
            {
                float lossyZ = transform.lossyScale.z != 0f ? Mathf.Abs(transform.lossyScale.z) : 1f;
                Vector3 size = box.size;
                size.z = Mathf.Max(size.z, 30f / lossyZ);
                box.size = size;

                Vector3 center = box.center;
                center.z = 0f;
                box.center = center;
            }
        }

        Collider2D col2d = GetComponent<Collider2D>();
        if (col2d != null)
        {
            col2d.isTrigger = true;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        TryStartStormTransition(other != null ? other.gameObject : null);
    }

    private void OnTriggerStay(Collider other)
    {
        TryStartStormTransition(other != null ? other.gameObject : null);
    }

    private void OnCollisionEnter(Collision collision)
    {
        TryStartStormTransition(collision != null ? collision.gameObject : null);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryStartStormTransition(other != null ? other.gameObject : null);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        TryStartStormTransition(other != null ? other.gameObject : null);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        TryStartStormTransition(collision != null ? collision.gameObject : null);
    }

    private void TryStartStormTransition(GameObject hitObj)
    {
        if (isTransitioning || hitObj == null) return;

        PlayerMovement pm = hitObj.GetComponent<PlayerMovement>() ?? 
                           hitObj.GetComponentInParent<PlayerMovement>() ?? 
                           hitObj.GetComponentInChildren<PlayerMovement>();

        if (pm == null && (hitObj.CompareTag("Player") || hitObj.name.Contains("Player") || hitObj.transform.root.name.Contains("Player")))
        {
            pm = Object.FindFirstObjectByType<PlayerMovement>();
        }

        if (pm != null)
        {
            if (string.IsNullOrWhiteSpace(nextSceneName))
            {
                Debug.LogError("[StormSceneTransition] nextSceneName 是空的，無法轉場。", this);
                return;
            }

            isTransitioning = true;
            StartCoroutine(VortexSuctionAndTransition(pm));
        }
    }

    private IEnumerator VortexSuctionAndTransition(PlayerMovement pm)
    {
        Debug.Log($"🌪️【風暴吸入轉場】主角踏入固定轉場區域！啟動水平 X 引力吸入與原地奔跑演出...");

        // 1. 啟動背景龍捲風跟隨 (若有指定)
        if (backgroundTornadoes != null)
        {
            foreach (var t in backgroundTornadoes)
            {
                if (t != null) t.ActivateFollow();
            }
        }

        // 2. 播放狂風暴風音效
        if (stormVortexSFX != null)
        {
            AudioSource.PlayClipAtPoint(stormVortexSFX, transform.position, AudioManager.ScaleSfx(sfxVolume));
        }

        // 3. 透過 PlayerMovement 原生物理牽引 (100% 適應地形斜坡與地面碰撞，絕不穿模)
        float distanceX = Mathf.Abs(transform.position.x - pm.transform.position.x);
        float calculatedSpeed = Mathf.Clamp(distanceX / Mathf.Max(0.5f, suctionDuration), 1.8f, 6.0f);

        pm.StartWindSuction(transform.position.x, calculatedSpeed);

        float elapsed = 0f;
        while (elapsed < suctionDuration)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        pm.StopWindSuction();
        pm.isCutsceneFrozen = true;

        // 4. 到達中心，啟動畫面黑屏淡出
        CreateFadeImage();

        float timer = 0f;
        while (timer < fadeDuration)
        {
            timer += Time.deltaTime;
            if (fadeImage != null)
            {
                fadeImage.color = new Color(0, 0, 0, Mathf.Lerp(0f, 1f, timer / fadeDuration));
            }
            yield return null;
        }

        if (fadeImage != null)
        {
            fadeImage.color = new Color(0, 0, 0, 1f);
        }

        yield return new WaitForSeconds(0.1f);

        // 6. 設定跨場景指定出生點並切換載入 desert 場景
        if (!string.IsNullOrEmpty(targetSpawnPointName))
        {
            PlayerRespawnSystem.QueueNextSceneSpawn(targetSpawnPointName);
        }

        // ★【文字卡】畫面此時已全黑 → 播 M2 →（維持全黑）→ 才載入荒原
        //   順序：龍捲風帶走她 → 讀她的心聲 → 落在荒原
        if (StoryCardPlayer.Instance != null && StoryCardPlayer.Instance.HasCard(storyCardId))
        {
            yield return StoryCardPlayer.Instance.Play(storyCardId, false, false);
        }

        string targetScene = nextSceneName.Trim();
        Debug.Log($"✨【風暴轉場完成】主角抵達風暴核心！載入場景：'{targetScene}'，指定出生點：'{targetSpawnPointName}'");
        SceneManager.LoadScene(targetScene);
    }

    private void CreateFadeImage()
    {
        GameObject canvasObj = GameObject.Find("TransitionCanvas_System");
        if (canvasObj == null)
        {
            canvasObj = new GameObject("TransitionCanvas_System");
            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 9999;
            canvasObj.AddComponent<UnityEngine.UI.CanvasScaler>();
        }

        GameObject fadeObj = new GameObject("PureStormFadeScreen");
        fadeObj.transform.SetParent(canvasObj.transform, false);
        fadeImage = fadeObj.AddComponent<UnityEngine.UI.Image>();
        fadeImage.color = new Color(0, 0, 0, 0f);

        RectTransform rect = fadeImage.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.sizeDelta = Vector2.zero;
        rect.anchoredPosition = Vector2.zero;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.55f, 0.1f, 0.8f);
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            Gizmos.DrawWireCube(col.bounds.center, col.bounds.size);
        }
        else
        {
            Gizmos.DrawWireSphere(transform.position, 2.0f);
        }

        // 畫出吸入中心垂直線 (橘色)
        Gizmos.color = new Color(1f, 0.3f, 0f, 0.9f);
        Gizmos.DrawLine(new Vector3(transform.position.x, transform.position.y - 5f, transform.position.z),
                        new Vector3(transform.position.x, transform.position.y + 15f, transform.position.z));

        #if UNITY_EDITOR
        UnityEditor.Handles.Label(transform.position + Vector3.up * 1.5f, $"🌪️ 固定風暴吸入轉場區 (僅吸入 X 軸 ➔ {nextSceneName})");
        #endif
    }
}
