using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ★1010 廢墟地板接縫卡人：玩家走到 x≈65.38 就走不動（跳起來才過得去）。
///
/// 實機 log（Editor.log）：Position (65.38, -127.35)、isGrounded True、沒有任何鎖定、指令速度 6.00，
/// 但 x 一直停在 65.38，鏡頭 PlayerX 連續好幾百幀都是 65.38；跳起來（y 抬到 -123.66）就過去了。
/// 位置剛好對得上地板的接縫：
///   Ruin_Ground_Loop (6)  x 27.01～66.83
///   Ruin_Ground_Loop      x 66.75～106.57   （只重疊 0.08 米，頂面同高 y -129.06）
///   玩家碰撞盒右緣 = 65.38 + 0.3（偏移）+ 1.065（半寬）≈ 66.75 → 剛好貼到第二塊地板的左側面。
/// 兩塊盒子的頂面共面，盒狀碰撞體的底前緣會被「接縫內部邊」卡住（ghost collision）。
/// 狼在 (2)/(4) 接縫、坡腳接頭也是同一個原因（見 WolfEnemy.UpdateStuckRecovery 的註解）。
///
/// 做法：不改場景。進場景時，把名字開頭是 Ruin_Ground_Loop、頂面和底面一樣高、左右相連或重疊的地板碰撞盒，
///       合併成一個連續的碰撞盒（接縫就不存在了），原本的碰撞盒關掉（貼圖不動）。
///       合併後頂面高度不變（頂面差在 mergeTopTolerance 內時取最高那塊，最多差幾毫米）。
/// 關掉：RuinsGroundSeamMerge.Enabled = false（重新載入場景後就是原本的樣子）。
/// </summary>
public static class RuinsGroundSeamMerge
{
    public static bool Enabled = true;

    /// <summary>要合併的地板：物件名字開頭。</summary>
    public static string namePrefix = "Ruin_Ground_Loop";

    /// <summary>兩塊地板左右最多隔幾米還算相連。</summary>
    public static float maxGapX = 0.3f;

    /// <summary>頂面、底面高度差在這個範圍內（米）才合併。</summary>
    public static float mergeTopTolerance = 0.05f;

    public static bool logEvents = true;

    const string MergedPrefix = "[RuinsGroundMerged_";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Apply();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Apply();
    }

    public static void Apply()
    {
        if (!Enabled) return;

        var floors = new List<BoxCollider>();
        foreach (BoxCollider box in Object.FindObjectsByType<BoxCollider>(FindObjectsInactive.Exclude))
        {
            if (box == null || !box.enabled || box.isTrigger) continue;
            if (!box.gameObject.name.StartsWith(namePrefix)) continue;
            if (IsRotated(box.transform)) continue;
            floors.Add(box);
        }
        if (floors.Count < 2) return;

        floors.Sort((a, b) => a.bounds.min.x.CompareTo(b.bounds.min.x));

        int groupIndex = 0;
        var group = new List<BoxCollider> { floors[0] };
        Bounds merged = floors[0].bounds;
        for (int i = 1; i <= floors.Count; i++)
        {
            bool joins = false;
            if (i < floors.Count)
            {
                Bounds b = floors[i].bounds;
                joins = b.min.x <= merged.max.x + maxGapX
                        && Mathf.Abs(b.max.y - merged.max.y) <= mergeTopTolerance
                        && Mathf.Abs(b.min.y - merged.min.y) <= mergeTopTolerance
                        && floors[i].gameObject.CompareTag(group[0].gameObject.tag);
            }

            if (joins)
            {
                group.Add(floors[i]);
                merged.Encapsulate(floors[i].bounds);
                continue;
            }

            if (group.Count >= 2) Merge(group, merged, ++groupIndex);

            if (i < floors.Count)
            {
                group = new List<BoxCollider> { floors[i] };
                merged = floors[i].bounds;
            }
        }
    }

    static void Merge(List<BoxCollider> group, Bounds bounds, int index)
    {
        GameObject first = group[0].gameObject;
        GameObject go = new GameObject(MergedPrefix + index + "]");
        go.layer = first.layer;
        try { go.tag = first.tag; } catch { /* 標籤不存在就維持 Untagged */ }
        go.transform.position = bounds.center;

        BoxCollider box = go.AddComponent<BoxCollider>();
        box.size = bounds.size;
        box.sharedMaterial = group[0].sharedMaterial;

        foreach (BoxCollider old in group) old.enabled = false;

        if (logEvents)
        {
            Debug.Log("[RuinsGroundSeamMerge] 合併 " + group.Count + " 塊地板碰撞盒（" + group[0].name + " … " + group[group.Count - 1].name + "）"
                      + " → x " + bounds.min.x.ToString("F2") + "～" + bounds.max.x.ToString("F2")
                      + "、頂面 y " + bounds.max.y.ToString("F3") + "，接縫不存在了");
        }
    }

    static bool IsRotated(Transform t)
    {
        Vector3 e = t.eulerAngles;
        return Mathf.Abs(Mathf.DeltaAngle(e.x, 0f)) > 0.1f || Mathf.Abs(Mathf.DeltaAngle(e.y, 0f)) > 0.1f || Mathf.Abs(Mathf.DeltaAngle(e.z, 0f)) > 0.1f;
    }
}
