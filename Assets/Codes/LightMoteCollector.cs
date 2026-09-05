using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ★0905 廢墟光絮的存量與她身上的暖光（0904 定案 #3）。
/// 掛在主角身上（由 RuinsMoteDirector 自動掛，不動 Prefab）：
///   ・存量＝收到幾顆；沒有數字、沒有進度條——她身上的暖金光暈愈亮＝收得愈多（GDD §5「顯示」、§16 無 HUD）
///   ・被狼咬住時掉約 1/3，散在附近繼續漂（WolfEnemy.AttachToPlayer 補一行呼叫 NotifyWolfAttached）
///   ・收滿之前拉桿拉不動：按 E 只會晃一下＋悶響（LeverSystem 補兩行呼叫 IsLeverLocked／NotifyLeverStuck）
///   ・收滿一次就永久解鎖（之後被咬掉只影響光暈，不再鎖回去——不做挫折迴圈）
/// 暖光是暖金（#F1CA81 一路），不是冷藍——冷藍是「他」的顏色（GDD §11）。
/// </summary>
[DisallowMultipleComponent]
public class LightMoteCollector : MonoBehaviour
{
    public static LightMoteCollector Instance { get; private set; }

    [Header("存量")]
    public int required = 6;
    public int count = 0;
    [Tooltip("被狼咬住掉多少比例（GDD：約 1/3）")]
    [Range(0f, 1f)] public float dropFraction = 0.34f;
    [Tooltip("兩次掉落之間的冷卻（狼一直咬不會一直掉）")]
    public float dropCooldown = 2.5f;
    [Tooltip("收滿一次就永久解鎖拉桿")]
    public bool unlockOnceForever = true;

    [Header("暖光（她身上的光暈＝存量）")]
    public Color auraColor = new Color(0.95f, 0.79f, 0.50f, 1f);
    public float auraAlphaEmpty = 0.06f;
    public float auraAlphaFull = 0.62f;
    public float auraScaleEmpty = 2.6f;
    public float auraScaleFull = 4.6f;
    public Vector3 auraLocalOffset = new Vector3(0f, 1.1f, 0.3f);   // z 往後一點：光在她身後
    public int auraSortingOrder = 3;

    [Header("音（可留空；留空會借用場景裡 GuidanceLight 的 absorbSFX）")]
    public AudioClip collectClip;
    [Range(0f, 1f)] public float collectVolume = 0.6f;

    [Header("拉桿")]
    [Tooltip("要被光絮鎖住的拉桿（由 Director 指定；空＝不鎖任何拉桿）")]
    public LeverSystem gatedLever;

    public bool IsFull => count >= required;
    public bool IsUnlocked => _unlockedOnce || IsFull;
    public event System.Action<int, int> OnCountChanged;   // (count, required)
    public event System.Action OnFull;

    private bool _unlockedOnce = false;
    private float _lastDropTime = -99f;
    private float _flare = 0f;
    private SpriteRenderer _aura;
    private Transform _auraT;
    private float _auraLevel = 0f;
    private AudioSource _audio;
    private AudioClip _thud;
    private Texture2D _auraTex;
    private Sprite _auraSprite;
    private Coroutine _stuckShake;

    private void Awake()
    {
        Instance = this;
        BuildAura();
        _audio = gameObject.AddComponent<AudioSource>();
        _audio.playOnAwake = false;
        _audio.spatialBlend = 0f;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (_auraSprite != null) Destroy(_auraSprite);
        if (_auraTex != null) Destroy(_auraTex);
        if (_thud != null) Destroy(_thud);
    }

    private void Update()
    {
        float fill = required > 0 ? Mathf.Clamp01((float)count / required) : 0f;
        _auraLevel = Mathf.MoveTowards(_auraLevel, fill, Time.deltaTime * 0.8f);
        _flare = Mathf.MoveTowards(_flare, 0f, Time.deltaTime * 1.5f);

        if (_aura != null)
        {
            float pulse = 1f + 0.05f * Mathf.Sin(Time.time * 2.0f);
            float a = Mathf.Lerp(auraAlphaEmpty, auraAlphaFull, _auraLevel) + _flare * 0.35f;
            Color c = auraColor; c.a = Mathf.Clamp01(a);
            _aura.color = c;
            float sc = Mathf.Lerp(auraScaleEmpty, auraScaleFull, _auraLevel) * pulse * (1f + _flare * 0.25f);
            _auraT.localScale = new Vector3(sc, sc, 1f);
            _auraT.localPosition = auraLocalOffset;
        }
    }

    // ── 被 LightMote 呼叫 ─────────────────────────────────────────
    public void OnMoteCollected(LightMote mote)
    {
        count++;
        _flare = Mathf.Max(_flare, 0.6f);
        if (collectClip == null)
        {
            GuidanceLight gl = FindFirstObjectByType<GuidanceLight>();
            if (gl != null) collectClip = gl.absorbSFX;
        }
        if (collectClip != null && _audio != null) _audio.PlayOneShot(collectClip, collectVolume * AudioManager.SfxVolume);

        if (OnCountChanged != null) OnCountChanged(count, required);
        if (IsFull && !_unlockedOnce)
        {
            _unlockedOnce = unlockOnceForever;
            _flare = 1f;
            Debug.Log("[LightMoteCollector] 光絮收滿（" + count + "/" + required + "）——拉桿解鎖。Q3-S7 路開了。");
            if (OnFull != null) OnFull();
        }
    }

    // ── 狼咬住：掉 1/3 ────────────────────────────────────────────
    /// <summary>WolfEnemy.AttachToPlayer 補的一行。沒有 Collector 時什麼都不做。</summary>
    public static void NotifyWolfAttached()
    {
        if (Instance != null) Instance.DropSome();
    }

    public void DropSome()
    {
        if (count <= 0) return;
        if (IsUnlocked && unlockOnceForever && _unlockedOnce) return;   // 已經解鎖就不再掉（不做挫折迴圈）
        if (Time.time - _lastDropTime < dropCooldown) return;
        _lastDropTime = Time.time;

        int n = Mathf.RoundToInt(count * dropFraction);
        if (n < 1) n = 1;
        if (n > count) n = count;
        count -= n;
        _flare = 0f;
        Debug.Log("[LightMoteCollector] 被狼咬住，掉了 " + n + " 顆光絮（剩 " + count + "/" + required + "）。");

        Vector3 origin = transform.position + Vector3.up * 1.2f;
        for (int i = 0; i < n; i++)
        {
            float side = (i % 2 == 0) ? 1f : -1f;
            float vx = side * Random.Range(2.0f, 3.6f);
            float vy = Random.Range(3.0f, 4.5f);
            LightMote m = LightMote.Spawn(origin, this, true);
            m.LaunchDropped(new Vector3(vx, vy, 0f));
        }
        if (OnCountChanged != null) OnCountChanged(count, required);
    }

    // ── 拉桿鎖 ───────────────────────────────────────────────────
    /// <summary>LeverSystem 補的一行：這支拉桿現在拉不拉得動。沒有 Collector／沒被指定＝拉得動。</summary>
    public static bool IsLeverLocked(LeverSystem lever)
    {
        if (Instance == null || lever == null) return false;
        if (Instance.gatedLever != lever) return false;
        return !Instance.IsUnlocked;
    }

    /// <summary>拉不動時的回饋：拉桿晃一下＋悶響。</summary>
    public static void NotifyLeverStuck(LeverSystem lever)
    {
        if (Instance == null || lever == null) return;
        Instance.PlayStuck(lever);
    }

    private void PlayStuck(LeverSystem lever)
    {
        if (_thud == null) _thud = BuildThud();
        if (_audio != null && _thud != null) _audio.PlayOneShot(_thud, 0.8f * AudioManager.SfxVolume);
        Transform t = lever.leverRenderer != null ? lever.leverRenderer.transform : lever.transform;
        if (_stuckShake != null) StopCoroutine(_stuckShake);
        _stuckShake = StartCoroutine(ShakeRoutine(t));
        Debug.Log("[LightMoteCollector] 拉桿拉不動：光絮 " + count + "/" + required + "。");
    }

    private IEnumerator ShakeRoutine(Transform t)
    {
        Quaternion baseRot = t.localRotation;
        float dur = 0.35f, el = 0f;
        while (el < dur)
        {
            el += Time.deltaTime;
            float p = el / dur;
            float ang = Mathf.Sin(p * Mathf.PI * 5f) * 5f * (1f - p);
            t.localRotation = baseRot * Quaternion.Euler(0f, 0f, ang);
            yield return null;
        }
        t.localRotation = baseRot;
        _stuckShake = null;
    }

    // ── 建構 ─────────────────────────────────────────────────────
    private void BuildAura()
    {
        int n = 256;
        _auraTex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        _auraTex.name = "Procedural_MoteAura";
        _auraTex.hideFlags = HideFlags.HideAndDontSave;
        _auraTex.wrapMode = TextureWrapMode.Clamp;
        Color32[] px = new Color32[n * n];
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n * 2f - 1f;
                float v = (y + 0.5f) / n * 2f - 1f;
                float r = Mathf.Clamp01(Mathf.Sqrt(u * u + v * v));
                float a = Mathf.SmoothStep(1f, 0f, r);
                a = a * a;                                   // 高斯感：中心實、邊緣散
                px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255f * a));
            }
        }
        _auraTex.SetPixels32(px);
        _auraTex.Apply();
        _auraSprite = Sprite.Create(_auraTex, new Rect(0f, 0f, n, n), new Vector2(0.5f, 0.5f), 100f);
        _auraSprite.hideFlags = HideFlags.HideAndDontSave;

        GameObject go = new GameObject("LightMoteAura (自動生成)");
        _auraT = go.transform;
        _auraT.SetParent(transform, false);
        _auraT.localPosition = auraLocalOffset;
        _aura = go.AddComponent<SpriteRenderer>();
        _aura.sprite = _auraSprite;
        _aura.sortingOrder = auraSortingOrder;
        Color c = auraColor; c.a = auraAlphaEmpty;
        _aura.color = c;
        _auraT.localScale = new Vector3(auraScaleEmpty, auraScaleEmpty, 1f);
    }

    /// <summary>拉桿卡住的悶響：70Hz 短促正弦＋一點噪音，0.18 秒。</summary>
    private static AudioClip BuildThud()
    {
        int rate = 22050;
        int len = Mathf.RoundToInt(rate * 0.18f);
        float[] data = new float[len];
        System.Random rng = new System.Random(905);
        for (int i = 0; i < len; i++)
        {
            float t = (float)i / rate;
            float env = Mathf.Exp(-t * 22f);
            float s = Mathf.Sin(2f * Mathf.PI * 70f * t) * 0.8f + ((float)rng.NextDouble() * 2f - 1f) * 0.15f * Mathf.Exp(-t * 60f);
            data[i] = Mathf.Clamp(s * env, -1f, 1f);
        }
        AudioClip clip = AudioClip.Create("Procedural_LeverThud", len, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
