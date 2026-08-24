using UnityEngine;

/// <summary>
/// 統一棉絮粒子系統控制器 (Unified Fluff Particle System Controller)。
/// 1. 維持美術原創設計：完全靜態固定於世界天空背景（覆蓋 1000 米超大區域，不跟隨相機）。
/// 2. 整合 9 種棉絮切片姿態於單一粒子系統中，生成時隨機抽取，生成後外貌固定。
/// 3. 【加量加密升級】：大幅提高生成速率與密度，解決原本稀疏不明顯的問題，重現夢幻棉花海！
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(ParticleSystem))]
public class FluffSystemController : MonoBehaviour
{
    [Header("粒子尺寸控制 (Particle Size Controls)")]
    [Tooltip("棉絮粒子最小尺寸 (美術原版大小)")]
    [Range(0.1f, 5.0f)]
    public float minParticleSize = 0.6f;

    [Tooltip("棉絮粒子最大尺寸 (美術原版大小)")]
    [Range(0.1f, 10.0f)]
    public float maxParticleSize = 1.8f;

    [Header("分佈範圍控制 (Distribution Range Controls - 美術原版超大天空盒)")]
    [Tooltip("生成區域水平寬度 (覆蓋 1000 米長天空區域)")]
    public float spawnAreaWidth = 1000f;

    [Tooltip("生成區域垂直高度 (覆蓋天空高度)")]
    public float spawnAreaHeight = 200f;

    [Tooltip("生成區域前後深度 (Scale Z)")]
    public float spawnAreaDepth = 4.0f;

    [Header("生成數量與動態 (Emission & Dynamics - 加密加量)")]
    [Tooltip("每秒生成棉絮數量 (大幅提升至 450/s，使 1000 米範圍隨時密集顯眼)")]
    [Range(10f, 1000f)]
    public float emissionRate = 450f;

    [Tooltip("棉絮最小存活時間 (秒)")]
    public float particleLifetimeMin = 6.0f;

    [Tooltip("棉絮最大存活時間 (秒)")]
    public float particleLifetimeMax = 12.0f;

    [Tooltip("微風右向推進力 (X 軸速度)")]
    public float windForceX = 0.6f;

    [Tooltip("不規則氣流搖晃強度 (Turbulence Noise)")]
    [Range(0f, 2.0f)]
    public float turbulenceStrength = 0.5f;

    private ParticleSystem ps;

    private void Awake()
    {
        EnsureScale();
        UpdateParticleSettings();
    }

    private void OnEnable()
    {
        EnsureScale();
        UpdateParticleSettings();
    }

    private void OnValidate()
    {
        UpdateParticleSettings();
    }

    private void EnsureScale()
    {
        // 確保 LocalScale 保持 1:1，不被微縮
        if (transform.localScale != Vector3.one)
        {
            transform.localScale = Vector3.one;
        }
    }

    public void UpdateParticleSettings()
    {
        if (ps == null) ps = GetComponent<ParticleSystem>();
        if (ps == null) return;

        // 1. 基礎主模組設定 (尺寸、生命週期與最大粒子數)
        var main = ps.main;
        main.playOnAwake = true;
        main.loop = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 6000; // 支援 1000 米大範圍內的高密度常駐粒子
        main.startLifetime = new ParticleSystem.MinMaxCurve(particleLifetimeMin, particleLifetimeMax);
        main.startSize = new ParticleSystem.MinMaxCurve(minParticleSize, maxParticleSize);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, 360f * Mathf.Deg2Rad);
        main.startColor = new Color(1.0f, 0.99f, 0.95f, 0.9f);
        main.gravityModifier = -0.012f;

        // 2. 生成數量 (加強至 450/s)
        var emission = ps.emission;
        emission.rateOverTime = emissionRate;

        // 3. 生成分佈範圍 (美術原版超大天空盒 1000m x 200m)
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(spawnAreaWidth, spawnAreaHeight, spawnAreaDepth);

        // 4. 關鍵核心：9 種棉絮姿態 3x3 網格 - 生成時隨機選擇姿態，生成後固定外貌！
        var tsa = ps.textureSheetAnimation;
        tsa.enabled = true;
        tsa.mode = ParticleSystemAnimationMode.Grid;
        tsa.numTilesX = 3;
        tsa.numTilesY = 3;
        tsa.animation = ParticleSystemAnimationType.WholeSheet;
        tsa.timeMode = ParticleSystemAnimationTimeMode.Lifetime;

        // 設定 frameOverTime 恆為 0 階梯，使每個粒子在整個生命週期內保持生成的固定幀，不再切換幀
        tsa.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
        // 生成瞬間隨機從 0 ~ 8 幀 (9 種姿態) 中選取一種
        tsa.startFrame = new ParticleSystem.MinMaxCurve(0f, 8f);

        // 5. 風速與不規則飄動
        var velocityOverLife = ps.velocityOverLifetime;
        velocityOverLife.enabled = true;
        velocityOverLife.x = new ParticleSystem.MinMaxCurve(windForceX * 0.5f, windForceX * 1.5f);
        velocityOverLife.y = new ParticleSystem.MinMaxCurve(-0.1f, 0.3f);
        velocityOverLife.z = new ParticleSystem.MinMaxCurve(0f, 0f);

        var noise = ps.noise;
        noise.enabled = turbulenceStrength > 0f;
        noise.strength = turbulenceStrength;
        noise.frequency = 0.6f;
        noise.scrollSpeed = 0.3f;

        var rotOverLife = ps.rotationOverLifetime;
        rotOverLife.enabled = true;
        rotOverLife.z = new ParticleSystem.MinMaxCurve(-35f * Mathf.Deg2Rad, 35f * Mathf.Deg2Rad);

        var colOverLife = ps.colorOverLifetime;
        colOverLife.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.98f, 0.92f), 1f) },
            new GradientAlphaKey[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.2f), new GradientAlphaKey(0.9f, 0.8f), new GradientAlphaKey(0f, 1f) }
        );
        colOverLife.color = grad;

        var psRenderer = GetComponent<ParticleSystemRenderer>();
        if (psRenderer != null)
        {
            psRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            psRenderer.sortingLayerName = "Default";
            psRenderer.sortingOrder = 22; // 浮在背景貼圖前端
        }
    }
}
