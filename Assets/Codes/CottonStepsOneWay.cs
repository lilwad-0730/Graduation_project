using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ★1024 棉花堡方塊陷阱（30 章 B3、32 章第六節，建議 3）：
///   玩家會順手把方塊推到第一階雲朵的正下方；從方塊跳第二階沒跳到、掉回地面之後，
///   她站在方塊旁邊剛好在第一階雲朵正下方，一跳就撞頭，上不去也回不去，要「按住 Shift＋A」把方塊拉出來才能重爬。
///   → 第一、二階雲朵改成「單向平台」：從下面可以直接穿上去，站在上面照常踩；方塊在哪裡都卡不住。
///   做法：換場景時找到「stair decided_0」「stair decided_0 (1)」兩個台階，掛上翊安的 OneWayPlatform3D（場景檔不動）。
///   第三階（stair decided_0 (2)）不動：踩到它會傳送到上層城堡（TeleportTrigger 用碰撞判斷），照舊。
///   關掉：CottonStepsOneWay.Enabled = false。
/// </summary>
public static class CottonStepsOneWay
{
    public static bool Enabled = true;

    static readonly string[] StepNames = { "stair decided_0", "stair decided_0 (1)" };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Apply(SceneManager.GetActiveScene());
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) { Apply(scene); }

    static void Apply(Scene scene)
    {
        if (!Enabled || !scene.IsValid()) return;
        int done = 0;
        GameObject[] roots = scene.GetRootGameObjects();
        for (int r = 0; r < roots.Length; r++)
        {
            Transform[] all = roots[r].GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Transform t = all[i];
                if (System.Array.IndexOf(StepNames, t.name) < 0) continue;
                Collider col = t.GetComponent<Collider>();
                if (col == null || col.isTrigger) continue;
                if (t.GetComponent<OneWayPlatform3D>() != null) { done++; continue; }
                t.gameObject.AddComponent<OneWayPlatform3D>();
                done++;
            }
        }
        if (done > 0) Debug.Log("【棉花堡雲朵】★1024 " + done + " 階改成單向平台（從下面可以穿上去；場景沒改）");
    }
}
