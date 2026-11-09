using System.Collections;
using UnityEngine;

/// <summary>
/// ★1109j 鳥群個體差異：一整群鳥大小、顏色、拍翅節奏一模一樣，看起來像壁紙。
/// 每隻鳥（含增援複本）各自：
///   ‧ 大小 ±scaleJitter（預設 10%，均勻縮放；命中判定看渲染器外框，會自動跟著縮放）
///   ‧ 亮度 ±brightnessJitter（預設 8%）：乘在身體材質的 _BaseColor／_Color RGB 上。
///     刻意不用 MaterialPropertyBlock——IndividualBirdEnemy.SetAlpha（死亡／消失淡出）是直接改材質顏色的 alpha，
///     MPB 的顏色會蓋掉它、淡出就失效；直接改材質實例的 RGB 不影響 alpha，兩邊相容。
///   ‧ 動畫速度 ±animSpeedJitter（預設 10%）：IndividualBirdEnemy.PlayAnim 會把 Animator.speed 重設成 1，
///     所以這裡每個 LateUpdate 補一次（只在速度剛好是 1 時；暫停＝0、慢動作＝0.5 不會動它）
///   ‧ 開場拍翅起始相位（Animator normalizedTime 偏移）
/// 亂數由「物件名稱＋出生位置」雜湊決定：同一隻鳥每次進遊戲長一樣，增援複本因為位置不同各自不同。
/// 不改任何玩法數值；眼睛（BirdHostileEyes 的 SpriteRenderer）不動。
/// 由 IndividualBirdEnemy.Awake 自動加上，場景裡不需要手動掛。遠景裝飾鳥群之後另外做，這裡不讓玩法鳥變小或變淡（避免誤導危險距離）。
/// </summary>
[DisallowMultipleComponent]
public class BirdVariation : MonoBehaviour
{
    [Range(0f, 0.3f)] public float scaleJitter = 0.10f;
    [Range(0f, 0.3f)] public float brightnessJitter = 0.08f;
    [Range(0f, 0.3f)] public float animSpeedJitter = 0.10f;
    [Tooltip("開場是否隨機拍翅相位")]
    public bool randomizeAnimPhase = true;

    // Instantiate 複本會把這兩個序列化欄位一起帶走，所以複本不會把「父鳥已經縮放過的 scale」當成基準
    [SerializeField, HideInInspector] private Vector3 baseScale;
    [SerializeField, HideInInspector] private bool baseSet;
    // 同理：原始（沒調過亮度的）共用材質，依 renderer 順序攤平存起來；複本用它當亮度基準，不會在父鳥已調過的材質上再疊一次
    [SerializeField, HideInInspector] private Material[] origFlat;

    private float _speedMult = 1f;
    private float _phase;
    private Animator[] _anims;
    private Renderer[] _rends;
    private Material[][] _origMats;
    private Material[][] _instMats;
    private IndividualBirdEnemy _bird;
    private bool _applied;

    /// <summary>目前這隻鳥的亮度倍率／大小倍率（除錯、截圖用）</summary>
    public float BrightnessMult { get; private set; }
    public float ScaleMult { get; private set; }

    private void Awake() { Apply(); }

    /// <summary>套用大小、亮度、動畫速度（可重複呼叫，不會疊加）。</summary>
    public void Apply()
    {
        if (_applied) Revert();
        if (!baseSet) { baseScale = transform.localScale; baseSet = true; }

        uint seed = Hash(name, transform.position);
        float rScale = Hash01(seed, 1), rBright = Hash01(seed, 2), rSpeed = Hash01(seed, 3);
        _phase = Hash01(seed, 4);

        ScaleMult = 1f + (rScale * 2f - 1f) * scaleJitter;
        BrightnessMult = 1f + (rBright * 2f - 1f) * brightnessJitter;
        _speedMult = 1f + (rSpeed * 2f - 1f) * animSpeedJitter;

        transform.localScale = baseScale * ScaleMult;

        _bird = GetComponent<IndividualBirdEnemy>();
        _anims = GetComponentsInChildren<Animator>(true);

        // 亮度：只動 3D 身體（SkinnedMeshRenderer／MeshRenderer），不動眼睛的 SpriteRenderer、特效
        _rends = GetComponentsInChildren<Renderer>(true);
        _origMats = new Material[_rends.Length][];
        _instMats = new Material[_rends.Length][];
        bool haveFlat = origFlat != null && origFlat.Length > 0;
        System.Collections.Generic.List<Material> flat = new System.Collections.Generic.List<Material>();
        int fi = 0;
        for (int i = 0; i < _rends.Length; i++)
        {
            Renderer r = _rends[i];
            if (!(r is SkinnedMeshRenderer) && !(r is MeshRenderer)) continue;
            Material[] current = r.sharedMaterials;
            Material[] shared = new Material[current.Length];
            for (int m = 0; m < current.Length; m++)
            {
                shared[m] = (haveFlat && fi < origFlat.Length) ? origFlat[fi] : current[m];
                flat.Add(shared[m]);
                fi++;
            }
            Material[] inst = new Material[shared.Length];
            for (int m = 0; m < shared.Length; m++)
            {
                if (shared[m] == null) continue;
                inst[m] = new Material(shared[m]) { name = shared[m].name + " (bird var)" };
                Tint(inst[m], "_BaseColor");
                Tint(inst[m], "_Color");
            }
            _origMats[i] = shared;
            _instMats[i] = inst;
            r.sharedMaterials = inst;
        }
        if (!haveFlat) origFlat = flat.ToArray();
        _applied = true;
    }

    private void Tint(Material m, string prop)
    {
        if (!m.HasProperty(prop)) return;
        Color c = m.GetColor(prop);
        m.SetColor(prop, new Color(Mathf.Clamp01(c.r * BrightnessMult), Mathf.Clamp01(c.g * BrightnessMult), Mathf.Clamp01(c.b * BrightnessMult), c.a));
    }

    private void Start()
    {
        if (randomizeAnimPhase && Application.isPlaying) StartCoroutine(PhaseNextFrame());
    }

    // IndividualBirdEnemy.Start 會用 Random.value 排一次起始相位，增援複本則是 PlayAnim 從頭播；
    // 這裡等一幀後再用這隻鳥自己的雜湊值重排一次（還在待機才排，已經進入攻擊的不打斷）
    private IEnumerator PhaseNextFrame()
    {
        yield return null;
        if (_bird != null && _bird.IsHostile) yield break;
        ApplyPhase();
    }

    /// <summary>把所有 Animator 目前的 state 跳到這隻鳥專屬的 normalizedTime 相位。</summary>
    public void ApplyPhase()
    {
        if (_anims == null) return;
        for (int i = 0; i < _anims.Length; i++)
        {
            Animator a = _anims[i];
            if (a == null || !a.isActiveAndEnabled || a.runtimeAnimatorController == null) continue;
            AnimatorStateInfo st = a.GetCurrentAnimatorStateInfo(0);
            a.Play(st.fullPathHash, 0, (_phase + i * 0.37f) % 1f);
        }
    }

    /// <summary>編輯模式預覽用：不需要等一幀，立刻排相位並評估一次動畫。</summary>
    public void ApplyPhaseAndEvaluate()
    {
        ApplyPhase();
        if (_anims == null) return;
        foreach (Animator a in _anims) if (a != null && a.isActiveAndEnabled) a.Update(0f);
    }

    private void LateUpdate()
    {
        if (_anims == null) return;
        if (Mathf.Approximately(_speedMult, 1f)) return;
        for (int i = 0; i < _anims.Length; i++)
        {
            Animator a = _anims[i];
            if (a != null && Mathf.Approximately(a.speed, 1f)) a.speed = _speedMult;
        }
    }

    /// <summary>還原大小與材質（編輯模式預覽收尾、或元件被移除時）。</summary>
    public void Revert()
    {
        if (!_applied) return;
        if (baseSet) transform.localScale = baseScale;
        if (_rends != null)
        {
            for (int i = 0; i < _rends.Length; i++)
            {
                if (_rends[i] != null && _origMats[i] != null) _rends[i].sharedMaterials = _origMats[i];
                if (_instMats[i] != null) foreach (Material m in _instMats[i]) if (m != null) DestroyObj(m);
            }
        }
        _origMats = null; _instMats = null;
        _applied = false;
    }

    private void OnDestroy()
    {
        // 遊戲中鳥被銷毀／場景結束時，材質實例交給引擎清；編輯模式（預覽）才主動還原
        if (!Application.isPlaying) Revert();
        else if (_instMats != null)
            foreach (Material[] arr in _instMats) if (arr != null) foreach (Material m in arr) if (m != null) Destroy(m);
    }

    private static void DestroyObj(Object o)
    {
        if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
    }

    // ── 雜湊（FNV-1a）：不用 string.GetHashCode，它在不同平台／版本不保證一樣 ──
    private static uint Hash(string s, Vector3 p)
    {
        uint h = 2166136261u;
        for (int i = 0; i < s.Length; i++) { h ^= s[i]; h *= 16777619u; }
        h ^= (uint)Mathf.RoundToInt(p.x * 100f); h *= 16777619u;
        h ^= (uint)Mathf.RoundToInt(p.y * 100f); h *= 16777619u;
        return h;
    }

    private static float Hash01(uint seed, uint salt)
    {
        uint h = seed ^ (salt * 0x9E3779B1u);
        h ^= h >> 16; h *= 0x85EBCA6Bu; h ^= h >> 13; h *= 0xC2B2AE35u; h ^= h >> 16;
        return (h & 0xFFFFFF) / (float)0x1000000;
    }
}
