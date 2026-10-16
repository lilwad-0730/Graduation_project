using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ★1016 上層城堡（棉花堡第三個台階白光傳送上去之後）的重生輔助。
///
/// 實玩看到的（28 章）：
///   1. 在斷橋掉下去重生（回到上層城堡起點），光球被「離重生點最近的路徑點」拉回下層城堡的 P1
///      （下層 P1 離上層起點約 43 米，比上層的 P6 約 62 米還近）。她離光球超過 13 米，光球就停在下層等她，
///      上層從此沒有光球帶路，一直到跳崖進廢墟才又出現。
///   2. 斷橋掉下去要從上層城堡起點重走約 30 秒。
///
/// 這裡做的（都只在執行時，場景不用改）：
///   1. 重生點就是傳送點的落點（上層城堡起點）時，光球回到「第一次上來時」的位置與下一個點
///      （照 TeleportTrigger 自己的做法：落點上方 1.5 米、targetWaypointIndex）。
///   2. 斷橋前加一個存檔點：她站在斷橋那段橋面上（BridgeGap_DeathZone 左緣往左 checkpointBeforeGap 米起、
///      往右 checkpointAfterGapStart 米內，而且比觸發區頂端高 minHeightAboveGapZone 米以上）就記下來；
///      掉下去從這裡重來，光球放到她前方最近、同一層的路徑點等她。
///
/// 程式自動掛上（場景裡有 TeleportTrigger 和 GuidanceLight 才掛，名稱 ★1016_UpperCastleRespawnAssist）；
/// enableInBuild = false 整個不裝；兩件事各有開關。
/// </summary>
public class UpperCastleRespawnAssist : MonoBehaviour
{
    public static bool enableInBuild = true;

    [Header("1. 斷橋掉下去重生：光球回到第一次上來時的位置")]
    public bool restoreOrbAtTeleportDestination = true;
    [Tooltip("重生點離傳送落點幾米內算「就是傳送落點」")]
    public float destinationTolerance = 2.5f;

    [Header("2. 斷橋前的存檔點")]
    public bool addBridgeCheckpoint = true;
    public string bridgeGapName = "BridgeGap_DeathZone";
    [Tooltip("從斷橋觸發區左緣往左幾米起，算「到了斷橋前」")]
    public float checkpointBeforeGap = 6f;
    [Tooltip("從斷橋觸發區左緣往右幾米內都算（橋面還沒斷的那段）")]
    public float checkpointAfterGapStart = 8f;
    [Tooltip("要比斷橋觸發區的頂端高幾米以上，才算站在橋面上")]
    public float minHeightAboveGapZone = 5f;
    [Tooltip("找光球要等的路徑點：跟重生點的高度差在這個範圍內才算同一層")]
    public float sameLevelHeight = 15f;

    public bool logEvents = true;

    PlayerMovement _pm;
    GuidanceLight _orb;
    Collider _gap;
    bool _checkpointSet;
    Vector3 _checkpointPos;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Install(SceneManager.GetActiveScene());
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) { Install(scene); }

    static void Install(Scene scene)
    {
        if (!enableInBuild || !scene.IsValid()) return;
        if (FindAnyObjectByType<UpperCastleRespawnAssist>() != null) return;
        if (FindAnyObjectByType<TeleportTrigger>() == null || FindAnyObjectByType<GuidanceLight>() == null) return;
        GameObject go = new GameObject("★1016_UpperCastleRespawnAssist");
        SceneManager.MoveGameObjectToScene(go, scene);
        go.AddComponent<UpperCastleRespawnAssist>();
    }

    void OnEnable() { PlayerRespawnSystem.OnResettablesReset += HandleResettablesReset; }
    void OnDisable() { PlayerRespawnSystem.OnResettablesReset -= HandleResettablesReset; }

    void Start()
    {
        _orb = FindAnyObjectByType<GuidanceLight>();
        GameObject g = string.IsNullOrEmpty(bridgeGapName) ? null : GameObject.Find(bridgeGapName);
        if (g != null) _gap = g.GetComponent<Collider>();
        if (logEvents)
            Debug.Log("【上層城堡】★1016 已安裝：斷橋掉下去重生時光球回到上層；" +
                      (_gap != null ? ("斷橋前存檔點（x " + (_gap.bounds.min.x - checkpointBeforeGap).ToString("F1") + "～" + (_gap.bounds.min.x + checkpointAfterGapStart).ToString("F1") + "）") : "找不到 " + bridgeGapName + "，不加斷橋前存檔點"));
    }

    void Update()
    {
        if (!addBridgeCheckpoint || _checkpointSet || _gap == null) return;
        if (_pm == null) { _pm = FindAnyObjectByType<PlayerMovement>(); if (_pm == null) return; }
        if (PlayerRespawnSystem.IsAnyRespawning || !_pm.isGrounded || _pm.isUnderwater) return;

        Vector3 p = _pm.transform.position;
        Bounds b = _gap.bounds;
        if (p.x < b.min.x - checkpointBeforeGap || p.x > b.min.x + checkpointAfterGapStart) return;
        if (p.y < b.max.y + minHeightAboveGapZone || p.y > b.max.y + minHeightAboveGapZone + 15f) return;

        PlayerRespawnSystem rs = PlayerRespawnSystem.Instance;
        if (rs == null) return;
        _checkpointPos = p;
        rs.SetSafeGroundPosition(_checkpointPos);
        _checkpointSet = true;
        if (logEvents) Debug.Log("【上層城堡】★1016 斷橋前存檔點：" + _checkpointPos.ToString("F1") + "（掉下去從這裡重來）");
    }

    /// <summary>全場 IResettable 跑完（光球已被拉到「最近的路徑點」）之後、她還沒被傳送之前。</summary>
    void HandleResettablesReset()
    {
        if (_orb == null) _orb = FindAnyObjectByType<GuidanceLight>();
        if (_orb == null || !_orb.isActiveAndEnabled) return;
        Vector3 r = PlayerRespawnSystem.ActiveRespawnPosition;
        if (r == Vector3.zero) return;

        // 1. 重生在傳送落點（上層城堡起點）：光球回到第一次上來時的位置
        if (restoreOrbAtTeleportDestination)
        {
            foreach (TeleportTrigger t in FindObjectsByType<TeleportTrigger>(FindObjectsInactive.Exclude))
            {
                if (t == null || t.destination == null || !t.updateRespawnPoint) continue;
                if (t.guidanceLight != null && t.guidanceLight != _orb) continue;
                Vector3 dest = t.destination.position + Vector3.right * t.destinationOffsetX;
                if (Mathf.Abs(dest.x - r.x) > destinationTolerance || Mathf.Abs(dest.y - r.y) > destinationTolerance) continue;

                _orb.TeleportLight(dest + Vector3.up * 1.5f, t.targetWaypointIndex);
                if (logEvents) Debug.Log("【上層城堡】★1016 在上層城堡起點重生：光球回到第一次上來時的位置（下一個點 " + t.targetWaypointIndex + "），不再被拉回下層");
                return;
            }
        }

        // 2. 重生在斷橋前的存檔點：光球到她前方最近、同一層的路徑點等她
        if (addBridgeCheckpoint && _checkpointSet
            && Mathf.Abs(r.x - _checkpointPos.x) < destinationTolerance && Mathf.Abs(r.y - _checkpointPos.y) < 3f)
        {
            int idx = FirstWaypointAhead(r);
            if (idx >= 0)
            {
                _orb.TeleportLight(_orb.waypoints[idx].position, idx);
                if (logEvents) Debug.Log("【上層城堡】★1016 在斷橋前重生：光球到前方的 " + _orb.waypoints[idx].name + " 等她");
            }
        }
    }

    int FirstWaypointAhead(Vector3 from)
    {
        if (_orb.waypoints == null) return -1;
        int best = -1;
        float bestX = float.MaxValue;
        for (int i = 0; i < _orb.waypoints.Length; i++)
        {
            Transform w = _orb.waypoints[i];
            if (w == null) continue;
            Vector3 wp = w.position;
            if (wp.x <= from.x + 2f || Mathf.Abs(wp.y - from.y) > sameLevelHeight) continue;
            if (wp.x < bestX) { bestX = wp.x; best = i; }
        }
        return best;
    }
}
