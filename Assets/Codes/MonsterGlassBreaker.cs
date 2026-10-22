using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 玻璃館怪物前進摧毀玻璃平台。
/// 掛在玻璃館影子怪物 (ShadowMonsterController) 身上：怪物追逐前進時，
/// 只要牠的 BoxCollider 碰到玻璃平台，就會摧毀它，斷絕玩家往回走的退路。
///
/// 場景裡沒有 Destructible 的玻璃平台會在碎裂時自動補上，
/// 並套用 mirror wall_001 的切片數量、不規則度、爆裂力與消失時間。
/// </summary>
[RequireComponent(typeof(BoxCollider))]
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
    [Tooltip("怪物身上的 ShadowMonsterController (留空自動在自身/父物件尋找)")]
    public ShadowMonsterController monsterController;

    [Header("★1016 公平：只打碎她身後的玻璃")]
    [Tooltip("怪物的碰撞盒左右各約 6 公尺寬，玻璃地板一片約 11 公尺：怪物還在她身後十幾公尺，就會先把她腳下那一片打碎、她直接掉下去。\n" +
             "打開＝只打碎整片都在她身後（右緣比她的位置還後面 behindPlayerMargin 公尺）的玻璃，斷她的退路；她腳下、她前面的不碎（等她走過去再碎）。\n" +
             "關掉＝原本（碰到就碎）")]
    public bool onlyBreakBehindPlayer = true;

    [Tooltip("玻璃右緣要比她的位置後面幾公尺才打碎")]
    public float behindPlayerMargin = 1.0f;
    private class GlassTarget
    {
        public Transform tr;
        public Vector3 initialPosition;
        public Quaternion initialRotation;
        public bool broken;
        public bool protectedFromBreak;

        // 內建消失表現用的原始狀態
        public SpriteRenderer[] renderers;
        public Color[] originalColors;
        public Collider[] colliders;
        public bool[] originalColliderEnabled;
    }

    private readonly List<GlassTarget> _targets = new List<GlassTarget>();

    private void Start()
    {
        if (monsterController == null)
        {
            monsterController = GetComponent<ShadowMonsterController>();
            if (monsterController == null) monsterController = GetComponentInParent<ShadowMonsterController>();
        }

        BoxCollider monsterCollider = GetComponent<BoxCollider>();
        monsterCollider.isTrigger = true;

        BuildTargetList();
        ProtectGlassNearestToCandles();

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
            initialRotation = tr.rotation,
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

    private void ProtectGlassNearestToCandles()
    {
        if (monsterController == null || monsterController.candles == null) return;

        foreach (var candle in monsterController.candles)
        {
            if (candle == null) continue;

            GlassTarget nearest = null;
            float nearestDistance = float.MaxValue;
            // 只比畫面上的左右、上下距離：燭台為了不蓋住玩家手部被往後放（Z），不能讓 Z 影響挑哪片玻璃
            Vector3 cp = candle.transform.position;
            foreach (var target in _targets)
            {
                if (target == null || target.tr == null) continue;

                float distance = target.colliders.Length == 0
                    ? (target.tr.position - new Vector3(cp.x, cp.y, target.tr.position.z)).sqrMagnitude
                    : float.MaxValue;

                foreach (var collider in target.colliders)
                {
                    if (collider == null) continue;
                    Bounds b = collider.bounds;
                    distance = Mathf.Min(distance, b.SqrDistance(new Vector3(cp.x, cp.y, b.center.z)));
                }

                if (distance >= nearestDistance) continue;
                nearestDistance = distance;
                nearest = target;
            }

            if (nearest != null) nearest.protectedFromBreak = true;
        }
    }
    private void OnTriggerEnter(Collider other)
    {
        if (!CanBreakGlass()) return;

        CandleCollectible candle = other.GetComponent<CandleCollectible>();
        if (candle == null) candle = other.GetComponentInParent<CandleCollectible>();
        if (candle != null)
        {
            candle.TryCollect(gameObject);
            Debug.Log($"【燭火摧毀】怪物 BoxCollider 碰到 '{candle.name}'，觸發碎裂！");
            Shatter(candle.transform);
            return;
        }

        TryBreakGlass(other);
    }

    /// <summary>★1016 碰撞盒一直壓著還沒碎的玻璃：等那片玻璃整片落到她身後再碎（onlyBreakBehindPlayer 開著才需要）。</summary>
    private void OnTriggerStay(Collider other)
    {
        if (!onlyBreakBehindPlayer) return;
        if (!CanBreakGlass()) return;
        TryBreakGlass(other);
    }

    private void TryBreakGlass(Collider other)
    {
        foreach (var t in _targets)
        {
            if (t.broken || t.protectedFromBreak || t.tr == null) continue;
            if (other.transform != t.tr && !other.transform.IsChildOf(t.tr)) continue;

            // ★1016 她腳下、她前面的玻璃先不碎
            if (onlyBreakBehindPlayer && !IsBehindPlayer(t)) return;

            t.broken = true;
            Debug.Log($"【玻璃摧毀】怪物 BoxCollider 碰到 '{t.tr.name}'，觸發摧毀！");
            BreakTarget(t);
            return;
        }
    }

    /// <summary>★1016 這片玻璃整片都在她身後嗎（右緣 &lt; 她的 X − behindPlayerMargin）。找不到她就照原本的規則（算在身後）。</summary>
    private bool IsBehindPlayer(GlassTarget t)
    {
        Transform p = monsterController != null ? monsterController.player : null;
        if (p == null) return true;

        float maxX = float.NegativeInfinity;
        if (t.colliders != null)
        {
            foreach (var c in t.colliders)
            {
                if (c == null || !c.enabled) continue;
                maxX = Mathf.Max(maxX, c.bounds.max.x);
            }
        }
        if (float.IsNegativeInfinity(maxX)) maxX = t.tr.position.x;
        return maxX < p.position.x - behindPlayerMargin;
    }

    private bool CanBreakGlass()
    {
        if (monsterController == null) return true;

        var state = monsterController.currentState;
        return state == ShadowMonsterController.MonsterState.Chasing ||
               state == ShadowMonsterController.MonsterState.Punishing;
    }
    /// <summary>使用 mirror wall_001 的 Destructible 設定製作實體玻璃碎片。</summary>
    private void BreakTarget(GlassTarget t)
    {
        Shatter(t.tr);
    }

    private static void Shatter(Transform target)
    {
        Destructible destructible = target.GetComponent<Destructible>();
        if (destructible == null) destructible = target.gameObject.AddComponent<Destructible>();

        destructible.shatteredPrefab = null;
        destructible.keepShatteredOnReset = false;
        destructible.shatterOnCollision = true;
        destructible.disappearDelay = 2f;
        destructible.minGridSubdivisions = 6;
        destructible.jitterAmount = 0.35f;
        destructible.explosionForce = 2.5f;
        destructible.shatterSFX = null;
        destructible.followUpSandSFX = null;
        destructible.Shatter();
    }
    // --- IResettable：玩家重生時復原所有被摧毀的平台，讓怪物重新走一次時能再摧毀一遍 ---
    public void ResetToInitialState()
    {
        if (monsterController != null && monsterController.candles != null)
        {
            foreach (var candle in monsterController.candles)
            {
                if (candle == null) continue;
                Destructible destructible = candle.GetComponent<Destructible>();
                if (destructible != null) destructible.ResetToInitialState();
            }
        }

        foreach (var t in _targets)
        {
            if (t == null || t.tr == null) continue;

            t.broken = false;
            Destructible destructible = t.tr.GetComponent<Destructible>();
            if (destructible != null) destructible.ResetToInitialState();
            t.tr.position = t.initialPosition;
            t.tr.rotation = t.initialRotation;
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
