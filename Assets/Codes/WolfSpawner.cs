using UnityEngine;
using System.Collections;

/// <summary>
/// 狼動態生成出生點系統。
/// 當主角觸發後，複製生成一隻全新的狼，漸漸淡入顯示，並在主角離開設定距離後啟動追逐。
/// </summary>
public class WolfSpawner : MonoBehaviour
{
    [Header("狼的模板 (可拉場景中的狼物件，或專案的狼預製體 Prefab)")]
    [Tooltip("複製生成新狼的模板物件。必須掛載了 WolfEnemy, WolfSpriteAnimator 等腳本。")]
    public GameObject wolfTemplate;

    [Header("生成觸發設定")]
    [Tooltip("觸發模式：\n0 = 3D碰撞器觸發 (需在出生點加上 Box Collider 並勾選 IsTrigger)\n1 = 主角 X 座標超過出生點\n2 = 主角與出生點距離小於感應距離")]
    public int triggerType = 0;
    
    [Tooltip("X 座標偏置量 (僅在 triggerType = 1 時生效，主角 X >= 出生點 X + 偏置量時觸發)")]
    public float triggerXOffset = 0f;
    
    [Tooltip("感應距離 (僅在 triggerType = 2 時生效，主角與出生點距離小於此值時觸發)")]
    public float triggerDistance = 5f;

    [Header("生成後追蹤設定")]
    [Tooltip("新狼生成後，主角必須「離開出生點多少距離」後，這隻狼才開始啟動追逐")]
    public float startChasePlayerDistance = 8f;

    [Tooltip("新狼生成時的漸變顯現（淡入）時間")]
    public float fadeDuration = 1.5f;

    private bool hasSpawned = false;
    private GameObject spawnedWolf;
    private WolfEnemy spawnedWolfEnemy;
    private Transform playerTransform;
    private bool isWolfActivated = false;

    private void Start()
    {
        // 尋找玩家 (優先使用 PlayerMovement 組件搜尋，防呆且不依賴 Tag 判定)
        PlayerMovement pm = FindAnyObjectByType<PlayerMovement>();
        if (pm == null) pm = FindFirstObjectByType<PlayerMovement>();
        #pragma warning disable CS0618
        if (pm == null) pm = (PlayerMovement)FindObjectOfType(typeof(PlayerMovement));
        #pragma warning restore CS0618

        if (pm != null)
        {
            playerTransform = pm.transform;
        }
        else
        {
            // 備用：使用 Tag 搜尋
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
            {
                playerTransform = playerObj.transform;
            }
        }

        if (playerTransform == null)
        {
            Debug.LogError($"【狼出生點】'{gameObject.name}' 找不到主角！請確認主角物件上有掛載 PlayerMovement 腳本。");
        }
        else
        {
            Debug.Log($"【狼出生點】'{gameObject.name}' 已成功鎖定主角：'{playerTransform.name}'");
        }

        // 防呆：如果模板是場景中的物件，遊戲開始時先將其隱藏，避免場景多出一隻不動的狼
        if (wolfTemplate != null && wolfTemplate.scene.name != null)
        {
            wolfTemplate.SetActive(false);
            Debug.Log($"【狼出生點】已自動隱藏場景中的狼模板：'{wolfTemplate.name}'");
        }
    }

    private void Update()
    {
        if (playerTransform == null) return;

        // 1. 偵測生成條件
        if (!hasSpawned)
        {
            bool shouldSpawn = false;

            if (triggerType == 1) // X 座標判定
            {
                if (playerTransform.position.x >= (transform.position.x + triggerXOffset))
                {
                    shouldSpawn = true;
                }
            }
            else if (triggerType == 2) // 距離靠近判定
            {
                float dist = Vector3.Distance(playerTransform.position, transform.position);
                if (dist <= triggerDistance)
                {
                    shouldSpawn = true;
                }
            }

            if (shouldSpawn)
            {
                SpawnWolf();
            }
        }

        // 2. 偵測新狼啟動追蹤
        if (hasSpawned && spawnedWolf != null && !isWolfActivated)
        {
            // 計算主角目前與出生點的距離
            float distFromSpawner = Vector3.Distance(playerTransform.position, transform.position);

            if (distFromSpawner >= startChasePlayerDistance)
            {
                ActivateWolf();
            }
        }
    }

    // 3D 碰撞器觸發 (triggerType = 0)
    private void OnTriggerEnter(Collider other)
    {
        if (triggerType == 0 && !hasSpawned)
        {
            // 偵測是否為玩家
            if (other.CompareTag("Player") || other.GetComponentInParent<PlayerMovement>() != null)
            {
                SpawnWolf();
            }
        }
    }

    private void SpawnWolf()
    {
        if (wolfTemplate == null)
        {
            Debug.LogError($"【狼出生點】'{gameObject.name}' 尚未指定狼的模板！");
            return;
        }

        hasSpawned = true;
        Debug.Log($"【狼出生點】'{gameObject.name}' 觸發成功！正在生成新狼...");

        // 複製生成新狼
        spawnedWolf = Instantiate(wolfTemplate, transform.position, transform.rotation);
        spawnedWolf.name = "Spawned_Wolf_" + System.Guid.NewGuid().ToString().Substring(0, 4);
        spawnedWolf.SetActive(true);

        // 尋找新狼身上的 WolfEnemy 腳本並暫時停用，讓牠暫時不開始追逐
        spawnedWolfEnemy = spawnedWolf.GetComponent<WolfEnemy>();
        if (spawnedWolfEnemy == null) spawnedWolfEnemy = spawnedWolf.GetComponentInChildren<WolfEnemy>();

        if (spawnedWolfEnemy != null)
        {
            spawnedWolfEnemy.enabled = false; // 暫時關閉狼的追蹤邏輯
        }

        // 啟動淡入協程
        StartCoroutine(FadeInWolf(spawnedWolf));
    }

    private void ActivateWolf()
    {
        isWolfActivated = true;
        if (spawnedWolfEnemy != null)
        {
            spawnedWolfEnemy.enabled = true; // 啟動追擊
            Debug.Log($"【狼出生點】主角已離開拉開距離 ({startChasePlayerDistance}米)，新狼 '{spawnedWolf.name}' 啟動追逐主角！");
        }
    }

    private IEnumerator FadeInWolf(GameObject wolfObj)
    {
        // 取得新狼身上的所有 SpriteRenderer（包括骨骼、外觀子物件）
        SpriteRenderer[] renderers = wolfObj.GetComponentsInChildren<SpriteRenderer>();
        if (renderers.Length == 0) yield break;

        // 初始設為完全透明
        foreach (var sr in renderers)
        {
            if (sr != null)
            {
                Color c = sr.color;
                sr.color = new Color(c.r, c.g, c.b, 0f);
            }
        }

        // 漸漸顯示
        float timer = 0f;
        while (timer < fadeDuration)
        {
            timer += Time.deltaTime;
            float alpha = Mathf.Lerp(0f, 1f, timer / fadeDuration);
            foreach (var sr in renderers)
            {
                if (sr != null)
                {
                    Color c = sr.color;
                    sr.color = new Color(c.r, c.g, c.b, alpha);
                }
            }
            yield return null;
        }

        // 確保恢復完全不透明
        foreach (var sr in renderers)
        {
            if (sr != null)
            {
                Color c = sr.color;
                sr.color = new Color(c.r, c.g, c.b, 1f);
            }
        }
    }
}
