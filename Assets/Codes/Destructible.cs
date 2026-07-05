// Trigger compile 8
using UnityEngine;

public class Destructible : MonoBehaviour
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

            // 動態確保碎片掛載了控制消失的組件
            ShatteredObject shatteredComp = shatteredInstance.GetComponent<ShatteredObject>();
            if (shatteredComp == null)
            {
                shatteredComp = shatteredInstance.AddComponent<ShatteredObject>();
            }
            shatteredComp.disappearDelay = disappearDelay;
        }
        else
        {
            // 如果沒有指定預製體，則自動對當前的 2D Sprite 進行動態切片碎裂
            SpriteRenderer sr = GetComponent<SpriteRenderer>();
            if (sr != null && sr.sprite != null)
            {
                ShatterSprite(sr);
            }
            else
            {
                Debug.LogWarning($"[Destructible] {gameObject.name} 未指定碎片預製體，且沒有可供碎裂的 SpriteRenderer。");
            }
        }

        // 銷毀原完整物件
        Destroy(gameObject);
    }

    private void ShatterSprite(SpriteRenderer sr)
    {
        Sprite originalSprite = sr.sprite;
        Texture2D texture = originalSprite.texture;
        Rect rect = originalSprite.textureRect;
        float ppu = originalSprite.pixelsPerUnit;

        // 建立碎裂碎片根節點
        GameObject root = new GameObject(gameObject.name + "_Shattered");
        root.transform.position = transform.position;
        root.transform.rotation = transform.rotation;
        root.transform.localScale = transform.localScale;

        // 掛載 ShatteredObject 元件處理物理和消失
        ShatteredObject shatteredComp = root.AddComponent<ShatteredObject>();
        shatteredComp.disappearDelay = disappearDelay;
        shatteredComp.explosionForce = explosionForce;

        // 建立隨機化的切面網格坐標
        float[] xLines = new float[columns + 1];
        float[] yLines = new float[rows + 1];

        xLines[0] = rect.x;
        xLines[columns] = rect.x + rect.width;
        yLines[0] = rect.y;
        yLines[rows] = rect.y + rect.height;

        float avgWidth = rect.width / columns;
        for (int i = 1; i < columns; i++)
        {
            float nominal = rect.x + i * avgWidth;
            // 允許最大 35% 的隨機位移偏移，確保形狀不規則
            xLines[i] = nominal + Random.Range(-avgWidth * 0.35f, avgWidth * 0.35f);
        }

        float avgHeight = rect.height / rows;
        for (int j = 1; j < rows; j++)
        {
            float nominal = rect.y + j * avgHeight;
            yLines[j] = nominal + Random.Range(-avgHeight * 0.35f, avgHeight * 0.35f);
        }

        for (int x = 0; x < columns; x++)
        {
            for (int y = 0; y < rows; y++)
            {
                float xStart = xLines[x];
                float xEnd = xLines[x + 1];
                float yStart = yLines[y];
                float yEnd = yLines[y + 1];

                float shardWidth = xEnd - xStart;
                float shardHeight = yEnd - yStart;

                // 排除極小無意義的切片
                if (shardWidth < 1f || shardHeight < 1f) continue;

                // 計算碎片的 UV 區塊 (Rect)
                Rect subRect = new Rect(xStart, yStart, shardWidth, shardHeight);

                // 在 GPU 上為碎片建立獨立 Sprite
                Sprite shardSprite = Sprite.Create(texture, subRect, new Vector2(0.5f, 0.5f), ppu);

                // 建立碎片物件
                GameObject shard = new GameObject($"Shard_{x}_{y}");
                shard.transform.SetParent(root.transform);

                // 計算碎片相對於原物件中心點 (Pivot) 的本地座標
                float shardCenterX = xStart + shardWidth * 0.5f;
                float shardCenterY = yStart + shardHeight * 0.5f;

                float localX = ((shardCenterX - rect.x) - originalSprite.pivot.x) / ppu;
                float localY = ((shardCenterY - rect.y) - originalSprite.pivot.y) / ppu;
                shard.transform.localPosition = new Vector3(localX, localY, 0f);
                shard.transform.localScale = Vector3.one;

                // 複製原渲染器的設定
                SpriteRenderer shardSr = shard.AddComponent<SpriteRenderer>();
                shardSr.sprite = shardSprite;
                shardSr.color = sr.color;
                shardSr.sortingLayerID = sr.sortingLayerID;
                shardSr.sortingOrder = sr.sortingOrder;
                shardSr.material = sr.material;

                // 加入物理效果 (改用 3D 物理以與 3D 的地板/平台物件碰撞)
                BoxCollider shardCol = shard.AddComponent<BoxCollider>();
                // 設置碰撞體尺寸使其與 Sprite 大小吻合，並給予 Z 軸 1.0 的厚度以確保碰撞接觸
                shardCol.size = new Vector3(shardWidth / ppu, shardHeight / ppu, 1f);

                Rigidbody shardRb = shard.AddComponent<Rigidbody>();
                // 限制物理運算在 X-Y 平面（鎖定 Z 軸位移與 X/Y 軸旋轉）
                shardRb.constraints = RigidbodyConstraints.FreezePositionZ | 
                                      RigidbodyConstraints.FreezeRotationX | 
                                      RigidbodyConstraints.FreezeRotationY;
            }
        }
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
