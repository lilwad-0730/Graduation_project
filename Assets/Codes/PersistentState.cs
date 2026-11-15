using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ★1111 「世界狀態」存檔的共用接口（存檔點快照用）。
///
///  做法：每個「玩過就會改變」的機關，各自掛一個實作 IPersistentState 的小元件，自己回報「我現在是什麼狀態」（一小段文字）。
///        踩到存檔點時，ProgressSave 把全部回報收成一份快照，跟著那個存檔點存進槽裡；
///        「繼續」把人放到存檔點、場景跑起來之後，再把快照一個一個還原回去。
///  規則：
///   ・還原只會「往前」——機關已經比快照更進展，就不倒退（玩過的不刷新）。
///   ・每一個機關還原出錯都只略過它自己，不影響其他機關、也不會讓玩家卡死。
///   ・沒有快照（舊存檔）＝什麼都不還原，跟以前一樣。
///   ・只收「有註冊的」，不掃整個場景，所以每 2 秒也不會有效能負擔。
/// </summary>
public interface IPersistentState
{
    /// <summary>這個機關在快照裡的名字（同一場景內不能重複）。</summary>
    string StateKey { get; }
    /// <summary>回報目前狀態；回傳 null 或空字串＝這次不存。</summary>
    string CaptureState();
    /// <summary>還原狀態。必須可以重複呼叫（還原兩次結果一樣）。</summary>
    void RestoreState(string data);
}

[Serializable]
public class StateEntry
{
    public string key;
    public string value;
}

public static class PersistentState
{
    private static readonly List<IPersistentState> _list = new List<IPersistentState>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { _list.Clear(); }

    public static void Register(IPersistentState s) { if (s != null && !_list.Contains(s)) _list.Add(s); }
    public static void Unregister(IPersistentState s) { _list.Remove(s); }

    private static bool IsDead(IPersistentState s)
    {
        UnityEngine.Object o = s as UnityEngine.Object;
        if (o == null) return !object.ReferenceEquals(s, null) && s is UnityEngine.Object;   // 轉型後是 null：不是 Unity 物件就算活著；是 Unity 物件但已被銷毀就算死了
        return false;
    }

    /// <summary>收一份快照。沒有任何機關回報時回傳空清單。</summary>
    public static List<StateEntry> CaptureAll()
    {
        var res = new List<StateEntry>();
        for (int i = _list.Count - 1; i >= 0; i--)
        {
            IPersistentState s = _list[i];
            if (IsDead(s)) { _list.RemoveAt(i); continue; }
            try
            {
                string v = s.CaptureState();
                if (!string.IsNullOrEmpty(v)) res.Add(new StateEntry { key = s.StateKey, value = v });
            }
            catch (Exception e) { Debug.LogWarning("【世界狀態】收集「" + SafeKey(s) + "」出錯，略過：" + e.Message); }
        }
        return res;
    }

    /// <summary>把快照還原回去。回傳成功還原的個數。</summary>
    public static int RestoreAll(List<StateEntry> entries)
    {
        if (entries == null || entries.Count == 0) return 0;
        int ok = 0;
        for (int i = _list.Count - 1; i >= 0; i--)
        {
            IPersistentState s = _list[i];
            if (IsDead(s)) { _list.RemoveAt(i); continue; }
            string key = SafeKey(s);
            StateEntry e = entries.Find(x => x != null && x.key == key);
            if (e == null) continue;
            try { s.RestoreState(e.value); ok++; }
            catch (Exception ex) { Debug.LogWarning("【世界狀態】還原「" + key + "」出錯，略過：" + ex.Message); }
        }
        return ok;
    }

    private static string SafeKey(IPersistentState s) { try { return s.StateKey; } catch { return "?"; } }
}
