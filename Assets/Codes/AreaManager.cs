using UnityEngine;

/// <summary>
/// 全域區域管理器，負責監控玩家目前在哪個背景，並在離開時重置上一個背景。
/// 請將這個腳本掛載在一個空的 GameManager 物件上。
/// </summary>
public class AreaManager : MonoBehaviour
{
    public static AreaManager Instance { get; private set; }

    [Header("狀態監控 (唯讀，請勿手動拖曳)")]
    [Tooltip("玩家目前正在哪個背景中")]
    public GameArea currentArea;

    private void Awake()
    {
        // 確保場景中只有一個 AreaManager
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
        }
        else
        {
            Instance = this;
        }
    }

    /// <summary>
    /// 當玩家踩進一個新的 GameArea 觸發器時，由 GameArea 主動呼叫
    /// </summary>
    public void OnPlayerEnterArea(GameArea newArea)
    {
        // 如果玩家真的進到了一個「不同」的新區域
        if (currentArea != null && currentArea != newArea)
        {
            Debug.Log($"【背景切換】玩家從 {currentArea.gameObject.name} 進入了 {newArea.gameObject.name}。即將重置舊背景...");
            // 重置上一個區域
            currentArea.ResetArea();
        }

        // 更新當前區域為新的區域
        currentArea = newArea;
    }
}
