using UnityEngine;

[RequireComponent(typeof(Collider))]
public class BGMZone : MonoBehaviour
{
    [Header("這個區域要播放哪首音樂？")]
    public AudioClip levelMusic;

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
        if (other.CompareTag("Player"))
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
        // 檢查離開的是不是玩家
        if (other.CompareTag("Player"))
        {
            if (levelMusic != null && AudioManager.Instance != null)
            {
                // 只有當目前正在播的音樂真的是我們這區的音樂時，我們才把它停掉
                // (避免玩家先走進B區，才離開A區，結果A區把B區的音樂給關了)
                if (AudioManager.Instance.GetCurrentClip() == levelMusic)
                {
                    AudioManager.Instance.StopBGM();
                }
            }
        }
    }
}
