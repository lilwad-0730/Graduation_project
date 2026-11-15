using System.Collections;
using UnityEngine;

/// <summary>
/// ★1116 玻璃館「鏡光反擊」（candle_01 (3) 這一段）。掛在玻璃牆上。
///
/// 原本：燭台在玻璃牆前，翻牆的小平台在燭台「後面」（怪物那一側）——點完要往回走、迎面撞上怪物，很容易被抓。
/// 現在這一段：
///   ・玻璃牆太高，跳不過去；前面地上有一個可以推的「鏡箱」，要推到牆邊當墊腳石才翻得過去。
///   ・鏡箱前面有燭台。點亮的瞬間，燭光打到斜放的玻璃牆、反射成一道光束射向身後的怪物：
///     怪物被震退、定住幾秒——這幾秒就是推鏡箱、翻牆的時間。
///   ・先推箱子再點燭也可以，但那時怪物已經貼很近，點燭的 1.8 秒會很刺激。
/// 這支負責：點亮那一刻牆面亮起＋爆光＋光束（燭台 → 牆 → 怪物）＋讓怪物定住。
/// 燭火留空會自動找離牆 20 公尺內最近的燭火。
/// </summary>
public class MirrorWallCandleReflect : MonoBehaviour
{
    [Tooltip("要映在這面牆上的燭火（留空＝自動找 20 公尺內最近的一根）")]
    public CandleAuraGlow candle;

    [Header("牆面閃光")]
    public float flashIntensity = 30f;
    public float flashRange = 22f;
    [Tooltip("牆亮起來再退回去要幾秒")]
    public float glowSeconds = 1.2f;
    public Color glowColor = new Color(1f, 0.9f, 0.65f, 1f);

    [Header("反射光束")]
    [Tooltip("光束從燭火射到牆、再射回怪物，各花幾秒")]
    public float beamTravelSeconds = 0.18f;
    [Tooltip("光束射到怪物之後留多久再淡掉")]
    public float beamHoldSeconds = 0.6f;
    public float beamFadeSeconds = 0.5f;
    public float beamWidth = 0.45f;
    public Color beamColor = new Color(1f, 0.93f, 0.7f, 1f);
    [Tooltip("光束射回去時離地多高（相對燭火的火焰），要高過鏡箱")]
    public float beamReturnHeight = 2.5f;

    [Header("打中怪物")]
    [Tooltip("怪物被定住幾秒")]
    public float stunSeconds = 4f;
    [Tooltip("怪物被震退幾公尺")]
    public float stunKnockback = 10f;

    private SpriteRenderer _sr;
    private Color _baseColor;
    private bool _wasLit;
    private float _glowStart = -999f;
    private LineRenderer _beam;

    private void Start()
    {
        _sr = GetComponent<SpriteRenderer>();
        if (_sr != null) _baseColor = _sr.color;
        if (candle == null)
        {
            float best = 20f;
            foreach (CandleAuraGlow c in FindObjectsByType<CandleAuraGlow>(FindObjectsInactive.Include))
            {
                float d = Vector2.Distance(c.transform.position, transform.position);
                if (d < best) { best = d; candle = c; }
            }
        }
        _wasLit = candle != null && candle.IsLit;
    }

    private void Update()
    {
        if (candle == null) return;
        bool lit = candle.IsLit;
        if (lit && !_wasLit) StartCoroutine(ReflectRoutine());
        _wasLit = lit;   // 重生後燭火變回沒點亮，下次點亮會再來一次

        if (_sr == null) return;
        float t = (Time.time - _glowStart) / Mathf.Max(0.05f, glowSeconds);
        if (t >= 0f && t < 1f) _sr.color = Color.Lerp(glowColor, _baseColor, t * t);
        else if (t >= 1f && t < 1.5f) _sr.color = _baseColor;
    }

    private void OnDisable()
    {
        if (_beam != null) _beam.enabled = false;
        if (_sr != null && _glowStart > 0f) _sr.color = _baseColor;
    }

    private IEnumerator ReflectRoutine()
    {
        ShadowMonsterController monster = ShadowMonsterController.Instance;
        Vector3 flame = candle.transform.position + new Vector3(0f, 1.6f, 0f);
        flame.z = -1f;
        Vector3 hitWall = new Vector3(transform.position.x - 0.25f, flame.y + beamReturnHeight * 0.5f, -1f);
        float dir = monster != null && monster.transform.position.x > transform.position.x ? 1f : -1f;

        // 1. 燭火 → 牆
        EnsureBeam();
        _beam.enabled = true;
        _beam.positionCount = 2;
        SetBeamAlpha(1f);
        for (float t = 0f; t < beamTravelSeconds; t += Time.deltaTime)
        {
            _beam.SetPosition(0, flame);
            _beam.SetPosition(1, Vector3.Lerp(flame, hitWall, t / beamTravelSeconds));
            yield return null;
        }

        // 2. 牆亮起來、爆光
        _glowStart = Time.time;
        if (flashIntensity > 0f)
        {
            GameObject go = new GameObject("MirrorWallFlash");
            go.transform.position = transform.position + new Vector3(0f, 1f, -3f);
            Light l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = glowColor;
            l.range = Mathf.Max(1f, flashRange);
            l.intensity = flashIntensity;
            go.AddComponent<FadeAndDestroyLight>().duration = glowSeconds;
        }

        // 3. 牆 → 怪物（射到畫面外也照樣射，終點跟著怪物）
        _beam.positionCount = 3;
        _beam.SetPosition(0, flame);
        _beam.SetPosition(1, hitWall);
        Vector3 returnStart = hitWall;
        for (float t = 0f; t < beamTravelSeconds; t += Time.deltaTime)
        {
            Vector3 end = BeamEnd(monster, flame, dir);
            _beam.SetPosition(2, Vector3.Lerp(returnStart, end, t / beamTravelSeconds));
            yield return null;
        }

        bool hit = monster != null && monster.StunByMirrorLight(stunSeconds, stunKnockback);
        Debug.Log($"【鏡光反擊】{candle.name} 點亮：燭光經 {name} 反射 → {(hit ? "打中怪物" : "怪物不在追逐中，只有光")}");

        // 4. 留一下、淡掉
        for (float t = 0f; t < beamHoldSeconds; t += Time.deltaTime)
        {
            _beam.SetPosition(2, BeamEnd(monster, flame, dir));
            yield return null;
        }
        for (float t = 0f; t < beamFadeSeconds; t += Time.deltaTime)
        {
            _beam.SetPosition(2, BeamEnd(monster, flame, dir));
            SetBeamAlpha(1f - t / beamFadeSeconds);
            yield return null;
        }
        _beam.enabled = false;
    }

    /// <summary>光束射回去的終點：怪物的位置（找不到怪物就往牆的反方向射 40 公尺）。高度保持在火焰上方，越過鏡箱。</summary>
    private Vector3 BeamEnd(ShadowMonsterController monster, Vector3 flame, float dir)
    {
        float y = flame.y + beamReturnHeight;
        if (monster != null) return new Vector3(monster.transform.position.x, y, -1f);
        return new Vector3(transform.position.x + dir * 40f, y, -1f);
    }

    private void EnsureBeam()
    {
        if (_beam != null) return;
        GameObject go = new GameObject("MirrorLightBeam");
        go.transform.SetParent(transform, false);
        _beam = go.AddComponent<LineRenderer>();
        _beam.useWorldSpace = true;
        _beam.numCapVertices = 4;
        _beam.numCornerVertices = 2;
        _beam.widthCurve = new AnimationCurve(new Keyframe(0f, beamWidth * 0.5f), new Keyframe(0.5f, beamWidth), new Keyframe(1f, beamWidth * 0.7f));
        // 跟牆用同一個 Sprite 材質（專案一定有、打包也會帶），沒有就退回內建的 Sprites/Default
        Material m = _sr != null ? _sr.sharedMaterial : null;
        if (m == null) { Shader s = Shader.Find("Sprites/Default"); if (s != null) m = new Material(s); }
        if (m != null) _beam.sharedMaterial = m;
        if (_sr != null) { _beam.sortingLayerID = _sr.sortingLayerID; _beam.sortingOrder = _sr.sortingOrder + 5; }
        _beam.enabled = false;
    }

    private void SetBeamAlpha(float a)
    {
        if (_beam == null) return;
        Color c0 = beamColor; c0.a = a;
        Color c1 = beamColor; c1.a = a * 0.6f;
        _beam.startColor = c0;
        _beam.endColor = c1;
    }
}
