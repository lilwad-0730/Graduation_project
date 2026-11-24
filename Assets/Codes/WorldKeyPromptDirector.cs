using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ★1125 換場景時自動把「浮在物件上方」的操作提示裝到對應的物件上（場景檔、組員的程式都不動）：
///   ・棉花堡的方塊石頭（ShiftPushRock）　　　　Shift／X　抓住推拉
///   ・玻璃館鏡箱（MirrorCrate）　　　　　　　　Shift＋D／X＋搖桿右　推到玻璃牆邊
///   ・廢墟的巨石（RollingRockVisual）　　　　　Shift＋D／X＋搖桿右　推上坡
///   ・燭火（CandleAuraGlow，還沒點亮的）　　　　S／Y　點燃
/// 玩家真的按了對應的鍵兩次（燭火三次），同一種提示就不再出現。
/// 開關：WorldKeyPromptDirector.Enabled＝false（或 WorldKeyPrompt.Enabled＝false）＝全部不裝。
/// </summary>
public class WorldKeyPromptDirector : MonoBehaviour
{
    public static bool Enabled = true;

    private static bool GrabPressed()
    {
        return Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift) || PadInput.GrabDown;
    }
    private static bool CandlePressed()
    {
        return Input.GetKeyDown(KeyCode.S) || PadInput.CandleDown;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Install(SceneManager.GetActiveScene());
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single) return;
        Install(scene);
    }

    private static void Install(Scene scene)
    {
        if (!Enabled || !scene.IsValid()) return;
        foreach (WorldKeyPromptDirector d in FindObjectsByType<WorldKeyPromptDirector>(FindObjectsInactive.Include))
            if (d.gameObject.scene == scene) return;
        GameObject host = new GameObject("[WorldKeyPromptDirector]");
        SceneManager.MoveGameObjectToScene(host, scene);
        host.AddComponent<WorldKeyPromptDirector>().StartCoroutine(host.GetComponent<WorldKeyPromptDirector>().Run());
    }

    private readonly HashSet<Transform> _done = new HashSet<Transform>();

    /// <summary>★1125 每個互動物件各自記「學會了沒」：同一種東西（例如五根燭火）每一個都要提示一次，不是學一次就全部不出。</summary>
    private static string IdOf(string kind, Transform t)
    {
        return kind + "@" + t.gameObject.scene.name + "@" + t.name + "@" + Mathf.RoundToInt(t.position.x) + "," + Mathf.RoundToInt(t.position.y);
    }

    private IEnumerator Run()
    {
        // 物件有些是場景開始後才生出來的：等一下掃一次，之後再補掃兩次
        yield return new WaitForSecondsRealtime(1.5f);
        Scan();
        yield return new WaitForSecondsRealtime(4f);
        Scan();
        yield return new WaitForSecondsRealtime(10f);
        Scan();
    }

    private void Scan()
    {
        if (!Enabled || !WorldKeyPrompt.Enabled) return;
        int n = 0;

        foreach (ShiftPushRock r in FindObjectsByType<ShiftPushRock>(FindObjectsInactive.Exclude))
        {
            if (r == null || !_done.Add(r.transform)) continue;
            WorldKeyPrompt.Attach(IdOf("shiftRock", r.transform), r.transform,
                new[] { "key_Shift" }, new[] { "btn_X" },
                6.5f, 2.0f, 1.6f, 2, GrabPressed, null);
            n++;
        }

        foreach (MirrorCrate c in FindObjectsByType<MirrorCrate>(FindObjectsInactive.Exclude))
        {
            if (c == null || !_done.Add(c.transform)) continue;
            WorldKeyPrompt.Attach(IdOf("mirrorCrate", c.transform), c.transform,
                new[] { "key_Shift", "plus", "key_D" }, new[] { "btn_X", "plus", "stick_right" },
                8f, 2.0f, 1.9f, 2, GrabPressed, null);
            n++;
        }

        foreach (RollingRockVisual b in FindObjectsByType<RollingRockVisual>(FindObjectsInactive.Exclude))
        {
            if (b == null || !_done.Add(b.transform)) continue;
            WorldKeyPrompt.Attach(IdOf("boulder", b.transform), b.transform,
                new[] { "key_Shift", "plus", "key_D" }, new[] { "btn_X", "plus", "stick_right" },
                10f, 2.4f, 2.3f, 3, GrabPressed, null);
            n++;
        }

        foreach (CandleAuraGlow cd in FindObjectsByType<CandleAuraGlow>(FindObjectsInactive.Exclude))
        {
            if (cd == null || !_done.Add(cd.transform)) continue;
            CandleAuraGlow cc = cd;
            WorldKeyPrompt.Attach(IdOf("candle", cd.transform), cd.transform,
                new[] { "key_S" }, new[] { "btn_Y" },
                7f, 1.9f, 2.5f, 1, CandlePressed, () => cc == null || cc.IsLit);
            n++;
        }

        if (n > 0) Debug.Log("【操作提示】★1125 " + SceneManager.GetActiveScene().name + "：" + n + " 個物件上方裝了浮動按鍵提示");
    }
}
