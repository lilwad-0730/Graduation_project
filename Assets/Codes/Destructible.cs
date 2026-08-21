using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// 玻璃不規則細小四邊形爆裂組件 (Irregular Micro Quad Glass Shatter System)。
/// 根據平台實際 Bounds 的長寬比例動態計算高密度細小正方形基礎網格 (Aspect-Ratio Aware Grid)，
/// 搭配強效不規則頂點抖動，將平台本體裁切為 120~200 塊細小晶瑩的玻璃切片爆裂！
/// </summary>
public class Destructible : MonoBehaviour, IResettable
{
    [Header("Shattered Prefab (選填)")]
    public GameObject shatteredPrefab;

    [Header("碎裂設定")]
    [Tooltip("碰撞時是否自動碎裂")]
    public bool shatterOnCollision = false;

    [Tooltip("碎片消失延遲 (秒)")]
    public float disappearDelay = 2.5f;

    [Tooltip("短邊切分網格數 (將依長寬比例自動計算長邊細分)")]
    [Range(3, 10)]
    public int minGridSubdivisions = 6;

    [Tooltip("四邊形不規則抖動程度 (0.1 ~ 0.45)")]
    [Range(0.1f, 0.45f)]
    public float jitterAmount = 0.42f;

    [Tooltip("碎裂爆裂力道")]
    public float explosionForce = 4.5f;

    [Header("🎵 碎裂音效 (Shatter SFX)")]
    [Tooltip("碎裂時播放的音效 (例如 玻璃碎裂.mp3 / 玻璃館_破鏡牆.wav)")]
    public AudioClip shatterSFX;
    [Range(0f, 1f)] public float sfxVolume = 0.95f;

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

        // 播放碎裂音效
        if (shatterSFX != null)
        {
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFXAt(shatterSFX, transform.position, sfxVolume);
            else AudioSource.PlayClipAtPoint(shatterSFX, transform.position, sfxVolume);
        }

        if (shatteredPrefab != null)
        {
            GameObject shatteredInstance = Instantiate(shatteredPrefab, transform.position, transform.rotation);
            shatteredInstance.transform.localScale = transform.localScale;
            ShatteredObject shatteredComp = shatteredInstance.GetComponent<ShatteredObject>();
            if (shatteredComp == null) shatteredComp = shatteredInstance.AddComponent<ShatteredObject>();
            shatteredComp.disappearDelay = disappearDelay;
            gameObject.SetActive(false);
        }
        else
        {
            SpriteRenderer sr = GetComponent<SpriteRenderer>();
            if (sr == null) sr = GetComponentInChildren<SpriteRenderer>();

            if (sr != null && sr.sprite != null)
            {
                ShatterSpriteToMicroGlassQuads(sr);
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

    /// <summary>
    /// 生成長寬比例吻合的高密度細小不規則玻璃切片 (Aspect-Ratio Aware Micro Glass Quads)
    /// </summary>
    private void ShatterSpriteToMicroGlassQuads(SpriteRenderer sr)
    {
        Sprite sprite = sr.sprite;
        Texture2D texture = sprite.texture;
        Rect textureRect = sprite.textureRect;
        Bounds bounds = sr.bounds;
        Vector3 centerPos = bounds.center;

        GameObject root = new GameObject(gameObject.name + "_MicroGlassShattered");
        root.transform.position = centerPos;

        float width = bounds.size.x;
        float height = bounds.size.y;
        float aspectRatio = Mathf.Max(0.1f, width / Mathf.Max(0.01f, height));

        int rws = Mathf.Max(3, minGridSubdivisions);
        int cols = Mathf.Clamp(Mathf.RoundToInt(rws * aspectRatio), 6, 60);

        // 1. 建立帶有強效隨機抖動的高密度網格 (Jittered Vertex Grid)
        Vector2[,] grid = new Vector2[cols + 1, rws + 1];
        Random.InitState((int)(Time.time * 1000f) + gameObject.GetInstanceID());

        for (int x = 0; x <= cols; x++)
        {
            for (int y = 0; y <= rws; y++)
            {
                float normX = (float)x / cols;
                float normY = (float)y / rws;

                float offsetX = (x > 0 && x < cols) ? Random.Range(-jitterAmount, jitterAmount) / cols : 0f;
                float offsetY = (y > 0 && y < rws) ? Random.Range(-jitterAmount, jitterAmount) / rws : 0f;

                grid[x, y] = new Vector2(
                    Mathf.Clamp01(normX + offsetX),
                    Mathf.Clamp01(normY + offsetY)
                );
            }
        }

        // 2. 構建大量細小晶瑩不規則四邊形切片 Mesh
        for (int x = 0; x < cols; x++)
        {
            for (int y = 0; y < rws; y++)
            {
                Vector2 p0 = grid[x, y];         // Top-Left
                Vector2 p1 = grid[x + 1, y];     // Top-Right
                Vector2 p2 = grid[x + 1, y + 1]; // Bottom-Right
                Vector2 p3 = grid[x, y + 1];     // Bottom-Left

                CreateSingleMicroQuadShard(root.transform, sprite, texture, textureRect, bounds, p0, p1, p2, p3, sr, x, y);
            }
        }

        ShatteredObject shatteredComp = root.AddComponent<ShatteredObject>();
        shatteredComp.disappearDelay = disappearDelay;
        shatteredComp.explosionForce = explosionForce;
    }

    private void CreateSingleMicroQuadShard(Transform parent, Sprite sprite, Texture2D texture, Rect rect, Bounds bounds, Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, SpriteRenderer origSr, int gridX, int gridY)
    {
        GameObject shard = new GameObject($"{gameObject.name}_MicroShard_{gridX}_{gridY}");
        shard.transform.SetParent(parent);

        Vector2 centerUV = (p0 + p1 + p2 + p3) * 0.25f;
        Vector3 worldPos = new Vector3(
            bounds.min.x + centerUV.x * bounds.size.x,
            bounds.min.y + centerUV.y * bounds.size.y,
            bounds.center.z + Random.Range(-0.02f, 0.02f)
        );
        shard.transform.position = worldPos;

        Vector3 v0 = new Vector3((p0.x - centerUV.x) * bounds.size.x, (p0.y - centerUV.y) * bounds.size.y, 0f);
        Vector3 v1 = new Vector3((p1.x - centerUV.x) * bounds.size.x, (p1.y - centerUV.y) * bounds.size.y, 0f);
        Vector3 v2 = new Vector3((p2.x - centerUV.x) * bounds.size.x, (p2.y - centerUV.y) * bounds.size.y, 0f);
        Vector3 v3 = new Vector3((p3.x - centerUV.x) * bounds.size.x, (p3.y - centerUV.y) * bounds.size.y, 0f);

        Vector2 uv0 = new Vector2((rect.x + p0.x * rect.width) / texture.width, (rect.y + p0.y * rect.height) / texture.height);
        Vector2 uv1 = new Vector2((rect.x + p1.x * rect.width) / texture.width, (rect.y + p1.y * rect.height) / texture.height);
        Vector2 uv2 = new Vector2((rect.x + p2.x * rect.width) / texture.width, (rect.y + p2.y * rect.height) / texture.height);
        Vector2 uv3 = new Vector2((rect.x + p3.x * rect.width) / texture.width, (rect.y + p3.y * rect.height) / texture.height);

        Mesh mesh = new Mesh();
        mesh.vertices = new Vector3[] { v0, v1, v2, v3 };
        mesh.uv = new Vector2[] { uv0, uv1, uv2, uv3 };
        mesh.triangles = new int[] { 0, 1, 2, 0, 2, 3 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        MeshFilter mf = shard.AddComponent<MeshFilter>();
        mf.mesh = mesh;

        MeshRenderer mr = shard.AddComponent<MeshRenderer>();
        mr.sharedMaterial = origSr.material;
        mr.material.mainTexture = texture;
        mr.sortingLayerID = origSr.sortingLayerID;
        mr.sortingOrder = origSr.sortingOrder + 1;

        BoxCollider col = shard.AddComponent<BoxCollider>();
        col.isTrigger = true;

        Rigidbody rb = shard.AddComponent<Rigidbody>();
        rb.useGravity = true;
        rb.constraints = RigidbodyConstraints.FreezePositionZ;

        // 純自由落體：極微幅水平漂移，主要由自然重力拉引沉降與輕盈自轉
        Vector3 freeFallDir = new Vector3(
            Random.Range(-0.2f, 0.2f),
            Random.Range(-0.8f, -0.2f),
            0f
        );

        rb.AddForce(freeFallDir * (explosionForce * Random.Range(0.5f, 1.0f)), ForceMode.Impulse);
        rb.AddTorque(Random.insideUnitSphere * Random.Range(2.0f, 6.0f), ForceMode.Impulse);
    }
}
