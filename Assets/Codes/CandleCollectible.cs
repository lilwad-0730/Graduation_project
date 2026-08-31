using UnityEngine;

/// <summary>
/// 燭火可收集物件。
/// 
/// 【場景建置】
///   1. 在 dark glasses 場景中建立或選取燭火物件。
///   2. 確認有 BoxCollider（IsTrigger = true）。
///   3. 掛載此腳本。
///   4. 在 ShadowMonsterController 的 candles[] 欄位手動拖曳所有燭火物件。
///
/// 【收集判定】
///   玩家（Tag = "Player" 或有 PlayerMovement 組件）進入碰撞框時觸發收集。
///   收集後通知 ShadowMonsterController，並隱藏自身視覺組件。
///   支援 IResettable，重生後自動復原。
/// </summary>
[RequireComponent(typeof(Collider))]
public class CandleCollectible : MonoBehaviour, IResettable
{
    [Header("燭火/道具設定")]
    [Tooltip("收集後是否立刻隱藏（保留 GameObject 本身，僅隱藏視覺）")]
    public bool hideOnCollect = true;

    [Tooltip("收集時是否播放粒子特效 Prefab（可留空）")]
    public GameObject collectFxPrefab;

    [Header("🎵 收集音效 (Collect SFX)")]
    [Tooltip("收集燭火或育兒道具時播放的音效 (例如 水下_育兒物品_奶瓶, 水下_育兒物品_搖鈴, 水下_物件接觸_01 等)")]
    public AudioClip collectSFX;
    [Range(0f, 1f)] public float sfxVolume = 0.9f;

    [Header("【狀態觀察 (勿手動修改)】")]
    public bool isCollected = false;

    // 組件快取
    private Collider _col;
    private Renderer[] _renderers;
    private Light[] _lights;
    private ParticleSystem[] _particles;

    // ──────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        _col = GetComponent<Collider>();
        if (_col == null) _col = gameObject.AddComponent<BoxCollider>();
        _col.isTrigger = true;

        // ★ 自動擴增 Z 軸厚度至 30 米，保證 100% 覆蓋 3D 怪物模型，徹底無視 Z 軸圖層落差！
        if (_col is BoxCollider box)
        {
            float scaleZ = Mathf.Abs(transform.lossyScale.z) > 0.001f ? Mathf.Abs(transform.lossyScale.z) : 1f;
            Vector3 sz = box.size;
            sz.z = 30f / scaleZ;
            box.size = sz;
        }

        // 快取子物件中所有視覺組件（包括含在子物件中的）
        CacheVisualComponents();
    }

    private void CacheVisualComponents()
    {
        _renderers = GetComponentsInChildren<Renderer>(true);
        _lights = GetComponentsInChildren<Light>(true);
        _particles = GetComponentsInChildren<ParticleSystem>(true);
    }

    private void RefreshVisualComponents()
    {
        CacheVisualComponents();
    }

    private void Update()
    {
        if (isCollected) return;

        // ★ 主動 2.5D 平面距離判定 (前後 3.5 米，上下 6.5 米)
        if (ShadowMonsterController.Instance != null && ShadowMonsterController.Instance.currentState != ShadowMonsterController.MonsterState.Dormant)
        {
            Transform monsterTrans = ShadowMonsterController.Instance.transform;
            float dx = Mathf.Abs(transform.position.x - monsterTrans.position.x);
            float dy = Mathf.Abs(transform.position.y - monsterTrans.position.y);
            if (dx <= 3.5f && dy <= 6.5f)
            {
                Collect();
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (isCollected) return;
        if (!IsMonster(other.gameObject)) return;

        Collect();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 公開：收集/削弱邏輯
    // ──────────────────────────────────────────────────────────────────────────

    public void Collect()
    {
        isCollected = true;
        Debug.Log($"🔥【燭火削弱】影子怪物碰觸到 {gameObject.name}！觸發怪物削弱與縮小！(X = {transform.position.x:F1})");

        // 通知影子怪物系統
        if (ShadowMonsterController.Instance != null)
            ShadowMonsterController.Instance.OnCandleCollected(this);
        else
            Debug.LogWarning($"【燭火】找不到 ShadowMonsterController！請確認場景中有影子怪物物件。");

        // 播放收集音效
        if (collectSFX != null)
        {
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFXAt(collectSFX, transform.position, sfxVolume);
            else AudioSource.PlayClipAtPoint(collectSFX, transform.position, AudioManager.ScaleSfx(sfxVolume));
        }

        // 播放收集特效
        if (collectFxPrefab != null)
            Instantiate(collectFxPrefab, transform.position, Quaternion.identity);

        // 隱藏燭火視覺組件（但 GameObject 保持 Active 以支援重置）
        if (hideOnCollect)
        {
            RefreshVisualComponents();
            foreach (var renderer in _renderers)
                if (renderer != null) renderer.enabled = false;

            foreach (var l in _lights)
                if (l != null) l.enabled = false;

            foreach (var ps in _particles)
                if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);

            _col.enabled = false;
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 私有：怪物判定（多重驗證）
    // ──────────────────────────────────────────────────────────────────────────

    private bool IsMonster(GameObject go)
    {
        if (go == null) return false;

        // 1. 直接比對影子怪物實例與層級
        if (ShadowMonsterController.Instance != null)
        {
            if (go == ShadowMonsterController.Instance.gameObject || go.transform.IsChildOf(ShadowMonsterController.Instance.transform))
            {
                return true;
            }
        }

        // 2. 比對腳本組件
        if (go.GetComponent<ShadowMonsterController>() != null || 
            go.GetComponentInParent<ShadowMonsterController>() != null || 
            go.GetComponentInChildren<ShadowMonsterController>() != null)
        {
            return true;
        }

        // 3. 安全名稱比對
        string name = go.name.ToLower();
        if (name.Contains("monster") || name.Contains("mutant") || name.Contains("shadow"))
        {
            return true;
        }

        return false;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // IResettable：重生後由 ShadowMonsterController.ResetToInitialState() 呼叫
    // ──────────────────────────────────────────────────────────────────────────

    public void ResetToInitialState()
    {
        if (!isCollected) return; // 尚未被收集，不需要重置

        isCollected = false;

        // 重新啟用碰撞器
        _col.enabled = true;

        // 恢復所有視覺組件
        foreach (var renderer in _renderers)
            if (renderer != null) renderer.enabled = true;

        foreach (var l in _lights)
            if (l != null) l.enabled = true;

        foreach (var ps in _particles)
        {
            if (ps != null)
            {
                ps.Clear();
                ps.Play();
            }
        }

        Debug.Log($"【燭火】{gameObject.name} 已重置，可再次被收集。");
    }
}
