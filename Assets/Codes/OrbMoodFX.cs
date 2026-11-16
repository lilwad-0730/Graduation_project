using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ★1116 B5 光球性格：光球會對她的處境有反應。全部用一顆「光球分身」做，真正的光球（GuidanceLight，沿路點飛的嚮導）一根頭髮都不動，
/// 所以嚮導的路點、觸碰、等待邏輯完全不受影響。
///
///  1) 回頭逼退狼（狼被逼退的那一刻）：她面前亮起一顆光球，朝狼閃幾下，狼退完再慢慢淡掉。
///     出現得柔：先從一點小光脹開（附帶一圈很淡的光暈擴散），不是啪一下出現。
///  2) 她站著不動一陣子：一顆小光點慢慢浮出來，繞著她飄。她一動就淡掉。
///  3) 她最近死過好幾次：不繞圈、也不亮，改成偏暗、貼在她肩後上方輕輕呼吸，像在陪她。
///     「最近」＝過去 180 秒內重置的次數（PlayerRespawnSystem.OnResettablesReset）；久沒死就回到 2)。
///
///  只讀：PlayerMovement 位置／面向、WolfEnemy 的「123 退後」狀態（反射）、重置事件。不改任何既有腳本。
///  目前只開廢墟。關掉：OrbMoodFX.Enabled = false。各行為各有開關。
/// </summary>
public class OrbMoodFX : MonoBehaviour
{
    public static bool Enabled = true;
    public static bool WolfFlashEnabled = false;   // ★1117 企劃不要：回頭逼退狼時的光球閃爍，預設關掉
    public static bool IdleCompanionEnabled = true;
    public static bool ConsoleOnDeathsEnabled = true;

    // ───── 企劃可調 ─────
    public static float FlashDistance = 2.8f;       // 在她面前多遠
    public static float FlashSeconds = 1.6f;        // 閃爍段長度（之後維持到狼退完）
    public static float FlashMaxSeconds = 3.2f;     // 整段最長
    public static float FlashCooldown = 3.5f;
    public static float WolfRange = 30f;            // 狼離她多近才算
    public static float IdleStillSeconds = 6f;      // 站多久光點才出現
    public static float DeathWindowSeconds = 180f;
    public static int DeathsToConsole = 2;

    // ───── 死亡次數記錄（專案原本沒有）─────
    private static readonly List<float> s_deathTimes = new List<float>();
    private static float s_sceneLoadTime;
    /// <summary>這次遊戲累積的重置（死亡）次數。</summary>
    public static int TotalDeaths;
    public static int RecentDeaths
    {
        get
        {
            float now = Time.unscaledTime;
            for (int i = s_deathTimes.Count - 1; i >= 0; i--)
                if (now - s_deathTimes[i] > DeathWindowSeconds) s_deathTimes.RemoveAt(i);
            return s_deathTimes.Count;
        }
    }

    private static void OnReset()
    {
        if (Time.unscaledTime - s_sceneLoadTime < 3f) return;   // 剛進場景的初始化重置不算
        TotalDeaths++;
        s_deathTimes.Add(Time.unscaledTime);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        PlayerRespawnSystem.OnResettablesReset -= OnReset;
        PlayerRespawnSystem.OnResettablesReset += OnReset;
        SceneManager.sceneLoaded -= OnLoaded;
        SceneManager.sceneLoaded += OnLoaded;
        s_sceneLoadTime = Time.unscaledTime;
        Ensure(SceneManager.GetActiveScene());
    }

    private static void OnLoaded(Scene s, LoadSceneMode m) { s_sceneLoadTime = Time.unscaledTime; Ensure(s); }

    private static void Ensure(Scene s)
    {
        if (!Enabled || !s.IsValid()) return;
        string n = s.name.ToLowerInvariant();
        if (!(n.Contains("ruin") || n.Contains("desert"))) return;   // 廢墟與荒原
        if (FindFirstObjectByType<OrbMoodFX>() != null) return;
        new GameObject("[光球性格]").AddComponent<OrbMoodFX>();
    }

    // ───── 執行期 ─────
    private enum Mode { None, Flash, Idle }

    private PlayerMovement _pm;
    private Collider _pcol;
    private GuidanceLight _orb;
    private WolfEnemy[] _wolves = new WolfEnemy[0];
    private static FieldInfo s_retreatField;
    private float _nextFind;

    private SpriteRenderer _core, _halo, _ring;
    private Texture2D _tex;
    private Sprite _softSprite;
    private Vector3 _coreBaseScale = Vector3.one * 0.5f;
    private Color _coreColor = new Color(1f, 0.93f, 0.72f, 1f);
    private bool _haveOrbLook;

    private Mode _mode = Mode.None;
    private float _vis;                 // 0～1 主透明度
    private float _modeT;               // 進入目前模式後的秒數
    private float _flashDir = 1f;
    private float _lastFlashEnd = -99f;
    private bool _wasRetreating;
    private float _stillT;
    private Vector3 _lastPlayerPos;
    private Vector3 _pos;
    private Vector3 _vel;
    private bool _posValid;

    private void Awake()
    {
        if (s_retreatField == null)
            s_retreatField = typeof(WolfEnemy).GetField("_dbgRetreating", BindingFlags.Instance | BindingFlags.NonPublic);

        _tex = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        _tex.wrapMode = TextureWrapMode.Clamp;
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                float dx = ((x + 0.5f) / 64f - 0.5f) * 2f, dy = ((y + 0.5f) / 64f - 0.5f) * 2f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - d);
                _tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
            }
        _tex.Apply(false, true);
        _softSprite = Sprite.Create(_tex, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f), 64f);
        _softSprite.hideFlags = HideFlags.HideAndDontSave;

        _halo = MakeSR("光暈", transform, _softSprite, -1);
        _ring = MakeSR("擴散光暈", transform, _softSprite, -2);
        _core = MakeSR("光球分身", transform, _softSprite, 0);
        SetVisible(false);
    }

    private SpriteRenderer MakeSR(string n, Transform parent, Sprite sp, int orderOffset)
    {
        var go = new GameObject(n);
        go.hideFlags = HideFlags.DontSave;
        go.transform.SetParent(parent, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sp;
        sr.sortingOrder = 400 + orderOffset;
        Shader sh = Shader.Find("Sprites/Default");
        if (sh != null) sr.sharedMaterial = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
        return sr;
    }

    private void OnDestroy()
    {
        foreach (var sr in new[] { _halo, _ring, _core })
            if (sr != null && sr.sharedMaterial != null && sr.sharedMaterial.hideFlags == HideFlags.HideAndDontSave) Destroy(sr.sharedMaterial);
        if (_softSprite != null) Destroy(_softSprite);
        if (_tex != null) Destroy(_tex);
    }

    private void SetVisible(bool v)
    {
        if (_core != null) _core.enabled = v;
        if (_halo != null) _halo.enabled = v;
        if (_ring != null) _ring.enabled = v;
    }

    /// <summary>把真正光球的樣子（貼圖、顏色、材質、大小）抄一份給分身，這樣看起來是同一顆。</summary>
    private void CopyOrbLook()
    {
        if (_orb == null) return;
        SpriteRenderer best = null;
        foreach (var sr in _orb.GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (sr == null || sr.sprite == null) continue;
            if (best == null || sr.sortingOrder > best.sortingOrder
                || (sr.sortingOrder == best.sortingOrder && sr.bounds.size.x < best.bounds.size.x)) best = sr;
        }
        if (best == null) return;
        _core.sprite = best.sprite;
        if (best.sharedMaterial != null) _core.sharedMaterial = best.sharedMaterial;
        _core.sortingLayerID = best.sortingLayerID;
        _halo.sortingLayerID = best.sortingLayerID;
        _ring.sortingLayerID = best.sortingLayerID;
        _core.sortingOrder = best.sortingOrder + 1;
        _halo.sortingOrder = best.sortingOrder;
        _ring.sortingOrder = best.sortingOrder - 1;
        // ★實測：荒原是橘色背景，跟真光球同色的分身會融進去看不見，所以往白色拉 45%，亮一點才讀得出來
        Color c = Color.Lerp(best.color, Color.white, 0.45f); c.a = 1f;
        _coreColor = c;
        _coreBaseScale = best.transform.lossyScale;
        if (_coreBaseScale.x < 0.01f) _coreBaseScale = Vector3.one * 0.5f;
        _haveOrbLook = true;
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;
        if (Time.timeScale < 0.01f) { return; }

        if (Time.unscaledTime >= _nextFind || _pm == null)
        {
            _nextFind = Time.unscaledTime + 1f;
            if (_pm == null) { _pm = FindFirstObjectByType<PlayerMovement>(); if (_pm != null) _pcol = _pm.GetComponent<Collider>(); }
            if (_orb == null) _orb = FindFirstObjectByType<GuidanceLight>(FindObjectsInactive.Include);
            if (!_haveOrbLook && _orb != null) CopyOrbLook();
            _wolves = FindObjectsByType<WolfEnemy>(FindObjectsSortMode.None);
        }
        if (_pm == null || !Enabled) { Fade(0f, dt); Apply(); return; }

        Vector3 pp = _pm.transform.position;
        Vector3 center = _pcol != null ? _pcol.bounds.center : pp + Vector3.up;
        float move = (pp - _lastPlayerPos).magnitude;
        _lastPlayerPos = pp;
        bool still = dt > 0.0001f && move / dt < 0.15f && _pm.isGrounded && !_pm.IsControlLocked;
        _stillT = still ? _stillT + dt : 0f;

        // ── 看狼有沒有被逼退 ──
        bool retreatingNow = false;
        float nearestDir = 0f, nearestDist = float.MaxValue;
        if (s_retreatField != null)
        {
            for (int i = 0; i < _wolves.Length; i++)
            {
                WolfEnemy w = _wolves[i];
                if (w == null || !w.isActiveAndEnabled) continue;
                float dx = w.transform.position.x - pp.x;
                if (Mathf.Abs(dx) > WolfRange) continue;
                if (!(bool)s_retreatField.GetValue(w)) continue;
                retreatingNow = true;
                if (Mathf.Abs(dx) < nearestDist) { nearestDist = Mathf.Abs(dx); nearestDir = Mathf.Sign(dx); }
            }
        }

        // ── 決定模式 ──
        bool canFlash = WolfFlashEnabled && Time.unscaledTime - _lastFlashEnd > FlashCooldown;
        if (_mode != Mode.Flash && retreatingNow && !_wasRetreating && canFlash && nearestDir != 0f)
        {
            EnterMode(Mode.Flash);
            _flashDir = nearestDir;
        }
        else if (_mode == Mode.Flash)
        {
            if (nearestDir != 0f) _flashDir = nearestDir;
            bool holdOn = retreatingNow || _modeT < FlashSeconds;
            if (!holdOn || _modeT > FlashMaxSeconds)
            {
                _lastFlashEnd = Time.unscaledTime;
                EnterMode(Mode.None);
            }
        }
        _wasRetreating = retreatingNow;

        int deaths = RecentDeaths;
        bool consoling = ConsoleOnDeathsEnabled && deaths >= DeathsToConsole;
        if (_mode != Mode.Flash)
        {
            float need = consoling ? 3f : IdleStillSeconds;
            if (IdleCompanionEnabled && _stillT >= need && !retreatingNow)
            {
                if (_mode != Mode.Idle) EnterMode(Mode.Idle);
            }
            else if (_mode == Mode.Idle && (_stillT < 0.3f || retreatingNow))
            {
                EnterMode(Mode.None);
            }
        }

        _modeT += dt;

        // ── 位置、亮度 ──
        float face = _pm.FacingDirection.x >= 0f ? 1f : -1f;
        Vector3 target = _pos;
        float targetVis = 0f;
        float flick = 1f;
        float scaleMul = 1f;
        float comfort = Mathf.Clamp01(MotionComfort.Scale);

        if (_mode == Mode.Flash)
        {
            // 在她面對的方向（也就是狼那一邊）前面，靠近頭的高度
            float lunge = 0.28f * comfort * Mathf.Max(0f, Mathf.Sin(_modeT * 9f)) * (_modeT < FlashSeconds ? 1f : 0f);
            target = new Vector3(pp.x + _flashDir * (FlashDistance + lunge), center.y + 0.9f, pp.z - 0.2f);
            targetVis = _modeT < FlashSeconds ? 1f : 0.75f;
            // 出現：0～0.35 秒從一點小光脹開（過衝一點點再回來）
            float u = Mathf.Clamp01(_modeT / 0.35f);
            scaleMul = EaseOutBack(u);
            // 朝狼閃：0.35 秒之後約 3 下／秒，深度跟動態強度走（調小就閃得輕）
            if (_modeT > 0.35f && _modeT < FlashSeconds)
            {
                float depth = Mathf.Lerp(0.2f, 0.55f, comfort);
                float s = 0.5f + 0.5f * Mathf.Sin((_modeT - 0.35f) * 6.2832f * 3f);
                flick = 1f - depth * (1f - s * s);
            }
            // 擴散光暈：剛出現那 0.5 秒往外擴、淡掉
            float ru = Mathf.Clamp01(_modeT / 0.5f);
            _ring.transform.localScale = _coreBaseScale * Mathf.Lerp(1f, 3.2f, ru);
            SetA(_ring, _coreColor, 0.35f * (1f - ru) * _vis);
        }
        else if (_mode == Mode.Idle)
        {
            if (deaths >= DeathsToConsole && ConsoleOnDeathsEnabled)
            {
                // 陪著：偏暗，貼在她肩後上方，慢慢呼吸
                float br = 0.5f + 0.5f * Mathf.Sin(_modeT * 1.3f);
                target = new Vector3(pp.x - face * 0.85f, center.y + 0.95f + 0.07f * Mathf.Sin(_modeT * 1.1f) * comfort, pp.z - 0.2f);
                targetVis = 0.55f;
                flick = Mathf.Lerp(0.55f, 0.85f, br);
                scaleMul = 0.9f;
            }
            else
            {
                // 繞著她慢慢飄：約 7 秒一圈，橢圓、略偏上
                float ang = _modeT * 6.2832f / 7f;
                float k = comfort;
                target = new Vector3(center.x + Mathf.Cos(ang) * 2.4f, center.y + 1.0f + Mathf.Sin(ang) * 0.7f * k, pp.z - 0.2f);
                targetVis = 0.95f;
                flick = 0.85f + 0.15f * (0.5f + 0.5f * Mathf.Sin(_modeT * 2.1f));
                scaleMul = 1.0f;
            }
            _ring.transform.localScale = _coreBaseScale * 1.0f;
            SetA(_ring, _coreColor, 0f);
        }
        else
        {
            SetA(_ring, _coreColor, 0f);
        }

        if (!_posValid) { _pos = target; _vel = Vector3.zero; _posValid = true; }
        else if (_mode == Mode.Flash) _pos = Vector3.SmoothDamp(_pos, target, ref _vel, 0.12f, 40f, dt);
        else _pos = Vector3.SmoothDamp(_pos, target, ref _vel, 0.45f, 12f, dt);

        Fade(targetVis, dt);
        transform.position = _pos;
        _core.transform.localScale = _coreBaseScale * scaleMul;
        _halo.transform.localScale = _coreBaseScale * scaleMul * 3.0f;
        float a = _vis * flick;
        SetA(_core, _coreColor, a);
        SetA(_halo, _coreColor, a * 0.5f);
        bool vis = _vis > 0.003f;
        if (_core.enabled != vis) SetVisible(vis);
        if (_ring.enabled && !vis) _ring.enabled = false;
    }

    private void Apply() { }

    private void EnterMode(Mode m)
    {
        // 從沒顯示到顯示：位置先放到目標，再淡入，才不會從別的地方滑過來
        if (_vis < 0.02f) _posValid = false;
        _mode = m;
        _modeT = 0f;
    }

    private void Fade(float target, float dt)
    {
        float rate = target > _vis ? (_mode == Mode.Flash ? 1f / 0.3f : 1f / 1.6f) : (1f / 0.9f);
        _vis = Mathf.MoveTowards(_vis, target, rate * dt);
    }

    private static void SetA(SpriteRenderer sr, Color baseColor, float a)
    {
        if (sr == null) return;
        Color c = baseColor; c.a = Mathf.Clamp01(a);
        sr.color = c;
    }

    private static float EaseOutBack(float x)
    {
        const float c1 = 1.4f, c3 = c1 + 1f;
        float t = x - 1f;
        return 1f + c3 * t * t * t + c1 * t * t;
    }
}
