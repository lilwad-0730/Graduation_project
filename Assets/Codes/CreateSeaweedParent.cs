using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public class CreateSeaweedParent
{
    static CreateSeaweedParent()
    {
        EditorApplication.delayCall += CreateParentForSeaweed4;
    }

    [MenuItem("Tools/Create Parent for Little Seaweed 4")]
    public static void CreateParentForSeaweed4()
    {
        var target = GameObject.Find("little seaweed 4");
        if (target == null)
        {
            // 非海底場景不輸出警告，避免 Console 混亂
            return;
        }

        if (target.transform.parent != null && target.transform.parent.name.EndsWith("_Parent"))
        {
            return;
        }

        Vector3 worldPos = target.transform.position;

        // 建立父物件
        GameObject parentGO = new GameObject("little seaweed 4_Parent");
        parentGO.transform.position = worldPos;
        parentGO.transform.rotation = Quaternion.identity;
        parentGO.transform.localScale = Vector3.one;

        // 設定階層關係並重置本地座標
        target.transform.SetParent(parentGO.transform, true);

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
        Debug.Log($"[CreateSeaweedParent] 成功為 little seaweed 4 建立父物件 'little seaweed 4_Parent' (位置: {worldPos})！");
    }
}
