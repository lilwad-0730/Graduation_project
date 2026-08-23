using UnityEngine;
using System.Collections;

/// <summary>
/// 專用高質感 2D 平台玻璃平台爆裂與粒子特效系統 (Glass Shatter FX System)。
/// 結合：
///   1. 本體不規則四邊形切片爆裂 (Destructible Physical Glass Quad Mesh Shatter)
///   2. Stage 1: 踩上時震動 + 裂痕前兆 + 少量細小落屑
///   3. Stage 2: 爆裂瞬間 0.08 秒冰藍反光微亮點 (Glass Burst Flash)
///   4. Stage 3: 雙層高效能 Particle System (微粒子與切片噴發)
/// 支援隱藏本體貼圖與 Collider 但保持粒子與物理切片生命週期播放完畢，支援 IResettable 關卡重置。
/// </summary>
public class GlassShatterFX : MonoBehaviour, IResettable
{
    [Header("Stage 1 - 踩踏前兆與震動")]
    [Tooltip("踩上至爆裂的預兆時間 (秒)")]
    public float delayBeforeShatter = 2.0f;

    [Tooltip("震動強度")]
    public float shakeIntensity = 0.05f;

    [Header("Stage 2 - 爆裂瞬間光效 (Glass Burst Flash)")]
    [Tooltip("瞬間反光亮點持續時間 (秒)")]
    public float burstFlashDuration = 0.08f;

    [Tooltip("反光亮點顏色 (淡白/冰藍)")]
    public Color burstFlashColor = new Color(0.85f, 0.95f, 1.0f, 0.9f);

    [Header("Stage 3 - Layer 1: 主要玻璃切片 (Main Glass Shards)")]
    [Tooltip("主要碎片數量 (15~25 個，提升數量並縮小尺寸)")]
    public Vector2Int mainShardCountRange = new Vector2Int(15, 25);

    [Tooltip("主要碎片生命週期 (秒)")]
    public Vector2 mainShardLifetimeRange = new Vector2(0.6f, 1.3f);

    [Tooltip("主要碎片初速度 (純自由落體極低初速)")]
    public Vector2 mainShardSpeedRange = new Vector2(0.3f, 1.2f);

    [Tooltip("主要碎片尺寸 (原本 50~70% 大小，保留少量中型，主體更細碎)")]
    public Vector2 mainShardSizeRange = new Vector2(0.12f, 0.35f);

    [Tooltip("重力加速度影響 (純自由落體重力)")]
    public float mainShardGravityModifier = 2.5f;

    [Header("Stage 3 - Layer 2: 細小玻璃碎屑 (Micro Glass Particles)")]
    [Tooltip("細小碎屑數量 (30~50 個，大量強化細節感)")]
    public Vector2Int microParticleCountRange = new Vector2Int(30, 50);

    [Tooltip("細小碎屑生命週期 (0.2~0.6 秒)")]
    public Vector2 microParticleLifetimeRange = new Vector2(0.2f, 0.6f);

    [Tooltip("細小碎屑初速度 (柔和散落 0.8~2.2)")]
    public Vector2 microParticleSpeedRange = new Vector2(0.8f, 2.2f);

    [Tooltip("細小碎屑尺寸 (極小晶瑩玻璃粒子 0.02~0.08)")]
    public Vector2 microParticleSizeRange = new Vector2(0.02f, 0.08f);

    // --- 內部組件與狀態 ---
    private SpriteRenderer mainSr;
    private Collider mainCol;
    private Rigidbody mainRb;

    private ParticleSystem mainShardsPs;
    private ParticleSystem microDustPs;
    private GameObject flashObj;
    private SpriteRenderer flashSr;

    private Vector3 initialPos;
    private Quaternion initialRot;
    private bool isShattered = false;
    private bool isTriggered = false;

    private static Sprite cachedShardSprite;
    private static Sprite cachedMicroSprite;
    private static Sprite cachedFlashSprite;

    private void Awake()
    {
        mainSr = GetComponent<SpriteRenderer>();
        mainCol = GetComponent<Collider>();
        mainRb = GetComponent<Rigidbody>();

        initialPos = transform.position;
        initialRot = transform.rotation;

        SetupParticleSystems();
        SetupBurstFlashObject();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (IsPlayer(collision.gameObject)) TriggerBreakSequence();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (IsPlayer(other.gameObject)) TriggerBreakSequence();
    }

    private bool IsPlayer(GameObject go)
    {
        if (go == null) return false;
        if (go.CompareTag("Player")) return true;
        if (go.name.ToLower().Contains("player") || go.GetComponent<PlayerMovement>() != null) return true;
        return false;
    }

    public void TriggerBreakSequence()
    {
        if (isTriggered || isShattered) return;
        isTriggered = true;
        StartCoroutine(ShatterSequenceRoutine());
    }

    private IEnumerator ShatterSequenceRoutine()
    {
        float timer = 0f;
        float dustTimer = 0f;

        while (timer < delayBeforeShatter)
        {
            timer += Time.deltaTime;
            dustTimer += Time.deltaTime;

            Vector3 shakeOffset = Random.insideUnitSphere * shakeIntensity;
            shakeOffset.z = 0f;
            transform.position = initialPos + Vector3.down * (timer * 0.2f) + shakeOffset;

            if (dustTimer > 0.4f && microDustPs != null)
            {
                dustTimer = 0f;
                microDustPs.Emit(Random.Range(1, 3));
            }

            yield return null;
        }

        transform.position = initialPos;
        ExecuteShatter();
    }

    public void ExecuteShatter()
    {
        if (isShattered) return;
        isShattered = true;

        // 1. 觸發平台本體切面不規則四邊形網格切片碎裂 (Physical Mesh Shatter)
        Destructible dest = GetComponent<Destructible>();
        if (dest != null)
        {
            dest.Shatter();
        }

        // 2. 關閉本體 SpriteRenderer 與 Collider (讓玩家自然掉落)
        if (mainSr != null) mainSr.enabled = false;
        if (mainCol != null) mainCol.enabled = false;
        if (mainRb != null)
        {
            if (!mainRb.isKinematic) mainRb.linearVelocity = Vector3.zero;
            mainRb.isKinematic = true;
            mainRb.useGravity = false;
        }

        Transform overlay = transform.Find("CrackOverlay");
        if (overlay != null) overlay.gameObject.SetActive(false);

        // 3. 播放 Stage 2 瞬間反光亮點 (Glass Burst Flash)
        if (flashObj != null && gameObject.activeInHierarchy)
        {
            StartCoroutine(FlashRoutine());
        }

        // 4. 觸發 Stage 3 雙層粒子發射 (主碎片 + 細碎微粒)
        if (mainShardsPs != null)
        {
            int count = Random.Range(mainShardCountRange.x, mainShardCountRange.y + 1);
            mainShardsPs.Emit(count);
        }

        if (microDustPs != null)
        {
            int count = Random.Range(microParticleCountRange.x, microParticleCountRange.y + 1);
            microDustPs.Emit(count);
        }
    }

    private IEnumerator FlashRoutine()
    {
        if (flashObj == null || flashSr == null) yield break;

        flashObj.SetActive(true);
        float timer = 0f;

        while (timer < burstFlashDuration)
        {
            timer += Time.deltaTime;
            float progress = Mathf.Clamp01(timer / burstFlashDuration);
            Color c = burstFlashColor;
            c.a = burstFlashColor.a * (1f - progress);
            flashSr.color = c;
            yield return null;
        }

        flashObj.SetActive(false);
    }

    private void SetupParticleSystems()
    {
        Transform shardsTrans = transform.Find("MainGlassShardsPS");
        GameObject shardsGo;
        if (shardsTrans == null)
        {
            shardsGo = new GameObject("MainGlassShardsPS");
            shardsGo.transform.SetParent(transform, false);
            shardsGo.transform.localPosition = Vector3.zero;
        }
        else
        {
            shardsGo = shardsTrans.gameObject;
        }

        mainShardsPs = shardsGo.GetComponent<ParticleSystem>();
        if (mainShardsPs == null) mainShardsPs = shardsGo.AddComponent<ParticleSystem>();

        var mainModule = mainShardsPs.main;
        mainModule.playOnAwake = false;
        mainModule.loop = false;
        mainModule.simulationSpace = ParticleSystemSimulationSpace.World;
        mainModule.startLifetime = new ParticleSystem.MinMaxCurve(mainShardLifetimeRange.x, mainShardLifetimeRange.y);
        mainModule.startSpeed = new ParticleSystem.MinMaxCurve(mainShardSpeedRange.x, mainShardSpeedRange.y);
        mainModule.startSize = new ParticleSystem.MinMaxCurve(mainShardSizeRange.x, mainShardSizeRange.y);
        mainModule.startRotation = new ParticleSystem.MinMaxCurve(0f, 360f * Mathf.Deg2Rad);
        mainModule.gravityModifier = mainShardGravityModifier;
        mainModule.startColor = new Color(0.9f, 0.96f, 1.0f, 0.85f);

        var emission = mainShardsPs.emission;
        emission.enabled = false;

        var shape = mainShardsPs.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(GetBoundsSize().x * 0.8f, 0.2f, 0.1f);

        var rotOverLifetime = mainShardsPs.rotationOverLifetime;
        rotOverLifetime.enabled = true;
        rotOverLifetime.z = new ParticleSystem.MinMaxCurve(-240f * Mathf.Deg2Rad, 240f * Mathf.Deg2Rad);

        var psRenderer = shardsGo.GetComponent<ParticleSystemRenderer>();
        if (psRenderer != null)
        {
            if (mainSr != null)
            {
                psRenderer.sortingLayerID = mainSr.sortingLayerID;
                psRenderer.sortingOrder = mainSr.sortingOrder + 2;
            }
            if (cachedShardSprite == null) cachedShardSprite = CreateGlassShardSprite();
            psRenderer.renderMode = ParticleSystemRenderMode.Mesh;
            psRenderer.mesh = CreateShardMesh();
            psRenderer.material = GetOrCreateParticleMaterial();
        }

        Transform microTrans = transform.Find("MicroGlassDustPS");
        GameObject microGo;
        if (microTrans == null)
        {
            microGo = new GameObject("MicroGlassDustPS");
            microGo.transform.SetParent(transform, false);
            microGo.transform.localPosition = Vector3.zero;
        }
        else
        {
            microGo = microTrans.gameObject;
        }

        microDustPs = microGo.GetComponent<ParticleSystem>();
        if (microDustPs == null) microDustPs = microGo.AddComponent<ParticleSystem>();

        var microMain = microDustPs.main;
        microMain.playOnAwake = false;
        microMain.loop = false;
        microMain.simulationSpace = ParticleSystemSimulationSpace.World;
        microMain.startLifetime = new ParticleSystem.MinMaxCurve(microParticleLifetimeRange.x, microParticleLifetimeRange.y);
        microMain.startSpeed = new ParticleSystem.MinMaxCurve(microParticleSpeedRange.x, microParticleSpeedRange.y);
        microMain.startSize = new ParticleSystem.MinMaxCurve(microParticleSizeRange.x, microParticleSizeRange.y);
        microMain.startRotation = new ParticleSystem.MinMaxCurve(0f, 360f * Mathf.Deg2Rad);
        microMain.gravityModifier = 1.2f;
        microMain.startColor = new Color(1.0f, 1.0f, 1.0f, 0.95f);

        var microEmission = microDustPs.emission;
        microEmission.enabled = false;

        var microShape = microDustPs.shape;
        microShape.shapeType = ParticleSystemShapeType.Hemisphere;
        microShape.radius = GetBoundsSize().x * 0.3f;
        microShape.rotation = new Vector3(90f, 0f, 0f);

        var colorOverLifetime = microDustPs.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] { new GradientColorKey(Color.white, 0.0f), new GradientColorKey(new Color(0.7f, 0.9f, 1.0f), 1.0f) },
            new GradientAlphaKey[] { new GradientAlphaKey(1.0f, 0.0f), new GradientAlphaKey(0.0f, 1.0f) }
        );
        colorOverLifetime.color = grad;

        var microRenderer = microGo.GetComponent<ParticleSystemRenderer>();
        if (microRenderer != null)
        {
            if (mainSr != null)
            {
                microRenderer.sortingLayerID = mainSr.sortingLayerID;
                microRenderer.sortingOrder = mainSr.sortingOrder + 3;
            }
            microRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            microRenderer.material = GetOrCreateParticleMaterial();
        }
    }

    private void SetupBurstFlashObject()
    {
        Transform child = transform.Find("GlassBurstFlash");
        if (child == null)
        {
            flashObj = new GameObject("GlassBurstFlash");
            flashObj.transform.SetParent(transform, false);
            flashObj.transform.localPosition = new Vector3(0f, 0f, -0.02f);
        }
        else
        {
            flashObj = child.gameObject;
        }

        flashSr = flashObj.GetComponent<SpriteRenderer>();
        if (flashSr == null) flashSr = flashObj.AddComponent<SpriteRenderer>();

        if (cachedFlashSprite == null) cachedFlashSprite = CreateFlashSprite();
        flashSr.sprite = cachedFlashSprite;
        flashSr.color = burstFlashColor;

        if (mainSr != null)
        {
            flashSr.sortingLayerID = mainSr.sortingLayerID;
            flashSr.sortingOrder = mainSr.sortingOrder + 5;
        }

        Vector3 boundsSize = GetBoundsSize();
        flashObj.transform.localScale = new Vector3(boundsSize.x * 1.2f, boundsSize.y * 1.8f, 1.0f);
        flashObj.SetActive(false);
    }

    private Vector3 GetBoundsSize()
    {
        if (mainSr != null && mainSr.sprite != null) return mainSr.bounds.size;
        return new Vector3(4f, 1f, 1f);
    }

    private Material particleMat;
    private Material GetOrCreateParticleMaterial()
    {
        if (particleMat == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Mobile/Particles/Additive");
            particleMat = new Material(shader);
        }
        return particleMat;
    }

    private Mesh CreateShardMesh()
    {
        Mesh mesh = new Mesh();
        mesh.vertices = new Vector3[]
        {
            new Vector3(-0.3f, 0.4f, 0f),
            new Vector3(0.4f, 0.2f, 0f),
            new Vector3(0.2f, -0.4f, 0f),
            new Vector3(-0.4f, -0.1f, 0f)
        };
        mesh.uv = new Vector2[]
        {
            new Vector2(0f, 1f),
            new Vector2(1f, 1f),
            new Vector2(1f, 0f),
            new Vector2(0f, 0f)
        };
        mesh.triangles = new int[] { 0, 1, 2, 0, 2, 3 };
        mesh.RecalculateNormals();
        return mesh;
    }

    private Sprite CreateGlassShardSprite()
    {
        int size = 64;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float normX = (float)x / size;
                float normY = (float)y / size;
                if (normX + normY < 1.3f && normX - normY < 0.6f && normY - normX < 0.6f)
                {
                    pixels[y * size + x] = new Color(0.85f, 0.95f, 1.0f, 0.85f);
                }
                else
                {
                    pixels[y * size + x] = Color.clear;
                }
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    private Sprite CreateFlashSprite()
    {
        int size = 128;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
        float radius = size * 0.48f;
        Color[] pixels = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center) / radius;
                if (dist >= 1f)
                {
                    pixels[y * size + x] = Color.clear;
                }
                else
                {
                    float alpha = Mathf.Exp(-4.0f * dist * dist);
                    pixels[y * size + x] = new Color(0.9f, 0.96f, 1.0f, alpha);
                }
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    public void ResetToInitialState()
    {
        StopAllCoroutines();
        isTriggered = false;
        isShattered = false;

        transform.position = initialPos;
        transform.rotation = initialRot;

        if (mainSr != null) mainSr.enabled = true;
        if (mainCol != null) mainCol.enabled = true;
        if (mainRb != null)
        {
            if (!mainRb.isKinematic) mainRb.linearVelocity = Vector3.zero;
            mainRb.isKinematic = true;
            mainRb.useGravity = false;
        }

        if (flashObj != null) flashObj.SetActive(false);

        if (mainShardsPs != null) mainShardsPs.Clear();
        if (microDustPs != null) microDustPs.Clear();

        Destructible dest = GetComponent<Destructible>();
        if (dest != null)
        {
            dest.ResetToInitialState();
        }
    }
}
