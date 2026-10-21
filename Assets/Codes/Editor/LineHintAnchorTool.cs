#if UNITY_EDITOR
using System.Globalization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// ★1021 把企劃的線條畫提示（LineHintSign）變成可以在 Scene 視窗拖動調位置的標記。
/// 完整用法寫在 LineHintAnchor.cs 開頭；簡單說：先 Play 一次進關卡 → 停掉 → 選單 Tools ▸ 提示圖示 ▸ 建立線條畫提示的可拖動標記 → 拖 → 存檔。
/// 標記存在場景檔裡，commit 後組員 pull 下來就一樣（這支工具本身也會跟著 git 給組員用）。
/// </summary>
public static class LineHintAnchorTool
{
    [MenuItem("Tools/提示圖示/建立線條畫提示的可拖動標記")]
    public static void CreateAnchors()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("提示圖示", "請先停掉 Play 再按這個選單（標記要建在編輯中的場景，Play 中建的會在停止時消失）。", "好");
            return;
        }
        string key = "LineHintRec_" + scene.name;
        string rec = EditorPrefs.GetString(key, "");
        if (string.IsNullOrEmpty(rec))
        {
            EditorUtility.DisplayDialog("提示圖示",
                "還沒有「" + scene.name + "」這關的提示位置紀錄。\n\n請先按 Play 進到這個關卡玩一下（提示一出現，位置就會自動記下），停掉之後再按一次這個選單。", "好");
            return;
        }

        int made = 0, existed = 0;
        GameObject last = null;
        foreach (string line in rec.Split('\n'))
        {
            if (string.IsNullOrEmpty(line)) continue;
            string[] f = line.Split('|');
            if (f.Length < 6) continue;
            string name = f[0], res = f[1];
            float x = P(f[2]), y = P(f[3]), z = P(f[4]);

            LineHintAnchor found = null;
            foreach (LineHintAnchor a in Object.FindObjectsByType<LineHintAnchor>(FindObjectsInactive.Include))
                if (a.signName == name) { found = a; break; }
            if (found != null) { existed++; last = found.gameObject; continue; }

            GameObject go = new GameObject("LineHintAnchor_" + name.Replace("LineHint_", ""));
            go.transform.position = new Vector3(x, y, z);
            LineHintAnchor anchor = go.AddComponent<LineHintAnchor>();
            anchor.signName = name;
            anchor.resourcePath = res;
            anchor.worldWidth = 0f;      // 0＝照程式算的大小；想改再填
            Undo.RegisterCreatedObjectUndo(go, "建立線條畫提示標記");
            made++;
            last = go;
        }

        if (made > 0) EditorSceneManager.MarkSceneDirty(scene);
        if (last != null)
        {
            Selection.activeGameObject = last;
            EditorGUIUtility.PingObject(last);
            SceneView.lastActiveSceneView?.FrameSelected();
        }
        Debug.Log("【提示圖示】" + scene.name + "：新建 " + made + " 個標記、原本就有 " + existed + " 個。在 Scene 視窗拖金色框，改完存檔（Ctrl+S）。");
        EditorUtility.DisplayDialog("提示圖示",
            "新建 " + made + " 個標記（原本就有 " + existed + " 個）。\n\n在 Scene 視窗拖金色框調位置，改完記得存檔（Ctrl+S）。\n標記刪掉＝回到程式算的位置。", "好");
    }

    [MenuItem("Tools/提示圖示/清掉這關的位置紀錄（重新記）")]
    public static void ClearRecord()
    {
        string key = "LineHintRec_" + EditorSceneManager.GetActiveScene().name;
        EditorPrefs.DeleteKey(key);
        Debug.Log("【提示圖示】已清掉 " + key + "；下次 Play 進這關會重新記。");
    }

    private static float P(string s)
    {
        float v;
        return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0f;
    }
}
#endif
