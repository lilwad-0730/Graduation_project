using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// ★1109j 荒原的「一天」：玩家從左走到綠洲，光線從明亮午後慢慢走到金黃傍晚、綠洲的夕陽暖光，到了就是「終於到了」。
///
/// 做法（全部程序產生，沒有新增任何貼圖資產、沒有碰關卡裡任何道具）：
///  1) 全場乘色：一張貼著鏡頭、畫在所有世界物件最上面（sortingOrder 50，在 60 的世界字幕之下）的乘色面片。
///     地面、岩柱、仙人掌、綠洲（棕櫚／岸邊／水）、主角、鳥全部吃同一個乘色 → 綠洲永遠對得上地面，
///     而且不用去改任何 SpriteRenderer.color（WindShelter／DynamicFadeShelter 會寫岩柱的 color，硬寫會打架）。
///  2) 天空漸層：畫在背景（sortingOrder -1）之上、地面之下的一片頂點色漸層，頂端偏玫瑰橘、地平線偏金黃，只蓋天空上半部。
///  3) 低懸太陽光暈：同一層的加色柔光（大光暈＋小亮心），越往綠洲越亮、越低，風暴時變弱。
///  4) 3D 的主角、鳥：Directional Light 顏色／強度、環境光往暖色偏一點（原值會快取、停用時還原）。
///  5) 風暴：吹風時整體往「暗＋沙塵黃」混，漸入漸出。讀 WindGustSystem（全場陣風，StormHazardWave 觸發區只是它的碰撞範圍），
///     另外開了 manualStorm 手動鉤子給別的腳本／測試用。
///
/// 另外會 Shader.SetGlobalColor("_DesertTint"／"_DesertSkyTint")、SetGlobalFloat("_DesertStorm")，之後要在別的 shader 讀都行。
/// 編輯模式預設完全不動（不改場景）；要預覽就勾 previewInEditMode＋調 previewX（／previewStorm），取消勾選就全部還原。
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class DesertTimeOfDay : MonoBehaviour
{
    // ───────────────────────── 驅動 ─────────────────────────
    [Header("驅動（玩家 X → 漸層時間）")]
    [Tooltip("玩家的 Tag；找不到就維持上一個位置")]
    public string playerTag = "Player";
    [Tooltip("漸層時間 0 對應的世界 X")]
    public float startX = 0f;
    [Tooltip("漸層時間 1 對應的世界 X（綠洲水池約 273）")]
    public float endX = 290f;
    [Tooltip("評估用的 X 最快每秒跟著跑多少。玩家重生被搬回起點時，色調會在幾秒內慢慢轉回，不是瞬間跳")]
    public float followMaxSpeed = 45f;

    [Header("編輯模式預覽（不勾＝編輯模式完全不動場景）")]
    public bool previewInEditMode = false;
    public float previewX = 0f;
    [Range(0f, 1f)] public float previewStorm = 0f;

    // ───────────────────────── 全場乘色 ─────────────────────────
    [Header("全場乘色（乘在所有世界物件上；白＝不變）。Gradient 時間＝(X-startX)/(endX-startX)")]
    public Gradient groundTint = MakeGradient(
        new float[] { 0f, 0.34f, 0.62f, 0.76f, 0.90f, 1f },
        new Color[] {
            new Color(1f, 1f, 1f),
            new Color(1f, 0.99f, 0.97f),
            new Color(1f, 0.935f, 0.84f),
            new Color(1f, 0.90f, 0.77f),
            new Color(1f, 0.865f, 0.73f),
            new Color(1f, 0.85f, 0.71f) });

    // ───────────────────────── 天空 ─────────────────────────
    [Header("天空漸層（RGB＝顏色，Alpha＝不透明度）")]
    [Tooltip("天空頂端（畫面上緣）")]
    public Gradient skyTop = MakeGradient(
        new float[] { 0f, 0.48f, 0.62f, 0.76f, 0.90f, 1f },
        new Color[] {
            new Color(1f, 0.60f, 0.50f), new Color(1f, 0.60f, 0.50f), new Color(0.95f, 0.56f, 0.38f),
            new Color(0.92f, 0.46f, 0.32f), new Color(0.84f, 0.36f, 0.36f), new Color(0.78f, 0.31f, 0.40f) },
        new float[] { 0f, 0f, 0.10f, 0.20f, 0.30f, 0.34f });
    [Tooltip("天空靠地平線處")]
    public Gradient skyHorizon = MakeGradient(
        new float[] { 0f, 0.48f, 0.62f, 0.76f, 0.90f, 1f },
        new Color[] {
            new Color(1f, 0.80f, 0.45f), new Color(1f, 0.80f, 0.45f), new Color(1f, 0.78f, 0.42f),
            new Color(1f, 0.70f, 0.34f), new Color(1f, 0.62f, 0.28f), new Color(1f, 0.58f, 0.26f) },
        new float[] { 0f, 0f, 0.10f, 0.22f, 0.36f, 0.42f });
    [Tooltip("天空漸層『地平線』的世界 Y。低於這裡再往下 skyFadeBelow 米淡出到 0（遠景岩壁底部約 -4、頂部約 1）")]
    public float horizonWorldY = 3.5f;
    [Tooltip("地平線下方多少米內淡出")]
    public float skyFadeBelow = 4f;
    [Tooltip("漸層曲線指數：<1 讓顏色更快從地平線色換成頂端色")]
    public float skyGradientPower = 0.75f;
    public int skySortingOrder = -1;
    public float skyZ = 0.45f;

    // ───────────────────────── 太陽 ─────────────────────────
    [Header("低懸太陽光暈（加色）")]
    [Tooltip("太陽強度（0～1）對漸層時間。傍晚後才慢慢浮現")]
    public AnimationCurve sunAmount = new AnimationCurve(
        new Keyframe(0f, 0f), new Keyframe(0.5f, 0f), new Keyframe(0.66f, 0.18f), new Keyframe(0.82f, 0.6f), new Keyframe(1f, 1f));
    [Tooltip("太陽中心的世界 Y 對漸層時間（越走越低）")]
    public AnimationCurve sunHeight = new AnimationCurve(
        new Keyframe(0f, 17f), new Keyframe(0.5f, 13f), new Keyframe(0.75f, 7f), new Keyframe(1f, 3.0f));
    public Gradient sunColor = MakeGradient(
        new float[] { 0f, 0.6f, 0.8f, 1f },
        new Color[] { new Color(1f, 0.88f, 0.65f), new Color(1f, 0.82f, 0.52f), new Color(1f, 0.72f, 0.40f), new Color(1f, 0.64f, 0.32f) });
    [Tooltip("太陽在畫面水平的位置（相對鏡頭中心，−0.5～0.5 的畫面寬度比例）")]
    [Range(-0.5f, 0.5f)] public float sunViewportX = 0.2f;
    [Tooltip("大光暈直徑（世界米）")]
    public float sunHaloSize = 46f;
    [Tooltip("小亮心直徑（世界米）")]
    public float sunCoreSize = 11f;
    [Range(0f, 1f)] public float sunHaloAlpha = 0.75f;
    [Range(0f, 1f)] public float sunCoreAlpha = 0.85f;
    public int sunSortingOrder = -1;
    public float sunZ = 0.42f;

    // ───────────────────────── 3D 燈光 ─────────────────────────
    [Header("3D 燈光（主角、鳥）：乘在原本的顏色上")]
    public Gradient directionalLightTint = MakeGradient(
        new float[] { 0f, 0.5f, 0.75f, 1f },
        new Color[] { Color.white, new Color(1f, 0.97f, 0.93f), new Color(1f, 0.92f, 0.82f), new Color(1f, 0.88f, 0.74f) });
    [Tooltip("Directional Light 強度倍率對漸層時間")]
    public AnimationCurve directionalIntensity = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 1.1f));
    public Gradient ambientTint = MakeGradient(
        new float[] { 0f, 0.5f, 0.75f, 1f },
        new Color[] { Color.white, new Color(1f, 0.98f, 0.95f), new Color(1f, 0.94f, 0.87f), new Color(1f, 0.91f, 0.82f) });

    // ───────────────────────── 風暴 ─────────────────────────
    [Header("風暴（吹風時變暗、沙塵黃；漸入漸出）")]
    [Tooltip("自動讀 WindGustSystem（吹風＝強度、起風前兆＝一小部分）")]
    public bool useAutoStorm = true;
    [Tooltip("手動鉤子：別的腳本或測試可直接設 0～1，與自動值取較大者")]
    [Range(0f, 1f)] public float manualStorm = 0f;
    [Tooltip("起風前兆（沙塵線）占風暴強度的比例，0＝前兆不變色")]
    [Range(0f, 1f)] public float telegraphWeight = 0.25f;
    public float stormFadeInSeconds = 0.9f;
    public float stormFadeOutSeconds = 1.8f;
    [Tooltip("風暴全強度時的全場乘色（暗＋沙塵黃）")]
    public Color stormTint = new Color(0.80f, 0.70f, 0.52f);
    [Tooltip("風暴全強度時的天空沙塵色（RGB），不透明度見下")]
    public Color stormSkyColor = new Color(0.86f, 0.66f, 0.36f);
    [Range(0f, 1f)] public float stormSkyAlpha = 0.42f;
    [Tooltip("風暴全強度時太陽光暈剩幾成")]
    [Range(0f, 1f)] public float stormSunKeep = 0.25f;
    [Tooltip("風暴全強度時 Directional Light 強度倍率")]
    public float stormLightIntensity = 0.85f;

    [Header("疊色用 shader（Custom/DesertTodOverlay，指到 Assets/Shaders/DesertTimeOfDay；用欄位引用才會被打包進建置）")]
    public Shader overlayShader;

    // ───────────────────────── 唯讀狀態 ─────────────────────────
    /// <summary>目前（平滑後）的風暴強度 0～1，別的腳本可讀。</summary>
    public float StormIntensity { get { return _storm; } }
    /// <summary>目前用來評估的 X。</summary>
    public float EvalX { get { return _x; } }

    // ───────────────────────── 內部 ─────────────────────────
    private const string ObjPrefix = "__TOD_";
    private Transform _player;
    private float _x;
    private bool _hasX;
    private float _storm;

    private GameObject _tintGO, _skyGO, _haloGO, _coreGO;
    private MeshRenderer _tintMR, _skyMR, _haloMR, _coreMR;
    private Mesh _quad, _skyMesh;
    private Material _tintMat, _skyMat, _haloMat, _coreMat;
    private Texture2D _haloTex, _coreTex;

    // 這一幀算好的狀態（相機位置相關的幾何在 beginCameraRendering 才擺）
    private Color _skyTopCol, _skyHorCol;
    private float _sunAmt, _sunY;
    private Color _sunCol;
    private bool _built;

    // 燈光快取（OnDisable 還原）
    private Light[] _dirLights;
    private Color[] _dirLightColors;
    private float[] _dirLightIntensities;
    private Color _origAmbient;
    private bool _lightsCached;

    private static Gradient MakeGradient(float[] times, Color[] cols, float[] alphas = null)
    {
        Gradient g = new Gradient();
        GradientColorKey[] ck = new GradientColorKey[times.Length];
        GradientAlphaKey[] ak = new GradientAlphaKey[times.Length];
        for (int i = 0; i < times.Length; i++)
        {
            ck[i] = new GradientColorKey(cols[i], times[i]);
            ak[i] = new GradientAlphaKey(alphas != null ? alphas[i] : 1f, times[i]);
        }
        g.SetKeys(ck, ak);
        return g;
    }

    private bool Active { get { return Application.isPlaying || previewInEditMode; } }

    // ───────────────────────── 生命週期 ─────────────────────────
    private void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        _hasX = false;
        if (Active) Build();
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
        Teardown();
        Shader.SetGlobalColor("_DesertTint", Color.white);
        Shader.SetGlobalColor("_DesertSkyTint", Color.white);
        Shader.SetGlobalFloat("_DesertStorm", 0f);
    }

#if UNITY_EDITOR
    private void Reset() { AutoAssignShader(); }

    private void OnValidate()
    {
        AutoAssignShader();
        if (!Application.isPlaying)
        {
            // 取消預覽 → 全部還原；開著預覽 → 立刻重算（延後到 delayCall，OnValidate 裡不能建物件）
            EditorApplication.delayCall += () =>
            {
                if (this == null) return;
                if (previewInEditMode) Refresh(); else Teardown();
            };
        }
    }

    private void AutoAssignShader()
    {
        if (overlayShader != null) return;
        overlayShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/DesertTimeOfDay/DesertTodOverlay.shader");
    }
#endif

    private void LateUpdate()
    {
        if (!Active) { if (_built) Teardown(); return; }
        float dt = Application.isPlaying ? Time.unscaledDeltaTime : 0f;
        Tick(dt, !Application.isPlaying);
    }

    /// <summary>停止編輯模式預覽：取消勾選並立刻還原燈光、移除暫時物件（截圖腳本收尾用）。</summary>
    public void StopPreview()
    {
        previewInEditMode = false;
        Teardown();
    }

    /// <summary>編輯模式用：立刻照 previewX／previewStorm 重算（給 Inspector 預覽與截圖腳本）。</summary>
    public void Refresh()
    {
        if (!Active) return;
        Tick(0f, true);
    }

    // ───────────────────────── 每幀 ─────────────────────────
    private void Tick(float dt, bool instant)
    {
        if (!_built && !Build()) return;

        // 1) 玩家 X
        float targetX = previewX;
        if (Application.isPlaying)
        {
            if (_player == null)
            {
                GameObject p = GameObject.FindWithTag(playerTag);
                if (p != null) _player = p.transform;
            }
            targetX = _player != null ? _player.position.x : (_hasX ? _x : startX);
        }
        if (!_hasX || instant) { _x = targetX; _hasX = true; }
        else _x = Mathf.MoveTowards(_x, targetX, Mathf.Max(1f, followMaxSpeed) * dt);

        // 2) 風暴強度
        float target = Application.isPlaying ? manualStorm : previewStorm;
        if (useAutoStorm && Application.isPlaying) target = Mathf.Max(target, ReadAutoStorm());
        if (instant) _storm = target;
        else
        {
            float sec = target > _storm ? stormFadeInSeconds : stormFadeOutSeconds;
            _storm = Mathf.MoveTowards(_storm, target, dt / Mathf.Max(0.05f, sec));
        }

        ApplyState(Mathf.InverseLerp(startX, endX, _x), _storm);
    }

    /// <summary>吹風＝推力強度；前兆＝前兆進度×telegraphWeight；永久停風（綠洲前）＝0。WindGustSystem 是全場陣風（風痕鋪滿全屏、推力全域），所以不另外比對 StormHazardWave 的觸發區。</summary>
    private float ReadAutoStorm()
    {
        WindGustSystem w = WindGustSystem.Instance;
        if (w == null || w.IsStoppedForever) return 0f;
        if (w.CurrentState == WindState.Blowing) return Mathf.Clamp01(Mathf.Max(0.15f, w.PushStrength01));
        if (w.IsTelegraphing) return telegraphWeight * w.TelegraphProgress01;
        return 0f;
    }

    private void ApplyState(float t, float s)
    {
        // 全場乘色
        Color g = groundTint.Evaluate(t);
        Color tint = new Color(g.r * Mathf.Lerp(1f, stormTint.r, s), g.g * Mathf.Lerp(1f, stormTint.g, s), g.b * Mathf.Lerp(1f, stormTint.b, s), 1f);
        _tintMat.SetColor("_Color", tint);
        Shader.SetGlobalColor("_DesertTint", tint);
        Shader.SetGlobalFloat("_DesertStorm", s);

        // 天空
        Color top = skyTop.Evaluate(t);
        Color hor = skyHorizon.Evaluate(t);
        Color st = stormSkyColor;
        _skyTopCol = new Color(Mathf.Lerp(top.r, st.r, s), Mathf.Lerp(top.g, st.g, s), Mathf.Lerp(top.b, st.b, s), Mathf.Lerp(top.a, Mathf.Max(top.a, stormSkyAlpha), s));
        _skyHorCol = new Color(Mathf.Lerp(hor.r, st.r, s), Mathf.Lerp(hor.g, st.g, s), Mathf.Lerp(hor.b, st.b, s), Mathf.Lerp(hor.a, Mathf.Max(hor.a, stormSkyAlpha * 0.8f), s));
        Shader.SetGlobalColor("_DesertSkyTint", _skyTopCol);

        // 太陽
        _sunAmt = Mathf.Clamp01(sunAmount.Evaluate(t)) * Mathf.Lerp(1f, stormSunKeep, s);
        _sunY = sunHeight.Evaluate(t);
        _sunCol = sunColor.Evaluate(t);
        Color hc = _sunCol; hc.a = _sunAmt * sunHaloAlpha;
        Color cc = Color.Lerp(_sunCol, Color.white, 0.45f); cc.a = _sunAmt * sunCoreAlpha;
        _haloMat.SetColor("_Color", hc);
        _coreMat.SetColor("_Color", cc);
        _haloGO.SetActive(_sunAmt > 0.002f);
        _coreGO.SetActive(_sunAmt > 0.002f);

        // 3D 燈光
        ApplyLights(t, s);
    }

    private void ApplyLights(float t, float s)
    {
        if (!_lightsCached) return;
        Color lt = directionalLightTint.Evaluate(t);
        float li = Mathf.Max(0f, directionalIntensity.Evaluate(t)) * Mathf.Lerp(1f, stormLightIntensity, s);
        Color storm = new Color(Mathf.Lerp(1f, stormTint.r, s), Mathf.Lerp(1f, stormTint.g, s), Mathf.Lerp(1f, stormTint.b, s));
        for (int i = 0; i < _dirLights.Length; i++)
        {
            if (_dirLights[i] == null) continue;
            Color o = _dirLightColors[i];
            _dirLights[i].color = new Color(o.r * lt.r * storm.r, o.g * lt.g * storm.g, o.b * lt.b * storm.b, o.a);
            _dirLights[i].intensity = _dirLightIntensities[i] * li;
        }
        Color at = ambientTint.Evaluate(t);
        RenderSettings.ambientLight = new Color(_origAmbient.r * at.r * storm.r, _origAmbient.g * at.g * storm.g, _origAmbient.b * at.b * storm.b, _origAmbient.a);
    }

    // ───────────────────────── 幾何（每台相機繪製前才擺，避免 Cinemachine 更新順序造成邊緣晃動） ─────────────────────────
    private void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
    {
        if (!_built || cam == null || cam != Camera.main) return;
        if (_tintGO == null || _skyGO == null) return;

        Transform ct = cam.transform;
        float size = cam.orthographic ? cam.orthographicSize : 17f;
        float aspect = cam.aspect;
        float halfH = size, halfW = size * aspect;

        // 乘色面片：貼在鏡頭前，略放大 25% 蓋住邊緣
        float d = Mathf.Max(cam.nearClipPlane + 0.5f, 1f);
        float h = cam.orthographic ? halfH * 2f : 2f * d * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        _tintGO.transform.SetPositionAndRotation(ct.position + ct.forward * d, ct.rotation);
        _tintGO.transform.localScale = new Vector3(h * aspect * 1.25f, h * 1.25f, 1f);

        // 天空漸層
        BuildSkyMesh(ct.position.x, ct.position.y + halfH * 1.05f, halfW * 1.15f);
        _skyGO.transform.position = new Vector3(0f, 0f, skyZ);

        // 太陽
        if (_haloGO.activeSelf)
        {
            Vector3 sp = new Vector3(ct.position.x + sunViewportX * halfW * 2f, _sunY, sunZ);
            _haloGO.transform.position = sp;
            _haloGO.transform.localScale = new Vector3(sunHaloSize, sunHaloSize, 1f);
            _coreGO.transform.position = new Vector3(sp.x, sp.y, sunZ - 0.01f);
            _coreGO.transform.localScale = new Vector3(sunCoreSize, sunCoreSize, 1f);
        }
    }

    private const int SkyRows = 32;
    private Vector3[] _skyV;
    private Color[] _skyC;

    private void BuildSkyMesh(float cx, float yTop, float halfW)
    {
        float yBottom = horizonWorldY - Mathf.Max(0.1f, skyFadeBelow);
        if (yTop <= yBottom + 0.5f) { _skyMR.enabled = false; return; }
        _skyMR.enabled = true;

        if (_skyV == null) { _skyV = new Vector3[(SkyRows + 1) * 2]; _skyC = new Color[(SkyRows + 1) * 2]; }
        float zero = 0f;
        for (int i = 0; i <= SkyRows; i++)
        {
            float u = (float)i / SkyRows;
            float y = Mathf.Lerp(yBottom, yTop, u);
            float hgt = Mathf.Clamp01((y - horizonWorldY) / Mathf.Max(0.1f, yTop - horizonWorldY));
            float k = Mathf.Pow(hgt, Mathf.Max(0.05f, skyGradientPower));
            float fade = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(yBottom, horizonWorldY + 2f, y));
            Color c = Color.Lerp(_skyHorCol, _skyTopCol, k);
            c.a *= fade;
            _skyV[i * 2] = new Vector3(cx - halfW, y, zero);
            _skyV[i * 2 + 1] = new Vector3(cx + halfW, y, zero);
            _skyC[i * 2] = c;
            _skyC[i * 2 + 1] = c;
        }
        _skyMesh.vertices = _skyV;
        _skyMesh.colors = _skyC;
        _skyMesh.RecalculateBounds();
    }

    // ───────────────────────── 建立／拆除 ─────────────────────────
    private bool Build()
    {
        if (_built) return true;
        if (overlayShader == null) overlayShader = Shader.Find("Custom/DesertTodOverlay");
        if (overlayShader == null)
        {
            Debug.LogWarning("[DesertTimeOfDay] 找不到 Custom/DesertTodOverlay shader，時間感效果停用。請把 Assets/Shaders/DesertTimeOfDay/DesertTodOverlay.shader 指到 overlayShader 欄位。");
            return false;
        }

        // 清掉殘留（網域重新載入後舊的 DontSave 子物件還在）
        ClearChildren();

        _quad = new Mesh { name = ObjPrefix + "Quad", hideFlags = HideFlags.HideAndDontSave };
        _quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
        _quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
        _quad.colors = new[] { Color.white, Color.white, Color.white, Color.white };
        _quad.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        _quad.bounds = new Bounds(Vector3.zero, new Vector3(1000f, 1000f, 10f));

        _skyMesh = new Mesh { name = ObjPrefix + "SkyMesh", hideFlags = HideFlags.HideAndDontSave };
        _skyMesh.MarkDynamic();
        _skyV = new Vector3[(SkyRows + 1) * 2];
        _skyC = new Color[(SkyRows + 1) * 2];
        for (int i = 0; i <= SkyRows; i++) { _skyV[i * 2] = new Vector3(-1, i, 0); _skyV[i * 2 + 1] = new Vector3(1, i, 0); _skyC[i * 2] = _skyC[i * 2 + 1] = Color.clear; }
        _skyMesh.vertices = _skyV;
        Vector2[] uv = new Vector2[_skyV.Length];
        int[] tris = new int[SkyRows * 6];
        for (int i = 0; i < SkyRows; i++)
        {
            int a = i * 2, b = i * 2 + 1, c = i * 2 + 2, d = i * 2 + 3;
            tris[i * 6 + 0] = a; tris[i * 6 + 1] = c; tris[i * 6 + 2] = b;
            tris[i * 6 + 3] = c; tris[i * 6 + 4] = d; tris[i * 6 + 5] = b;
        }
        _skyMesh.uv = uv;
        _skyMesh.colors = _skyC;
        _skyMesh.triangles = tris;
        _skyMesh.bounds = new Bounds(Vector3.zero, new Vector3(1000f, 1000f, 10f));

        _haloTex = MakeRadial(128, 2.2f, "Halo");
        _coreTex = MakeRadial(64, 3.4f, "Core");

        _tintMat = MakeMat("Tint", 2 /*DstColor*/, 0 /*Zero*/, 8 /*Always*/, null);
        _skyMat = MakeMat("Sky", 5 /*SrcAlpha*/, 10 /*OneMinusSrcAlpha*/, 4 /*LEqual*/, null);
        _haloMat = MakeMat("Halo", 5, 1 /*One*/, 4, _haloTex);
        _coreMat = MakeMat("Core", 5, 1, 4, _coreTex);

        _tintGO = MakeObj("Tint", _quad, _tintMat, 50, out _tintMR);
        _skyGO = MakeObj("Sky", _skyMesh, _skyMat, skySortingOrder, out _skyMR);
        _haloGO = MakeObj("SunHalo", _quad, _haloMat, sunSortingOrder, out _haloMR);
        _coreGO = MakeObj("SunCore", _quad, _coreMat, sunSortingOrder, out _coreMR);

        CacheLights();
        _built = true;
        return true;
    }

    private void Teardown()
    {
        RestoreLights();
        ClearChildren();
        DestroyObj(_quad); DestroyObj(_skyMesh);
        DestroyObj(_tintMat); DestroyObj(_skyMat); DestroyObj(_haloMat); DestroyObj(_coreMat);
        DestroyObj(_haloTex); DestroyObj(_coreTex);
        _quad = null; _skyMesh = null; _tintMat = _skyMat = _haloMat = _coreMat = null; _haloTex = _coreTex = null;
        _tintGO = _skyGO = _haloGO = _coreGO = null;
        _built = false;
        _hasX = false;
    }

    private void ClearChildren()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform c = transform.GetChild(i);
            if (c != null && c.name.StartsWith(ObjPrefix)) DestroyObj(c.gameObject);
        }
    }

    private static void DestroyObj(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
    }

    private Material MakeMat(string n, int src, int dst, int ztest, Texture tex)
    {
        Material m = new Material(overlayShader) { name = ObjPrefix + n, hideFlags = HideFlags.HideAndDontSave };
        m.SetFloat("_SrcBlend", src);
        m.SetFloat("_DstBlend", dst);
        m.SetFloat("_ZTest", ztest);
        if (tex != null) m.SetTexture("_MainTex", tex);
        return m;
    }

    private GameObject MakeObj(string n, Mesh mesh, Material mat, int order, out MeshRenderer mr)
    {
        GameObject go = new GameObject(ObjPrefix + n);
        go.hideFlags = HideFlags.DontSaveInEditor | HideFlags.NotEditable;
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.sortingOrder = order;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = LightProbeUsage.Off;
        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return go;
    }

    // 白色徑向衰減貼圖（alpha 做衰減；power 越大中心越集中）
    private static Texture2D MakeRadial(int size, float power, string n)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.name = ObjPrefix + n; tex.hideFlags = HideFlags.HideAndDontSave; tex.wrapMode = TextureWrapMode.Clamp;
        float half = (size - 1) * 0.5f;
        Color32[] px = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - half) * (x - half) + (y - half) * (y - half)) / half;
                float a = d >= 1f ? 0f : Mathf.Pow(1f - d, power);
                px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return tex;
    }

    // ───────────────────────── 燈光快取 ─────────────────────────
    private void CacheLights()
    {
        if (_lightsCached) return;
        System.Collections.Generic.List<Light> list = new System.Collections.Generic.List<Light>();
        foreach (Light l in FindObjectsByType<Light>())
            if (l.type == LightType.Directional) list.Add(l);
        _dirLights = list.ToArray();
        _dirLightColors = new Color[_dirLights.Length];
        _dirLightIntensities = new float[_dirLights.Length];
        for (int i = 0; i < _dirLights.Length; i++) { _dirLightColors[i] = _dirLights[i].color; _dirLightIntensities[i] = _dirLights[i].intensity; }
        _origAmbient = RenderSettings.ambientLight;
        _lightsCached = true;
    }

    private void RestoreLights()
    {
        if (!_lightsCached) return;
        for (int i = 0; i < _dirLights.Length; i++)
        {
            if (_dirLights[i] == null) continue;
            _dirLights[i].color = _dirLightColors[i];
            _dirLights[i].intensity = _dirLightIntensities[i];
        }
        RenderSettings.ambientLight = _origAmbient;
        _lightsCached = false;
    }
}
