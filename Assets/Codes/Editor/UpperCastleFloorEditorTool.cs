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
        // ★1019 只有棉花堡場景（或場景裡有 PatioFloor）才套用。SampleScene 現在是墜落＋廢墟，
        //   不能再自動長出上層城堡的地板、斜坡、斷橋死區（以前是 SampleScene＝整個遊戲，才會這樣寫）。
        if (scene.name == "CottonCastle" || GameObject.Find("PatioFloor") != null)
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

        if (!(scene.name == "CottonCastle" || GameObject.Find("PatioFloor") != null))
        {
            Debug.LogWarning("【城堡地面修復】這個場景（" + scene.name + "）不是棉花堡，沒有套用。請在 CottonCastle 場景執行。");
            return;
        }
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
