using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;
using System.Linq;

public class CreateCloudFloorTool : Editor
{
    [MenuItem("Tools/Create Cloud Floor")]
    public static void GenerateCloudFloor()
    {
        var currentScene = EditorSceneManager.GetActiveScene();
        
        var backgrounds = GameObject.FindObjectsOfType<GameObject>()
            .Where(g => g.name.Contains("SkyBackground"))
            .ToArray();

        if (backgrounds.Length == 0)
        {
            Debug.LogError("找不到名字包含 SkyBackground 的物件！");
            return;
        }

        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float groundY = -4f;

        foreach (var bg in backgrounds)
        {
            Renderer r = bg.GetComponent<Renderer>();
            if (r != null)
            {
                minX = Mathf.Min(minX, r.bounds.min.x);
                maxX = Mathf.Max(maxX, r.bounds.max.x);
                groundY = r.bounds.min.y + 1.5f; 
            }
            else
            {
                minX = Mathf.Min(minX, bg.transform.position.x - 10f);
                maxX = Mathf.Max(maxX, bg.transform.position.x + 10f);
            }
        }

        Texture2D cloudTex0 = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Sprites/clouds pic 0.png");
        Texture2D cloudTex1 = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Sprites/clouds pic 1 .png");
        
        if (cloudTex0 == null && cloudTex1 == null)
        {
            Debug.LogError("找不到雲朵的 Texture 素材！麻煩確認圖片在 Assets/Sprites/ 中。");
            return;
        }
        
        // 算出圖片與世界的比例 (預設當作 5 單位寬)
        float defaultCloudWidth = 5f; 

        GameObject cloudFloorRoot = GameObject.Find("CloudFloorRoot");
        if (cloudFloorRoot != null) 
        {
            Undo.DestroyObjectImmediate(cloudFloorRoot);
        }
        
        cloudFloorRoot = new GameObject("CloudFloorRoot");
        Undo.RegisterCreatedObjectUndo(cloudFloorRoot, "Create Cloud Floor Root");

        float currentX = minX - (defaultCloudWidth * 0.5f);
        int i = 0;
        
        Texture2D[] availableTexs = new Texture2D[] { cloudTex0, cloudTex1 }.Where(t => t != null).ToArray();

        // 為了將 2D 轉化出 3D 立體感，我們使用包含鏤空 (Cutout) 效果的 Standard Material
        Material templateMat = new Material(Shader.Find("Standard"));
        templateMat.SetFloat("_Mode", 1); // 1 = Cutout
        templateMat.SetOverrideTag("RenderType", "TransparentCutout");
        templateMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
        templateMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
        templateMat.SetInt("_ZWrite", 1);
        templateMat.EnableKeyword("_ALPHATEST_ON");
        templateMat.DisableKeyword("_ALPHABLEND_ON");
        templateMat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        templateMat.renderQueue = 2450;
        templateMat.SetFloat("_Cutoff", 0.1f); // 透明閥值

        while (currentX <= maxX + (defaultCloudWidth * 0.5f))
        {
            Texture2D selectedTex = availableTexs[Random.Range(0, availableTexs.Length)];
            
            // --- 【修復：碰撞框都在同一條線】 ---
            // 建立一個根物件 (Root) 專門處理「完美的平整碰撞」，不再有上下起伏
            GameObject cloudObj = new GameObject($"CloudPlatform_3D_{i}");
            cloudObj.transform.SetParent(cloudFloorRoot.transform);
            cloudObj.transform.position = new Vector3(currentX, groundY, 0f); 
            
            // 物理碰撞統一在此，保證玩家走在完全水平的一條直線上
            BoxCollider col = cloudObj.AddComponent<BoxCollider>();
            float sWidth = defaultCloudWidth;
            float sHeight = 2f;
            
            col.size = new Vector3(sWidth * 0.9f, sHeight * 0.6f, 10f); 
            col.center = new Vector3(0, -sHeight * 0.1f, 0);
            cloudObj.tag = "Ground"; // 保持正確的 tag

            // --- 【修復：建立 3D 視覺模型】 ---
            // 將 3D Cube 作為視覺子物件，這樣視覺上能有高低起伏與厚度，但不影響物理！
            GameObject visualCloud = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visualCloud.name = "3D_CloudMesh";
            visualCloud.transform.SetParent(cloudObj.transform);
            
            // 隨機高低起伏套用在視覺 Mesh 上即可
            float yOffset = Random.Range(-0.3f, 0.3f);
            visualCloud.transform.localPosition = new Vector3(0, yOffset, 0);
            
            // 設定 Cube 尺寸：賦予 Z 軸厚度來營造 3D 模型效果
            visualCloud.transform.localScale = new Vector3(sWidth, sHeight, 3f); // 厚度設為 3

            // 配置材質並套用 2D 貼圖
            Material cloudInstanceMat = new Material(templateMat);
            cloudInstanceMat.mainTexture = selectedTex;
            cloudInstanceMat.color = new Color(0.9f, 0.9f, 0.9f); // 稍微調亮讓雲朵潔白
            
            Renderer renderer = visualCloud.GetComponent<Renderer>();
            renderer.sharedMaterial = cloudInstanceMat;
            
            // 刪除 Cube 預設碰撞體，避免干擾我們完美的平地碰撞
            GameObject.DestroyImmediate(visualCloud.GetComponent<Collider>());

            Undo.RegisterCreatedObjectUndo(cloudObj, "Create 3D Cloud Platform");

            currentX += sWidth * 0.8f;
            i++;
        }

        EditorSceneManager.MarkSceneDirty(currentScene);
        EditorSceneManager.SaveScene(currentScene);

        Debug.Log($"成功建立 3D 雲朵模型版地板！並且所有碰撞框(BoxCollider)現在都嚴格對齊在同一條完美的水平線上！");
        Selection.activeGameObject = cloudFloorRoot;
    }
}
