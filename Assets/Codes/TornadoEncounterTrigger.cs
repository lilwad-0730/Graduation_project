using UnityEngine;

/// <summary>
/// 龍捲風遭遇觸發區 (Tornado Encounter Trigger)
/// 放置在斜坡路徑上（例如剛上斜坡一下下的位置 X≈198）。
/// 1. 平時龍捲風已在該處盤旋橫移，玩家上斜坡即可遠遠看見。
/// 2. 玩家踏入此區域（撞入龍捲風）時，啟動相機鎖定，龍捲風隨鏡頭移動並持續左右橫移。
/// 3. 可在 Unity Scene 視窗直接用滑鼠拖曳此觸發框，自由微調遭遇位置！
/// </summary>
[RequireComponent(typeof(Collider))]
public class TornadoEncounterTrigger : MonoBehaviour
{
    [Header("🌪️ 目標龍捲風")]
    [Tooltip("要啟動相機跟隨的龍捲風物件 (若留空會自動尋找場景中的龍捲風)")]
    public TornadoFollowCamera targetTornado;

    [Header("🔊 衝入風暴音效 (選填)")]
    [Tooltip("踏入龍捲風時播放的強風音效")]
    public AudioClip encounterSFX;
    [Range(0f, 1f)] public float sfxVolume = 0.9f;

    private bool _triggered = false;

    private void Awake()
    {
        var col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;

        var col2d = GetComponent<Collider2D>();
        if (col2d != null) col2d.isTrigger = true;
    }

    private void Start()
    {
        if (targetTornado == null)
        {
            targetTornado = Object.FindFirstObjectByType<TornadoFollowCamera>();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        TryTrigger(other.gameObject);
    }

    private void OnTriggerStay(Collider other)
    {
        TryTrigger(other.gameObject);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryTrigger(other.gameObject);
    }

    private void TryTrigger(GameObject obj)
    {
        if (_triggered || obj == null) return;

        if (obj.CompareTag("Player") || obj.name.Contains("Player") || obj.GetComponent<PlayerMovement>() != null)
        {
            _triggered = true;
            if (targetTornado != null)
            {
                targetTornado.ActivateFollow();
            }
            if (encounterSFX != null)
            {
                AudioSource.PlayClipAtPoint(encounterSFX, transform.position, AudioManager.ScaleSfx(sfxVolume));
            }
            Debug.Log("🌪️【龍捲風遭遇】玩家衝入斜坡龍捲風！龍捲風已鎖定至鏡頭中央持續橫移！", this);
        }
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.25f);
        var col = GetComponent<Collider>();
        if (col != null)
        {
            Gizmos.DrawCube(col.bounds.center, col.bounds.size);
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.9f);
            Gizmos.DrawWireCube(col.bounds.center, col.bounds.size);
        }
        else
        {
            Gizmos.DrawWireSphere(transform.position, 3f);
        }

        #if UNITY_EDITOR
        UnityEditor.Handles.Label(transform.position + Vector3.up * 2f, "🌪️ 龍捲風遭遇觸發區 (碰觸後龍捲風鎖定鏡頭)");
        #endif
    }
}
