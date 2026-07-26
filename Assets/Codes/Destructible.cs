using UnityEngine;
using System.Collections;

public class Destructible : MonoBehaviour, IResettable
{
    [Header("Shattered Prefab to Spawn (Optional)")]
    public GameObject shatteredPrefab;

    [Header("Shatter Settings")]
    public bool shatterOnCollision = true;
    public float minCollisionVelocity = 2f;
    public float disappearDelay = 1f; // 碎裂後幾秒開始消失

    [Header("Auto-Shatter Grid Settings (If no prefab)")]
    public int columns = 4;
    public int rows = 4;
    public float explosionForce = 5f;

    private bool hasShattered = false;

    // 儲存初始狀態以供重置
    private Vector3 initialPosition;
    private Quaternion initialRotation;
    private Vector3 initialScale;
    private bool isInitiallyActive;

    private void Awake()
    {
        initialPosition = transform.position;
        initialRotation = transform.rotation;
        initialScale = transform.localScale;
        isInitiallyActive = gameObject.activeSelf;
    }

    // 可供外部調用（例如玩家攻擊、機關觸發）
    public void Shatter()
    {
        if (hasShattered) return;
        hasShattered = true;

        if (shatteredPrefab != null)
        {
            // 在當前位置生成預製的碎片，並同步縮放比例
            GameObject shatteredInstance = Instantiate(shatteredPrefab, transform.position, transform.rotation);
            shatteredInstance.transform.localScale = transform.localScale;

            ShatteredObject shatteredComp = shatteredInstance.GetComponent<ShatteredObject>();
            if (shatteredComp == null)
            {
                shatteredComp = shatteredInstance.AddComponent<ShatteredObject>();
            }
            shatteredComp.disappearDelay = disappearDelay;
            gameObject.SetActive(false);
        }
        else
        {
            // 自動搜尋此物件或子物件上的 2D SpriteRenderer 進行 2D 動態切片碎裂！
            SpriteRenderer sr = GetComponent<SpriteRenderer>();
            if (sr == null) sr = GetComponentInChildren<SpriteRenderer>();

            if (sr != null && sr.sprite != null)
            {
                ShatterSprite(sr);
                gameObject.SetActive(false);
            }
            else
            {
                // 如果連 2D Sprite 都沒有，才走平滑沉降淡出備用方案
                StartCoroutine(SafeCollapseRoutine());
            }
        }
    }

    private IEnumerator SafeCollapseRoutine()
    {
        // 1. 立刻關閉碰撞器，使掩體保護失效
        Collider[] colliders = GetComponentsInChildren<Collider>();
        foreach (var c in colliders)
        {
            if (c != null) c.enabled = false;
        }

        // 2. 石柱平滑沉降瓦解演出 (0.5 秒)
        float elapsed = 0f;
        float duration = 0.5f;
        Vector3 startScale = transform.localScale;
        Vector3 startPos = transform.position;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            transform.localScale = Vector3.Lerp(startScale, Vector3.zero, t);
            transform.position = startPos + Vector3.down * (t * 0.5f);
            yield return null;
        }

        gameObject.SetActive(false);
    }

    /// <summary>
    /// 當背景區域重置時，還原物件狀態
    /// </summary>
    public void ResetToInitialState()
    {
        gameObject.SetActive(isInitiallyActive);
        transform.position = initialPosition;
        transform.rotation = initialRotation;
        transform.localScale = initialScale;
        hasShattered = false;
    }

    private void ShatterSprite(SpriteRenderer sr)
    {
        // 建立碎裂碎片容器
        GameObject root = new GameObject(gameObject.name + "_Shattered");
        root.transform.position = transform.position;
        root.transform.rotation = transform.rotation;
        root.transform.localScale = transform.localScale;

        Bounds bounds = sr.bounds;
        Vector3 center = bounds.center;
        Vector3 size = bounds.size;

        int shardCols = 3;
        int shardRows = 4;

        for (int x = 0; x < shardCols; x++)
        {
            for (int y = 0; y < shardRows; y++)
            {
                GameObject shard = new GameObject($"RockShard_{x}_{y}");
                shard.transform.SetParent(root.transform);

                float pctX = (x + 0.5f) / shardCols - 0.5f;
                float pctY = (y + 0.5f) / shardRows - 0.5f;

                Vector3 pos = center + new Vector3(pctX * size.x * 0.7f, pctY * size.y * 0.7f, Random.Range(-0.05f, 0.05f));
                shard.transform.position = pos;

                // 計算 2D 碎石區塊縮放
                float scaleX = (size.x / shardCols) * Random.Range(0.6f, 0.9f);
                float scaleY = (size.y / shardRows) * Random.Range(0.6f, 0.9f);
                shard.transform.localScale = new Vector3(scaleX, scaleY, 1f);
                shard.transform.rotation = Quaternion.Euler(0, 0, Random.Range(-45f, 45f));

                // 複製原 2D 圖片與材質 (100% 免除 Read/Write 設定限制，必然成功爆破顯示！)
                SpriteRenderer shardSr = shard.AddComponent<SpriteRenderer>();
                shardSr.sprite = sr.sprite;
                shardSr.color = sr.color;
                shardSr.sortingLayerID = sr.sortingLayerID;
                shardSr.sortingOrder = sr.sortingOrder;
                shardSr.material = sr.material;

                // 物理碰撞與剛體
                BoxCollider shardCol = shard.AddComponent<BoxCollider>();
                shardCol.size = new Vector3(1f, 1f, 1f);

                Rigidbody shardRb = shard.AddComponent<Rigidbody>();
                shardRb.constraints = RigidbodyConstraints.FreezePositionZ;
            }
        }

        // 所有 12 塊碎石子物件生成完畢後，最後才掛載 ShatteredObject！這樣物理推力才能 100% 作用在所有碎片上！
        ShatteredObject shatteredComp = root.AddComponent<ShatteredObject>();
        shatteredComp.disappearDelay = disappearDelay;
        shatteredComp.explosionForce = explosionForce;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (shatterOnCollision)
        {
            Shatter();
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (shatterOnCollision)
        {
            Shatter();
        }
    }
}
