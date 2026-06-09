using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 代表一個「背景」的區域範圍。
/// 請將此腳本掛載在帶有 3D BoxCollider (IsTrigger = true) 的空物件上。
/// </summary>
[RequireComponent(typeof(Collider))]
public class GameArea : MonoBehaviour
{
    [Header("設定")]
    [Tooltip("如果打勾，遊戲開始時會自動把這個 BoxCollider 範圍內的 IResettable 物件抓進名單中。")]
    public bool autoFindResettablesOnStart = true;
    
    [Header("重置名單 (可手動拖曳或自動抓取)")]
    // 利用 MonoBehaviour 的型別來在 Inspector 顯示，雖然底層是 interface
    // 但因為 Unity Inspector 原生不支援直接拖曳 Interface，所以這裡用 List 來內部管理
    private List<IResettable> resettableObjects = new List<IResettable>();

    private void Start()
    {
        // 確保 Collider 是 Trigger
        Collider col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;

        if (autoFindResettablesOnStart)
        {
            // 在開始時，找到世界中所有的腳本
            MonoBehaviour[] allScripts = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            foreach (var script in allScripts)
            {
                // 如果這個腳本有實作 IResettable 介面
                if (script is IResettable resettable)
                {
                    // 並且這個物件的座標，剛好在這個背景的 3D BoxCollider 範圍內
                    if (col.bounds.Contains(script.transform.position))
                    {
                        resettableObjects.Add(resettable);
                    }
                }
            }
            Debug.Log($"【GameArea】背景 {gameObject.name} 自動掃描到了 {resettableObjects.Count} 個可重置物件。");
        }
    }

    /// <summary>
    /// 提供給腳本在執行期間動態加入名單的方法 (例如動態生成的怪物)
    /// </summary>
    public void RegisterResettable(IResettable resettable)
    {
        if (!resettableObjects.Contains(resettable))
        {
            resettableObjects.Add(resettable);
        }
    }

    /// <summary>
    /// 執行重置動作，讓名單內的所有物件回到初始狀態
    /// </summary>
    public void ResetArea()
    {
        int count = 0;
        foreach (var obj in resettableObjects)
        {
            // 確保物件還沒被破壞 (Destory) 掉
            if (obj != null)
            {
                obj.ResetToInitialState();
                count++;
            }
        }
        Debug.Log($"【GameArea】已重置了 {gameObject.name} 區域內的 {count} 個物件！");
    }

    // 當玩家踏入這個背景時
    private void OnTriggerEnter(Collider other)
    {
        // 偵測是否為玩家踏入新區域 (利用 Tag 或是身上有沒有 PlayerRespawnSystem 來判斷)
        if (other.CompareTag("Player") || other.GetComponent<PlayerRespawnSystem>() != null)
        {
            if (AreaManager.Instance != null)
            {
                AreaManager.Instance.OnPlayerEnterArea(this);
            }
            else
            {
                Debug.LogWarning("場景中沒有找到 AreaManager！請建立一個空物件並掛上 AreaManager 腳本。");
            }
        }
    }
}
