using UnityEngine;
using UnityEngine.Playables;

[RequireComponent(typeof(BoxCollider))]
public class TimelineTrigger : MonoBehaviour
{
    [Header("動畫設定")]
    [Tooltip("請把有 Playable Director (Timeline) 的物件拖進來")]
    public PlayableDirector director;

    [Tooltip("動畫播完後是否可以重複觸發？")]
    public bool playOnce = true;
    
    private bool _hasPlayed = false;

    private void Start()
    {
        // 確保此物件是觸發器
        Collider col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        // 只有玩家能觸發
        if (other.CompareTag("Player"))
        {
            if (playOnce && _hasPlayed) return;

            if (director != null)
            {
                Debug.Log($"[TimelineTrigger] 玩家進入區域，開始播放過場動畫！");
                director.Play();
                _hasPlayed = true;

                // 如果需要在播放動畫時凍結玩家，您可以在這裡加上：
                // PlayerMovement pm = other.GetComponent<PlayerMovement>();
                // if (pm != null) pm.enabled = false;
                
                // (如果您需要等動畫播完再解凍玩家，可以透過 Timeline 內建的 Signal 來呼叫解凍)
            }
        }
    }
}
