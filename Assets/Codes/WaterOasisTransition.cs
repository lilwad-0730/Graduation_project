using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

/// <summary>
/// 掛載於綠洲水面 Trigger 物件。
/// 當玩家踩入綠洲水面時，接管控制使玩家緩慢沉入水底，並轉場載入水下關卡。
/// </summary>
[RequireComponent(typeof(Collider))]
public class WaterOasisTransition : MonoBehaviour, IResettable
{
    [Header("轉場與場景設定")]
    [Tooltip("要載入的水下關卡場景名稱 (預設為 underwater)")]
    public string nextSceneName = "underwater";

    [Tooltip("進入目標場景 (underwater) 後，要指定重生的隱形物件/重生點名稱 (若留空則使用該場景預設位置)")]
    public string targetSpawnPointName = "SpawnPoint_FromDesert";

    [Tooltip("玩家沉入水中的速度 (單位/秒)")]
    public float sinkSpeed = 1.0f;

    [Tooltip("需要下沉多少深度才啟動場景載入 (單位，預設 3)")]
    public float targetSinkDepth = 3.0f;

    [Tooltip("漸黑轉場淡出的時間長度 (秒，預設 1.5)")]
    public float fadeDuration = 1.5f;

    private bool isTransitioning = false;
    private Transform playerTransform;
    private Rigidbody playerRb;
    private PlayerMovement playerMovement;
    private UnityEngine.UI.Image fadeImage;

    private void Awake()
    {
        EnsureColliderSetup();
    }

    private void Start()
    {
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
        TryStartTransition(other != null ? other.gameObject : null);
    }

    private void OnTriggerStay(Collider other)
    {
        TryStartTransition(other != null ? other.gameObject : null);
    }

    private void OnCollisionEnter(Collision collision)
    {
        TryStartTransition(collision != null ? collision.gameObject : null);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryStartTransition(other != null ? other.gameObject : null);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        TryStartTransition(other != null ? other.gameObject : null);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        TryStartTransition(collision != null ? collision.gameObject : null);
    }

    private void TryStartTransition(GameObject hitObj)
    {
        if (isTransitioning || hitObj == null) return;

        PlayerMovement pm = hitObj.GetComponent<PlayerMovement>();
        if (pm == null) pm = hitObj.GetComponentInParent<PlayerMovement>();
        if (pm == null) pm = hitObj.GetComponentInChildren<PlayerMovement>();

        if (pm != null)
        {
            StartTransition(pm.gameObject);
            return;
        }

        if (hitObj.CompareTag("Player") || hitObj.name.Contains("Player") || (hitObj.transform.root != null && hitObj.transform.root.name.Contains("Player")))
        {
            StartTransition(hitObj);
        }
    }

    private void StartTransition(GameObject playerObj)
    {
        if (string.IsNullOrWhiteSpace(nextSceneName))
        {
            Debug.LogError("[WaterOasisTransition] nextSceneName 是空的，無法轉場。", this);
            return;
        }

        isTransitioning = true;
        playerTransform = playerObj.transform;
        playerRb = playerObj.GetComponent<Rigidbody>();
        playerMovement = playerObj.GetComponent<PlayerMovement>();

        Debug.Log("【綠洲轉場】玩家觸碰水面，開始沉入水底...");
        StartCoroutine(SinkAndTransitionSequence());
    }

    private IEnumerator SinkAndTransitionSequence()
    {
        // 1. 鎖定玩家控制與物理狀態
        if (playerMovement != null)
        {
            playerMovement.isCutsceneFrozen = true; // 鎖定鍵盤輸入
        }

        if (playerRb != null)
        {
            playerRb.useGravity = false;
            playerRb.linearVelocity = Vector3.zero;
            playerRb.angularVelocity = Vector3.zero;
            // 物理防呆：鎖定 X 與 Z 軸，只允許 Y 軸下沉，防止下沉中被強風推走
            playerRb.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezePositionZ;
        }

        // 2. 動態生成全螢幕漸黑圖片進行淡出
        CreateFadeImage();

        float startY = playerTransform.position.y;
        float targetY = startY - targetSinkDepth;

        // 3. 執行平滑下沉與漸黑效果
        float fadeTimer = 0f;
        while (playerTransform.position.y > targetY + 0.05f)
        {
            // 移動玩家座標
            playerTransform.position = Vector3.MoveTowards(
                playerTransform.position,
                new Vector3(playerTransform.position.x, targetY, playerTransform.position.z),
                sinkSpeed * Time.deltaTime
            );

            // 淡出至全黑
            if (fadeImage != null && fadeTimer < fadeDuration)
            {
                fadeTimer += Time.deltaTime;
                fadeImage.color = new Color(0, 0, 0, Mathf.Lerp(0f, 1f, fadeTimer / fadeDuration));
            }

            yield return null;
        }

        // 4. 強制完全黑屏
        if (fadeImage != null)
        {
            fadeImage.color = new Color(0, 0, 0, 1f);
        }
        yield return new WaitForSeconds(0.2f);

        // 5. 設定跨場景指定出生點並載入下一關卡場景
        if (!string.IsNullOrEmpty(targetSpawnPointName))
        {
            PlayerRespawnSystem.QueueNextSceneSpawn(targetSpawnPointName);
        }

        string targetScene = nextSceneName.Trim();
        Debug.Log($"【綠洲轉場】下沉完畢，開始載入場景: '{targetScene}'，目標出生點物件為：'{targetSpawnPointName}'");
        SceneManager.LoadScene(targetScene);
    }

    private void CreateFadeImage()
    {
        // 嘗試抓取系統的重生 Canvas，若沒有則動態生成一個
        GameObject canvasObj = GameObject.Find("RespawnCanvas_System");
        if (canvasObj == null)
        {
            canvasObj = new GameObject("TransitionCanvas");
            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;
            canvasObj.AddComponent<UnityEngine.UI.CanvasScaler>();
        }

        GameObject fadeObj = new GameObject("OasisFadeScreen");
        fadeObj.transform.SetParent(canvasObj.transform, false);
        fadeImage = fadeObj.AddComponent<UnityEngine.UI.Image>();
        fadeImage.color = new Color(0, 0, 0, 0f);

        RectTransform rect = fadeImage.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.sizeDelta = Vector2.zero;
        rect.anchoredPosition = Vector2.zero;
    }

    // --- IResettable 實作 ---
    public void ResetToInitialState()
    {
        StopAllCoroutines();
        isTransitioning = false;

        if (fadeImage != null)
        {
            Destroy(fadeImage.gameObject);
        }

        // 重設玩家的物理與移動狀態（防呆）
        if (playerRb != null)
        {
            playerRb.useGravity = true;
            playerRb.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionZ;
        }
        if (playerMovement != null)
        {
            playerMovement.isCutsceneFrozen = false;
        }
    }
}
