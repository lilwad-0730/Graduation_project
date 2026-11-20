using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// ★1120 廢墟（含開場天空墜落）主角立體光。
/// 廢墟場景沒有主光，只有環境光（Flat、偏暗的藍灰），3D 的主角整個被照成一樣的灰，看起來像 2D 色塊。
/// 這裡加兩盞「只照主角」的方向光，讓她有明暗面和輪廓：
///   ・主光：左上前方、暖色（配廢墟的米黃色調），照出帽兜、衣服皺褶、頭髮線條的起伏。
///   ・輪廓光：右後上方，把背、肩膀、頭髮的後緣提亮一點，讓她從米色背景裡分出來。
/// 亮度刻意很低：她的貼圖其實是米色、淺棕頭髮，原本的深灰剪影是「沒打光」造成的。主光超過約 0.1 就會整個變回米色，
/// 跟繪本裡深色帽 T、黑色線團頭髮的設定不合；0.05／0.15 保留深色，只多出明暗與輪廓。
/// 只照主角：兩盞燈的 Rendering Layer 只有「Character」（Tags & Layers 裡已經有這層，沙漠的「主角補光」也用它），
/// 主角身上的 3D 模型加上這一層；背景、雲、狼都是 2D 圖，不受影響。
/// 純程式自動掛上（場景名含 Ruined），不用改場景檔。關掉：RuinsCharacterLighting.Enabled = false。
/// 數值可以在 Play 時選「★1120 主角立體光（自動生成）」直接調，會即時套用。
/// </summary>
public class RuinsCharacterLighting : MonoBehaviour
{
    public static bool Enabled = true;

    /// <summary>Tags & Layers → Rendering Layers 的第 1 層「Character」。</summary>
    private const uint CharacterLayer = 1u << 1;

    [Header("主光（左上前方）")]
    public Color keyColor = new Color(1f, 0.92f, 0.8f);
    public float keyIntensity = 0.05f;
    [Tooltip("燈的角度：X＝由上往下照的角度，Y＝水平方向（0＝從鏡頭這邊直直照進去，正值＝從畫面左邊照過來）")]
    public Vector3 keyEuler = new Vector3(38f, 35f, 0f);

    [Header("輪廓光（右後上方）")]
    public Color rimColor = new Color(1f, 0.95f, 0.88f);
    public float rimIntensity = 0.15f;
    [Tooltip("180 左右＝從主角背後往鏡頭照；大於 180＝從畫面右後方")]
    public Vector3 rimEuler = new Vector3(22f, 205f, 0f);

    private Light _key, _rim;
    private PlayerMovement _pm;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Install(SceneManager.GetActiveScene());
    }

    private static void OnSceneLoaded(Scene s, LoadSceneMode m) { Install(s); }

    private static void Install(Scene s)
    {
        if (!Enabled || !s.IsValid() || s.name.IndexOf("ruined", System.StringComparison.OrdinalIgnoreCase) < 0) return;
        if (FindAnyObjectByType<RuinsCharacterLighting>() != null) return;
        var go = new GameObject("★1120 主角立體光（自動生成）");
        SceneManager.MoveGameObjectToScene(go, s);
        go.AddComponent<RuinsCharacterLighting>();
    }

    private void Start()
    {
        _key = MakeLight("主光", keyEuler, keyColor, keyIntensity);
        _rim = MakeLight("輪廓光", rimEuler, rimColor, rimIntensity);
    }

    private void Update()
    {
        if (_pm == null)
        {
            _pm = FindAnyObjectByType<PlayerMovement>();
            if (_pm != null) TagCharacter(_pm.transform);
        }
        Apply(_key, keyEuler, keyColor, keyIntensity);
        Apply(_rim, rimEuler, rimColor, rimIntensity);
    }

    /// <summary>主角身上的 3D 模型加上 Character 層（原本的 Default 層保留，其他燈照舊照得到）。</summary>
    private static void TagCharacter(Transform root)
    {
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (r is SpriteRenderer || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
            r.renderingLayerMask |= CharacterLayer;
        }
    }

    private Light MakeLight(string name, Vector3 euler, Color color, float intensity)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var l = go.AddComponent<Light>();
        l.type = LightType.Directional;
        l.shadows = LightShadows.None;   // 主角很小，陰影貼圖解析度不夠，會變成一塊一塊的髒斑
        l.renderMode = LightRenderMode.ForcePixel;
        var d = l.GetUniversalAdditionalLightData();
        d.renderingLayers = CharacterLayer;   // 只照 Character 層
        Apply(l, euler, color, intensity);
        return l;
    }

    private static void Apply(Light l, Vector3 euler, Color color, float intensity)
    {
        if (l == null) return;
        l.transform.rotation = Quaternion.Euler(euler);
        l.color = color;
        l.intensity = intensity;
    }
}
