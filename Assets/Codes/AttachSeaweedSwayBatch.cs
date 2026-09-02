#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public class AttachSeaweedSwayBatch
{
    static AttachSeaweedSwayBatch()
    {
        EditorApplication.update += AutoAttach;
    }

    private static double _lastCheckTime = 0;

    private static void AutoAttach()
    {
        // 每 0.5 秒定期自動掃描場景中新產生的海草物件
        if (EditorApplication.timeSinceStartup - _lastCheckTime < 0.5) return;
        _lastCheckTime = EditorApplication.timeSinceStartup;

        var allObjects = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include);
        int addedCount = 0;

        foreach (var go in allObjects)
        {
            string n = go.name.ToLower();
            if (n.Contains("seaweed") || n.Contains("weed"))
            {
                var sway = go.GetComponent<SeaweedSway>();
                if (sway == null)
                {
                    sway = go.AddComponent<SeaweedSway>();
                    
                    // 給予獨立隨機相位與幅度，避免所有海草同步擺動
                    sway.phaseOffset = Random.Range(0f, Mathf.PI * 2f);
                    sway.maxSwayAngle = Random.Range(8f, 13f);
                    sway.cycleDuration = Random.Range(5.2f, 6.8f);

                    addedCount++;
                }
            }
        }

        if (addedCount > 0)
        {
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            Debug.Log($"[AttachSeaweedSwayBatch] 自動為 {addedCount} 個海草物件 (包含 liittle seaweed) 套用水下擺動腳本！");
        }
    }
}
#endif
