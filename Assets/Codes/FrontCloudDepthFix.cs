using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ★1117 墜落時手穿雲：廢墟墜落段的「前景雲層」(front_layer) 原本在 z=-3，
/// 而墜落動畫裡她的手／身體最靠鏡頭可以伸到 z≈-4.3（模型範圍到 -4.8），
/// 不透明的手比半透明的雲還靠近鏡頭，深度測試就讓手「穿」到雲前面。
/// 做法：載入廢墟時把 front_layer 底下每一片雲的 z 推到 -5.4（比她任何一個部位都更靠鏡頭）。
/// 鏡頭是正交的，z 改變不會讓雲變大變小；排序（sortingOrder）不變。只改位置，不碰場景檔。
/// 關掉：FrontCloudDepthFix.Enabled = false。
/// </summary>
public static class FrontCloudDepthFix
{
    public static bool Enabled = true;
    public static float TargetZ = -5.4f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        SceneManager.sceneLoaded -= OnLoaded;
        SceneManager.sceneLoaded += OnLoaded;
        Apply(SceneManager.GetActiveScene());
    }

    private static void OnLoaded(Scene s, LoadSceneMode m) { Apply(s); }

    private static void Apply(Scene s)
    {
        if (!Enabled || !s.IsValid() || !s.name.ToLowerInvariant().Contains("ruin")) return;
        foreach (GameObject root in s.GetRootGameObjects())
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != "front_layer") continue;
                foreach (Transform c in t)
                {
                    Vector3 p = c.position;
                    if (p.z > TargetZ) { p.z = TargetZ; c.position = p; }
                }
            }
        }
    }
}
