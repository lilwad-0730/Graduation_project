using UnityEngine;

/// <summary>
/// 廢墟狂風暴沙全螢幕環境特效 (Ruined Storm & Flying Sand VFX)
/// 商業級獨立遊戲 (如《奧日》、《風之旅人》) 風格的橫向呼嘯暴風系統：
/// 1. 【高速風線 (Wind Streaks)】：半透明拉長氣流風紋，高速劃過螢幕，呈現強烈風壓感。
/// 2. 【飛馳風沙碎屑 (Flying Sand & Debris)】：密集微小沙粒與塵屑隨風渦流翻滾飄動。
/// 3. 【智慧相機跟隨 (Camera Follow Viewport)】：粒子發射器始終貼合螢幕視野，無死角覆蓋。
/// 4. 【自動掛載 / 即開即用】：可在 Inspector 自由調整風速、風向、密度與顏色。
/// </summary>
[ExecuteAlways]
public class RuinedStormWindVFX : MonoBehaviour
{
    [Header("🌪️ 風暴整體控制 (Storm Master Controls)")]
    [Tooltip("風向 (預設 -1 代表向左呼嘯，1 代表向右)")]
    public float windDirectionX = -1.0f;

    [Tooltip("風速倍率 (預設 24，數值越高風速越狂暴)")]
    [Range(10f, 60f)]
    public float windSpeed = 26f;

    [Tooltip("暴風沙顏色 (沙漠沙金色調)")]
    public Color stormColor = new Color(0.92f, 0.76f, 0.52f, 0.38f);

    [Header("💨 風線設定 (Wind Streaks)")]
    [Tooltip("每秒生成風線數量")]
    [Range(5f, 80f)]
    public float streakEmissionRate = 28f;

    [Tooltip("風線拉長倍率 (Length Scale)")]
    [Range(1f, 10f)]
    public float streakLengthScale = 4.5f;

    [Header("🏜️ 飛沙碎屑設定 (Flying Sand Grains)")]
    [Tooltip("每秒生成飛沙碎屑數量")]
    [Range(20f, 300f)]
    public float sandEmissionRate = 95f;

    [Tooltip("氣流擾動旋轉強度 (Turbulence)")]
    [Range(0f, 3f)]
    public float turbulenceStrength = 1.2f;

    [Header("📷 視野覆蓋 (Camera Viewport)")]
    public float targetZ = 1.0f;

    private ParticleSystem _streakPS;
    private ParticleSystem _sandPS;
    private Camera _mainCam;

    private void Awake()
    {
        InitializeComponents();
    }

    private void OnEnable()
    {
        InitializeComponents();
    }

    private void OnValidate()
    {
        UpdateParticleProperties();
    }

    private void LateUpdate()
    {
        if (_mainCam == null) _mainCam = Camera.main ?? Object.FindFirstObjectByType<Camera>();
        if (_mainCam != null)
        {
            Vector3 camPos = _mainCam.transform.position;
            transform.position = new Vector3(camPos.x, camPos.y, targetZ);
        }
    }

    public void InitializeComponents()
    {
        // 1. 初始化風線粒子系統 (Wind Streaks)
        Transform streakTrans = transform.Find("WindStreaks");
        if (streakTrans == null)
        {
            GameObject streakObj = new GameObject("WindStreaks");
            streakObj.transform.SetParent(transform, false);
            _streakPS = streakObj.AddComponent<ParticleSystem>();
        }
        else
        {
            _streakPS = streakTrans.GetComponent<ParticleSystem>();
        }

        // 2. 初始化飛沙碎屑粒子系統 (Sand Grains)
        Transform sandTrans = transform.Find("SandGrains");
        if (sandTrans == null)
        {
            GameObject sandObj = new GameObject("SandGrains");
            sandObj.transform.SetParent(transform, false);
            _sandPS = sandObj.AddComponent<ParticleSystem>();
        }
        else
        {
            _sandPS = sandTrans.GetComponent<ParticleSystem>();
        }

        UpdateParticleProperties();
    }

    public void UpdateParticleProperties()
    {
        SetupWindStreaks();
        SetupSandGrains();
    }

    private void SetupWindStreaks()
    {
        if (_streakPS == null) return;

        // 主模組
        var main = _streakPS.main;
        main.playOnAwake = true;
        main.loop = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.2f, 0.45f);
        main.startColor = stormColor;
        main.gravityModifier = 0.02f;

        // 發射量
        var emission = _streakPS.emission;
        emission.rateOverTime = streakEmissionRate;

        // 發射形狀 (相機視野右側/左側長條盒)
        var shape = _streakPS.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(45f, 26f, 4f);

        // 速度 (橫向高速噴射)
        var vel = _streakPS.velocityOverLifetime;
        vel.enabled = true;
        float dir = Mathf.Sign(windDirectionX);
        float minSpd = windSpeed * 0.75f * dir;
        float maxSpd = windSpeed * 1.25f * dir;
        vel.x = new ParticleSystem.MinMaxCurve(Mathf.Min(minSpd, maxSpd), Mathf.Max(minSpd, maxSpd));
        vel.y = new ParticleSystem.MinMaxCurve(-1.5f, 0.8f);
        vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

        // 色彩漸層 (快速淡入 ➔ 風線呼嘯 ➔ 淡出)
        var col = _streakPS.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] { new GradientColorKey(stormColor, 0f), new GradientColorKey(Color.white, 1f) },
            new GradientAlphaKey[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(stormColor.a, 0.2f), new GradientAlphaKey(stormColor.a, 0.75f), new GradientAlphaKey(0f, 1f) }
        );
        col.color = grad;

        // 渲染器 (拉長條紋 Stretched Billboard)
        var rend = _streakPS.GetComponent<ParticleSystemRenderer>();
        if (rend != null)
        {
            rend.renderMode = ParticleSystemRenderMode.Stretch;
            rend.velocityScale = 0.05f;
            rend.lengthScale = streakLengthScale;
            rend.sortingLayerName = "Default";
            rend.sortingOrder = 24;

            if (rend.sharedMaterial == null || rend.sharedMaterial.shader.name.Contains("Standard"))
            {
                Shader s = Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
                if (s != null) rend.material = new Material(s);
            }
        }
    }

    private void SetupSandGrains()
    {
        if (_sandPS == null) return;

        // 主模組
        var main = _sandPS.main;
        main.playOnAwake = true;
        main.loop = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.25f);
        main.startColor = new Color(stormColor.r, stormColor.g, stormColor.b, 0.65f);
        main.gravityModifier = 0.04f;

        // 發射量
        var emission = _sandPS.emission;
        emission.rateOverTime = sandEmissionRate;

        // 發射形狀
        var shape = _sandPS.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(45f, 26f, 4f);

        // 速度
        var vel = _sandPS.velocityOverLifetime;
        vel.enabled = true;
        float dir = Mathf.Sign(windDirectionX);
        float minSpd = windSpeed * 0.55f * dir;
        float maxSpd = windSpeed * 1.05f * dir;
        vel.x = new ParticleSystem.MinMaxCurve(Mathf.Min(minSpd, maxSpd), Mathf.Max(minSpd, maxSpd));
        vel.y = new ParticleSystem.MinMaxCurve(-2.0f, 1.2f);
        vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

        // 氣流渦流噪訊 (Turbulence Noise)
        var noise = _sandPS.noise;
        noise.enabled = turbulenceStrength > 0f;
        noise.strength = turbulenceStrength;
        noise.frequency = 0.8f;
        noise.scrollSpeed = 1.5f;

        // 旋轉動態
        var rot = _sandPS.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-180f * Mathf.Deg2Rad, 180f * Mathf.Deg2Rad);

        // 色彩淡入淡出
        var col = _sandPS.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] { new GradientColorKey(stormColor, 0f), new GradientColorKey(stormColor, 1f) },
            new GradientAlphaKey[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.7f, 0.15f), new GradientAlphaKey(0.7f, 0.8f), new GradientAlphaKey(0f, 1f) }
        );
        col.color = grad;

        // 渲染器 (Billboard)
        var rend = _sandPS.GetComponent<ParticleSystemRenderer>();
        if (rend != null)
        {
            rend.renderMode = ParticleSystemRenderMode.Billboard;
            rend.sortingLayerName = "Default";
            rend.sortingOrder = 24;

            if (rend.sharedMaterial == null || rend.sharedMaterial.shader.name.Contains("Standard"))
            {
                Shader s = Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
                if (s != null) rend.material = new Material(s);
            }
        }
    }
}
