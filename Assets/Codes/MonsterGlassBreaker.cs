using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 玻璃館怪物前進摧毀玻璃平台。
/// 掛在玻璃館影子怪物 (ShadowMonsterController) 身上：怪物追逐前進時，
/// 只要牠的 X 座標越過某片玻璃平台，就會摧毀它，斷絕玩家往回走的退路。
///
/// ★ 重點：場景裡大部分的 glass floor_0 (N) 只有 SpriteRenderer + BoxCollider + AtmosphericCrackFloor，
///   身上「沒有」BreakableGlassFloor / GlassShatterFX / Destructible 任何碎裂腳本，
///   所以這裡採用階梯式摧毀：有什麼碎裂腳本就用什麼，都沒有就用內建的淡出墜落表現。
/// </summary>
public class MonsterGlassBreaker : MonoBehaviour, IResettable
{
    [Header("要摧毀的玻璃平台")]
    [Tooltip("直接把 glass floor_0 s 這種父物件拖進來，底下所有子物件都會被納入摧毀名單")]
    public Transform[] glassFloorContainers;

    [Tooltip("額外個別指定的玻璃平台物件")]
    public Transform[] extraGlassFloors;

    [Tooltip("自動搜尋：名稱含有這些關鍵字的物件都會被納入摧毀名單 (清空則完全不自動搜尋)")]
    public string[] autoSearchNameKeywords = { "glass floor", "glass platform" };

    [Header("觸發設定")]
    [Tooltip("怪物 X 座標超過玻璃平台 X 座標多少距離後，判定為「已經走過去」並摧毀它")]
    public float passDistance = 1.0f;

    [Tooltip("怪物身上的 ShadowMonsterController (留空自動在自身/父物件尋找)")]
    public ShadowMonsterController monsterController;

    [Tooltip("開始追逐當下就已經在怪物後方的平台，是否略過不摧毀 (避免一瞬間整條路的地板同時碎掉)")]
    public bool skipFloorsBehindAtStart = true;

    [Header("沒有碎裂腳本時的預設消失表現")]
    [Tooltip("淡出與墜落所需時間 (秒)")]
    public float fallbackFadeDuration = 0.45f;

    [Tooltip("消失前往下墜落的距離")]
    public float fallbackDropDistance = 1.5f;

    private class GlassTarget
    {
        public Transform tr;
        public Vector3 initialPosition;
        public bool broken;

        // 內建消失表現用的原始狀態
        public SpriteRenderer[] renderers;
        public Color[] originalColors;
        public Collider[] colliders;
        public bool[] originalColliderEnabled;
    }

    private readonly List<GlassTarget> _targets = new List<GlassTarget>();
    private bool _chaseStartCaptured = false;
    private float _chaseStartX = 0f;

    private void Start()
    {
        if (monsterController == null)
        {
            monsterController = GetComponent<ShadowMonsterController>();
            if (monsterController == null) monsterController = GetComponentInParent<ShadowMonsterController>();
        }

        BuildTargetList();

        Debug.Log($"【玻璃摧毀】'{gameObject.name}' 初始化完成：納入 {_targets.Count} 片玻璃平台，" +
                  $"monsterController = {(monsterController != null ? monsterController.gameObject.name : "null")}");
    }

    private void BuildTargetList()
    {
        _targets.Clear();
        HashSet<Transform> seen = new HashSet<Transform>();

        // 1. 指定的父容器：收其底下所有子物件
        if (glassFloorContainers != null)
        {
            foreach (var container in glassFloorContainers)
            {
                if (container == null) continue;
                foreach (Transform child in container)
                {
                    TryAddTarget(child, seen);
                }
            }
        }

        // 2. 額外個別指定
        if (extraGlassFloors != null)
        {
            foreach (var tr in extraGlassFloors) TryAddTarget(tr, seen);
        }

        // 3. 依名稱關鍵字自動搜尋
        if (autoSearchNameKeywords != null && autoSearchNameKeywords.Length > 0)
        {
            SpriteRenderer[] all = FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var sr in all)
            {
                if (sr == null) continue;
                string n = sr.gameObject.name.ToLower();
                foreach (var keyword in autoSearchNameKeywords)
                {
                    if (string.IsNullOrEmpty(keyword)) continue;
                    if (n.Contains(keyword.ToLower()))
                    {
                        TryAddTarget(sr.transform, seen);
                        break;
                    }
                }
            }
        }
    }

    private void TryAddTarget(Transform tr, HashSet<Transform> seen)
    {
        if (tr == null || seen.Contains(tr)) return;
        if (tr == transform || tr.IsChildOf(transform)) return; // 不摧毀怪物自己身上的東西
        seen.Add(tr);

        GlassTarget t = new GlassTarget
        {
            tr = tr,
            initialPosition = tr.position,
            broken = false,
            renderers = tr.GetComponentsInChildren<SpriteRenderer>(true),
            colliders = tr.GetComponentsInChildren<Collider>(true)
        };

        t.originalColors = new Color[t.renderers.Length];
        for (int i = 0; i < t.renderers.Length; i++)
        {
            t.originalColors[i] = t.renderers[i] != null ? t.renderers[i].color : Color.white;
        }

        t.originalColliderEnabled = new bool[t.colliders.Length];
        for (int i = 0; i < t.colliders.Length; i++)
        {
            t.originalColliderEnabled[i] = t.colliders[i] != null && t.colliders[i].enabled;
        }

        _targets.Add(t);
    }

    private void Update()
    {
        if (_targets.Count == 0) return;

        // 只有怪物正在追逐/懲罰移動時才會摧毀，蟄伏或尚未開始追逐時不觸發
        if (monsterController != null)
        {
            var state = monsterController.currentState;
            if (state != ShadowMonsterController.MonsterState.Chasing &&
                state != ShadowMonsterController.MonsterState.Punishing)
            {
                return;
            }
        }

        float monsterX = transform.position.x;

        // 開始追逐當下記住起點：起點後方的平台直接標記為已處理，不會整條路一次全碎
        if (!_chaseStartCaptured)
        {
            _chaseStartCaptured = true;
            _chaseStartX = monsterX;

            if (skipFloorsBehindAtStart)
            {
                int skipped = 0;
                foreach (var t in _targets)
                {
                    if (t.tr != null && t.tr.position.x < _chaseStartX - passDistance)
                    {
                        t.broken = true;
                        skipped++;
                    }
                }
                Debug.Log($"【玻璃摧毀】開始追逐 (怪物 x={_chaseStartX:F1})，略過後方已通過的 {skipped} 片平台。");
            }
        }

        foreach (var t in _targets)
        {
            if (t.broken || t.tr == null) continue;

            if (monsterX - t.tr.position.x >= passDistance)
            {
                t.broken = true;
                Debug.Log($"【玻璃摧毀】怪物 x={monsterX:F1} 越過 '{t.tr.name}' (x={t.tr.position.x:F1})，觸發摧毀！");
                BreakTarget(t);
            }
        }
    }

    /// <summary>階梯式摧毀：身上有什麼碎裂腳本就用什麼，都沒有就用內建的淡出墜落。</summary>
    private void BreakTarget(GlassTarget t)
    {
        BreakableGlassFloor breakable = t.tr.GetComponent<BreakableGlassFloor>();
        if (breakable != null)
        {
            breakable.TriggerBreakSequence();
            return;
        }

        GlassShatterFX fx = t.tr.GetComponent<GlassShatterFX>();
        if (fx != null)
        {
            fx.TriggerBreakSequence();
            return;
        }

        Destructible destructible = t.tr.GetComponent<Destructible>();
        if (destructible != null)
        {
            destructible.Shatter();
            return;
        }

        StartCoroutine(FallbackBreakRoutine(t));
    }

    /// <summary>沒有任何碎裂腳本的普通玻璃地磚：關掉碰撞後往下墜落並淡出消失。</summary>
    private IEnumerator FallbackBreakRoutine(GlassTarget t)
    {
        if (t.colliders != null)
        {
            foreach (var c in t.colliders)
            {
                if (c != null) c.enabled = false;
            }
        }

        Vector3 startPos = t.tr != null ? t.tr.position : Vector3.zero;
        Vector3 endPos = startPos + Vector3.down * fallbackDropDistance;

        float duration = Mathf.Max(fallbackFadeDuration, 0.1f);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (t.tr == null) yield break;

            elapsed += Time.deltaTime;
            float p = Mathf.Clamp01(elapsed / duration);

            t.tr.position = Vector3.Lerp(startPos, endPos, p);

            if (t.renderers != null)
            {
                for (int i = 0; i < t.renderers.Length; i++)
                {
                    if (t.renderers[i] == null) continue;
                    Color c = t.originalColors[i];
                    c.a = t.originalColors[i].a * (1f - p);
                    t.renderers[i].color = c;
                }
            }

            yield return null;
        }

        if (t.tr != null) t.tr.gameObject.SetActive(false);
    }

    // --- IResettable：玩家重生時復原所有被摧毀的平台，讓怪物重新走一次時能再摧毀一遍 ---
    public void ResetToInitialState()
    {
        StopAllCoroutines();
        _chaseStartCaptured = false;

        foreach (var t in _targets)
        {
            if (t == null || t.tr == null) continue;

            t.broken = false;
            t.tr.position = t.initialPosition;
            t.tr.gameObject.SetActive(true);

            if (t.renderers != null)
            {
                for (int i = 0; i < t.renderers.Length; i++)
                {
                    if (t.renderers[i] != null) t.renderers[i].color = t.originalColors[i];
                }
            }

            if (t.colliders != null)
            {
                for (int i = 0; i < t.colliders.Length; i++)
                {
                    if (t.colliders[i] != null) t.colliders[i].enabled = t.originalColliderEnabled[i];
                }
            }
        }
    }
}
