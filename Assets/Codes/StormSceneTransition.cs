using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

/// <summary>
/// 廢墟風暴水下關卡切換傳送門 (StormSceneTransition)。
/// 純粹的場景切換機制，完全不包含任何受傷、閃紅邊、扣血或重生系統。
/// 當主角與風暴接觸累積滿 5 秒時，自動淡出並切換載入 "underwater" 水下場景。
/// </summary>
[RequireComponent(typeof(Collider))]
public class StormSceneTransition : MonoBehaviour
{
    [Header("場景切換設定")]
    [Tooltip("目標切換的關卡場景名稱 (預設為 desert)")]
    public string nextSceneName = "desert";

    [Tooltip("進入目標場景 (desert) 後，要指定重生的隱形物件/重生點名稱 (若留空則使用該場景預設位置)")]
    public string targetSpawnPointName = "SpawnPoint_FromSampleScene";

    [Tooltip("主角必須與風暴接觸維持多久才啟動轉場 (秒，預設 5.0)")]
    public float contactTimeRequired = 5.0f;

    [Tooltip("轉場淡出時間 (秒，預設 1.5)")]
    public float fadeDuration = 1.5f;

    [Header("風暴巡邏移動 (風暴橫掃)")]
    [Tooltip("是否開啟風暴左右平滑橫掃移動")]
    public bool enableMovement = true;

    [Tooltip("風暴左右橫掃移動的單側距離 (例如 6 代表往左 6 米、往右 6 米)")]
    public float sweepDistance = 6.0f;

    [Tooltip("風暴橫掃移動的速度")]
    public float sweepSpeed = 2.0f;

    [Header("即時進度觀察 (唯讀)")]
    [Tooltip("目前接觸時間計數")]
    public float currentContactTimer = 0f;
    public bool isPlayerInside = false;

    private bool isTransitioning = false;
    private float startPosX;
    private Transform playerTransform;
    private PlayerMovement playerMovement;
    private UnityEngine.UI.Image fadeImage;

    private void Start()
    {
        startPosX = transform.position.x;

        // ★ 自動校正目標場景為 desert (解決 Inspector 殘留舊數值 underwater 的問題)
        if (string.IsNullOrEmpty(nextSceneName) || nextSceneName.Equals("underwater", System.StringComparison.OrdinalIgnoreCase))
        {
            nextSceneName = "desert";
        }

        // 確保碰撞體設為 Trigger 模式，純感應主角進入
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            col.isTrigger = true;
        }

        // 防呆防護：如果該物件被誤設了會引發受傷重生的 Tag，自動更正為 Untagged
        string currentTag = gameObject.tag;
        if (currentTag == "WolfEnemy" || currentTag == "Enemy" || currentTag == "Hazard")
        {
            gameObject.tag = "Untagged";
            Debug.Log($"【風暴轉場】已自動重置 '{gameObject.name}' 的 Tag 為 Untagged，確保不會誤觸發受傷與重生系統。");
        }

        // 防呆防護：如果物件上誤掛了 WolfEnemy 等攻擊腳本，將其停用
        WolfEnemy wolfScript = GetComponent<WolfEnemy>();
        if (wolfScript != null)
        {
            wolfScript.enabled = false;
            Debug.LogWarning($"【風暴轉場】偵測到 '{gameObject.name}' 上被誤掛了 WolfEnemy 攻擊腳本，已為您自動停用！");
        }
    }

    private void Update()
    {
        if (isTransitioning) return;

        // 1. 執行風暴左右橫掃巡邏移動
        if (enableMovement)
        {
            float offsetX = Mathf.Sin(Time.time * sweepSpeed) * sweepDistance;
            transform.position = new Vector3(startPosX + offsetX, transform.position.y, transform.position.z);
        }

        // 2. 當主角在風暴範圍內時進行接觸計時 (純計時，無受傷扣血)
        if (isPlayerInside)
        {
            currentContactTimer += Time.deltaTime;

            // 接觸滿 5 秒，觸發純切換場景
            if (currentContactTimer >= contactTimeRequired)
            {
                StartSceneTransition();
            }
        }
        else
        {
            // 離開風暴後重置計時
            if (currentContactTimer > 0f)
            {
                currentContactTimer = Mathf.Max(0f, currentContactTimer - Time.deltaTime * 2f);
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (isTransitioning) return;

        if (other.tag == "Player" || other.GetComponentInParent<PlayerMovement>() != null)
        {
            isPlayerInside = true;

            PlayerMovement pm = other.GetComponent<PlayerMovement>();
            if (pm == null) pm = other.GetComponentInParent<PlayerMovement>();
            if (pm != null)
            {
                playerMovement = pm;
                playerTransform = pm.transform;
            }

            Debug.Log($"【風暴切換場景】主角進入風暴範圍！開始計算切換進度 ({currentContactTimer:F1}/{contactTimeRequired} 秒)...");
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (isTransitioning) return;

        if (other.tag == "Player" || other.GetComponentInParent<PlayerMovement>() != null)
        {
            isPlayerInside = false;
            Debug.Log("【風暴切換場景】主角離開風暴，切換進度重置。");
        }
    }

    private void StartSceneTransition()
    {
        isTransitioning = true;
        Debug.Log($"【風暴切換場景】接觸滿 {contactTimeRequired} 秒！開始切換載入水下場景：'{nextSceneName}'");
        StartCoroutine(TransitionSequence());
    }

    private IEnumerator TransitionSequence()
    {
        // 1. 鎖定玩家移動控制
        if (playerMovement != null)
        {
            playerMovement.isCutsceneFrozen = true;
        }

        // 2. 生成畫面淡出 UI (純黑屏淡出，絕無閃紅邊/受傷/扣血/重生效果)
        CreateFadeImage();

        // 3. 漸漸淡出
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

        // 4. 設定跨場景指定出生點並切換載入 desert 場景
        if (!string.IsNullOrEmpty(targetSpawnPointName))
        {
            PlayerRespawnSystem.NextSceneSpawnTargetName = targetSpawnPointName;
        }

        Debug.Log($"【風暴轉場】開始載入場景 '{nextSceneName}'，指定出生點物件為：'{targetSpawnPointName}'");
        SceneManager.LoadScene(nextSceneName);
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
}
