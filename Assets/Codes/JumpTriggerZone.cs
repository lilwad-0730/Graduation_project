using UnityEngine;

public class JumpTriggerZone : MonoBehaviour
{
    [Header("目標平台設定")]
    [Tooltip("請把對應的樓梯/平台拖曳到這裡 (該物件需要掛載 TriggerablePlatform 腳本)")]
    public TriggerablePlatform targetPlatform;

    private void OnTriggerEnter(Collider other)
    {
        // 偵測是否為玩家碰觸到此空中判定區
        PlayerMovement player = other.GetComponent<PlayerMovement>();
        if (player == null)
        {
            player = other.GetComponentInParent<PlayerMovement>();
        }

        if (player != null && targetPlatform != null)
        {
            // 啟用樓梯的實體碰撞，讓玩家可以落在樓梯上
            targetPlatform.EnableCollision();
        }
    }
}
