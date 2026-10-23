using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ★1016 測試用快捷鍵（只在 TesterMode 開著時才裝：編輯器、Development Build、或正式版放了測試檔）。
/// 正式版沒有測試檔就完全不存在，觀眾按不到。
///
///   F6：傳送到光球旁邊（廢墟、棉花堡、水下的光球帶路；光球在飛就等牠停）
///   F8：往右傳送到下一個點　　F7：往左傳送到上一個點
///     ・星空玻璃館：每一片玻璃地板（追逐、燭火、結局照常運作，只是不用自己跳）；
///       傳送點會避開黑影觸發區（只靠測試鍵傳送不會開追，要測追逐就自己走進觸發區）
///     ・水下：還沒撿的日誌紙（由左到右）；F9 傳送到通關巨石旁邊（★1023 巨石已消散＝傳進出口的水幕）
///     ・★1023 荒原：F9 傳送到光球左邊 4 米（還沒拿到光球時）
///     ・其他關卡：場景裡的重生點（RespawnPoint）；棉花堡另加第三個台階（照遊戲流程傳送到上層城堡）
///   F5（★1017，SampleScene）：直接到廢墟推巨石，按兩次——
///     第一次（還不在廢墟）：傳到廢墟落地的地方（P12 左邊），照正常流程翻開命運之書、光球在 P12；
///     第二次（已經在廢墟、巨石還沒放下）：光球放到 P13、她傳到光球旁（碰到就照正常流程放巨石、砸牆、滾到起點）。
///   F4（★1017，CottonCastle）：傳到上層城堡跳崖處上方，直接掉進跳崖觸發區（看墜落、落進廢墟那一段）。
///   F10（★1021，任何場景）：直接跳到「結局漫畫」——跟玻璃館吃完最後一根燭火之後一樣，進 Book 場景翻結尾漫畫，翻完自動捲名單。
///     用來測結局漫畫的翻頁、倒數圈，不用每次從頭打到玻璃館。
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

    /// <summary>★1021 直接進結局漫畫（做法跟 ShadowMonsterController.PlayEndingRoutine 最後一步相同：EndingMode＝true、載入 Book）。</summary>
    private static void JumpToEndingComic()
    {
        if (!Application.CanStreamedLevelBeLoaded("Book"))
        {
            Debug.LogWarning("【測試鍵】F10：Build Settings 裡找不到 Book 場景，沒辦法進結局漫畫");
            return;
        }
        Debug.Log("【測試鍵】F10：直接進結局漫畫");
        Time.timeScale = 1f;
        BookTransitionManager.isChapterMode = false;
        EndCredits.EndingMode = true;
        SceneManager.LoadScene("Book");
    }

    private PlayerMovement _pm;

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F10)) { JumpToEndingComic(); return; }

        bool next = Input.GetKeyDown(KeyCode.F8);
        bool prev = Input.GetKeyDown(KeyCode.F7);
        bool rock = Input.GetKeyDown(KeyCode.F9);
        bool orb = Input.GetKeyDown(KeyCode.F6);
        bool ruins = Input.GetKeyDown(KeyCode.F5);
        bool skyDive = Input.GetKeyDown(KeyCode.F4);
        if (!next && !prev && !rock && !orb && !ruins && !skyDive) return;

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

        if (ruins)
        {
            if (!sceneName.Contains("samplescene") && !sceneName.Contains("ruined")) { Debug.Log("【測試鍵】F5 只在 SampleScene（墜落＋廢墟）有用；在 CottonCastle 請用 F4 跳崖，會換場景到 SampleScene"); return; }
            // ★1017 第一次：還不在廢墟 → 傳到廢墟落地的地方（進廢墟時會把光球重置到 P12、翻開命運之書，照正常流程）
            if (p.y > -60f)
            {
                GameObject p12 = GameObject.Find("P12");
                Vector3 land = p12 != null ? p12.transform.position + new Vector3(-6f, 0f, 0f) : new Vector3(55f, -123f, 0f);
                Warp(land, "廢墟落地處（再按一次 F5 到 P13）");
                return;
            }
            // 第二次：已經在廢墟 → 光球放到 P13、她傳到光球旁（只在巨石還沒放下時）
            LeverSystem lever = FindAnyObjectByType<LeverSystem>();
            if (lever != null && lever.targetRock != null && !lever.targetRock.isKinematic) { Debug.Log("【測試鍵】巨石已經放下了，F5 不動"); return; }
            GuidanceLight g = FindAnyObjectByType<GuidanceLight>(FindObjectsInactive.Include);
            if (g == null || !g.TeleportToWaypointName("P13")) { Debug.Log("【測試鍵】找不到光球或 P13"); return; }
            Warp(g.transform.position + new Vector3(-1.2f, -0.6f, 0f), "廢墟 P13 光球旁");
            return;
        }

        if (skyDive)
        {
            // ★1017 上層城堡跳崖：傳到跳崖觸發區上方（P11 光球點上面一點），直接掉進去
            if (!sceneName.Contains("cottoncastle")) { Debug.Log("【測試鍵】F4 只在 CottonCastle（上層城堡）有用"); return; }   // ★1019 棉花堡獨立場景
            GameObject p11 = GameObject.Find("P11");
            if (p11 == null) { Debug.Log("【測試鍵】找不到 P11"); return; }
            Warp(p11.transform.position + new Vector3(2f, 3f, 0f), "上層城堡跳崖處");
            return;
        }

        if (rock)
        {
            // ★1023 荒原：F9＝傳到光球左邊 4 米（還沒拿到時），測光球那一段不用從頭走
            if (DesertOrbDirector.GateActive && !DesertOrbDirector.Collected && !float.IsNegativeInfinity(DesertOrbDirector.OrbX))
            {
                Warp(new Vector3(DesertOrbDirector.OrbX - 4f, p.y + 1f, p.z), "荒原光球左邊");
                return;
            }
            QuestClearBarrierRock r = FindAnyObjectByType<QuestClearBarrierRock>();
            if (r != null) { Warp(r.transform.position + new Vector3(-6f, 4f, 0f), "通關巨石旁"); return; }
            // ★1023 巨石已經消散：F9＝傳進出口（水下的水幕），接 M4 文字卡與轉場漫畫
            SceneTransitionZone exit = FindAnyObjectByType<SceneTransitionZone>();
            if (exit != null)
            {
                Collider ec = exit.GetComponent<Collider>();
                Vector3 c = ec != null ? ec.bounds.center : exit.transform.position;
                Warp(c, "出口（" + exit.gameObject.name + "）");
            }
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
            // 黑影觸發區：傳送點落在裡面就往右挪出去（只用測試鍵傳送不會開追；要測追逐就自己走進觸發區）
            List<Bounds> avoid = new List<Bounds>();
            foreach (Collider c in FindObjectsByType<Collider>())
                if (c != null && c.isTrigger && c.enabled && c.gameObject.name.Contains("ShadowMonsterTrigger")) avoid.Add(c.bounds);
            foreach (Collider c in FindObjectsByType<Collider>())
            {
                if (c == null || c.isTrigger || !c.enabled) continue;
                if (!c.gameObject.name.StartsWith("glass floor")) continue;
                Bounds b = c.bounds;
                float x = b.center.x;
                foreach (Bounds a in avoid)
                    if (x > a.min.x - 1.5f && x < a.max.x + 1.5f) x = Mathf.Min(b.max.x - 0.5f, a.max.x + 1.5f);
                pts.Add(new Vector3(x, b.max.y + 1.6f, 0f));
            }
        }
        else
        {
            foreach (GameObject g in GameObject.FindGameObjectsWithTag("RespawnPoint"))
                if (g != null) pts.Add(g.transform.position);
            // 棉花堡第三個台階（TeleportTrigger）：傳到台階左端上方，照遊戲本身的流程走兩步、白光傳送到上層城堡
            foreach (TeleportTrigger t in FindObjectsByType<TeleportTrigger>(FindObjectsInactive.Exclude))
            {
                if (t == null) continue;
                Collider c = t.GetComponent<Collider>();
                if (c != null) pts.Add(new Vector3(c.bounds.min.x + 0.6f, c.bounds.max.y + 1.6f, 0f));
                else pts.Add(t.transform.position + Vector3.up * 1.6f);
            }
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
