using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// ★1118c 書頭怪每掉一隻手臂就多染一層血，掉完四隻＝跟 BookHeadMonster_withBlood 預製物一樣的貼圖。
///
/// 做法：
///   ・只改「材質實例」上的貼圖（Renderer.materials 會複製一份），不換 Material 物件、不動磁碟上的材質資產，
///     所以 SetVisualAlpha（MaterialPropertyBlock 的 _BaseColor）、受擊閃光、殘影、眼睛光暈等其他系統照舊運作。
///   ・血量 0＝原貼圖；0＜血量＜1＝用 Graphics.Blit 在 GPU 上把「原貼圖→染血貼圖」依比例混成一張 RenderTexture（sRGB、含 mipmap，最大 2048）
///     貼在 _BaseMap／_MainTex（法線、金屬度維持原本的）；血量 1＝直接用 withBlood 的反照率＋法線＋金屬度。
///   ・混合用的小 shader（Hidden/BookHeadBloodLerp）、染血材質、噴血材質都放在 Resources/MonsterBlood，打包時一定帶得進去。
///   ・同一個 Body 的子網格依材質名稱裡的 part_N 對應到 withBlood_part_N。
///   ・也負責噴血特效（噴在手臂斷口、再滴一陣子）和共用的碎屑材質（取代在建置時可能被剔除的 Shader.Find）。
/// 由 MonsterArmShedding 在準備好時自動掛上、驅動。
/// </summary>
[DisallowMultipleComponent]
public class MonsterBloodLook : MonoBehaviour
{
    [Tooltip("血量變化（例如 0.25→0.5）漸變花幾秒")]
    public float blendSeconds = 0.8f;
    [Tooltip("混合貼圖的最大邊長（最大 2048）")]
    public int maxBlendSize = 2048;
    [Tooltip("血的微弱自發光強度（0＝關）。場景只有很暗的環境光，怪物本來幾乎全黑，沒有這個染血貼圖看不出來；只有「有血的地方」會透出暗紅")]
    public float bloodGlow = 0.35f;

    private class Slot
    {
        public Renderer r;
        public int idx;
        public Material inst;
        public int part;
    }

    private class Part
    {
        public Texture origAlbedo, origBump, origMetal;
        public Texture bloodAlbedo, bloodBump, bloodMetal;
        public Material bloodAsset;
        public RenderTexture rt;
        public RenderTexture glowRt;
    }

    private static readonly int ID_BaseMap = Shader.PropertyToID("_BaseMap");
    private static readonly int ID_MainTex = Shader.PropertyToID("_MainTex");
    private static readonly int ID_BumpMap = Shader.PropertyToID("_BumpMap");
    private static readonly int ID_MetalMap = Shader.PropertyToID("_MetallicGlossMap");
    private static readonly int ID_BloodTex = Shader.PropertyToID("_BloodTex");
    private static readonly int ID_T = Shader.PropertyToID("_T");
    private static readonly int ID_Mode = Shader.PropertyToID("_Mode");
    private static readonly int ID_EmissionMap = Shader.PropertyToID("_EmissionMap");
    private static readonly int ID_EmissionColor = Shader.PropertyToID("_EmissionColor");

    private readonly List<Slot> _slots = new List<Slot>();
    private readonly Dictionary<Renderer, Material[]> _origShared = new Dictionary<Renderer, Material[]>();
    private readonly Part[] _parts = new Part[4];   // 1..3
    private Material _lerpMat;
    private bool _ready, _failed;
    private float _cur, _target;
    private int _state = -1;   // 0 原貼圖、1 混合、2 染血貼圖

    /// <summary>目前顯示的血量 0..1（測試／驗證用）。</summary>
    public float CurrentLevel => _cur;
    public bool IsReady => _ready;

    // ───────── 準備 ─────────
    public bool Prepare()
    {
        if (_ready) return true;
        if (_failed) return false;
        _lerpMat = Resources.Load<Material>("MonsterBlood/BookHeadBloodLerp");
        if (_lerpMat == null) { _failed = true; Debug.LogWarning("【血】★1118c 找不到 Resources/MonsterBlood/BookHeadBloodLerp 材質，怪物不染血"); return false; }

        var rx = new Regex(@"part_(\d)");
        foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (r == null) continue;
            Material[] shared = r.sharedMaterials;
            bool any = false;
            for (int i = 0; i < shared.Length; i++)
            {
                if (shared[i] == null) continue;
                var m = rx.Match(shared[i].name);
                if (!m.Success) continue;
                int part = int.Parse(m.Groups[1].Value);
                if (part < 1 || part > 3) continue;
                if (_parts[part] == null && !LoadPart(part, shared[i])) continue;
                any = true;
            }
            if (!any) continue;

            _origShared[r] = shared;
            Material[] inst = r.materials;   // 複製成這個 Renderer 專屬的材質實例
            for (int i = 0; i < inst.Length && i < shared.Length; i++)
            {
                if (shared[i] == null) continue;
                var m = rx.Match(shared[i].name);
                if (!m.Success) continue;
                int part = int.Parse(m.Groups[1].Value);
                if (part < 1 || part > 3 || _parts[part] == null) continue;
                _slots.Add(new Slot { r = r, idx = i, inst = inst[i], part = part });
            }
        }
        if (_slots.Count == 0) { _failed = true; Debug.LogWarning("【血】★1118c 沒找到 M_BookHeadMonster_part_N 材質，怪物不染血"); return false; }
        _ready = true;
        Debug.Log($"【血】★1118c 準備好：{_slots.Count} 個材質槽，{CountParts()} 個部位（混合貼圖最大 {maxBlendSize}）");
        return true;
    }

    private int CountParts() { int c = 0; for (int i = 1; i <= 3; i++) if (_parts[i] != null) c++; return c; }

    private bool LoadPart(int part, Material normalAsset)
    {
        Material blood = Resources.Load<Material>("MonsterBlood/BookHeadBlood_part_" + part);
        if (blood == null) { Debug.LogWarning($"【血】★1118c 找不到 Resources/MonsterBlood/BookHeadBlood_part_{part}"); return false; }
        var p = new Part { bloodAsset = blood };
        p.origAlbedo = normalAsset.GetTexture(ID_BaseMap);
        p.origBump = normalAsset.GetTexture(ID_BumpMap);
        p.origMetal = normalAsset.GetTexture(ID_MetalMap);
        p.bloodAlbedo = blood.GetTexture(ID_BaseMap);
        p.bloodBump = blood.GetTexture(ID_BumpMap);
        p.bloodMetal = blood.GetTexture(ID_MetalMap);
        if (p.origAlbedo == null || p.bloodAlbedo == null) return false;
        _parts[part] = p;
        return true;
    }

    // ───────── 對外 ─────────
    /// <summary>設定血量 0..1。animate＝在 blendSeconds 內漸變；否則立刻換。</summary>
    public void SetLevel(float level, bool animate)
    {
        if (!Prepare()) return;
        _target = Mathf.Clamp01(level);
        if (!animate || !isActiveAndEnabled) { _cur = _target; Apply(_cur); }
    }

    /// <summary>掉下去的手臂用的染血材質（資產本身，不會被改）。依 Renderer 目前的子網格 part_N 對應。</summary>
    public Material[] BloodSharedMaterials(Renderer r)
    {
        Material[] cur = r.sharedMaterials;
        var res = new Material[cur.Length];
        for (int i = 0; i < cur.Length; i++)
        {
            res[i] = cur[i];
            foreach (var s in _slots)
                if (s.r == r && s.idx == i) { res[i] = _parts[s.part].bloodAsset; break; }
        }
        return res;
    }

    private void Update()
    {
        if (!_ready || Mathf.Approximately(_cur, _target)) return;
        float step = Time.deltaTime * 0.25f / Mathf.Max(0.05f, blendSeconds);   // 每 blendSeconds 秒走 0.25（一隻手臂的血量）
        _cur = Mathf.MoveTowards(_cur, _target, step);
        Apply(_cur);
    }

    // ───────── 套用 ─────────
    private void Apply(float level)
    {
        if (level <= 0.0005f)
        {
            if (_state != 0) { foreach (var s in _slots) { SetTex(s, _parts[s.part].origAlbedo, _parts[s.part].origBump, _parts[s.part].origMetal); SetGlow(s, null); } ReleaseRTs(); _state = 0; }
            return;
        }
        if (level >= 0.9995f)
        {
            if (_state != 2)
            {
                BakeGlow(1f);
                foreach (var s in _slots) { SetTex(s, _parts[s.part].bloodAlbedo, _parts[s.part].bloodBump, _parts[s.part].bloodMetal); SetGlow(s, _parts[s.part].glowRt); }
                ReleaseAlbedoRTs(); _state = 2;
            }
            return;
        }
        for (int p = 1; p <= 3; p++)
        {
            Part pt = _parts[p];
            if (pt == null) continue;
            EnsureRT(pt);
            _lerpMat.SetTexture(ID_BloodTex, pt.bloodAlbedo);
            _lerpMat.SetFloat(ID_T, level);
            _lerpMat.SetFloat(ID_Mode, 0f);
            Graphics.Blit(pt.origAlbedo, pt.rt, _lerpMat);
        }
        BakeGlow(level);
        foreach (var s in _slots) { SetTex(s, _parts[s.part].rt, _parts[s.part].origBump, _parts[s.part].origMetal); SetGlow(s, _parts[s.part].glowRt); }
        _state = 1;
    }

    /// <summary>血的發光遮罩（1024 以內），強度跟血量成正比。</summary>
    private void BakeGlow(float level)
    {
        if (bloodGlow <= 0f) return;
        for (int p = 1; p <= 3; p++)
        {
            Part pt = _parts[p];
            if (pt == null) continue;
            if (pt.glowRt == null)
            {
                int size = Mathf.Clamp(Mathf.Min(1024, Mathf.Max(pt.origAlbedo.width, pt.origAlbedo.height)), 64, 1024);
                var g = new RenderTexture(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                g.name = "BookHeadBloodGlow"; g.useMipMap = true; g.autoGenerateMips = true; g.filterMode = FilterMode.Trilinear;
                g.hideFlags = HideFlags.DontUnloadUnusedAsset;
                g.Create();
                pt.glowRt = g;
            }
            _lerpMat.SetTexture(ID_BloodTex, pt.bloodAlbedo);
            _lerpMat.SetFloat(ID_T, level);
            _lerpMat.SetFloat(ID_Mode, 1f);
            Graphics.Blit(pt.origAlbedo, pt.glowRt, _lerpMat);
        }
        _lerpMat.SetFloat(ID_Mode, 0f);
    }

    private void SetGlow(Slot s, Texture mask)
    {
        if (s.inst == null || !s.inst.HasProperty(ID_EmissionColor)) return;
        if (mask == null || bloodGlow <= 0f)
        {
            s.inst.SetTexture(ID_EmissionMap, null);
            s.inst.SetColor(ID_EmissionColor, Color.black);
            s.inst.DisableKeyword("_EMISSION");
            return;
        }
        s.inst.SetTexture(ID_EmissionMap, mask);
        s.inst.SetColor(ID_EmissionColor, new Color(1f, 1f, 1f, 1f) * bloodGlow);
        s.inst.EnableKeyword("_EMISSION");
    }

    private void EnsureRT(Part pt)
    {
        if (pt.rt != null) return;
        int size = Mathf.Clamp(Mathf.Max(pt.origAlbedo.width, pt.origAlbedo.height), 64, Mathf.Clamp(maxBlendSize, 64, 2048));
        var rt = new RenderTexture(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        rt.name = "BookHeadBloodBlend";
        rt.useMipMap = true;
        rt.autoGenerateMips = true;
        rt.anisoLevel = 4;
        rt.filterMode = FilterMode.Trilinear;
        rt.wrapMode = TextureWrapMode.Repeat;
        rt.hideFlags = HideFlags.DontUnloadUnusedAsset;
        rt.Create();
        pt.rt = rt;
    }

    private void ReleaseAlbedoRTs()
    {
        for (int p = 1; p <= 3; p++)
        {
            var pt = _parts[p];
            if (pt == null || pt.rt == null) continue;
            pt.rt.Release(); Destroy(pt.rt); pt.rt = null;
        }
    }

    private void ReleaseRTs()
    {
        ReleaseAlbedoRTs();
        for (int p = 1; p <= 3; p++)
        {
            var pt = _parts[p];
            if (pt == null || pt.glowRt == null) continue;
            pt.glowRt.Release(); Destroy(pt.glowRt); pt.glowRt = null;
        }
    }

    private static void SetTex(Slot s, Texture albedo, Texture bump, Texture metal)
    {
        if (s.inst == null) return;
        s.inst.SetTexture(ID_BaseMap, albedo);
        s.inst.SetTexture(ID_MainTex, albedo);
        if (bump != null) s.inst.SetTexture(ID_BumpMap, bump);
        if (metal != null) s.inst.SetTexture(ID_MetalMap, metal);
    }

    // ───────── 噴血 ─────────
    /// <summary>在 at 噴一陣暗紅色血滴（outDir＝往外噴的方向）再滴一陣子。S＝怪物視覺縮放。</summary>
    public void SprayBlood(Vector3 at, Vector3 outDir, float S)
    {
        Material fx = FxMaterial();
        if (fx == null) return;
        if (outDir.sqrMagnitude < 1e-6f) outDir = Vector3.up;
        outDir.Normalize();
        int layer = gameObject.layer;
        int order = 60;
        int sortLayer = 0;
        foreach (var s in _slots) { order = s.r.sortingOrder + 60; sortLayer = s.r.sortingLayerID; break; }

        var root = new GameObject("噴血（自動生成）");
        root.layer = layer;
        root.transform.position = at;
        Destroy(root, 4f);

        // 1) 一次噴出去的血滴（拉長成短線，看得出方向）
        var burst = MakePS(root.transform, "噴", fx, order, sortLayer, Quaternion.LookRotation(outDir));
        var main = burst.main;
        main.loop = false; main.playOnAwake = false; main.duration = 0.4f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 1.15f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.45f * S, 1.6f * S);
        main.startSize = new ParticleSystem.MinMaxCurve(0.012f * S, 0.034f * S);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.36f, 0.02f, 0.02f, 1f), new Color(0.2f, 0.01f, 0.012f, 1f));
        main.gravityModifier = 1.5f * S / 9.81f;
        main.maxParticles = 200;
        var em = burst.emission; em.rateOverTime = 0f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, 70, 90, 1, 0.05f) });
        var sh = burst.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 38f; sh.radius = 0.03f * S;
        var psr = burst.GetComponent<ParticleSystemRenderer>();
        psr.renderMode = ParticleSystemRenderMode.Stretch; psr.velocityScale = 0.018f; psr.lengthScale = 1.3f;
        var colA = burst.colorOverLifetime; colA.enabled = true; colA.color = FadeOutGradient(0.7f);

        // 2) 細霧（更小更多，散得更開）
        var mist = MakePS(root.transform, "霧", fx, order, sortLayer, Quaternion.LookRotation(outDir));
        var mm = mist.main;
        mm.loop = false; mm.playOnAwake = false; mm.duration = 0.3f;
        mm.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
        mm.startSpeed = new ParticleSystem.MinMaxCurve(0.2f * S, 0.9f * S);
        mm.startSize = new ParticleSystem.MinMaxCurve(0.006f * S, 0.016f * S);
        mm.startColor = new ParticleSystem.MinMaxGradient(new Color(0.4f, 0.03f, 0.03f, 0.9f), new Color(0.24f, 0.015f, 0.015f, 0.9f));
        mm.gravityModifier = 1.2f * S / 9.81f;
        mm.maxParticles = 120;
        var em2 = mist.emission; em2.rateOverTime = 0f;
        em2.SetBursts(new[] { new ParticleSystem.Burst(0f, 40, 60) });
        var sh2 = mist.shape; sh2.enabled = true; sh2.shapeType = ParticleSystemShapeType.Cone; sh2.angle = 65f; sh2.radius = 0.05f * S;
        var colB = mist.colorOverLifetime; colB.enabled = true; colB.color = FadeOutGradient(0.5f);

        // 3) 之後約 1 秒的滴落
        var drip = MakePS(root.transform, "滴", fx, order, sortLayer, Quaternion.LookRotation(Vector3.down));
        var dm = drip.main;
        dm.loop = false; dm.playOnAwake = false; dm.duration = 1.0f;
        dm.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.2f);
        dm.startSpeed = new ParticleSystem.MinMaxCurve(0.02f * S, 0.22f * S);
        dm.startSize = new ParticleSystem.MinMaxCurve(0.012f * S, 0.026f * S);
        dm.startColor = new ParticleSystem.MinMaxGradient(new Color(0.34f, 0.02f, 0.02f, 1f), new Color(0.2f, 0.01f, 0.012f, 1f));
        dm.gravityModifier = 1.4f * S / 9.81f;
        dm.maxParticles = 60;
        var em3 = drip.emission; em3.rateOverTime = 22f;
        var sh3 = drip.shape; sh3.enabled = true; sh3.shapeType = ParticleSystemShapeType.Sphere; sh3.radius = 0.05f * S;
        var psr3 = drip.GetComponent<ParticleSystemRenderer>();
        psr3.renderMode = ParticleSystemRenderMode.Stretch; psr3.velocityScale = 0.012f; psr3.lengthScale = 1.6f;
        var colC = drip.colorOverLifetime; colC.enabled = true; colC.color = FadeOutGradient(0.8f);

        burst.Play(); mist.Play(); drip.Play();
    }

    private static ParticleSystem MakePS(Transform parent, string name, Material mat, int order, int sortLayer, Quaternion rot)
    {
        var go = new GameObject(name);
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        go.transform.rotation = rot;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.sortingOrder = order;
        r.sortingLayerID = sortLayer;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return ps;
    }

    private static Gradient FadeOutGradient(float fadeStart)
    {
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, fadeStart), new GradientAlphaKey(0f, 1f) });
        return g;
    }

    // ───────── 共用的特效材質（取代 Shader.Find，打包時不會被剔除）─────────
    private static Material _fxMat;
    private static Texture2D _dot;

    /// <summary>碎屑／血滴用的材質：Resources/MonsterBlood/MonsterGoreFX（Sprites/Default）加一張程式生成的軟圓點。</summary>
    public static Material FxMaterial()
    {
        if (_fxMat != null) return _fxMat;
        Material src = Resources.Load<Material>("MonsterBlood/MonsterGoreFX");
        if (src != null) _fxMat = new Material(src);
        else
        {
            Shader sh = Shader.Find("Sprites/Default");
            if (sh == null) return null;
            _fxMat = new Material(sh);
        }
        _fxMat.name = "MonsterGoreFX（執行時）";
        _fxMat.mainTexture = Dot();
        _fxMat.hideFlags = HideFlags.DontUnloadUnusedAsset;
        return _fxMat;
    }

    private static Texture2D Dot()
    {
        if (_dot != null) return _dot;
        const int N = 32;
        var t = new Texture2D(N, N, TextureFormat.RGBA32, false);
        t.wrapMode = TextureWrapMode.Clamp;
        var px = new Color32[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(N * 0.5f, N * 0.5f)) / (N * 0.5f);
                float a = Mathf.Clamp01((1f - d) / 0.35f);
                px[y * N + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        t.SetPixels32(px);
        t.Apply(false);
        t.hideFlags = HideFlags.DontUnloadUnusedAsset;
        _dot = t;
        return t;
    }

    // ───────── 收尾 ─────────
    private void OnDestroy()
    {
        ReleaseRTs();
        foreach (var kv in _origShared) if (kv.Key != null) kv.Key.sharedMaterials = kv.Value;
        foreach (var s in _slots) if (s.inst != null) Destroy(s.inst);
        _slots.Clear();
    }
}
