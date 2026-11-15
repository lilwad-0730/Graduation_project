using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ★1111 廢墟光球的存檔點快照（IPersistentState 第一個使用者）。
///
///  問題：從廢墟第 2、3 個存檔點「繼續」時，玩家人已經在第一顆光球（P12）後面，但新的一輪場景光球還停在 P12，
///        要跑回去碰它才會飛——跟「玩過的不要刷新」不符。
///  做法：存檔點快照記「光球現在要去哪個路徑點」（記名字，不記編號，場景換順序也不會錯）；繼續時把光球直接放到那個路徑點等她。
///  只做廢墟、而且最多還原到 P13（拉桿旁）：
///   ・廢墟的存檔點都在 P13 之前，光球在 P12→P13 之間不會改變任何其他機關，所以只還原光球是安全的。
///   ・P13 之後（巨石、石牆、風暴）牽涉別的機關狀態，這裡沒有存，所以不還原，免得光球跑到後面、機關卻還在前面造成卡關。
///   ・其他關卡的光球一樣先不動（那些關的機關跟光球綁得更緊，要一起存才安全）。
///  不往回：光球已經比快照更前面就不動。
///  不改修毅的 GuidanceLight：這支是進場時自己掛上去的小元件，只用 GuidanceLight 已經公開的欄位與方法。
/// </summary>
public class GuidanceLightPersistence : MonoBehaviour, IPersistentState
{
    private const string MaxWaypointName = "P13";

    [System.Serializable] private class Rec { public string wp; public int idx; }

    private GuidanceLight _light;

    public string StateKey { get { return "guidanceLight"; } }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Boot()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single) return;
        if (ProgressSave.SceneLabel(scene.name) != "廢墟") return;
        GuidanceLight gl = FindFirstObjectByType<GuidanceLight>(FindObjectsInactive.Include);
        if (gl != null && gl.GetComponent<GuidanceLightPersistence>() == null) gl.gameObject.AddComponent<GuidanceLightPersistence>();
    }

    private void Awake() { _light = GetComponent<GuidanceLight>(); }
    private void OnEnable() { PersistentState.Register(this); }
    private void OnDisable() { PersistentState.Unregister(this); }
    private void OnDestroy() { PersistentState.Unregister(this); }

    private int IndexOf(string name)
    {
        if (_light == null || _light.waypoints == null) return -1;
        for (int i = 0; i < _light.waypoints.Length; i++)
            if (_light.waypoints[i] != null && _light.waypoints[i].name == name) return i;
        return -1;
    }

    public string CaptureState()
    {
        if (_light == null || _light.waypoints == null || _light.waypoints.Length == 0) return null;
        int idx = _light.CurrentWaypointIndex;
        if (idx < 0 || idx >= _light.waypoints.Length || _light.waypoints[idx] == null) return null;
        return JsonUtility.ToJson(new Rec { wp = _light.waypoints[idx].name, idx = idx });
    }

    public void RestoreState(string data)
    {
        if (_light == null || string.IsNullOrEmpty(data)) return;
        Rec r = JsonUtility.FromJson<Rec>(data);
        if (r == null || string.IsNullOrEmpty(r.wp)) return;

        int target = IndexOf(r.wp);
        int limit = IndexOf(MaxWaypointName);
        if (target < 0 || limit < 0) return;           // 找不到就不動，免得放到奇怪的地方
        if (target > limit) target = limit;            // 最多到 P13
        if (target <= _light.CurrentWaypointIndex) return;   // 光球已經在那裡或更前面：不倒退

        if (_light.TeleportToWaypointName(_light.waypoints[target].name))
        {
            // 重生系統用「最遠走到的路徑點」避免光球退回已觸發過的點，所以一併記上（欄位是 private，用反射設；失敗不影響還原）
            try
            {
                var f = typeof(GuidanceLight).GetField("maxWaypointReached", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (f != null && (int)f.GetValue(_light) < target) f.SetValue(_light, target);
            }
            catch { }
            Debug.Log("【世界狀態】光球還原到 " + _light.waypoints[target].name + "（跟存檔點一致，不用再跑回去碰）");
        }
    }
}
