using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// ★1115 重重落地：從天空傳送進廢墟後快速掉下來、砸到地上的那一刻，鏡頭猛震一下、腳下炸出一圈塵土。
///
///  觸發：PlayerMovement.OnHeavyRuinsLanding（只有「傳送進廢墟之後的快速墜落段」落地才會發，其他落地一律不觸發）。
///  做法：
///   ・鏡頭：寫 CameraTargetXFollower.ImpactShakeOffset（鏡頭最後一步才疊上去、平常 0），先猛地往下頓一下再衰減搖回來。
///     鏡頭有 Cinemachine 阻尼，太快的抖動會被抹平，所以用低頻（約 2.5 Hz）大一點的頓挫。
///   ・塵土：腳下一個程式產生的粒子（柔邊圓貼圖，不用美術），一次爆出、往兩側與上方散開、慢慢淡掉。
///  純視覺，不動剛體、不動時間、不動操作；enable 取消勾選＝完全沒有效果。
/// </summary>
public class RuinsHeavyLandingFX : MonoBehaviour
{
    [Header("開關")]
    public bool enableFX = true;

    [Header("鏡頭頓挫")]
    [Tooltip("最大下頓量（公尺）。鏡頭視野約 22 公尺高，1.5 約 7%，實測鏡頭阻尼會吃掉約一半")]
    public float kickMeters = 2.4f;   // ★1117 隊友反應落地力度不夠：1.5 → 2.4
    [Tooltip("衰減時間（秒）")]
    public float kickSeconds = 1.3f;   // ★1117 0.75 → 1.3（多出來的是顫抖的尾巴）
    [Tooltip("★1117 震顫的大小（占下頓量的比例）與頻率")]
    public float tremorRatio = 0.28f;
    public float tremorHz = 9f;
    [Tooltip("搖回來的頻率（赫茲）。低頻才不會被鏡頭阻尼抹平")]
    public float kickHz = 2.4f;

    [Header("塵土")]
    public bool enableDust = true;
    public int dustCount = 46;
    public Color dustColor = new Color(0.64f, 0.61f, 0.56f, 0.6f);

    private PlayerMovement _pm;
    private float _kickT = -1f;
    private float _kickAmp;
    private ParticleSystem _dust;
    private Material _mat;
    private Texture2D _tex;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        AttachAll();
    }

    private static void OnSceneLoaded(Scene s, LoadSceneMode m) { AttachAll(); }

    private static void AttachAll()
    {
        foreach (PlayerMovement pm in FindObjectsByType<PlayerMovement>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (pm != null && pm.GetComponent<RuinsHeavyLandingFX>() == null) pm.gameObject.AddComponent<RuinsHeavyLandingFX>();
    }

    private void Awake() { _pm = GetComponent<PlayerMovement>(); }
    private void OnEnable() { if (_pm != null) _pm.OnHeavyRuinsLanding += OnLand; }

    private void OnDisable()
    {
        if (_pm != null) _pm.OnHeavyRuinsLanding -= OnLand;
        CameraTargetXFollower.ImpactShakeOffset = Vector2.zero;
        if (_quietT >= 0f) { _quietT = -1f; AudioManager.ExternalDuckTarget = 1f; }   // 安靜到一半被關掉：把聲音還回去
    }

    private void OnDestroy()
    {
        CameraTargetXFollower.ImpactShakeOffset = Vector2.zero;
        if (_quietT >= 0f) { _quietT = -1f; AudioManager.ExternalDuckTarget = 1f; }
        if (_mat != null) Destroy(_mat);
        if (_tex != null) Destroy(_tex);
        if (_dust != null) Destroy(_dust.gameObject);
    }

    private void OnLand(float impactSpeed)
    {
        if (!enableFX) return;
        float k = Mathf.Clamp(impactSpeed / 14f, 0.5f, 1.4f);
        if (enableQuietMoment) StartQuietMoment();
        _kickT = 0f;
        _kickAmp = kickMeters * k * MotionComfort.Scale;   // ★1115 動態強度（減少晃動）：鏡頭頓挫跟著縮，塵土是畫面內的粒子不算晃動
        if (enableDust) EmitDust(k);
        Debug.Log($"[重落地] 落地速度 {impactSpeed:F1} 米／秒，鏡頭頓 {_kickAmp:F2} 公尺、塵土 {Mathf.RoundToInt(dustCount * k)} 顆");
    }

    // ───────── ★1115 B2 落地後的安靜時刻 ─────────
    //  砸地的聲音先聽到，過 quietDelay 秒世界忽然安靜下來（其他聲音壓到 quietLevel 倍，只剩風與腳邊的細碎聲），
    //  同時眼前一黑（曝光掉下去、慢慢亮回來，EmotionColorGradeFX.ImpactDip），停 quietHold 秒後，聲音慢慢回來。
    //  從「摔下來的緊張」到「活下來了」的釋放。聲音壓低走 AudioManager 本來就有的語音壓低機制（ExternalDuckTarget），不另開一套。
    [Header("★1115 落地後的安靜時刻")]
    public bool enableQuietMoment = true;
    [Tooltip("砸地後先讓砸地聲完整聽到，過這麼久（秒）才安靜下來")]
    public float quietDelay = 0.35f;
    [Tooltip("安靜維持多久（秒）")]
    public float quietHold = 2.2f;
    [Tooltip("安靜時其他聲音剩幾倍（0＝全靜、1＝不壓）")]
    [Range(0f, 1f)] public float quietLevel = 0.3f;
    [Tooltip("安靜結束後聲音慢慢回來花多久（秒）")]
    public float quietReleaseSeconds = 2.5f;
    [Tooltip("落地瞬間眼前一黑（曝光掉下去、慢慢亮回來）")]
    public bool quietEyeDip = true;
    private float _quietT = -1f;

    private void StartQuietMoment()
    {
        _quietT = 0f;
        if (quietEyeDip) EmotionColorGradeFX.ImpactDip = 1f;
    }

    private void UpdateQuietMoment()
    {
        if (_quietT < 0f) return;
        _quietT += Time.unscaledDeltaTime;
        if (_quietT >= quietDelay + quietHold)
        {
            AudioManager.ExternalDuckTarget = 1f;   // 放開；聲音依 ExternalReleaseSeconds 慢慢回來
            _quietT = -1f;
        }
        else if (_quietT >= quietDelay)
        {
            AudioManager.ExternalDuckTarget = quietLevel;
            AudioManager.ExternalReleaseSeconds = quietReleaseSeconds;
        }
    }

    private void LateUpdate()
    {
        UpdateQuietMoment();
        if (_kickT < 0f) return;
        _kickT += Time.unscaledDeltaTime;
        if (_kickT >= kickSeconds)
        {
            _kickT = -1f;
            CameraTargetXFollower.ImpactShakeOffset = Vector2.zero;
            return;
        }
        float u = _kickT / Mathf.Max(0.05f, kickSeconds);
        float env = Mathf.Exp(-5f * u);                                           // 衰減
        float osc = Mathf.Cos(_kickT * kickHz * 6.2832f);                          // 第一下往下頓
        // ★1117 震顫：頓挫之後疊一段中頻、慢慢衰減的顫抖（鏡頭阻尼會抹掉太快的抖動，所以用約 9 Hz、振幅大一點）
        float tEnv = Mathf.Exp(-3.2f * u) * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_kickT / 0.04f));
        float trem = _kickAmp * tremorRatio * tEnv;
        float tx = Mathf.Sin(_kickT * tremorHz * 6.2832f) * trem * 0.8f;
        float ty = Mathf.Sin(_kickT * tremorHz * 6.2832f * 1.31f + 1.3f) * trem * 0.55f;
        CameraTargetXFollower.ImpactShakeOffset = new Vector2(tx, -_kickAmp * env * osc + ty);
    }

    private void EnsureDust()
    {
        if (_dust != null) return;
        var go = new GameObject("RuinsLandingDust");
        go.hideFlags = HideFlags.DontSave;
        _dust = go.AddComponent<ParticleSystem>();
        _dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = _dust.main;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.7f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 9f);
        main.startSize = new ParticleSystem.MinMaxCurve(1.2f, 3.2f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
        main.gravityModifier = -0.05f;   // 灰塵微微上飄
        main.maxParticles = 200;
        main.startColor = dustColor;

        var em = _dust.emission;
        em.enabled = true;
        em.rateOverTime = 0f;

        var shape = _dust.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 80f;
        shape.radius = 1.4f;
        shape.rotation = new Vector3(-90f, 0f, 0f);   // 朝上、左右散開

        var lim = _dust.limitVelocityOverLifetime;
        lim.enabled = true;
        lim.dampen = 0.35f;
        lim.limit = 2.5f;

        var sz = _dust.sizeOverLifetime;
        sz.enabled = true;
        sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 1.5f)));

        var col = _dust.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0.55f, 0.55f), new GradientAlphaKey(0f, 1f) });
        col.color = g;

        var rend = _dust.GetComponent<ParticleSystemRenderer>();
        rend.renderMode = ParticleSystemRenderMode.Billboard;
        rend.sortingOrder = 450;
        rend.shadowCastingMode = ShadowCastingMode.Off;
        rend.receiveShadows = false;

        _tex = new Texture2D(32, 32, TextureFormat.RGBA32, false);
        _tex.wrapMode = TextureWrapMode.Clamp;
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                float dx = (x + 0.5f) / 32f - 0.5f, dy = (y + 0.5f) / 32f - 0.5f;
                float d = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                float a = Mathf.Clamp01(1f - d);
                _tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
            }
        _tex.Apply(false, true);

        Shader sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (sh != null)
        {
            _mat = new Material(sh) { mainTexture = _tex, hideFlags = HideFlags.HideAndDontSave };
            rend.material = _mat;
        }
    }

    private void EmitDust(float k)
    {
        EnsureDust();
        if (_dust == null) return;
        Collider col = GetComponent<Collider>();
        Vector3 p = transform.position;
        if (col != null) p = new Vector3(col.bounds.center.x, col.bounds.min.y + 0.2f, transform.position.z - 1.5f);
        _dust.transform.position = p;
        _dust.Emit(Mathf.RoundToInt(dustCount * k));
    }
}
