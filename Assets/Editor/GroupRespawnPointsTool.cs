using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Linq;
using System.Collections.Generic;

[InitializeOnLoad]
public static class GroupRespawnPointsTool
{
    private const string AutoRunSessionKey = "GroupRespawnPoints_AutoRunDone";

    static GroupRespawnPointsTool()
    {
        EditorApplication.delayCall += () =>
        {
            if (!SessionState.GetBool(AutoRunSessionKey, false))
            {
                SessionState.SetBool(AutoRunSessionKey, true);
                GroupRespawnPointsInActiveScene(isManual: false);
            }
        };
    }

    [MenuItem("Tools/📦 一鍵合併重生點群組 (Group RespawnPoints)")]
    public static void ExecuteMenu()
    {
        GroupRespawnPointsInActiveScene(isManual: true);
    }

    public static void GroupRespawnPointsInActiveScene(bool isManual)
    {
        var activeScene = EditorSceneManager.GetActiveScene();
        if (!activeScene.isLoaded) return;

        // 搜尋當前場景中所有掛有 RespawnPoint Tag 的物件 (包含未激活)
        var allGameObjects = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var respawnPoints = allGameObjects
            .Where(go => go != null && go.scene == activeScene && go.CompareTag("RespawnPoint"))
            .ToList();

        if (respawnPoints.Count == 0)
        {
            if (isManual)
            {
                EditorUtility.DisplayDialog("提示", $"在當前場景 [{activeScene.name}] 中未找到任何 Tag 為 'RespawnPoint' 的物件。", "確定");
            }
            return;
        }

        // 檢查場景中是否已有名為 "RespawnPoints" 的根物件
        GameObject groupObj = null;
        var rootObjects = activeScene.GetRootGameObjects();
        foreach (var root in rootObjects)
        {
            if (root.name == "RespawnPoints" || root.name == "RespawnPoint_Group")
            {
                groupObj = root;
                break;
            }
        }

        bool createdNewGroup = false;
        if (groupObj == null)
        {
            groupObj = new GameObject("RespawnPoints");
            groupObj.transform.position = Vector3.zero;
            groupObj.transform.rotation = Quaternion.identity;
            groupObj.transform.localScale = Vector3.one;
            Undo.RegisterCreatedObjectUndo(groupObj, "Create RespawnPoints Group");
            createdNewGroup = true;
        }

        // 如果不是手動觸發，且所有重生點已經都在群組底下，就不重複處理
        if (!isManual && !createdNewGroup && respawnPoints.All(rp => rp.transform.parent == groupObj.transform))
        {
            return;
        }

        Undo.RegisterFullObjectHierarchyUndo(groupObj, "Group Respawn Points");

        // 依 X 座標由左至右排序
        var sortedPoints = respawnPoints.OrderBy(rp => rp.transform.position.x).ToList();

        foreach (var point in sortedPoints)
        {
            if (point == groupObj) continue;

            if (point.transform.parent != groupObj.transform)
            {
                Undo.SetTransformParent(point.transform, groupObj.transform, "Set Parent to RespawnPoints");
            }
        }

        // 重新設定在群組內的順序（按 X 座標排列）
        for (int i = 0; i < sortedPoints.Count; i++)
        {
            sortedPoints[i].transform.SetSiblingIndex(i);
        }

        EditorSceneManager.MarkSceneDirty(activeScene);
        EditorSceneManager.SaveScene(activeScene);
        Selection.activeGameObject = groupObj;

        Debug.Log($"<color=#5dade2><b>[RespawnPoints]</b></color> 成功在場景 [{activeScene.name}] 中將 {sortedPoints.Count} 個重生點整理至 'RespawnPoints' 群組並存檔！");

        if (isManual)
        {
            EditorUtility.DisplayDialog("完成", $"已成功將場景中 {sortedPoints.Count} 個重生點合併至 [RespawnPoints] 群組，並依序由左至右排列！", "太棒了");
        }
    }
}
