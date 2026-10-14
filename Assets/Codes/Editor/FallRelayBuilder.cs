#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// ★1015 墜落途中接力傳送 一鍵建立工具。
///
/// 選單：Tools ▸ 墜落轉場 ▸ 建立第二段傳送（天空底→廢墟頂）
///   在目前開著的場景建立兩個物件：
///     ・FallRelay_Trigger          墜落途中碰到它，畫面白霧一下、瞬間把玩家搬到下面那個點，之後繼續用同一條速度曲線掉到地面
///     ・FallRelay_RuinsDropPoint    傳送落點（放在廢墟畫面的上方，玩家出現的位置）
///   預設位置只是「大概在現有墜落通道底部」，請在 Scene 視窗把它們拖到你分開背景之後的正確位置：
///     ・觸發區：蓋在天空那段畫面快要結束的地方（要比玩家墜落的 X 範圍寬）
///     ・落點：廢墟背景的上方，離地面至少 25 米，這樣傳過去之後還有一段「掉落」才著地。
///   重複按不會建兩份，會選到已經有的那一組。
/// </summary>
public static class FallRelayBuilder
{
    const string TriggerName = "FallRelay_Trigger";
    const string DropName = "FallRelay_RuinsDropPoint";

    [MenuItem("Tools/墜落轉場/建立第二段傳送（天空底→廢墟頂）")]
    public static void Build()
    {
        GameObject trig = GameObject.Find(TriggerName);
        GameObject drop = GameObject.Find(DropName);
        if (trig != null && drop != null)
        {
            Selection.activeGameObject = trig;
            EditorUtility.DisplayDialog("已經有了", "場景裡已經有 " + TriggerName + " 和 " + DropName + "，我選到觸發區了。\n直接在 Scene 視窗拖它們的位置就好。", "好");
            return;
        }

        // 找現有的第一段（棉花堡跳崖）傳送，拿它的白霧設定與落點 X 當預設
        SkyDiveTeleportTrigger first = null;
        foreach (SkyDiveTeleportTrigger t in Object.FindObjectsByType<SkyDiveTeleportTrigger>(FindObjectsSortMode.None))
            if (t != null && !t.keepControlledFall) { first = t; break; }

        float x = 51.7f;
        if (first != null && first.targetDropPoint != null) x = first.targetDropPoint.position.x;

        // 現有墜落通道的最低處：所有 FallingBackground 物件的最低邊緣
        float bottom = float.NaN;
        GameObject[] falls = null;
        try { falls = GameObject.FindGameObjectsWithTag("FallingBackground"); } catch { }
        if (falls != null)
        {
            foreach (GameObject g in falls)
            {
                Renderer r = g.GetComponentInChildren<Renderer>();
                float y = r != null ? r.bounds.min.y : g.transform.position.y;
                if (float.IsNaN(bottom) || y < bottom) bottom = y;
            }
        }
        if (float.IsNaN(bottom)) bottom = (first != null && first.targetDropPoint != null) ? first.targetDropPoint.position.y - 60f : -100f;

        if (trig == null)
        {
            trig = new GameObject(TriggerName);
            Undo.RegisterCreatedObjectUndo(trig, "建立墜落接力觸發區");
            trig.transform.position = new Vector3(x, bottom + 8f, 0f);
            BoxCollider box = trig.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(60f, 3f, 30f);
            SkyDiveTeleportTrigger sd = trig.AddComponent<SkyDiveTeleportTrigger>();
            sd.keepControlledFall = true;
            sd.transitionColor = first != null ? first.transitionColor : new Color(0.93f, 0.94f, 0.97f, 1f);
            sd.fadeOutDuration = 0.3f;     // 比第一段（0.2）慢一點點，快到底才不會突然
            sd.holdDuration = 0.12f;
            sd.fadeInDuration = 0.55f;
            if (first != null) { sd.transitionSFX = first.transitionSFX; sd.sfxVolume = first.sfxVolume; }
        }
        if (drop == null)
        {
            drop = new GameObject(DropName);
            Undo.RegisterCreatedObjectUndo(drop, "建立墜落接力落點");
            drop.transform.position = new Vector3(x, bottom, 0f);
        }
        trig.GetComponent<SkyDiveTeleportTrigger>().targetDropPoint = drop.transform;

        EditorSceneManager.MarkSceneDirty(trig.scene);
        Selection.activeGameObject = trig;
        EditorGUIUtility.PingObject(trig);
        Debug.Log("[墜落轉場] 已建立 " + TriggerName + "（" + trig.transform.position + "）和 " + DropName + "（" + drop.transform.position + "）。\n"
                  + "請在 Scene 視窗把觸發區拖到天空那段畫面快結束的地方、把落點拖到廢墟畫面的上方（離地面至少 25 米），然後存檔。");
        EditorUtility.DisplayDialog("建好了", "已建立：\n・" + TriggerName + "（墜落碰到它就白霧＋傳送）\n・" + DropName + "（傳送落點）\n\n接下來：在 Scene 視窗把它們拖到正確位置，再存檔。\n（預設位置只是大概在現有墜落通道的底部）", "好");
    }

    [MenuItem("Tools/墜落轉場/移除第二段傳送")]
    public static void Remove()
    {
        int n = 0;
        foreach (string name in new[] { TriggerName, DropName })
        {
            GameObject g = GameObject.Find(name);
            if (g != null) { Undo.DestroyObjectImmediate(g); n++; }
        }
        if (n > 0) EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("[墜落轉場] 移除了 " + n + " 個物件（記得存檔）。");
    }
}
#endif
