using UnityEngine;
using System.Collections;

/// <summary>
/// 狼動態生成出生點系統。
/// 當主角觸發後，複製生成一隻全新的狼，漸漸淡入顯示，並在主角離開設定距離後啟動追逐。
/// </summary>
public class WolfSpawner : MonoBehaviour, IResettable
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
    [Tooltip("狼生成的相對位置偏置 (預設 X = -6 米，表示生成在出生點/主角後方 6 米，防範狼直接重疊刷在主角身上)")]
    public Vector3 spawnOffset = new Vector3(-6f, 0f, 0f);

    [Tooltip("新狼生成後，主角必須「離開出生點多少距離」後，這隻狼才開始啟動追逐")]
    public float startChasePlayerDistance = 8f;

    [Tooltip("新狼生成時的漸變顯現（淡入）時間")]
    public float fadeDuration = 1.5f;

    private bool hasSpawned = false;
    private GameObject spawnedWolf;
    private WolfEnemy spawnedWolfEnemy;
    private Transform playerTransform;
    private Collider playerCollider;
    private Collider spawnedWolfCollider;
    private bool isWolfActivated = false;

    // ★1020 M：「廢墟：狼群的再復活後數量減少」
    //   原因：生狼的條件是「她走到出生點 2 米內」（triggerType 2）。咬滿 3 口後重生在挑戰起點（ChallengePlayerSpawn x 137），
    //   已經在 wolf1（x 123）、wolf2（x 135）的右邊，她不會再走回去，這兩隻就永遠不再生：第一次 4 隻、重生後 2 隻（10/20 log）。
    //   修法：重生把狼重置以後，等重生整段跑完（她已經站在重生點、畫面亮了），她已經走過的出生點照原位（出生點＋spawnOffset）補生；
    //   補生的狼等她從重生的位置開始動，才照原本「離出生點 startChasePlayerDistance 米」的規則放出去追——跟第一次走過來時一樣在她身後。
    //   只有重生造成的重置才補（背景區域切換的重置不補）。
    [Header("★1020 重生後補生（M：重生後狼變少）")]
    [Tooltip("重生後她已經在這個出生點右邊（走過了）也照樣生這隻狼。取消＝舊行為：走過的出生點不再生，重生後狼會變少")]
    public bool refillWhenPassedAfterRespawn = true;
    [Tooltip("補生的狼：她離開重生的位置多少米才開始追（另外還要符合原本的「離出生點 startChasePlayerDistance 米」）")]
    public float refillStartMoveDistance = 1.5f;
    private bool _checkPassedAfterReset;
    private bool _refilled;
    private float _refillPlayerX;

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
            playerCollider = pm.GetComponent<Collider>();
            if (playerCollider == null) playerCollider = pm.GetComponentInChildren<Collider>();
        }
        else
        {
            // 備用：使用 Tag 搜尋
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
            {
                playerTransform = playerObj.transform;
                playerCollider = playerObj.GetComponent<Collider>();
                if (playerCollider == null) playerCollider = playerObj.GetComponentInChildren<Collider>();
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

        // ★1020 重生後補生：重生還沒跑完（黑幕中、她還沒被傳到重生點）先不判斷，跑完只判斷一次
        if (_checkPassedAfterReset && !hasSpawned && !PlayerRespawnSystem.IsAnyRespawning)
        {
            _checkPassedAfterReset = false;
            if (refillWhenPassedAfterRespawn && PlayerPassedSpawner())
            {
                _refillPlayerX = playerTransform.position.x;
                Debug.Log($"【狼出生點】★1020 '{gameObject.name}' 重生後她已經在出生點右邊（她 x {_refillPlayerX:F1}，出生點 x {transform.position.x:F1}），照原位補生；她開始往前走才放出去追");
                SpawnWolf();
                _refilled = true;
            }
        }

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

            // ★1020 補生的狼：還要等她從重生的位置開始動（不然 wolf1 這種離她 14 米的，畫面一亮就衝過來）
            bool playerStarted = !_refilled || Mathf.Abs(playerTransform.position.x - _refillPlayerX) >= refillStartMoveDistance;
            if (distFromSpawner >= startChasePlayerDistance && playerStarted)
            {
                ActivateWolf();
            }
        }
    }

    /// <summary>★1020 她是不是已經走過這個出生點（關卡往右走；超過觸發範圍才算走過）。</summary>
    private bool PlayerPassedSpawner()
    {
        float margin = triggerType == 1 ? triggerXOffset : (triggerType == 2 ? triggerDistance : 0f);
        return playerTransform.position.x > transform.position.x + Mathf.Max(0f, margin);
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

        // 複製生成新狼 (依照 spawnOffset 位置偏置生成，防止與主角重疊)
        Vector3 spawnPos = transform.position + spawnOffset;
        spawnedWolf = Instantiate(wolfTemplate, spawnPos, transform.rotation);
        spawnedWolf.name = "Spawned_Wolf_" + System.Guid.NewGuid().ToString().Substring(0, 4);
        spawnedWolf.SetActive(true);

        // 取得新狼的碰撞體
        spawnedWolfCollider = spawnedWolf.GetComponent<Collider>();
        if (spawnedWolfCollider == null) spawnedWolfCollider = spawnedWolf.GetComponentInChildren<Collider>();

        // 暫時與主角忽略物理碰撞，防止生成瞬間誤咬
        if (spawnedWolfCollider != null && playerCollider != null)
        {
            Physics.IgnoreCollision(spawnedWolfCollider, playerCollider, true);
        }

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

        // 恢復與主角的碰撞感應
        if (spawnedWolfCollider != null && playerCollider != null)
        {
            Physics.IgnoreCollision(spawnedWolfCollider, playerCollider, false);
        }

        if (spawnedWolfEnemy != null)
        {
            spawnedWolfEnemy.enabled = true; // 啟動追擊
            spawnedWolfEnemy.StartChase();   // 強制設為追擊狀態
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

    // --- IResettable 實作 ---
    public void ResetToInitialState()
    {
        StopAllCoroutines();
        hasSpawned = false;
        isWolfActivated = false;
        // ★1020 重生造成的重置（重生系統在黑幕中呼叫，這時 IsAnyRespawning 已經是 true）：重生跑完後檢查要不要補生
        _checkPassedAfterReset = PlayerRespawnSystem.IsAnyRespawning;
        _refilled = false;
        if (spawnedWolf != null)
        {
            // ★0910：這隻狼可能正咬在玩家身上（是玩家的子物件）。
            //   直接 Destroy 的話，玩家身上的 attachedWolvesCount 不會減回去，
            //   減速懲罰會殘留。先讓狼自己走完鬆口流程再刪。
            if (spawnedWolfEnemy != null) spawnedWolfEnemy.ResetToInitialState();
            Destroy(spawnedWolf);
            spawnedWolf = null;
            spawnedWolfEnemy = null;
            spawnedWolfCollider = null;
        }
    }
}
