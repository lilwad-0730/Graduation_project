using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 我你他　鳥群系統（提案版）　接手／還原開關
///
/// ═══════════════════════════════════════════════════
/// ★ 預設什麼都不做。組員原本的鳥照常運作，新的這團只是多出來的景。
///   等大家同意了，再按下面任一種方式接手；隨時可以一鍵還原。
/// ═══════════════════════════════════════════════════
///
/// 【怎麼接手】三選一
///   A　在 Inspector 右鍵這個元件 →「① 接手：關掉舊鳥群、開啟新鳥群攻擊」
///   B　勾選 takeOverOnStart，之後每次進遊戲自動接手
///   C　程式呼叫 FlockTakeover.Instance.TakeOver()
///
/// 【怎麼還原】右鍵 →「② 還原：把舊鳥群開回來」，或呼叫 Revert()。
///
/// 【重要】接手＝把舊鳥「關掉」（SetActive false），不是刪掉。
///   場景檔裡組員那 57 隻一個都沒少，還原就整組回來。
///   真的要永久換掉是另一件事，要等大家點頭之後、由人手動刪。
/// </summary>
[DisallowMultipleComponent]
public class FlockTakeover : MonoBehaviour
{
    public static FlockTakeover Instance { get; private set; }

    [Header("新鳥群（留空＝找同一個物件上的）")]
    public ScatteredFlock newFlock;

    [Header("舊鳥群")]
    [Tooltip("組員的鳥群根物件名稱。留空就自動找場景裡所有 IndividualBirdEnemy 的共同父物件")]
    public string oldFlockRootName = "BirdEnemy";

    [Header("行為")]
    [Tooltip("進遊戲就自動接手？大家同意之前請保持關閉")]
    public bool takeOverOnStart = false;

    [Tooltip("接手時是否連帶打開新鳥群的俯衝攻擊")]
    public bool enableNewAttack = true;

    [Tooltip("在 Console 印出接手／還原的結果")]
    public bool logResult = true;

    private readonly List<GameObject> _disabled = new List<GameObject>();
    private bool _takenOver;

    public bool IsTakenOver { get { return _takenOver; } }

    private void Awake()
    {
        Instance = this;
        if (newFlock == null) newFlock = GetComponent<ScatteredFlock>();
    }

    private void Start()
    {
        if (takeOverOnStart) TakeOver();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ══════════════════════════════════════════
    [ContextMenu("① 接手：關掉舊鳥群、開啟新鳥群攻擊")]
    public void TakeOver()
    {
        if (_takenOver) { Log("已經接手過了，沒有重複動作。"); return; }

        _disabled.Clear();
        List<GameObject> targets = CollectOldFlock();
        for (int i = 0; i < targets.Count; i++)
        {
            if (targets[i] == null || !targets[i].activeSelf) continue;
            targets[i].SetActive(false);
            _disabled.Add(targets[i]);
        }

        if (newFlock != null && enableNewAttack) newFlock.attackEnabled = true;

        _takenOver = true;
        Log($"接手完成：關掉舊鳥群 {_disabled.Count} 個物件"
            + (newFlock != null && enableNewAttack ? "，新鳥群的俯衝攻擊已開啟。" : "。")
            + "（只是關掉，沒有刪除，隨時可還原）");
    }

    [ContextMenu("② 還原：把舊鳥群開回來")]
    public void Revert()
    {
        if (!_takenOver) { Log("目前就是原本的狀態，沒有東西要還原。"); return; }

        int n = 0;
        for (int i = 0; i < _disabled.Count; i++)
        {
            if (_disabled[i] == null) continue;
            _disabled[i].SetActive(true);
            n++;
        }
        _disabled.Clear();

        if (newFlock != null) newFlock.attackEnabled = false;

        _takenOver = false;
        Log($"還原完成：舊鳥群 {n} 個物件已開回來，新鳥群回到不攻擊的狀態。");
    }

    [ContextMenu("③ 只看狀態，不動任何東西")]
    public void ReportStatus()
    {
        List<GameObject> targets = CollectOldFlock();
        int birds = Object.FindObjectsByType<IndividualBirdEnemy>(FindObjectsSortMode.None).Length;
        Debug.Log($"【鳥群提案】目前狀態：{(_takenOver ? "新鳥群接手中" : "舊鳥群運作中（提案版只是額外的景）")}\n"
                + $"　舊鳥群根物件：{targets.Count} 個｜場景裡的 IndividualBirdEnemy：{birds} 隻\n"
                + $"　新鳥群：{(newFlock == null ? "沒接上" : newFlock.BirdCount + " 隻，攻擊 " + (newFlock.attackEnabled ? "開" : "關"))}", this);
    }

    // ══════════════════════════════════════════
    /// <summary>
    /// 找舊鳥群。先照名字找根物件；找不到就退而收集所有 IndividualBirdEnemy
    /// 的最上層共同父物件（沒有共同父就一隻一隻收）。
    /// </summary>
    private List<GameObject> CollectOldFlock()
    {
        List<GameObject> result = new List<GameObject>();

        IndividualBirdEnemy[] birds = Object.FindObjectsByType<IndividualBirdEnemy>(FindObjectsSortMode.None);
        if (birds.Length == 0) return result;

        if (!string.IsNullOrEmpty(oldFlockRootName))
        {
            for (int i = 0; i < birds.Length; i++)
            {
                Transform t = birds[i] != null ? birds[i].transform : null;
                while (t != null)
                {
                    if (t.name == oldFlockRootName)
                    {
                        if (!result.Contains(t.gameObject)) result.Add(t.gameObject);
                        break;
                    }
                    t = t.parent;
                }
            }
            if (result.Count > 0) return result;
        }

        // 退路：沒有共同根，就一隻一隻關
        for (int i = 0; i < birds.Length; i++)
        {
            if (birds[i] == null) continue;
            GameObject g = birds[i].gameObject;
            if (!result.Contains(g)) result.Add(g);
        }
        return result;
    }

    private void Log(string s)
    {
        if (logResult) Debug.Log("【鳥群提案】" + s, this);
    }
}
