using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

// Force execution in Edit Mode
[InitializeOnLoad]
public class SetupBreakableFloorInScenes
{
    private static bool executed = false;

    static SetupBreakableFloorInScenes()
    {
        EditorApplication.update += RunOnce;
    }

    private static void RunOnce()
    {
        if (executed) return;
        executed = true;
        EditorApplication.update -= RunOnce;
        SetupAll();
    }

    [MenuItem("Tools/Setup Breakable Glass Floor In All Scenes")]
    public static void SetupAll()
    {
        string[] guids = AssetDatabase.FindAssets("t:Scene");
        int count = 0;

        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var all = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            bool sceneModified = false;
            foreach (var go in all)
            {
                string name = go.name.ToLower();
                if (name.Contains("breakable") || name.Contains("glass floor_0 breakable") || name.Contains("glass_floor_breakable"))
                {
                    ConfigureBreakableFloor(go);
                    sceneModified = true;
                    count++;
                    Debug.Log($"[SetupBreakableFloorInScenes] 成功在場景 '{scene.name}' 為已有物件 '{go.name}' 配置踩踏碎裂與切片特效！");
                }
            }

            // 如果在目前場景中尚未找到名為 breakable 的物件，搜尋一個適當的 glass floor (如 glass floor_0 (8) 或懸空玻璃地磚) 設定為 glass floor_0 breakable
            if (!sceneModified && scene.name.Equals("dark glasses", System.StringComparison.OrdinalIgnoreCase))
            {
                GameObject candidate = GameObject.Find("glass floor_0 (8)");
                if (candidate == null)
                {
                    foreach (var go in all)
                    {
                        if (go.name.ToLower().Contains("glass floor_0") && !go.name.ToLower().Contains("moving"))
                        {
                            candidate = go;
                            break;
                        }
                    }
                }

                if (candidate != null)
                {
                    candidate.name = "glass floor_0 breakable";
                    Undo.RegisterCompleteObjectUndo(candidate, "Rename to glass floor_0 breakable");
                    ConfigureBreakableFloor(candidate);
                    sceneModified = true;
                    count++;
                    Debug.Log($"[SetupBreakableFloorInScenes] 成功將場景 '{scene.name}' 的 '{candidate.name}' 設置為 'glass floor_0 breakable' 並配置踩踏碎裂特效！");
                }
            }

            if (sceneModified)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
        }

        Debug.Log($"[SetupBreakableFloorInScenes] 全場景掃描與踩踏碎裂設定完成，共處理了 {count} 個可踩踏碎裂地磚！");
    }

    private static void ConfigureBreakableFloor(GameObject go)
    {
        Destructible dest = go.GetComponent<Destructible>();
        if (dest == null) dest = Undo.AddComponent<Destructible>(go);
        dest.columns = 4;
        dest.rows = 4;
        dest.explosionForce = 4.5f;
        dest.disappearDelay = 2.5f;

        BreakableGlassFloor bgf = go.GetComponent<BreakableGlassFloor>();
        if (bgf == null) bgf = Undo.AddComponent<BreakableGlassFloor>(go);
        bgf.delayBeforeShatter = 0.25f;
        bgf.enableWarningShake = true;

        Collider col = go.GetComponent<Collider>();
        if (col == null)
        {
            BoxCollider boxCol = Undo.AddComponent<BoxCollider>(go);
            SpriteRenderer sr = go.GetComponent<SpriteRenderer>();
            if (sr != null && sr.sprite != null)
            {
                boxCol.size = new Vector3(sr.bounds.size.x, sr.bounds.size.y, 1f);
            }
            else
            {
                boxCol.size = new Vector3(11.38f, 1.28f, 1f);
            }
        }

        EditorUtility.SetDirty(dest);
        EditorUtility.SetDirty(bgf);
        EditorUtility.SetDirty(go);
    }
}
