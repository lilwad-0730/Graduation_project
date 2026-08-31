using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

/// <summary>
/// 通用關卡轉場觸發器 (SceneTransitionZone)
/// 
/// 【適用情境】
///   1. 荒漠 (desert) ➔ 水下 (underwater)：使用 SinkWater 沉水沉浸式模式，主角踏入綠洲水面後平滑下沉並黑屏淡出。
///   2. 水下 (underwater) ➔ 玻璃館 (dark glasses)：使用 InstantFade 模式，游到底部裂縫/出口隱形框後優雅淡出。
///   3. 任意關卡之間的無縫轉移。
/// 
/// 【特色防呆】
///   - 踏入瞬間鎖定玩家輸入 (isCutsceneFrozen = true)，防止黑屏期間亂跑。
///   - 剛體物理保護，防止下沉或轉場時被狂風/怪物干擾。
///   - 原子級防重複觸發 (isTransitioning)。
///   - 支援指定下一關卡自訂出生點 (targetSpawnPointName)。
/// </summary>
[RequireComponent(typeof(Collider))]
public class SceneTransitionZone : MonoBehaviour, IResettable
{
    public enum TransitionMode
    {
        [Tooltip("平滑黑屏淡出模式：觸碰瞬間鎖定操作，平滑黑屏淡出後載入下一關卡 (適用於水下出口、邊界門)")]
        InstantFade,

        [Tooltip("綠洲沉水模式：觸碰水面後，主角緩慢沉入水底並伴隨黑屏淡出 (適用於荒漠綠洲 -> 水下)")]
        SinkWater,

        [Tooltip("📖 漫畫繪本轉場模式：進入繪本播放該章節漫畫，翻閱完畢後自動進入下一關卡")]
        ComicPictureBook
    }

    [Header("🎯 目標關卡設定 (Target Scene)")]
    [Tooltip("要切換載入的目標場景名稱 (例如 underwater, dark glasses, desert)")]
    public string nextSceneName = "underwater";

    [Tooltip("進入新場景後要指定重生的隱形物件名稱 (若留空則使用該場景預設主角位置)")]
    public string targetSpawnPointName = "";

    [Header("🎬 轉場演出模式 (Transition Mode)")]
    [Tooltip("選擇轉場演出方式")]
    public TransitionMode transitionMode = TransitionMode.InstantFade;

    [Header("📖 漫畫轉場設定 (僅在 ComicPictureBook 模式生效)")]
    [Tooltip("本章節漫畫起始頁碼 (0 起算)")]
    public int comicStartPage = 0;

    [Tooltip("本章節漫畫結束頁碼 (翻完此頁後進入 nextSceneName)")]
    public int comicEndPage = 13;

    [Tooltip("黑屏淡出時間 (秒，預設 1.2)")]
    public float fadeDuration = 1.2f;

    [Header("🌊 沉水模式專屬設定 (僅在 SinkWater 模式生效)")]
    [Tooltip("玩家沉入水中的下沉速度 (單位/秒，預設 1.2)")]
    public float sinkSpeed = 1.2f;

    [Tooltip("沉水深度 (米，預設 2.5 米)")]
    public float targetSinkDepth = 2.5f;

    [Header("🎵 轉場音效 (可選)")]
    [Tooltip("觸發轉場時播放的音效 (例如水聲或轉場氛圍音)")]
    public AudioClip transitionSFX;
    [Range(0f, 1f)] public float sfxVolume = 0.9f;

    // ──────────────── 內部狀態 ────────────────
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

        if (hitObj.CompareTag("Player") || hitObj.name == "Player" || (hitObj.transform.root != null && hitObj.transform.root.name.Contains("Player")))
        {
            StartTransition(hitObj);
        }
    }

    private void StartTransition(GameObject playerObj)
    {
        if (string.IsNullOrWhiteSpace(nextSceneName))
        {
            Debug.LogError("[SceneTransitionZone] nextSceneName 是空的，無法轉場。", this);
            return;
        }

        isTransitioning = true;
        playerTransform = playerObj.transform;
        playerRb = playerObj.GetComponent<Rigidbody>();
        playerMovement = playerObj.GetComponent<PlayerMovement>();

        Debug.Log($"【關卡轉場】觸發轉場至 [{nextSceneName}]，模式: {transitionMode}！");

        // 播放轉場音效
        if (transitionSFX != null)
        {
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(transitionSFX, sfxVolume);
            else AudioSource.PlayClipAtPoint(transitionSFX, transform.position, sfxVolume);
        }

        StartCoroutine(TransitionRoutine());
    }

    private IEnumerator TransitionRoutine()
    {
        // 1. 立即鎖定玩家操作與物理防呆
        if (playerMovement != null)
        {
            playerMovement.isCutsceneFrozen = true; // 鎖定鍵盤所有輸入
        }

        if (playerRb != null)
        {
            playerRb.linearVelocity = Vector3.zero;
            playerRb.angularVelocity = Vector3.zero;
            if (transitionMode == TransitionMode.SinkWater)
            {
                playerRb.useGravity = false;
                // 鎖定 X, Z，只允許 Y 下沉
                playerRb.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezePositionZ;
            }
        }

        // 2. 建立全螢幕黑屏淡出 UI
        CreateFadeImage();

        // 3. 根據模式執行演出
        if (transitionMode == TransitionMode.SinkWater)
        {
            float startY = playerTransform.position.y;
            float targetY = startY - targetSinkDepth;
            float sinkTimer = 0f;
            float maxSinkDuration = Mathf.Max(0.5f, fadeDuration);

            // 暫時將玩家碰撞器設為 Trigger，讓玩家視覺上順利沒入水窪，避免被綠洲下方的實體地面碰撞卡住無法沉降
            Collider playerCol = playerTransform.GetComponent<Collider>();
            if (playerCol != null) playerCol.isTrigger = true;

            while (playerTransform != null && sinkTimer < maxSinkDuration)
            {
                sinkTimer += Time.deltaTime;
                float progress = Mathf.Clamp01(sinkTimer / maxSinkDuration);

                // 平滑向下沉入水底
                playerTransform.position = Vector3.MoveTowards(
                    playerTransform.position,
                    new Vector3(playerTransform.position.x, targetY, playerTransform.position.z),
                    sinkSpeed * Time.deltaTime
                );

                // 平滑黑屏淡出
                if (fadeImage != null)
                {
                    fadeImage.color = new Color(0, 0, 0, progress);
                }

                yield return null;
            }
        }
        else // InstantFade
        {
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
        }

        // 4. 確保完全黑屏
        if (fadeImage != null)
        {
            fadeImage.color = Color.black;
        }

        yield return new WaitForSeconds(0.2f);

        // 5. 判斷轉場模式
        if (transitionMode == TransitionMode.ComicPictureBook)
        {
            Debug.Log($"📖【關卡轉場】啟動漫畫繪本中繼轉場 (頁碼 {comicStartPage} ~ {comicEndPage}) ➔ 下一關: [{nextSceneName}]");
            BookTransitionManager.OpenComicTransition(comicStartPage, comicEndPage, nextSceneName.Trim(), targetSpawnPointName);
            yield break;
        }

        // 設定跨場景出生點
        if (!string.IsNullOrEmpty(targetSpawnPointName))
        {
            PlayerRespawnSystem.QueueNextSceneSpawn(targetSpawnPointName);
        }

        string targetScene = nextSceneName.Trim();
        Debug.Log($"【關卡轉場】淡出完成，正在載入新場景: [{targetScene}]...");

        // 6. 載入場景
        try
        {
            SceneManager.LoadScene(targetScene);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"【關卡轉場錯誤】無法載入場景 '{targetScene}'：{ex.Message}\n" +
                           $"請確認是否已在 Unity 的 File -> Build Settings -> Scenes In Build 中加入該場景！");
            ResetToInitialState();
        }
    }

    private void CreateFadeImage()
    {
        // 嘗試抓取系統的 Canvas，若沒有則動態生成
        GameObject canvasObj = GameObject.Find("RespawnCanvas_System");
        if (canvasObj == null)
        {
            canvasObj = new GameObject("TransitionCanvas_System");
            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 9999;
            canvasObj.AddComponent<UnityEngine.UI.CanvasScaler>();
        }

        GameObject fadeObj = new GameObject("TransitionFadeScreen");
        fadeObj.transform.SetParent(canvasObj.transform, false);
        fadeImage = fadeObj.AddComponent<UnityEngine.UI.Image>();
        fadeImage.color = new Color(0, 0, 0, 0f);

        RectTransform rect = fadeImage.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.sizeDelta = Vector2.zero;
        rect.anchoredPosition = Vector2.zero;
    }

    // ─── IResettable 實作 ───
    public void ResetToInitialState()
    {
        StopAllCoroutines();
        isTransitioning = false;

        if (fadeImage != null)
        {
            Destroy(fadeImage.gameObject);
        }

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

    // ─── Editor 視覺標記 ───
    private void OnDrawGizmos()
    {
        Gizmos.color = transitionMode == TransitionMode.SinkWater 
            ? new Color(0.2f, 0.7f, 1f, 0.45f) // 水藍色
            : new Color(0.8f, 0.3f, 1f, 0.45f); // 紫色

        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            Gizmos.DrawCube(col.bounds.center, col.bounds.size);
            Gizmos.color = Color.white;
            Gizmos.DrawWireCube(col.bounds.center, col.bounds.size);
        }
        else
        {
            Gizmos.DrawCube(transform.position, new Vector3(3f, 3f, 2f));
        }

        #if UNITY_EDITOR
        string modeLabel = transitionMode == TransitionMode.SinkWater ? "🌊 綠洲沉水轉場" : "🚪 關卡傳送門";
        UnityEditor.Handles.Label(transform.position + Vector3.up * 1.5f, $"[{modeLabel}] -> {nextSceneName}");
        #endif
    }
}
