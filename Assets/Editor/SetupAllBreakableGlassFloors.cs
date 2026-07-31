using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

// Execute full scene scan setup in Edit Mode
[InitializeOnLoad]
public class SetupAllBreakableGlassFloors
{
    private static bool executed = false;

    static SetupAllBreakableGlassFloors()
    {
        EditorApplication.update += RunOnce;
    }

    private static void RunOnce()
    {
        if (executed) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode) return; // 不在 Play Mode 執行
        executed = true;
        EditorApplication.update -= RunOnce;
        ApplyToAll();
    }

    [MenuItem("Tools/Setup All Breakable Glass Floors Now")]
    public static void ApplyToAll()
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
                    ConfigureObject(go);
                    sceneModified = true;
                    count++;
                    Debug.Log($"[SetupAllBreakableGlassFloors] 成功為場景 '{scene.name}' 物件 '{go.name}' 配置踩踏碎裂與切片特效組件！");
                }
            }

            if (sceneModified)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
        }

        Debug.Log($"[SetupAllBreakableGlassFloors] 全場景設定與存檔完成，共升級配置了 {count} 個可踩踏碎裂玻璃地磚！");
    }

    private static void ConfigureObject(GameObject go)
    {
        Destructible dest = go.GetComponent<Destructible>();
        if (dest == null) dest = Undo.AddComponent<Destructible>(go);
        dest.columns = 4;
        dest.rows = 4;
        dest.explosionForce = 4.5f;
        dest.disappearDelay = 2.5f;
        dest.shatterOnCollision = false;

        BreakableGlassFloor bgf = go.GetComponent<BreakableGlassFloor>();
        if (bgf == null) bgf = Undo.AddComponent<BreakableGlassFloor>(go);
        bgf.delayBeforeShatter = 0.2f;
        bgf.enableWarningShake = true;
        bgf.shakeIntensity = 0.05f;

        BoxCollider col = go.GetComponent<BoxCollider>();
        if (col == null)
        {
            col = Undo.AddComponent<BoxCollider>(go);
            SpriteRenderer sr = go.GetComponent<SpriteRenderer>();
            if (sr != null && sr.sprite != null)
            {
                col.size = new Vector3(sr.bounds.size.x, sr.bounds.size.y, 2f);
            }
            else
            {
                col.size = new Vector3(11.38f, 1.28f, 2f);
            }
        }

        Rigidbody rb = go.GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = Undo.AddComponent<Rigidbody>(go);
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        EditorUtility.SetDirty(dest);
        EditorUtility.SetDirty(bgf);
        EditorUtility.SetDirty(col);
        EditorUtility.SetDirty(rb);
        EditorUtility.SetDirty(go);
    }
}
