using UnityEngine;
using System.Collections.Generic;

public class Destructible : MonoBehaviour, IResettable
{
    [Header("Shattered Prefab to Spawn (Optional)")]
    public GameObject shatteredPrefab;

    [Header("Shatter Settings")]
    public bool shatterOnCollision = true;
    public float minCollisionVelocity = 2f;
    public float disappearDelay = 2f; // 碎裂後幾秒開始消失

    [Header("Auto-Shatter Grid Settings")]
    public int columns = 4;
    public int rows = 5;
    public float explosionForce = 3f;

    private bool hasShattered = false;

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

    public void Shatter()
    {
        if (hasShattered) return;
        hasShattered = true;

        if (shatteredPrefab != null)
        {
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
            SpriteRenderer sr = GetComponent<SpriteRenderer>();
            if (sr == null) sr = GetComponentInChildren<SpriteRenderer>();

            if (sr != null && sr.sprite != null)
            {
                ShatterSprite(sr);
                gameObject.SetActive(false);
            }
            else
            {
                gameObject.SetActive(false);
            }
        }
    }

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
        if (sr == null || sr.sprite == null) return;

        // 建立碎裂碎片容器
        GameObject root = new GameObject(gameObject.name + "_Shattered");
        root.transform.position = transform.position;
        root.transform.rotation = transform.rotation;
        root.transform.localScale = transform.localScale;

        Sprite origSprite = sr.sprite;
        Texture2D tex = origSprite.texture;
        Rect origRect = origSprite.textureRect;
        float ppu = origSprite.pixelsPerUnit;

        int shardCols = columns > 0 ? columns : 4;
        int shardRows = rows > 0 ? rows : 5;

        float shardW = origRect.width / shardCols;
        float shardH = origRect.height / shardRows;

        Bounds bounds = sr.bounds;
        Vector3 wallMin = bounds.min;
        Vector3 wallSize = bounds.size;

        for (int x = 0; x < shardCols; x++)
        {
            for (int y = 0; y < shardRows; y++)
            {
                GameObject shard = new GameObject($"{gameObject.name}_Shard_{x}_{y}");
                shard.transform.SetParent(root.transform);

                // 精確計算碎片在原物件世界座標的位置
                float pctX = (x + 0.5f) / shardCols;
                float pctY = (y + 0.5f) / shardRows;

                Vector3 shardWorldPos = new Vector3(
                    wallMin.x + pctX * wallSize.x,
                    wallMin.y + pctY * wallSize.y,
                    transform.position.z + Random.Range(-0.02f, 0.02f)
                );
                shard.transform.position = shardWorldPos;

                // 直接裁切物件本體的圖案區域，產生 100% 物件本身的精確碎片 Sprite
                Rect subRect = new Rect(origRect.x + x * shardW, origRect.y + y * shardH, shardW, shardH);
                Sprite shardSprite = Sprite.Create(tex, subRect, new Vector2(0.5f, 0.5f), ppu);

                SpriteRenderer shardSr = shard.AddComponent<SpriteRenderer>();
                shardSr.sprite = shardSprite;
                shardSr.color = sr.color;
                shardSr.sortingLayerID = sr.sortingLayerID;
                shardSr.sortingOrder = sr.sortingOrder + 1;
                shardSr.material = sr.material;

                // 掛載 3D 碰撞體與剛體 (設為 isTrigger 避免物理互卡，順暢受重力下墜)
                BoxCollider col = shard.AddComponent<BoxCollider>();
                col.size = new Vector3(wallSize.x / shardCols * 0.85f, wallSize.y / shardRows * 0.85f, 0.2f);
                col.isTrigger = true;

                Rigidbody rb = shard.AddComponent<Rigidbody>();
                rb.useGravity = true;
                rb.constraints = RigidbodyConstraints.FreezePositionZ;

                // 賦予重力崩塌下墜初速度與隨機扭力
                Vector3 burstDir = new Vector3(Random.Range(-1.5f, 1.5f), Random.Range(-2.5f, -0.5f), 0f);
                rb.AddForce(burstDir * (explosionForce * 1.5f), ForceMode.Impulse);
                rb.AddTorque(Random.insideUnitSphere * explosionForce * 5.0f, ForceMode.Impulse);

                // 讓碎片忽略周圍地磚碰撞，確保能夠流暢地向下方重力沉降崩塌
                Collider[] envCols = Object.FindObjectsByType<Collider>(FindObjectsSortMode.None);
                foreach (var ec in envCols)
                {
                    if (ec != null && ec.gameObject != shard && (ec.transform.parent == null || ec.transform.parent != root.transform))
                    {
                        Physics.IgnoreCollision(col, ec, true);
                    }
                }
            }
        }

        ShatteredObject shatteredComp = root.AddComponent<ShatteredObject>();
        shatteredComp.disappearDelay = disappearDelay;
        shatteredComp.explosionForce = explosionForce;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (shatterOnCollision) Shatter();
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (shatterOnCollision) Shatter();
    }
}
