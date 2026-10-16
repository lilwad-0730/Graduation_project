using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ★1016 測試用快捷鍵（只在 TesterMode 開著時才裝：編輯器、Development Build、或正式版放了測試檔）。
/// 正式版沒有測試檔就完全不存在，觀眾按不到。
///
///   F6：傳送到光球旁邊（廢墟、棉花堡、水下的光球帶路；光球在飛就等牠停）
///   F8：往右傳送到下一個點　　F7：往左傳送到上一個點
///     ・星空玻璃館：每一片玻璃地板（追逐、燭火、結局照常運作，只是不用自己跳）
///     ・水下：還沒撿的日誌紙（由左到右）；F9 傳送到通關巨石旁邊
///     ・其他關卡：場景裡的重生點（RespawnPoint）
///   演出、重生、文字卡期間不傳送。
/// </summary>
public class TesterKeys : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Install(SceneManager.GetActiveScene());
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) { Install(scene); }

    private static void Install(Scene scene)
    {
        if (!scene.IsValid() || !TesterMode.Enabled) return;
        if (FindAnyObjectByType<TesterKeys>() != null) return;
        GameObject go = new GameObject("★1016_TesterKeys");
        SceneManager.MoveGameObjectToScene(go, scene);
        go.AddComponent<TesterKeys>();
    }

    private PlayerMovement _pm;

    private void Update()
    {
        bool next = Input.GetKeyDown(KeyCode.F8);
        bool prev = Input.GetKeyDown(KeyCode.F7);
        bool rock = Input.GetKeyDown(KeyCode.F9);
        bool orb = Input.GetKeyDown(KeyCode.F6);
        if (!next && !prev && !rock && !orb) return;

        if (_pm == null) _pm = FindAnyObjectByType<PlayerMovement>();
        if (_pm == null) return;
        if (PlayerRespawnSystem.IsAnyRespawning || _pm.IsControlLocked) { Debug.Log("【測試鍵】演出／重生中，不傳送"); return; }

        string sceneName = SceneManager.GetActiveScene().name.ToLower();
        Vector3 p = _pm.transform.position;

        if (orb)
        {
            GuidanceLight g = FindAnyObjectByType<GuidanceLight>();
            if (g == null || !g.isActiveAndEnabled) { Debug.Log("【測試鍵】這個場景沒有光球"); return; }
            if (g.IsBusy) { Debug.Log("【測試鍵】光球正在飛／被吸收，等牠停下來"); return; }
            Warp(g.transform.position + new Vector3(-1.2f, -0.6f, 0f), "光球旁");
            return;
        }

        if (rock)
        {
            QuestClearBarrierRock r = FindAnyObjectByType<QuestClearBarrierRock>();
            if (r != null) Warp(r.transform.position + new Vector3(-6f, 4f, 0f), "通關巨石旁");
            return;
        }

        List<Vector3> pts = new List<Vector3>();
        if (sceneName.Contains("underwater"))
        {
            foreach (CollectibleNote n in FindObjectsByType<CollectibleNote>())
            {
                if (n == null || n.IsCollected || !n.gameObject.activeInHierarchy) continue;
                if (n.GetComponent<SpriteRenderer>() == null) continue;   // 外層容器不算
                pts.Add(n.transform.position);   // 直接傳到紙上＝撿到
            }
        }
        else if (sceneName.Contains("dark glasses"))
        {
            foreach (Collider c in FindObjectsByType<Collider>())
            {
                if (c == null || c.isTrigger || !c.enabled) continue;
                if (!c.gameObject.name.StartsWith("glass floor")) continue;
                Bounds b = c.bounds;
                pts.Add(new Vector3(b.center.x, b.max.y + 1.6f, 0f));
            }
        }
        else
        {
            foreach (GameObject g in GameObject.FindGameObjectsWithTag("RespawnPoint"))
                if (g != null) pts.Add(g.transform.position);
        }
        if (pts.Count == 0) { Debug.Log("【測試鍵】這個場景沒有可傳送的點"); return; }
        pts.Sort((a, b) => a.x.CompareTo(b.x));

        Vector3? target = null;
        if (next)
        {
            foreach (Vector3 v in pts) if (v.x > p.x + 2f) { target = v; break; }
        }
        else
        {
            for (int i = pts.Count - 1; i >= 0; i--) if (pts[i].x < p.x - 2f) { target = pts[i]; break; }
        }
        if (target.HasValue) Warp(target.Value, next ? "下一個點" : "上一個點");
        else Debug.Log("【測試鍵】" + (next ? "右邊" : "左邊") + "沒有點了");
    }

    private void Warp(Vector3 pos, string label)
    {
        pos.z = _pm.transform.position.z;   // 深度照她原本的（水下是 −0.4）
        _pm.WarpTo(pos);
        Debug.LogWarning($"【測試鍵】★1016 傳送到{label} {pos}");
    }
}
