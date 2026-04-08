using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;

public class CreateWolfTool : Editor
{
    [MenuItem("Tools/Create Wolf In SampleScene")]
    public static void CreateWolf()
    {
        var currentScene = EditorSceneManager.GetActiveScene();
        bool isSampleScene = currentScene.name == "SampleScene";
        
        if (!isSampleScene)
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                currentScene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
            }
            else
            {
                Debug.LogWarning("操作已取消。");
                return;
            }
        }

        // 1. 建立最外層父物件 (物理及腳本持有者)
        GameObject wolf = new GameObject("WolfEnemy");
        wolf.transform.position = new Vector3(8f, 1.5f, 0f); // 稍微提高避免穿模

        // 物理與互動設定
        wolf.AddComponent<WolfEnemy>();
        BoxCollider mainCol = wolf.AddComponent<BoxCollider>();
        mainCol.size = new Vector3(2.5f, 1.8f, 1f);
        mainCol.center = new Vector3(0.1f, 0f, 0f);

        Rigidbody rb = wolf.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY | RigidbodyConstraints.FreezeRotationZ;
        }

        // 2. 建立帶有結構的 Low Poly 身體部位並自動貼圖
        AssetDatabase.ImportAsset("Assets/Textures/wolf_fur.png", ImportAssetOptions.ForceUpdate);
        Texture2D furTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/wolf_fur.png");

        Material wolfMat = new Material(Shader.Find("Standard"));
        wolfMat.color = new Color(0.8f, 0.8f, 0.8f); // 調整基底色較亮以顯示高清材質
        if (furTex != null)
        {
            wolfMat.mainTexture = furTex;
        }

        Material darkMat = new Material(Shader.Find("Standard"));
        darkMat.color = new Color(0.15f, 0.15f, 0.15f);

        // 建立可以整體上下顛簸的 BodyParent
        GameObject bodyParent = new GameObject("BodyParent");
        bodyParent.transform.SetParent(wolf.transform);
        bodyParent.transform.localPosition = Vector3.zero;

        // 實體身體
        CreateCubePart("BodyMesh", bodyParent.transform, new Vector3(0, 0, 0), new Vector3(1.6f, 0.7f, 0.6f), wolfMat);

        // 頭部 Pivot 與結構
        GameObject headPivot = new GameObject("HeadPivot");
        headPivot.transform.SetParent(bodyParent.transform);
        headPivot.transform.localPosition = new Vector3(0.8f, 0.2f, 0); // 脖子位置
        
        CreateCubePart("Head", headPivot.transform, new Vector3(0, 0.3f, 0), new Vector3(0.6f, 0.6f, 0.6f), wolfMat);
        CreateCubePart("Snout", headPivot.transform, new Vector3(0.35f, 0.15f, 0), new Vector3(0.4f, 0.3f, 0.4f), darkMat);
        CreateCubePart("EarL", headPivot.transform, new Vector3(-0.1f, 0.7f, 0.2f), new Vector3(0.15f, 0.3f, 0.2f), darkMat);
        CreateCubePart("EarR", headPivot.transform, new Vector3(-0.1f, 0.7f, -0.2f), new Vector3(0.15f, 0.3f, 0.2f), darkMat);

        // 尾巴 Pivot
        GameObject tailPivot = new GameObject("TailPivot");
        tailPivot.transform.SetParent(bodyParent.transform);
        tailPivot.transform.localPosition = new Vector3(-0.8f, 0.2f, 0); // 屁股頂端
        
        GameObject tail = CreateCubePart("Tail", tailPivot.transform, new Vector3(-0.4f, 0, 0), new Vector3(0.8f, 0.2f, 0.2f), wolfMat);

        // 腳部 Pivots (關節點設在身體四角)
        Transform legFL = CreateLeg("LegFL_Pivot", bodyParent.transform, new Vector3(0.6f, -0.2f, 0.25f), wolfMat);
        Transform legFR = CreateLeg("LegFR_Pivot", bodyParent.transform, new Vector3(0.6f, -0.2f, -0.25f), wolfMat);
        Transform legBL = CreateLeg("LegBL_Pivot", bodyParent.transform, new Vector3(-0.6f, -0.2f, 0.25f), wolfMat);
        Transform legBR = CreateLeg("LegBR_Pivot", bodyParent.transform, new Vector3(-0.6f, -0.2f, -0.25f), wolfMat);

        // 3. 掛載我們用來生成跑步動畫的程式腳本
        WolfProceduralAnimator animHelper = wolf.AddComponent<WolfProceduralAnimator>();
        animHelper.rb = rb;
        animHelper.bodyParent = bodyParent.transform;
        animHelper.headPivot = headPivot.transform;
        animHelper.tailPivot = tailPivot.transform;
        animHelper.legFL_Pivot = legFL;
        animHelper.legFR_Pivot = legFR;
        animHelper.legBL_Pivot = legBL;
        animHelper.legBR_Pivot = legBR;

        EditorSceneManager.MarkSceneDirty(currentScene);
        EditorSceneManager.SaveScene(currentScene);

        Debug.Log("生成了擁有階層關節的 Low Poly 風格狼，並已掛上程式驅動的動態跑步腳本！");
        Selection.activeGameObject = wolf;
    }

    private static GameObject CreateCubePart(string name, Transform parent, Vector3 localPos, Vector3 scale, Material mat)
    {
        GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = name;
        part.transform.SetParent(parent);
        part.transform.localPosition = localPos;
        part.transform.localScale = scale;
        GameObject.DestroyImmediate(part.GetComponent<Collider>());
        part.GetComponent<Renderer>().sharedMaterial = mat;
        return part;
    }

    private static Transform CreateLeg(string name, Transform parent, Vector3 pivotPos, Material mat)
    {
        GameObject pivot = new GameObject(name);
        pivot.transform.SetParent(parent);
        pivot.transform.localPosition = pivotPos;

        // 腳的幾何體 (向下延伸)
        GameObject legMesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
        legMesh.name = "Mesh";
        legMesh.transform.SetParent(pivot.transform);
        legMesh.transform.localPosition = new Vector3(0, -0.4f, 0); // 腳長度的一半往下移
        legMesh.transform.localScale = new Vector3(0.2f, 0.8f, 0.2f);
        GameObject.DestroyImmediate(legMesh.GetComponent<Collider>());
        legMesh.GetComponent<Renderer>().sharedMaterial = mat;

        return pivot.transform;
    }
}
