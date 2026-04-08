using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;

public class CreateFlatWolfTool : Editor
{
    [MenuItem("Tools/Create 2D Flat Wolf")]
    public static void CreateWolf2D()
    {
        var currentScene = EditorSceneManager.GetActiveScene();
        if (currentScene.name != "SampleScene")
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                currentScene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
            }
        }

        // 1. 建立 2D 根節點
        GameObject wolf = new GameObject("WanderingWolf2D_Flat");
        wolf.transform.position = new Vector3(0f, 1f, 0f); // 預設放中間

        // 2. 現在改用 SpriteRenderer 工作流程，不再需要建立 Unlit Material 了
        
        // 建立 Body (身體階層中心)
        GameObject bodyParent = new GameObject("BodyParent");
        bodyParent.transform.SetParent(wolf.transform);
        bodyParent.transform.localPosition = Vector3.zero;

        Create2DPart("BodyMesh", bodyParent.transform, Vector3.zero, new Vector2(1.6f, 0.7f));

        // 頭部 Pivot (2D 關節點)
        GameObject headPivot = new GameObject("HeadPivot");
        headPivot.transform.SetParent(bodyParent.transform);
        headPivot.transform.localPosition = new Vector3(0.8f, 0.2f, -0.01f); // Z稍微靠前，避免 Z-Fighting
        
        Create2DPart("HeadMesh", headPivot.transform, new Vector3(0.2f, 0.3f, 0), new Vector2(0.6f, 0.6f));
        Create2DPart("SnoutMesh", headPivot.transform, new Vector3(0.6f, 0.3f, -0.01f), new Vector2(0.3f, 0.3f));

        // 尾巴 Pivot
        GameObject tailPivot = new GameObject("TailPivot");
        tailPivot.transform.SetParent(bodyParent.transform);
        tailPivot.transform.localPosition = new Vector3(-0.8f, 0.2f, 0.01f); // Z軸稍後
        
        Create2DPart("TailMesh", tailPivot.transform, new Vector3(-0.4f, 0, 0), new Vector2(0.8f, 0.2f));

        // 建立腿部 Pivots
        Transform legFL = CreateLeg("LegFL", bodyParent.transform, new Vector3(0.6f, -0.2f, -0.02f));
        Transform legFR = CreateLeg("LegFR", bodyParent.transform, new Vector3(0.6f, -0.2f,  0.02f));
        Transform legBL = CreateLeg("LegBL", bodyParent.transform, new Vector3(-0.6f, -0.2f, -0.02f));
        Transform legBR = CreateLeg("LegBR", bodyParent.transform, new Vector3(-0.6f, -0.2f,  0.02f));

        // 3. 掛載自動移動與 2D 動畫腳本
        WanderingWolf2D patrolScript = wolf.AddComponent<WanderingWolf2D>();
        patrolScript.moveSpeed = 3f;
        patrolScript.patrolDistance = 7f;

        WolfProceduralAnimator2D animScript = wolf.AddComponent<WolfProceduralAnimator2D>();
        animScript.body = bodyParent.transform;
        animScript.headPivot = headPivot.transform;
        animScript.tailPivot = tailPivot.transform;
        animScript.legFL = legFL;
        animScript.legFR = legFR;
        animScript.legBL = legBL;
        animScript.legBR = legBR;

        EditorSceneManager.MarkSceneDirty(currentScene);
        EditorSceneManager.SaveScene(currentScene);

        Debug.Log("生成了擁有 2D 骨架與純色平面的徘徊狼！");
        Selection.activeGameObject = wolf;
    }

    private static GameObject Create2DPart(string name, Transform parent, Vector3 localPos, Vector2 scale, string spritePath = null)
    {
        // 建立純 2D 物件
        GameObject part = new GameObject(name);
        part.transform.SetParent(parent);
        part.transform.localPosition = localPos;
        part.transform.localScale = new Vector3(scale.x, scale.y, 1f);
        
        SpriteRenderer sr = part.AddComponent<SpriteRenderer>();
        // 為了讓你在切圖前能看到骨架位置，我們先給一個預設顏色表示 (或者你切圖後拖曳 Sprite 進來就會覆蓋)
        sr.color = new Color(0.8f, 0.8f, 0.8f, 0.5f); // 半透明預設框
        
        return part;
    }

    private static Transform CreateLeg(string name, Transform parent, Vector3 pivotPos)
    {
        GameObject pivot = new GameObject(name + "_Pivot");
        pivot.transform.SetParent(parent);
        pivot.transform.localPosition = pivotPos;

        // 腳的 Sprite 物件
        GameObject legSprite = new GameObject("Sprite");
        legSprite.transform.SetParent(pivot.transform);
        legSprite.transform.localPosition = new Vector3(0, -0.3f, 0); 
        legSprite.transform.localScale = new Vector3(0.5f, 0.5f, 1f);
        
        SpriteRenderer sr = legSprite.AddComponent<SpriteRenderer>();
        sr.color = new Color(0.6f, 0.6f, 0.6f, 0.5f);

        return pivot.transform;
    }
}
