using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ★1108 展示「AI 自動修復」用的假錯誤（故意留一個空值 bug）。
/// 用法：Play 中選單 Tools → AutoFix → 示範：製造一個程式錯誤。
/// 正常遊戲裡沒有任何東西會用到這支；展示完不需要的話可以整個刪掉。
/// </summary>
public class AutoFixDemo : MonoBehaviour
{
    private List<int> _scores;   // 故意忘了 new，用到就會 NullReferenceException

    private bool _fired;

    private void Update()
    {
        if (_fired) return;
        _fired = true;
        Debug.Log("【示範】正在製造一個故意的程式錯誤…");
        RecordScore(10);
    }

    private void RecordScore(int s)
    {
        _scores.Add(s);
        Debug.Log("【示範】分數已記錄：" + _scores.Count);
    }
}
