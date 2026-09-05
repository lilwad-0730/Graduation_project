using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ★0905 廢墟光絮導演（0904 定案 #3：核心動詞＝收集光絮，推石頭是手段）。
/// 進到有 LeverSystem 與 RuinsDoor 的場景（＝SampleScene 的廢墟段）自動生成，不動場景檔：
///   ・在主角身上掛 LightMoteCollector（暖光＝存量）
///   ・生成 N 顆光絮：預設 6 顆，位置依廢墟現況（狼在 43、石階 74～108、拉桿 109.5）——
///     其中兩顆放在高處，要把石階推到底下才構得到（推石頭＝取得手段）
///   ・場景裡若有名字以「LightMote_」開頭的空物件，就改用那些位置（組員想手擺就手擺，會蓋掉預設表）
///   ・把拉桿鎖起來：收滿才拉得動（收滿 → Q3-S7 路開了）
/// 整包關掉：RuinsMoteDirector.Enabled = false。Console 會印一行摘要。
/// </summary>
[DisallowMultipleComponent]
public class RuinsMoteDirector : MonoBehaviour
{
    public static bool Enabled = true;
    public static RuinsMoteDirector Instance { get; private set; }

    [System.Serializable]
    public class MoteSpot
    {
        public float x;
        [Tooltip("離地高度。>1.6 的通常要推石頭墊腳才構得到")]
        public float height = 0.5f;
        public string note;
    }

    [Header("光絮位置（場景有 LightMote_ 標記就忽略這張表）")]
    public string markerPrefix = "LightMote_";
    public List<MoteSpot> spots = new List<MoteSpot>
    {
        new MoteSpot { x = 32f,  height = 0.5f, note = "落地後第一顆：教會『它會跳開』" },
        new MoteSpot { x = 52f,  height = 0.5f, note = "第一隻狼（43）附近：一邊被追一邊撿" },
        new MoteSpot { x = 68f,  height = 3.0f, note = "高處：把石階（74）推過來才構得到" },
        new MoteSpot { x = 93f,  height = 0.5f, note = "兩座石階之間" },
        new MoteSpot { x = 103f, height = 3.2f, note = "高處：推石階（98.7／108）墊腳" },
        new MoteSpot { x = 114f, height = 0.5f, note = "拉桿旁最後一顆" },
    };

    [Header("找地面")]
    [Tooltip("往下打射線的起點 y（廢墟地面約 -128；棉花堡在上面，所以從 -95 往下打）")]
    public float rayOriginY = -95f;
    public float rayMaxDistance = 80f;
    public float groundFallbackY = -128.5f;
    [Tooltip("高處光絮上方要留的淨空（撞到天花板就往下移）")]
    public float headroom = 0.6f;

    [Header("拉桿鎖")]
    public bool gateLever = true;
    [Tooltip("要鎖的拉桿 x（找最近的一支）；0＝場景裡第一支")]
    public float gatedLeverX = 109.5f;

    private readonly List<LightMote> _motes = new List<LightMote>();

    // ─────────────────────────────────────────────────────────────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        TryInstall();
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) { TryInstall(); }

    private static void TryInstall()
    {
        if (!Enabled) return;
        if (FindFirstObjectByType<LeverSystem>() == null || FindFirstObjectByType<RuinsDoor>() == null) return;   // 只有廢墟同時有拉桿和石門
        if (FindFirstObjectByType<RuinsMoteDirector>() != null) return;
        new GameObject("RuinsMoteDirector (自動生成)").AddComponent<RuinsMoteDirector>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    private void Start() { StartCoroutine(ApplyNextFrame()); }

    private IEnumerator ApplyNextFrame()
    {
        yield return null;
        Apply();
    }

    public void Apply()
    {
        PlayerMovement pm = FindFirstObjectByType<PlayerMovement>();
        if (pm == null) { Debug.LogWarning("[RuinsMoteDirector] 找不到主角，光絮不生成。"); return; }

        LightMoteCollector collector = pm.GetComponent<LightMoteCollector>();
        if (collector == null) collector = pm.gameObject.AddComponent<LightMoteCollector>();

        // 1. 位置：標記優先
        List<Vector3> positions = new List<Vector3>();
        List<string> notes = new List<string>();
        int markers = 0;
        foreach (Transform t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (t == null || !t.name.StartsWith(markerPrefix)) continue;
            positions.Add(t.position);
            notes.Add(t.name);
            markers++;
        }
        if (markers == 0)
        {
            foreach (MoteSpot sp in spots)
            {
                if (sp == null) continue;
                float groundY = GroundYAt(sp.x);
                float h = Mathf.Max(0.3f, sp.height);
                // 天花板檢查
                RaycastHit hit;
                Vector3 from = new Vector3(sp.x, groundY + 0.3f, -1f);
                if (Physics.Raycast(from, Vector3.up, out hit, h + headroom, ~0, QueryTriggerInteraction.Ignore))
                {
                    h = Mathf.Max(0.3f, hit.distance - headroom);
                }
                positions.Add(new Vector3(sp.x, groundY + h, 0f));
                notes.Add(sp.note);
            }
        }

        // 2. 生成
        _motes.Clear();
        for (int i = 0; i < positions.Count; i++)
        {
            LightMote m = LightMote.Spawn(positions[i], collector, false);
            m.transform.SetParent(transform, true);
            _motes.Add(m);
        }
        collector.required = positions.Count;
        collector.count = 0;

        // 3. 拉桿鎖
        LeverSystem lever = null;
        if (gateLever)
        {
            LeverSystem[] levers = FindObjectsByType<LeverSystem>(FindObjectsSortMode.None);
            float best = float.MaxValue;
            foreach (LeverSystem l in levers)
            {
                if (l == null) continue;
                float d = gatedLeverX != 0f ? Mathf.Abs(l.transform.position.x - gatedLeverX) : 0f;
                if (d < best) { best = d; lever = l; }
            }
            collector.gatedLever = lever;
        }

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.Append("[RuinsMoteDirector] 光絮 ").Append(positions.Count).Append(" 顆（").Append(markers > 0 ? "依場景標記" : "依預設表").Append("）：");
        for (int i = 0; i < positions.Count; i++)
            sb.Append(" x").Append(positions[i].x.ToString("F0")).Append("/y").Append(positions[i].y.ToString("F1"));
        sb.Append("；拉桿鎖：").Append(lever != null ? lever.name + "@" + lever.transform.position.x.ToString("F1") : "無");
        Debug.Log(sb.ToString());
    }

    public float GroundYAt(float x)
    {
        RaycastHit hit;
        Vector3 origin = new Vector3(x, rayOriginY, -1f);
        if (Physics.Raycast(origin, Vector3.down, out hit, rayMaxDistance, ~0, QueryTriggerInteraction.Ignore))
            return hit.point.y;
        return groundFallbackY;
    }
}
