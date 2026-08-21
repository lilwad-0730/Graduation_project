using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.UI;

public static class SetupMenuHoverEffect
{
    [MenuItem("Tools/Setup Menu Hover Effect")]
    public static void ExecuteMenuItem()
    {
        RevertToPreviousStep();
        string result = Execute();
        Debug.Log(result);
    }

    [MenuItem("Tools/Revert To Previous Step")]
    public static void RevertToPreviousStep()
    {
        string scenePath = "Assets/Scenes/MainMenuScene.unity";
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

        System.Text.StringBuilder sb = new System.Text.StringBuilder();

        string[] revertNames = { "game-setting", "game-credits", "game-quitting", "Btn_Settings", "Btn_Credits", "Btn_Quit" };

        foreach (string rName in revertNames)
        {
            GameObject go = GameObject.Find(rName);
            if (go != null)
            {
                Object.DestroyImmediate(go);
                sb.AppendLine($"[REVERT] 已移除物件: '{rName}'");
            }
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();

        Debug.Log($"[REVERT COMPLETED]\n{sb}");
    }

    public static string Execute()
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();

        string selPath = "Assets/UI/menu_selcted.png";
        string unselPath = "Assets/UI/menu_slection.png";

        SetTextureToSprite(selPath, sb);
        SetTextureToSprite(unselPath, sb);

        AssetDatabase.Refresh();

        Sprite spriteSelected = AssetDatabase.LoadAssetAtPath<Sprite>(selPath);
        Sprite spriteUnselected = AssetDatabase.LoadAssetAtPath<Sprite>(unselPath);

        string scenePath = "Assets/Scenes/MainMenuScene.unity";
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

        Camera mainCam = Camera.main;
        if (mainCam != null && mainCam.GetComponent<UnityEngine.EventSystems.Physics2DRaycaster>() == null)
        {
            mainCam.gameObject.AddComponent<UnityEngine.EventSystems.Physics2DRaycaster>();
        }

        GameObject canvasGo = GameObject.Find("MainMenuCanvas");
        if (canvasGo != null && canvasGo.GetComponent<GraphicRaycaster>() == null)
        {
            canvasGo.AddComponent<GraphicRaycaster>();
        }

        if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            GameObject es = new GameObject("EventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }

        GameObject targetGo = GameObject.Find("game-start");
        if (targetGo != null)
        {
            // 確保 game-start 處於激活狀態
            targetGo.SetActive(true);

            // 確保位於 MainMenuCanvas 第一層
            GameObject canvasGo2 = GameObject.Find("MainMenuCanvas");
            if (canvasGo2 != null && targetGo.transform.parent != canvasGo2.transform)
            {
                targetGo.transform.SetParent(canvasGo2.transform, false);
            }

            RectTransform rt = targetGo.GetComponent<RectTransform>();
            if (rt == null) rt = targetGo.AddComponent<RectTransform>();
            
            // 重置居中位置與尺寸
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(-280f, 0f); // 放置在左側顯眼處
            rt.sizeDelta = new Vector2(480f, 120f);
            rt.localScale = Vector3.one;

            Image img = targetGo.GetComponent<Image>();
            if (img == null) img = targetGo.AddComponent<Image>();
            img.enabled = true;
            img.color = Color.white; // 確保顏色與透明度為不透明純白
            img.sprite = spriteUnselected;
            img.raycastTarget = true;

            MenuSpriteHoverEffect hoverComp = targetGo.GetComponent<MenuSpriteHoverEffect>();
            if (hoverComp == null) hoverComp = targetGo.AddComponent<MenuSpriteHoverEffect>();
            hoverComp.defaultSprite = spriteUnselected;
            hoverComp.hoverSprite = spriteSelected;

            // 確保子物件 Text 顯現
            Transform textTr = targetGo.transform.Find("Text");
            GameObject textGo = (textTr != null) ? textTr.gameObject : null;
            if (textGo == null)
            {
                textGo = new GameObject("Text");
                textGo.transform.SetParent(targetGo.transform, false);
            }

            textGo.SetActive(true);
            RectTransform textRt = textGo.GetComponent<RectTransform>();
            if (textRt == null) textRt = textGo.AddComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;

            TMPro.TextMeshProUGUI tmp = textGo.GetComponent<TMPro.TextMeshProUGUI>();
            if (tmp == null) tmp = textGo.AddComponent<TMPro.TextMeshProUGUI>();
            tmp.enabled = true;
            if (string.IsNullOrEmpty(tmp.text)) tmp.text = "開始遊戲";
            tmp.alignment = TMPro.TextAlignmentOptions.Center;
            tmp.fontSize = 32;
            tmp.color = Color.white;

            TMPro.TMP_FontAsset fontSdf = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>("Assets/TextMesh Pro/Chinese_UI_Font_SDF.asset");
            if (fontSdf != null) tmp.font = fontSdf;

            EditorUtility.SetDirty(textGo);
            EditorUtility.SetDirty(hoverComp);
            EditorUtility.SetDirty(targetGo);

            Debug.Log($"[FIX] game-start 已重新定位於 Canvas 顯眼處 Pos=({rt.anchoredPosition.x}, {rt.anchoredPosition.y}), Size=({rt.sizeDelta.x}, {rt.sizeDelta.y}), Active={targetGo.activeInHierarchy}");
            sb.AppendLine("[SUCCESS] 已修正 game-start 物件位置、尺寸與可見度！");
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();

        return sb.ToString();
    }

    private static void SetTextureToSprite(string path, System.Text.StringBuilder sb)
    {
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null)
        {
            if (importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.SaveAndReimport();
            }
        }
    }
}
