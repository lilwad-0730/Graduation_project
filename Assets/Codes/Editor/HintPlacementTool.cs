#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// ★1019 教學提示圖示（廢墟「回頭看狼」、荒原「風與石化」）的位置，不用改程式就能調。
///
/// 用法：
///   1. 開要調的場景（廢墟＝SampleScene、荒原＝desert）
///   2. 選單 Tools ▸ 提示圖示 ▸ 建立可拖動的提示物件
///      → 會在場景裡建一個物件，位置就是目前的自動位置（已經有就直接選到它）
///   3. 在 Scene 視窗拖它（或改 Inspector 的 Position）。金色的框就是圖示佔的範圍
///   4. 存檔（Ctrl+S）。之後遊戲就用你擺的位置，不會再自動生成
///   想改大小、淡入淡出距離、發光：選到那個物件，直接改 Inspector 上的欄位
/// 不想要了：把那個物件刪掉，回到自動位置。
/// </summary>
public static class HintPlacementTool
{
    [MenuItem("Tools/提示圖示/建立可拖動的提示物件")]
    public static void CreateForThisScene()
    {
        string scene = EditorSceneManager.GetActiveScene().name;
        int made = 0;

        RuinsLookBackHint ruins = Object.FindAnyObjectByType<RuinsLookBackHint>(FindObjectsInactive.Include);
        if (ruins != null) { Select(ruins.gameObject, "廢墟提示"); made++; }
        else
        {
            Vector3 pos;
            if (RuinsLookBackHint.TryGetAutoPosition(out pos))
            {
                GameObject go = new GameObject("RuinsLookBackHint");
                go.transform.position = pos;
                go.AddComponent<RuinsLookBackHint>();
                Created(go, "廢墟提示（回頭看狼）");
                made++;
            }
        }

        DesertGuideHint desert = Object.FindAnyObjectByType<DesertGuideHint>(FindObjectsInactive.Include);
        if (desert != null) { Select(desert.gameObject, "荒原提示"); made++; }
        else
        {
            Vector3 pos;
            if (DesertGuideHint.TryGetAutoPosition(out pos))
            {
                GameObject go = new GameObject("DesertGuideHint");
                go.transform.position = pos;
                go.AddComponent<DesertGuideHint>();
                Created(go, "荒原提示（風與石化）");
                made++;
            }
        }

        // ★1021 這關沒有「廢墟回頭看狼／荒原風與石化」這兩張 → 改成處理企劃的線條畫提示（水下、玻璃館、棉花堡…）
        if (made == 0)
            LineHintAnchorTool.CreateAnchors();
    }

    private static void Select(GameObject go, string label)
    {
        Selection.activeGameObject = go;
        EditorGUIUtility.PingObject(go);
        Debug.Log("【提示圖示】" + label + " 已經有物件了，直接選到它：拖它就能改位置，存檔記住。");
    }

    private static void Created(GameObject go, string label)
    {
        Undo.RegisterCreatedObjectUndo(go, "建立提示圖示");
        EditorSceneManager.MarkSceneDirty(go.scene);
        Selection.activeGameObject = go;
        EditorGUIUtility.PingObject(go);
        SceneView.lastActiveSceneView?.FrameSelected();
        Debug.Log("【提示圖示】已建立 " + label + "，位置 " + go.transform.position + "。在 Scene 視窗拖它、改完存檔（Ctrl+S）。");
    }
}
#endif
