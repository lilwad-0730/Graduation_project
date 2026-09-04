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

    [Tooltip("消失時往下墜落並旋轉的角度 (讓它看起來是碎掉塌下去，而不是單純淡出)")]
    public float fallbackTiltAngle = 25f;

    [Header("💥 碎裂爆發效果 (Shatter Burst)")]
    [Tooltip("摧毀瞬間是否噴出玻璃碎片粒子")]
    public bool spawnShatterBurst = true;

    [Tooltip("碎片數量")]
    public int shardCount = 70;

    [Tooltip("碎片噴飛速度範圍")]
    public Vector2 shardSpeedRange = new Vector2(5f, 15f);

    [Tooltip("碎片大小範圍")]
    public Vector2 shardSizeRange = new Vector2(0.08f, 0.55f);

    [Tooltip("碎片存活時間 (秒)")]
    public float shardLifetime = 1.6f;

    [Tooltip("碎片重力倍率 (越大掉越快)")]
    public float shardGravity = 3.2f;

    [Tooltip("爆開瞬間的白色閃光強度 (0 = 關閉)")]
    public float flashIntensity = 1f;

    [Tooltip("細碎粉塵數量 (大碎片之外再補一層細屑，讓爆炸更有份量)")]
    public int dustCount = 40;

    [Tooltip("碎片顏色 (玻璃冷白藍)")]
    public Color shardColor = new Color(0.85f, 0.95f, 1f, 0.95f);

    [Tooltip("碎裂音效 (選填)")]
    public AudioClip shatterSFX;
    [Range(0f, 1f)] public float shatterSFXVolume = 0.85f;

    private class GlassTarget
    {
        public Transform tr;
        public Vector3 initialPosition;
        public Quaternion initialRotation;
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

    /// <summary>沒有任何碎裂腳本的普通玻璃地磚：噴出碎片後，本體傾倒墜落並淡出消失。</summary>
    private IEnumerator FallbackBreakRoutine(GlassTarget t)
    {
        if (t.colliders != null)
        {
            foreach (var c in t.colliders)
            {
                if (c != null) c.enabled = false;
            }
        }

        if (spawnShatterBurst && t.tr != null) SpawnShatterBurst(t.tr);

        if (shatterSFX != null && t.tr != null)
        {
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFXAt(shatterSFX, t.tr.position, shatterSFXVolume);
            else AudioSource.PlayClipAtPoint(shatterSFX, t.tr.position, shatterSFXVolume);
        }

        Vector3 startPos = t.tr != null ? t.tr.position : Vector3.zero;
        Vector3 endPos = startPos + Vector3.down * fallbackDropDistance;
        Quaternion startRot = t.tr != null ? t.tr.rotation : Quaternion.identity;
        float tiltDir = Random.value > 0.5f ? 1f : -1f;
        Quaternion endRot = startRot * Quaternion.Euler(0f, 0f, tiltDir * fallbackTiltAngle);

        float duration = Mathf.Max(fallbackFadeDuration, 0.1f);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (t.tr == null) yield break;

            elapsed += Time.deltaTime;
            float p = Mathf.Clamp01(elapsed / duration);

            // 加速墜落 (p*p) 比等速更像塌陷
            t.tr.position = Vector3.Lerp(startPos, endPos, p * p);
            t.tr.rotation = Quaternion.Slerp(startRot, endRot, p);

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

        if (t.tr != null)
        {
            t.tr.rotation = startRot;
            t.tr.gameObject.SetActive(false);
        }
    }

    /// <summary>在指定位置噴出一次性玻璃碎片爆發 (程式生成，播完自動銷毀)</summary>
    private void SpawnShatterBurst(Transform tile)
    {
        Bounds b = new Bounds(tile.position, Vector3.one);
        SpriteRenderer sr = tile.GetComponentInChildren<SpriteRenderer>();
        if (sr != null) b = sr.bounds;

        GameObject burstObj = new GameObject("[GlassShatterBurst]");
        burstObj.transform.position = b.center;

        ParticleSystem ps = burstObj.AddComponent<ParticleSystem>();
        ps.Stop();

        var main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.duration = Mathf.Max(shardLifetime, 0.2f);
        main.startLifetime = new ParticleSystem.MinMaxCurve(shardLifetime * 0.6f, shardLifetime);
        main.startSpeed = new ParticleSystem.MinMaxCurve(shardSpeedRange.x, shardSpeedRange.y);
        main.startSize = new ParticleSystem.MinMaxCurve(shardSizeRange.x, shardSizeRange.y);
        main.startColor = shardColor;
        main.gravityModifier = shardGravity;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = Mathf.Max(shardCount, 1);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, (short)Mathf.Max(shardCount, 1)) });

        // 沿著地磚本身的寬度噴發，看起來才像整片碎開，而不是一個點爆炸
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(Mathf.Max(b.size.x, 0.3f), Mathf.Max(b.size.y * 0.5f, 0.15f), 0.1f);

        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-6f, 6f);

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] { new GradientColorKey(shardColor, 0f), new GradientColorKey(shardColor, 1f) },
            new GradientAlphaKey[] {
                new GradientAlphaKey(shardColor.a, 0f),
                new GradientAlphaKey(shardColor.a, 0.6f),
                new GradientAlphaKey(0f, 1f)
            });
        col.color = grad;

        var rend = ps.GetComponent<ParticleSystemRenderer>();
        if (rend != null)
        {
            // Stretch：碎片依飛行速度拉長，比正方塊更像玻璃尖片飛濺
            rend.renderMode = ParticleSystemRenderMode.Stretch;
            rend.lengthScale = 2.2f;
            rend.velocityScale = 0.08f;
            rend.sortingLayerName = sr != null ? sr.sortingLayerName : "Default";
            rend.sortingOrder = (sr != null ? sr.sortingOrder : 0) + 5;

            Shader s = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (s == null) s = Shader.Find("Particles/Standard Unlit");
            if (s == null) s = Shader.Find("Sprites/Default");
            if (s != null) rend.material = new Material(s);
        }

        ps.Play();
        Destroy(burstObj, shardLifetime + 0.5f);

        // 第二層：細碎粉塵，讓爆開的瞬間更有份量
        if (dustCount > 0) SpawnDustPuff(b, sr);

        // 第三層：爆開瞬間的白光閃一下
        if (flashIntensity > 0.01f) StartCoroutine(FlashRoutine(b, sr));
    }

    /// <summary>細碎粉塵層：慢速、擴散、淡出，補足爆炸的體積感</summary>
    private void SpawnDustPuff(Bounds b, SpriteRenderer sr)
    {
        GameObject dustObj = new GameObject("[GlassShatterDust]");
        dustObj.transform.position = b.center;

        ParticleSystem ps = dustObj.AddComponent<ParticleSystem>();
        ps.Stop();

        var main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 1.0f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 3f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.8f);
        main.startColor = new Color(shardColor.r, shardColor.g, shardColor.b, 0.35f);
        main.gravityModifier = 0.3f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = Mathf.Max(dustCount, 1);

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, (short)Mathf.Max(dustCount, 1)) });

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(Mathf.Max(b.size.x, 0.3f), Mathf.Max(b.size.y * 0.6f, 0.2f), 0.1f);

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.6f));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] { new GradientColorKey(shardColor, 0f), new GradientColorKey(shardColor, 1f) },
            new GradientAlphaKey[] { new GradientAlphaKey(0.5f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;

        var rend = ps.GetComponent<ParticleSystemRenderer>();
        if (rend != null)
        {
            rend.renderMode = ParticleSystemRenderMode.Billboard;
            rend.sortingLayerName = sr != null ? sr.sortingLayerName : "Default";
            rend.sortingOrder = (sr != null ? sr.sortingOrder : 0) + 4;

            Shader s = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (s == null) s = Shader.Find("Particles/Standard Unlit");
            if (s == null) s = Shader.Find("Sprites/Default");
            if (s != null) rend.material = new Material(s);
        }

        ps.Play();
        Destroy(dustObj, 2f);
    }

    /// <summary>爆開瞬間的白色閃光 (一個快速放大又消失的亮片)</summary>
    private IEnumerator FlashRoutine(Bounds b, SpriteRenderer sourceSr)
    {
        if (sourceSr == null || sourceSr.sprite == null) yield break;

        GameObject flashObj = new GameObject("[GlassShatterFlash]");
        flashObj.transform.position = b.center;
        flashObj.transform.rotation = sourceSr.transform.rotation;
        flashObj.transform.localScale = sourceSr.transform.lossyScale;

        SpriteRenderer fsr = flashObj.AddComponent<SpriteRenderer>();
        fsr.sprite = sourceSr.sprite;
        fsr.sortingLayerName = sourceSr.sortingLayerName;
        fsr.sortingOrder = sourceSr.sortingOrder + 6;

        float dur = 0.16f;
        Destroy(flashObj, dur + 0.2f); // 保險：即使協程被 StopAllCoroutines 中斷也不會殘留
        float t = 0f;
        Vector3 startScale = flashObj.transform.localScale;
        while (t < dur)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / dur);
            fsr.color = new Color(1f, 1f, 1f, (1f - p) * flashIntensity);
            flashObj.transform.localScale = startScale * (1f + p * 0.25f);
            yield return null;
        }

        Destroy(flashObj);
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
