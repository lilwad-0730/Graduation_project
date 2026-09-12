using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

[InitializeOnLoad]
public static class UpperCastleFloorEditorTool
{
    static UpperCastleFloorEditorTool()
    {
        EditorApplication.delayCall += DelayCheckAndSetup;
        EditorSceneManager.sceneOpened += (scene, mode) => DelayCheckAndSetup();
    }

    private static void DelayCheckAndSetup()
    {
        if (Application.isPlaying) return;
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.name == "SampleScene" || GameObject.Find("PatioFloor") != null)
        {
            ApplyCollidersInEditor(false);
        }
    }

    [MenuItem("Tools/🏰 城堡地面修復/一鍵套用上層城堡與雲海碰撞框")]
    public static void ManualApplyColliders()
    {
        ApplyCollidersInEditor(true);
    }

    public static void ApplyCollidersInEditor(bool logAndSave)
    {
        var scene = EditorSceneManager.GetActiveScene();

        var controller = UpperCastleFloorController.EnsureControllerExists();
        if (controller != null)
        {
            controller.EnsureAllColliders();
        }

        if (logAndSave)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveOpenScenes();
            Debug.Log("✅【城堡地面修復】已成功套用上層城堡與雲海碰撞框！可在 [UpperCastleFloorController] Inspector 自由微調數值。");
        }
    }
}
