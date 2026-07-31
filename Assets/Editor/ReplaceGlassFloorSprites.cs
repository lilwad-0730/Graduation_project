using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

// Force execution in Edit Mode
[InitializeOnLoad]
public class ReplaceGlassFloorSprites
{
    private static bool executed = false;

    static ReplaceGlassFloorSprites()
    {
        EditorApplication.update += RunOnce;
    }

    private static void RunOnce()
    {
        if (executed) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode) return; // 不在 Play Mode 執行
        executed = true;
        EditorApplication.update -= RunOnce;
        ReplaceSprites();
    }

    [MenuItem("Tools/Replace Glass Floor Sprites Now")]
    public static void ReplaceSprites()
    {
        Sprite newSprite = null;
        string[] guids = AssetDatabase.FindAssets("glass floor transparent_001 t:Sprite");
        if (guids.Length == 0) guids = AssetDatabase.FindAssets("glass_floor_transparent_001 t:Sprite");
        if (guids.Length == 0) guids = AssetDatabase.FindAssets("transparent_001 t:Sprite");
        if (guids.Length == 0) guids = AssetDatabase.FindAssets("glass floor transparent t:Sprite");

        if (guids.Length > 0)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guids[0]);
            newSprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            Debug.Log($"[ReplaceGlassFloorSprites] 找到目標 Sprite 資源: '{assetPath}'!");
        }
        else
        {
            var allSprites = AssetDatabase.FindAssets("t:Sprite");
            foreach (var g in allSprites)
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                if (p.ToLower().Contains("transparent"))
                {
                    newSprite = AssetDatabase.LoadAssetAtPath<Sprite>(p);
                    Debug.Log($"[ReplaceGlassFloorSprites] 找到包含 transparent 的 Sprite 資源: '{p}'!");
                    break;
                }
            }
        }

        if (newSprite == null)
        {
            Debug.LogError("[ReplaceGlassFloorSprites] 專案中未找到名為 'glass floor transparent_001' 的 Sprite 資源！");
            return;
        }

        string[] sceneGuids = AssetDatabase.FindAssets("t:Scene");
        int totalReplaced = 0;

        foreach (var sceneGuid in sceneGuids)
        {
            string scenePath = AssetDatabase.GUIDToAssetPath(sceneGuid);
            if (!scenePath.StartsWith("Assets/")) continue;

            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var all = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            bool sceneModified = false;
            foreach (var go in all)
            {
                if (go.name.ToLower().StartsWith("glass floor"))
                {
                    SpriteRenderer sr = go.GetComponent<SpriteRenderer>();
                    if (sr == null) sr = go.GetComponentInChildren<SpriteRenderer>();
                    if (sr != null)
                    {
                        Undo.RecordObject(sr, "Change Glass Floor Sprite");
                        sr.sprite = newSprite;
                        EditorUtility.SetDirty(sr);
                        EditorUtility.SetDirty(go);
                        sceneModified = true;
                        totalReplaced++;
                        Debug.Log($"[ReplaceGlassFloorSprites] 成功將場景 '{scene.name}' 物件 '{go.name}' 的造型替換為 '{newSprite.name}'！");
                    }
                }
            }

            if (sceneModified)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
        }

        Debug.Log($"[ReplaceGlassFloorSprites] 替換完成！共為 {totalReplaced} 個名稱開頭為 'glass floor' 的物件套用了全新 '{newSprite.name}' 造型！");
    }
}
