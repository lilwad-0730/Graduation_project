using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// ★1114 墜落風線：往上飛的細長白線，讓人感覺「在往下掉、很快」。
///
///  為什麼要有：墜落鏡頭拉很遠，主角在畫面上很小，只靠主角晃動看不出速度；背景又是一整張雲，
///            沒有東西相對移動，所以眼睛感覺不到在掉。風線就是補上「相對速度」。
///  做法：一個掛在主鏡頭底下的粒子系統（跟著鏡頭，不是世界座標），粒子在畫面下方生出、快速往上飛、拉長成線。
///        不用任何美術圖：貼圖是程式即時產生的 4×16 白色漸層。
///  跟別的機制的關係：
///   ・只讀 PlayerMovement.IsInControlledFall／FallLandingK，不寫任何東西；沒在墜落＝不發射、粒子自然飛完消失。
///   ・落地前依 FallLandingK 把發射量、速度收掉，跟畫面放慢同步。
///   ・粒子系統掛在鏡頭下：鏡頭被瞬移（墜落接力傳送）時跟著走，不會留在舊位置。
///   ・enable 取消勾選＝完全沒有效果。
/// </summary>
public class FallWindStreaksFX : MonoBehaviour
{
    [Header("開關")]
    public bool enableStreaks = true;
    [Range(0f, 3f)] public float intensity = 1f;
    [Header("外觀")]
    [Range(0f, 1f)] public float maxAlpha = 0.09f;   // 很淡：只是讓人隱約感覺在掉，不能搶畫面
    public float baseSpeed = 26f;          // 往上飛的速度（相對鏡頭，單位：公尺／秒，鏡頭視野 20～36 公尺高）
    public float lengthScale = 3f;         // 拉長倍率
    public float widthMeters = 0.09f;      // 線寬
    public float ratePerSecond = 9f;       // 每秒生幾條線
    public float fadeInSeconds = 1.2f;

    private PlayerMovement _pm;
    private ParticleSystem _ps;
    private ParticleSystem.EmissionModule _em;
    private ParticleSystem.MainModule _main;
    private ParticleSystem.ShapeModule _shape;
    private Camera _cam;
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
            if (pm != null && pm.GetComponent<FallWindStreaksFX>() == null) pm.gameObject.AddComponent<FallWindStreaksFX>();
    }

    private void Awake() { _pm = GetComponent<PlayerMovement>(); }

    private void OnDestroy()
    {
        if (_mat != null) Destroy(_mat);
        if (_tex != null) Destroy(_tex);
        if (_ps != null) Destroy(_ps.gameObject);
    }

    private bool EnsureSystem()
    {
        if (_ps != null && _cam != null) return true;
        _cam = Camera.main;
        if (_cam == null) return false;

        if (_ps != null) { Destroy(_ps.gameObject); _ps = null; }

        var go = new GameObject("FallWindStreaks");
        go.transform.SetParent(_cam.transform, false);
        go.transform.localPosition = new Vector3(0f, 0f, 5f);   // 鏡頭前方，正交鏡頭下深度不影響大小
        go.transform.localRotation = Quaternion.identity;

        _ps = go.AddComponent<ParticleSystem>();
        _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        _main = _ps.main;
        _main.loop = true;
        _main.playOnAwake = false;
        _main.simulationSpace = ParticleSystemSimulationSpace.Local;   // 跟著鏡頭走
        _main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.6f);
        _main.startSpeed = 0f;   // 速度由 velocityOverLifetime 給（往鏡頭「上」方向）
        _main.startSize = widthMeters;
        _main.maxParticles = 200;
        _main.startColor = new Color(1f, 1f, 1f, maxAlpha);

        _em = _ps.emission;
        _em.enabled = true;
        _em.rateOverTime = 0f;

        _shape = _ps.shape;
        _shape.enabled = true;
        _shape.shapeType = ParticleSystemShapeType.Box;
        _shape.position = new Vector3(0f, -12f, 0f);
        _shape.scale = new Vector3(30f, 0.1f, 0.1f);

        var vel = _ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.Local;
        vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);   // 三軸必須同一種模式（兩個常數），否則 Unity 報錯
        vel.y = new ParticleSystem.MinMaxCurve(baseSpeed * 0.8f, baseSpeed * 1.2f);
        vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

        var col = _ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
        col.color = g;

        var rend = _ps.GetComponent<ParticleSystemRenderer>();
        rend.renderMode = ParticleSystemRenderMode.Stretch;
        rend.lengthScale = lengthScale * 6f;
        rend.velocityScale = 0.05f;
        rend.cameraVelocityScale = 0f;
        rend.sortingOrder = 500;
        rend.shadowCastingMode = ShadowCastingMode.Off;
        rend.receiveShadows = false;

        _tex = new Texture2D(4, 16, TextureFormat.RGBA32, false);
        _tex.wrapMode = TextureWrapMode.Clamp;
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 4; x++)
            {
                float edge = 1f - Mathf.Abs((x + 0.5f) / 4f - 0.5f) * 2f;          // 左右邊淡出
                float along = Mathf.Sin((y + 0.5f) / 16f * Mathf.PI);              // 頭尾淡出
                _tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(edge * along)));
            }
        _tex.Apply(false, true);

        Shader sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (sh == null) return false;
        _mat = new Material(sh) { mainTexture = _tex, hideFlags = HideFlags.HideAndDontSave };
        rend.material = _mat;

        _ps.Play();
        return true;
    }

    private void LateUpdate()
    {
        bool falling = enableStreaks && intensity > 0f && _pm != null && _pm.IsInControlledFall;
        if (!falling)
        {
            if (_ps != null) { _em = _ps.emission; _em.rateOverTime = 0f; }   // 不再發射，已經在飛的自己飛完
            return;
        }
        if (!EnsureSystem()) return;

        float t = _pm.ControlledFallSeconds;
        float fade = fadeInSeconds <= 0.01f ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / fadeInSeconds));
        float settle = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_pm.FallLandingK / 0.6f));   // 接近地面→0
        float amp = intensity * fade * settle;

        // 依鏡頭視野調整：線從畫面底下外面生出，寬度＝畫面寬
        float halfH = _cam.orthographic ? _cam.orthographicSize : 15f;
        float halfW = halfH * Mathf.Max(1f, _cam.aspect);
        _shape.position = new Vector3(0f, -(halfH + 1f), 0f);
        _shape.scale = new Vector3(halfW * 2f, 0.1f, 0.1f);

        var vel = _ps.velocityOverLifetime;
        float spd = baseSpeed * (0.35f + 0.65f * settle) * (halfH / 15f);   // 視野越大飛越快，畫面上看起來速度才一致
        vel.y = new ParticleSystem.MinMaxCurve(spd * 0.8f, spd * 1.2f);

        _main.startColor = new Color(1f, 1f, 1f, maxAlpha * Mathf.Clamp01(amp));
        _main.startSize = widthMeters * (halfH / 15f);

        _em = _ps.emission;
        _em.rateOverTime = ratePerSecond * amp;
    }
}
