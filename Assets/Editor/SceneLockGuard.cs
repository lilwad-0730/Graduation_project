using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// 專案 Editor 智慧場景保護腳本。
/// 在進入 Play Mode 前自動記憶當前開啟的場景，
/// 退出 Play Mode 時忠實還原切回進入 Play Mode 前的該場景 (不硬性寫死特定關卡)。
/// </summary>
[InitializeOnLoad]
public class SceneLockGuard
{
    private static string previousScenePath = string.Empty;

    static SceneLockGuard()
    {
        EditorSceneManager.playModeStartScene = null;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        EditorSceneManager.playModeStartScene = null;

        if (state == PlayModeStateChange.ExitingEditMode)
        {
            var activeScene = EditorSceneManager.GetActiveScene();
            if (activeScene.IsValid() && !string.IsNullOrEmpty(activeScene.path))
            {
                previousScenePath = activeScene.path;
            }
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            EditorApplication.delayCall += RestorePreviousScene;
        }
    }

    private static void RestorePreviousScene()
    {
        EditorSceneManager.playModeStartScene = null;

        if (!string.IsNullOrEmpty(previousScenePath) && System.IO.File.Exists(previousScenePath))
        {
            var activeScene = EditorSceneManager.GetActiveScene();
            if (activeScene.path != previousScenePath)
            {
                Debug.Log($"[SceneLockGuard] 退出 Play Mode，精準還原切回 Play 前的場景: '{previousScenePath}'");
                EditorSceneManager.OpenScene(previousScenePath, OpenSceneMode.Single);
            }
        }
    }
}
