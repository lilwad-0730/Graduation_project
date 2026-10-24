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

    [Header("★1021 沒點燃的燭火")]
    [Tooltip("怪物靠近一根「玩家還沒點燃」的燭火後，多給玩家幾秒來得及點燃；超過就算漏掉（怪物不受傷、不計數，火焰閃一下熄滅）。")]
    public float unlitGraceSeconds = 0.3f;
    [Tooltip("已漏掉：怪物經過時玩家還沒點燃。重生時會清掉。")]
    public bool missed = false;

    private float _unlitSince = -1f;

    /// <summary>★1021 玩家點燃了嗎（沒有光暈元件的燭火一律當作已點燃，維持舊行為）。</summary>
    public bool IsLitByPlayer
    {
        get
        {
            CandleAuraGlow aura = GetComponent<CandleAuraGlow>();
            return aura == null || aura.IsLit;
        }
    }

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

        // ★ 精準 2.5D 物理碰撞盒：
        //   X 軸：寬度 0.8 米 (半寬 0.4 米，緊貼燭台本體，絕不提前觸發)
        //   Y 軸：高度 4.0 米 (涵蓋燭火火苗與底座高度)
        //   Z 軸：厚度 30.0 米 (徹底覆蓋 2.5D 前後景深落差)
        if (_col is BoxCollider box)
        {
            float scaleX = Mathf.Abs(transform.lossyScale.x) > 0.001f ? Mathf.Abs(transform.lossyScale.x) : 1f;
            float scaleY = Mathf.Abs(transform.lossyScale.y) > 0.001f ? Mathf.Abs(transform.lossyScale.y) : 1f;
            float scaleZ = Mathf.Abs(transform.lossyScale.z) > 0.001f ? Mathf.Abs(transform.lossyScale.z) : 1f;

            box.size = new Vector3(0.8f / scaleX, 4.0f / scaleY, 30.0f / scaleZ);
            box.center = new Vector3(0f, 1.8f / scaleY, 0f);
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
        // 單一權威物理 Trigger 判定為主要機制；若物件已被收集直接返回
        if (isCollected || missed) return;

        // ★ 主動 2.5D 平面距離判定 (前後 3.5 米，上下 6.5 米)
        if (ShadowMonsterController.Instance != null && ShadowMonsterController.Instance.currentState != ShadowMonsterController.MonsterState.Dormant)
        {
            // ★原本是拿怪物的 transform（Pivot 在腳底，y≈-60）跟燭火（y≈-36）比，
            //   dy 差 23 個單位，dy<=6.5 這個判定從來沒成立過。
            //   改成問怪物「你的身體有沒有碰到這根燭火」，縮放多少都算得準。
            if (ShadowMonsterController.Instance.OverlapsCandle(transform.position))
            {
                Collect();
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (isCollected || missed) return;
        if (!IsMonster(other.gameObject)) return;

        TryCollect(other.gameObject);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 公開：單一權威收集/削弱邏輯 (Single Authoritative Entry Point)
    // ──────────────────────────────────────────────────────────────────────────

    public bool TryCollect(GameObject collector = null)
    {
        // ★ 核心原子防護：確保整場遊戲每根燭火 100% 只被收集一次
        if (isCollected || missed) return false;

        // ★1021 沒被玩家點燃的燭火，怪物碰了也不算：先給寬限，寬限內玩家點燃就照常算；超過就漏掉
        if (!IsLitByPlayer)
        {
            if (_unlitSince < 0f) _unlitSince = Time.time;
            if (Time.time - _unlitSince >= Mathf.Max(0f, unlitGraceSeconds)) MarkMissed();
            return false;
        }

        isCollected = true;

        // 立即關閉自身 Collider，徹底杜絕同幀內物理重疊重複觸發
        if (_col != null) _col.enabled = false;

        Debug.Log($"🔥【燭火削弱】影子怪物收集 {gameObject.name}！觸發怪物削弱與縮小！(X = {transform.position.x:F1})");

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

        // 隱藏燭火視覺組件（但 GameObject 保持 Active 以支援 IResettable 重置）
        if (hideOnCollect)
        {
            CandleAuraGlow aura = GetComponent<CandleAuraGlow>();
            if (aura != null)
            {
                aura.HideAura();
            }
            else
            {
                RefreshVisualComponents();
                foreach (var renderer in _renderers)
                    if (renderer != null) renderer.enabled = false;

                foreach (var l in _lights)
                    if (l != null) l.enabled = false;

                foreach (var ps in _particles)
                    if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        return true;
    }

    public void Collect()
    {
        TryCollect();
    }

    /// <summary>★1021 怪物經過時玩家還沒點燃：記成漏掉、通知怪物，火焰閃一下熄滅當作提示。</summary>
    private void MarkMissed()
    {
        if (missed || isCollected) return;
        missed = true;
        _unlitSince = -1f;
        Debug.Log($"🕯️【燭火漏掉】{gameObject.name}（X = {transform.position.x:F1}）怪物經過時玩家還沒點燃，不算數。");
        if (ShadowMonsterController.Instance != null)
            ShadowMonsterController.Instance.OnCandleMissed(this);
        StartCoroutine(FlickerOutRoutine());
    }

    private System.Collections.IEnumerator FlickerOutRoutine()
    {
        RefreshVisualComponents();
        for (int i = 0; i < 3; i++)
        {
            foreach (var l in _lights) if (l != null) l.enabled = false;
            yield return new WaitForSeconds(0.07f);
            foreach (var l in _lights) if (l != null) l.enabled = true;
            yield return new WaitForSeconds(0.07f);
        }
        foreach (var l in _lights) if (l != null) l.enabled = false;
        foreach (var ps in _particles) if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    /// <summary>
    /// ★1016 安靜地算成已收集：不通知怪物、不播音效特效，只把狀態設好、外觀藏起來。
    /// 給「追逐中途存檔點重生」用：怪物身後、牠再也走不到的燭火，算牠吃過（由 ShadowMonsterController 自己同步數量與大小）。
    /// </summary>
    public void MarkCollectedSilently()
    {
        isCollected = true;
        if (_col == null) _col = GetComponent<Collider>();
        if (_col != null) _col.enabled = false;

        CandleAuraGlow aura = GetComponent<CandleAuraGlow>();
        if (aura != null)
        {
            aura.HideAura();
        }
        else
        {
            RefreshVisualComponents();
            foreach (var renderer in _renderers)
                if (renderer != null) renderer.enabled = false;
            foreach (var l in _lights)
                if (l != null) l.enabled = false;
            foreach (var ps in _particles)
                if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
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
        // ★1024 存檔點前面的燭火玩家已經過關，重生不刷新（由 ShadowMonsterController 統一處理，避免重生系統逐個重置時把它們復原）
        if (ShadowMonsterController.Instance != null && ShadowMonsterController.Instance.IsCandleKeptByCheckpoint(this))
        {
            if (!isCollected) MarkCollectedSilently();
            return;
        }

        if (!isCollected && !missed) return; // 沒被收集、也沒漏掉，不需要重置

        StopAllCoroutines();
        isCollected = false;
        missed = false;
        _unlitSince = -1f;
        if (_col == null) _col = GetComponent<Collider>();
        RefreshVisualComponents();

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
