using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ★1024 玻璃館「鏡牆、黑影出場可以直接跳過」（組外試玩回饋 9）：
///   鏡牆演出的觸發區（MirrorWall_CutsceneTrigger）只有 14 公尺高（y −40～−26）、黑影出場的觸發區（ShadowMonsterTrigger）也是，
///   從高台那條路走過去就碰不到：光沒交給鏡子、鏡子不碎、黑影不出來，後面的燭火段整段沒有意義。
///   → 進玻璃館時把這兩個觸發區拉高到整個館的高度（上下死亡線之間 y −56～65），從哪一層走過都會觸發。
///   只改執行中的碰撞框（場景檔不動）；演出本身照舊。關掉：GlassHallTriggerGuard.Enabled = false。
/// </summary>
public static class GlassHallTriggerGuard
{
    public static bool Enabled = true;
    /// <summary>觸發區要蓋到的高度範圍（世界座標 y）。</summary>
    public static float BottomY = -60f, TopY = 70f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Apply();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) { Apply(); }

    static void Apply()
    {
        if (!Enabled) return;
        int n = 0;
        MirrorWallAbsorbCutscene mirror = Object.FindAnyObjectByType<MirrorWallAbsorbCutscene>(FindObjectsInactive.Include);
        if (mirror != null && Stretch(mirror.GetComponent<BoxCollider>(), mirror.transform, "鏡牆演出")) n++;
        ShadowMonsterTriggerZone shadow = Object.FindAnyObjectByType<ShadowMonsterTriggerZone>(FindObjectsInactive.Include);
        if (shadow != null && Stretch(shadow.GetComponent<BoxCollider>(), shadow.transform, "黑影出場")) n++;
        if (n > 0) Debug.Log("【玻璃館觸發區】★1024 " + n + " 個觸發區拉高到整個館（y " + BottomY + "～" + TopY + "），從高台走過也會觸發（場景沒改）");
    }

    static bool Stretch(BoxCollider box, Transform t, string label)
    {
        if (box == null || !box.isTrigger) return false;
        float sy = Mathf.Abs(t.lossyScale.y) > 0.0001f ? Mathf.Abs(t.lossyScale.y) : 1f;
        float worldBottom = t.position.y + (box.center.y - box.size.y * 0.5f) * sy;
        float worldTop = t.position.y + (box.center.y + box.size.y * 0.5f) * sy;
        if (worldBottom <= BottomY + 0.5f && worldTop >= TopY - 0.5f) return false;   // 已經夠高
        float bottom = Mathf.Min(worldBottom, BottomY), top = Mathf.Max(worldTop, TopY);
        Vector3 c = box.center, s = box.size;
        c.y = ((bottom + top) * 0.5f - t.position.y) / sy;
        s.y = (top - bottom) / sy;
        box.center = c; box.size = s;
        Debug.Log("【玻璃館觸發區】★1024 " + label + "：y " + worldBottom.ToString("F1") + "～" + worldTop.ToString("F1") + " → " + bottom.ToString("F1") + "～" + top.ToString("F1"));
        return true;
    }
}
