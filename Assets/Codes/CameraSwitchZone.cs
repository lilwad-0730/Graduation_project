using UnityEngine;
using Unity.Cinemachine; // 注意：如果是舊版 Unity 可能是 using Cinemachine;

public class CameraSwitchZone : MonoBehaviour
{
    [Header("要把哪台攝影機的權重提高？")]
    public CinemachineVirtualCamera targetCamera;
    
    [Header("切換時的目標權重 (大於 10 就會搶走畫面)")]
    public int activePriority = 20;

    // 紀錄原本的權重，離開時還原
    private int originalPriority;

    void Start()
    {
        if (targetCamera != null)
        {
            originalPriority = targetCamera.Priority;
        }
    }

    // 當玩家「走進」這個隱形方塊時
    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player") && targetCamera != null)
        {
            targetCamera.Priority = activePriority; 
            Debug.Log("🎬 進入區域！切換成特殊視角！");
        }
    }

    // 當玩家「離開」這個隱形方塊時
    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player") && targetCamera != null)
        {
            targetCamera.Priority = originalPriority;
            Debug.Log("🎬 離開區域！視角還給主角！");
        }
    }
}