using UnityEngine;

/// <summary>
/// 輕量清爽版沙漠風砂特效 (Clean Desert Wind & Sand Streaks VFX)
/// 專注於純粹自然的水平風線與微小飛沙，徹底杜絕任何巨大方塊或複雜干擾：
/// 1. 【細長水平風線 (Sleek Horizontal Wind Streaks)】：純水平橫向拉長條紋，洗鍊俐落。
/// 2. 【細微飛沙塵點 (Fine Sand Specks)】：細小柔和的沙塵微粒隨風快速掠過。
/// 3. 【柔和漸層防方塊 (Soft Radial Texture)】：自動套用柔邊圓點貼圖，杜絕任何未貼圖方塊穿幫。
/// 4. 【簡潔穩定】：無冗餘圖層，效能極佳且視覺清爽。
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(ParticleSystem))]
public class DesertWindDustFX : MonoBehaviour
{
    [Header("🌪️ 風向與風速")]
    [Tooltip("風向 (預設 -1 代表從右往左吹向玩家)")]
    public float windDirectionX = -1.0f;

    [Tooltip("風速強度 (預設 28)")]
    [Range(10f, 60f)]
    public float windSpeed = 28f;

    [Tooltip("風沙顏色 (沙漠暖沙金)")]
    public Color dustColor = new Color(0.92f, 0.74f, 0.48f, 0.45f);

    [Tooltip("是否跟隨相機視野？(預設關閉，由場景 Transform 自由決定位置)")]
    public bool followCamera = false;

    [Header("💨 風線與飛沙數量")]
    [Tooltip("每秒風線數量 (預設 120，量多豐富)")]
    [Range(10f, 300f)]
    public float streakEmissionRate = 120f;

    [Tooltip("每秒微細飛沙數量 (預設 220，密集飛舞)")]
    [Range(20f, 500f)]
    public float sandGrainEmissionRate = 220f;

    [Tooltip("風線拉長倍率 (預設 6.5)")]
    [Range(2f, 15f)]
    public float streakLengthScale = 6.5f;

    [Header("📐 發射範圍 (Emitter Box Size)")]
    [Tooltip("發射盒尺寸 (寬度 X, 高度 Y, 深度 Z)")]
    public Vector3 emitterSize = new Vector3(50f, 22f, 1f);

    private ParticleSystem _mainStreakPS;
    private ParticleSystem _sandGrainPS;
    private Camera _cachedCam;
    private static Texture2D _softParticleTex;

    private void Awake()
    {
        InitializeComponents();
    }

    private void OnEnable()
    {
        InitializeComponents();
    }

    private void Start()
    {
        ApplyVFXSettings();
    }

    private void OnValidate()
    {
        ApplyVFXSettings();
    }

    private void LateUpdate()
    {
        transform.localRotation = Quaternion.identity;

        if (!followCamera) return;

        if (_cachedCam == null)
        {
            _cachedCam = Camera.main;
            if (_cachedCam == null) _cachedCam = FindFirstObjectByType<Camera>();
        }

        if (_cachedCam != null)
        {
            Vector3 camPos = _cachedCam.transform.position;
            transform.position = new Vector3(camPos.x, camPos.y, 0f);
        }
    }

    public void InitializeComponents()
    {
        transform.localRotation = Quaternion.identity;

        // 徹底清除可能存在的巨大方塊 DustHaze 子物件
        Transform oldHaze = transform.Find("DustHaze");
        if (oldHaze != null)
        {
            if (Application.isPlaying) Destroy(oldHaze.gameObject);
            else DestroyImmediate(oldHaze.gameObject);
        }

        _mainStreakPS = GetComponent<ParticleSystem>();

        // 搜尋或建立 SandGrains 子物件
        Transform sandChild = transform.Find("SandGrains");
        if (sandChild == null)
        {
            GameObject sandObj = new GameObject("SandGrains");
            sandObj.transform.SetParent(transform, false);
            sandObj.transform.localPosition = Vector3.zero;
            sandObj.transform.localRotation = Quaternion.identity;
            _sandGrainPS = sandObj.AddComponent<ParticleSystem>();
        }
        else
        {
            sandChild.localRotation = Quaternion.identity;
            _sandGrainPS = sandChild.GetComponent<ParticleSystem>();
        }

        ApplyVFXSettings();
    }

    public void ApplyVFXSettings()
    {
        transform.localRotation = Quaternion.identity;

        SetupWindStreaks();
        SetupSandGrains();
    }

    private void SetupWindStreaks()
    {
        if (_mainStreakPS == null) _mainStreakPS = GetComponent<ParticleSystem>();
        if (_mainStreakPS == null) return;

        var main = _mainStreakPS.main;
        main.playOnAwake = true;
        main.loop = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.30f);
        main.startColor = dustColor;
        main.gravityModifier = 0f;
        main.maxParticles = 1000;

        var emission = _mainStreakPS.emission;
        emission.enabled = true;
        emission.rateOverTime = streakEmissionRate;

        var shape = _mainStreakPS.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = emitterSize;
        shape.position = Vector3.zero;
        shape.rotation = Vector3.zero;

        var vel = _mainStreakPS.velocityOverLifetime;
        vel.enabled = true;
        float dir = Mathf.Sign(windDirectionX);
        float minX = windSpeed * 0.85f * dir;
        float maxX = windSpeed * 1.25f * dir;
        vel.x = new ParticleSystem.MinMaxCurve(Mathf.Min(minX, maxX), Mathf.Max(minX, maxX));
        vel.y = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
        vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

        var col = _mainStreakPS.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] { new GradientColorKey(dustColor, 0f), new GradientColorKey(dustColor, 1f) },
            new GradientAlphaKey[] {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(dustColor.a, 0.2f),
                new GradientAlphaKey(dustColor.a * 0.8f, 0.8f),
                new GradientAlphaKey(0f, 1f)
            }
        );
        col.color = grad;

        var rend = _mainStreakPS.GetComponent<ParticleSystemRenderer>();
        if (rend != null)
        {
            rend.renderMode = ParticleSystemRenderMode.Stretch;
            rend.lengthScale = streakLengthScale;
            rend.velocityScale = 0.05f;
            rend.sortingLayerName = "Default";
            rend.sortingOrder = 22;

            EnsureSoftParticleMaterial(rend);
        }
    }

    private void SetupSandGrains()
    {
        if (_sandGrainPS == null) return;

        var main = _sandGrainPS.main;
        main.playOnAwake = true;
        main.loop = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.3f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f); // 極細沙粒
        main.startColor = new Color(dustColor.r, dustColor.g, dustColor.b, Mathf.Clamp01(dustColor.a * 1.3f));
        main.gravityModifier = 0f;
        main.maxParticles = 1200;

        var emission = _sandGrainPS.emission;
        emission.enabled = true;
        emission.rateOverTime = sandGrainEmissionRate;

        var shape = _sandGrainPS.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = emitterSize;
        shape.position = Vector3.zero;
        shape.rotation = Vector3.zero;

        var vel = _sandGrainPS.velocityOverLifetime;
        vel.enabled = true;
        float dir = Mathf.Sign(windDirectionX);
        float minX = windSpeed * 0.7f * dir;
        float maxX = windSpeed * 1.1f * dir;
        vel.x = new ParticleSystem.MinMaxCurve(Mathf.Min(minX, maxX), Mathf.Max(minX, maxX));
        vel.y = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
        vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

        var col = _sandGrainPS.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] { new GradientColorKey(dustColor, 0f), new GradientColorKey(dustColor, 1f) },
            new GradientAlphaKey[] {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(Mathf.Clamp01(dustColor.a * 1.2f), 0.15f),
                new GradientAlphaKey(Mathf.Clamp01(dustColor.a * 1.0f), 0.85f),
                new GradientAlphaKey(0f, 1f)
            }
        );
        col.color = grad;

        var rend = _sandGrainPS.GetComponent<ParticleSystemRenderer>();
        if (rend != null)
        {
            rend.renderMode = ParticleSystemRenderMode.Billboard;
            rend.sortingLayerName = "Default";
            rend.sortingOrder = 22;

            EnsureSoftParticleMaterial(rend);
        }
    }

    /// <summary>
    /// 自動生成並賦予柔邊圓形粒子材質，徹底杜絕無貼圖方形 Quad 穿幫
    /// </summary>
    private Material _softMat;   // 只建一次。原本每個 LateUpdate 都 new Material＋rend.material（每幀漏兩個材質、Shader.Find 三次）

    private void EnsureSoftParticleMaterial(ParticleSystemRenderer rend)
    {
        if (rend == null) return;

        if (_softParticleTex == null)
        {
            _softParticleTex = CreateSoftCircleTexture(32);
        }

        if (_softMat == null)
        {
            Shader s = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (s == null) s = Shader.Find("Particles/Standard Unlit");
            if (s == null) s = Shader.Find("Sprites/Default");
            if (s == null) return;

            _softMat = new Material(s);
            _softMat.name = "DesertWind_SoftMat";
            _softMat.hideFlags = HideFlags.HideAndDontSave;   // ExecuteAlways 在編輯器就會生，別讓它被序列化進 .unity（每存檔都換 fileID、diff 700 行）
            if (_softMat.HasProperty("_BaseMap")) _softMat.SetTexture("_BaseMap", _softParticleTex);
            if (_softMat.HasProperty("_MainTex")) _softMat.SetTexture("_MainTex", _softParticleTex);
        }

        // sharedMaterial：不要用 .material（那會再複製一份，編輯器模式下還會噴 Instantiating material 警告）
        if (rend.sharedMaterial != _softMat) rend.sharedMaterial = _softMat;
    }

    /// <summary>
    /// 動態產生 32x32 柔和羽化圓點貼圖
    /// </summary>
    private static Texture2D CreateSoftCircleTexture(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.name = "Procedural_SoftCircle";
        tex.hideFlags = HideFlags.HideAndDontSave;   // 同上，不進場景檔
        Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
        float radius = size * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center);
                float t = Mathf.Clamp01(dist / radius);
                float alpha = Mathf.SmoothStep(1f, 0f, t);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        tex.Apply();
        return tex;
    }

    public void Play()
    {
        if (_mainStreakPS != null && !_mainStreakPS.isPlaying) _mainStreakPS.Play();
        if (_sandGrainPS != null && !_sandGrainPS.isPlaying) _sandGrainPS.Play();
    }

    public void Stop()
    {
        if (_mainStreakPS != null && _mainStreakPS.isPlaying) _mainStreakPS.Stop();
        if (_sandGrainPS != null && _sandGrainPS.isPlaying) _sandGrainPS.Stop();
    }
}