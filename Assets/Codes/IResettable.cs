using UnityEngine;

/// <summary>
/// 考取這張「可重置執照」的物件，當玩家離開該背景區域時，就會被系統自動恢復到原始狀態。
/// </summary>
public interface IResettable
{
    /// <summary>
    /// 當背景區域被卸載/重置時，系統會自動呼叫這個方法
    /// </summary>
    void ResetToInitialState();
}
