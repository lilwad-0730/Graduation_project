using UnityEngine;

[RequireComponent(typeof(Collider))]
public class BGMZone : MonoBehaviour
{
    [Header("這個區域要播放哪首音樂？")]
    public AudioClip levelMusic;

    [Header("離開區域設定")]
    [Tooltip("是否在離開此區域時停止音樂？(建議保持 false：讓音樂持續播放，直到進入下一個不同音樂的區域自動 Crossfade 交叉淡入淡出，切換關卡也不會中斷)")]
    public bool stopOnExit = false;

    private void Start()
    {
        // 防呆：確保這個物件身上的 Collider 有勾選 IsTrigger，否則玩家會撞到一堵隱形牆
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            col.isTrigger = true;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        // 檢查進來的是不是玩家
        if (other.CompareTag("Player") || other.GetComponentInParent<PlayerMovement>() != null)
        {
            if (levelMusic != null)
            {
                // 如果場景裡有 AudioManager，就叫它播放這首音樂
                if (AudioManager.Instance != null)
                {
                    AudioManager.Instance.PlayBGM(levelMusic);
                }
                else
                {
                    Debug.LogWarning("場景中沒有找到 AudioManager！請建立一個空物件並掛載 AudioManager 腳本。");
                }
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        // 只有當明確勾選 stopOnExit 時才在離開時停掉音樂
        if (stopOnExit && (other.CompareTag("Player") || other.GetComponentInParent<PlayerMovement>() != null))
        {
            if (levelMusic != null && AudioManager.Instance != null)
            {
                if (AudioManager.Instance.GetCurrentClip() == levelMusic)
                {
                    AudioManager.Instance.StopBGM();
                }
            }
        }
    }
}
