using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 進場景後在背景把「之後會用到的大圖」先讀進記憶體，劇情播到時 Resources.Load 直接拿現成的，不會當場讀檔卡一下。
/// 用 Resources.LoadAsync：讀檔和上傳顯卡都在背景分段做，不會卡畫面。離開場景就放掉，不佔記憶體。
/// 要加預載的圖，在 Plan 加一行「場景名稱 → 路徑」即可。
/// </summary>
public static class ScenePreloader
{
    // 場景名稱 → 該場景之後會用到的大圖（Resources 路徑，不含副檔名）
    private static readonly Dictionary<string, string[]> Plan = new Dictionary<string, string[]>
    {
        { "Ruined", new[] { "FateBook/ruins/page", "FateBook/ruins/sketch", "RespawnStory/D_freeze/page" } },
    };

    private static readonly List<Object> _held = new List<Object>();
    private static Runner _runner;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { _held.Clear(); _runner = null; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);   // 第一個場景的 sceneLoaded 已經過了
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        string[] paths;
        if (!Plan.TryGetValue(scene.name, out paths)) return;
        if (_runner == null)
        {
            GameObject go = new GameObject("[ScenePreloader]");
            go.hideFlags = HideFlags.HideAndDontSave;
            Object.DontDestroyOnLoad(go);
            _runner = go.AddComponent<Runner>();
        }
        _runner.StartCoroutine(Warm(scene.name, paths));
    }

    private static void OnSceneUnloaded(Scene scene)
    {
        if (!Plan.ContainsKey(scene.name)) return;
        _held.Clear();   // 交給 Unity 在換場景時清掉沒人用的資源
    }

    private static IEnumerator Warm(string sceneName, string[] paths)
    {
        yield return null;   // 讓出生、鏡頭、淡入先跑完，預載排在後面
        float t0 = Time.realtimeSinceStartup;
        for (int i = 0; i < paths.Length; i++)
        {
            if (SceneManager.GetActiveScene().name != sceneName) yield break;   // 已經離開，不讀了
            ResourceRequest req = Resources.LoadAsync<Texture2D>(paths[i]);
            yield return req;
            if (req.asset != null) _held.Add(req.asset);
        }
        Debug.Log($"【預載】{sceneName} 的 {_held.Count}/{paths.Length} 張大圖預載完成，花 {Time.realtimeSinceStartup - t0:F2} 秒（背景進行）");
    }

    private sealed class Runner : MonoBehaviour { }
}
