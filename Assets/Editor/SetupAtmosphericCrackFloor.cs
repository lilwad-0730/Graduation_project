using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

[InitializeOnLoad]
public class SetupAtmosphericCrackFloor
{
    private static bool executed = false;

    static SetupAtmosphericCrackFloor()
    {
        EditorApplication.update += RunOnce;
    }

    private static void RunOnce()
    {
        if (executed) return;
        executed = true;
        EditorApplication.update -= RunOnce;
        SetupTarget();
    }

    [MenuItem("Tools/Setup Atmospheric Crack Floor Now")]
    public static void SetupTarget()
    {
        var all = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        GameObject target = null;

        foreach (var go in all)
        {
            if (go.name.Equals("glass floor_0 (1)", System.StringComparison.OrdinalIgnoreCase))
            {
                target = go;
                break;
            }
        }

        if (target != null)
        {
            var dest = target.GetComponent<Destructible>();
            if (dest != null) Undo.DestroyObjectImmediate(dest);

            var bgf = target.GetComponent<BreakableGlassFloor>();
            if (bgf != null) Undo.DestroyObjectImmediate(bgf);

            var acf = target.GetComponent<AtmosphericCrackFloor>();
            if (acf == null) acf = Undo.AddComponent<AtmosphericCrackFloor>(target);

            // 預設為更加密集的裂痕視覺 (8 個撞擊中心，每個中心 14 條放射分支)
            acf.crackCenterCount = 8;
            acf.branchesPerCenter = 14;
            acf.crackStepCount = 30;

            EditorUtility.SetDirty(acf);
            EditorUtility.SetDirty(target);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();

            Debug.Log($"[SetupAtmosphericCrackFloor] 成功為 '{target.name}' 配置密集氣氛鏡面龜裂 (8 中心 / 14 分支)！");
        }
    }
}
