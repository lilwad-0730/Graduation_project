using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ★1110 狼的「出現感」：狼出現之前，先有一道狼的黑影貼著地面從畫面右邊飛快掠過去（往她身後的方向），
/// 帶起一路灰塵；影子才剛消失在左邊，狼就從出生點冒出來，出生那一瞬間再炸開一團灰塵。
/// 讓玩家先「感覺到有東西」，再看到牠——像大作那種預兆→登場的節奏。
///
/// 沒有新美術：影子用場景裡那隻狼模板現成的剪影（壓成近黑色、橫向拉長一點像速度模糊），灰塵用程式畫的柔邊圓點。
/// 規則：每個出生點只演一次（重生後沒生出狼的出生點可以再演）；暫停、過場、重生中、她已經很緊張時不演；
///      只在有狼出生點的場景自動啟動。關掉：WolfAmbushPreludeFX.Enabled = false。
/// </summary>
public class WolfAmbushPreludeFX : MonoBehaviour
{
    public static bool Enabled = true;

    // 企劃可調
    public static float TriggerMax = 18f;        // 她離出生點多遠（公尺，出生點在她前方）時影子開始掠過
    public static float TriggerMin = 9f;         // 太近就不演（來不及，直接讓狼出來）
    public static float Speed = 36f;             // 影子速度（公尺／秒）
    public static float MaxAlpha = 0.8f;
    public static float SpriteScale = 1.2f;
    public static float StretchX = 1.3f;         // 橫向拉長（像速度模糊）
    public static bool SpriteFacesRight = true;  // 狼模板那張圖本來朝右嗎（影子往左跑，朝右的要翻過來）
    public static Color ShadowColor = new Color(0.02f, 0.02f, 0.035f, 1f);
    /// <summary>已經演過幾次（驗收／除錯用）。</summary>
    public static int PlayCount;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        SceneManager.sceneLoaded -= OnLoaded;
        SceneManager.sceneLoaded += OnLoaded;
        Ensure(SceneManager.GetActiveScene());
    }

    private static void OnLoaded(Scene s, LoadSceneMode m) { Ensure(s); }

    private static void Ensure(Scene s)
    {
        if (!Enabled || !s.IsValid()) return;
        if (FindFirstObjectByType<WolfAmbushPreludeFX>() != null) return;
        if (FindObjectsByType<WolfSpawner>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length == 0) return;
        new GameObject("[狼的出現感]").AddComponent<WolfAmbushPreludeFX>();
    }

    private static FieldInfo s_spawned;
    private Transform _player;
    private Camera _cam;
    private WolfSpawner[] _spawners = new WolfSpawner[0];
    private readonly HashSet<WolfSpawner> _played = new HashSet<WolfSpawner>();
    private readonly HashSet<WolfEnemy> _seenWolves = new HashSet<WolfEnemy>();
    private float _nextScan;
    private float _sceneStart;
    private Sprite _wolfSprite, _dotSprite;

    // 掠過中的影子
    private SpriteRenderer _shadow;
    private bool _active;
    private float _x, _y, _bobT, _puffT, _endX;
    private WolfSpawner _for;

    private class Puff { public SpriteRenderer sr; public float age, life, rise, size0, size1, a0; }
    private readonly List<Puff> _puffs = new List<Puff>();

    private void Awake()
    {
        if (s_spawned == null) s_spawned = typeof(WolfSpawner).GetField("hasSpawned", BindingFlags.NonPublic | BindingFlags.Instance);
        _sceneStart = Time.unscaledTime;
        BuildDot();
    }

    private void OnEnable() { PlayerRespawnSystem.OnResettablesReset += OnReset; }
    private void OnDisable() { PlayerRespawnSystem.OnResettablesReset -= OnReset; }
    private void OnReset() { _played.Clear(); StopSweep(); }

    private void OnDestroy() { StopSweep(); for (int i = 0; i < _puffs.Count; i++) if (_puffs[i].sr != null) Destroy(_puffs[i].sr.gameObject); _puffs.Clear(); }

    private void BuildDot()
    {
        const int D = 32;
        Texture2D t = new Texture2D(D, D, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        float c = (D - 1) * 0.5f;
        for (int y = 0; y < D; y++)
            for (int x = 0; x < D; x++)
            {
                float r = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                float a = Mathf.Clamp01(1f - r); a = a * a * (3f - 2f * a);
                t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        t.Apply();
        _dotSprite = Sprite.Create(t, new Rect(0, 0, D, D), new Vector2(0.5f, 0.5f), 32f);
    }

    private bool CanPlay()
    {
        if (!Mathf.Approximately(Time.timeScale, 1f)) return false;
        if (Time.unscaledTime - _sceneStart < 4f) return false;
        PlayerRespawnSystem rs = PlayerRespawnSystem.Instance;
        if (rs != null && rs.IsRespawning) return false;
        if (PlayerMovement.IsHardCutsceneLocked) return false;
        if (EmotionDirector.Enabled && EmotionDirector.Tension > 0.4f) return false;
        if (GameRecoveryGuard.SafeMode) return false;
        return true;
    }

    private void Update()
    {
        if (!Enabled) { if (_active) StopSweep(); return; }
        float dt = Time.deltaTime;
        if (_cam == null) _cam = Camera.main;
        if (_player == null) { GameObject p = GameObject.FindWithTag("Player"); if (p != null) _player = p.transform; }

        UpdatePuffs(dt);
        if (_cam == null || _player == null) return;

        if (Time.unscaledTime >= _nextScan)
        {
            _nextScan = Time.unscaledTime + 0.25f;
            _spawners = FindObjectsByType<WolfSpawner>(FindObjectsSortMode.None);
            ScanNewWolves();
        }

        if (_active) { RunSweep(dt); return; }
        if (!CanPlay() || s_spawned == null) return;

        for (int i = 0; i < _spawners.Length; i++)
        {
            WolfSpawner sp = _spawners[i];
            if (sp == null || !sp.isActiveAndEnabled || _played.Contains(sp)) continue;
            if ((bool)s_spawned.GetValue(sp)) continue;
            float sdx = sp.transform.position.x - _player.position.x;
            if (sdx > TriggerMax || sdx < TriggerMin) continue;
            if (Mathf.Abs(sp.transform.position.y - _player.position.y) > 10f) continue;
            _played.Add(sp);
            StartSweep(sp);
            break;
        }
    }

    // ───────── 影子掠過 ─────────
    private Sprite FindWolfSprite()
    {
        if (_wolfSprite != null) return _wolfSprite;
        WolfEnemy[] ws = FindObjectsByType<WolfEnemy>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < ws.Length; i++)
        {
            SpriteRenderer sr = ws[i] != null ? ws[i].GetComponentInChildren<SpriteRenderer>(true) : null;
            if (sr != null && sr.sprite != null) { _wolfSprite = sr.sprite; break; }
        }
        return _wolfSprite;
    }

    private void StartSweep(WolfSpawner sp)
    {
        Sprite s = FindWolfSprite();
        if (s == null) return;
        _for = sp;
        float halfW = _cam.orthographic ? _cam.orthographicSize * _cam.aspect : 25f;
        float camX = _cam.transform.position.x;
        _x = camX + halfW + 6f;
        _endX = camX - halfW - 10f;
        _y = sp.transform.position.y;           // 出生點的高度＝狼貼地的高度
        _bobT = 0f; _puffT = 0f;

        GameObject go = new GameObject("狼的黑影");
        _shadow = go.AddComponent<SpriteRenderer>();
        _shadow.sprite = s;
        _shadow.sortingLayerName = "Default";
        _shadow.sortingOrder = 6;                // 要在場景擺設（書櫃之類）前面才看得到；玩家本人是 3D，不受這個影響
        _shadow.flipX = SpriteFacesRight;        // 往左跑
        go.transform.localScale = new Vector3(SpriteScale * StretchX, SpriteScale, 1f);
        go.transform.position = new Vector3(_x, _y, _player.position.z);
        _shadow.color = new Color(ShadowColor.r, ShadowColor.g, ShadowColor.b, 0f);
        _active = true;
        PlayCount++;
    }

    private void RunSweep(float dt)
    {
        if (_shadow == null) { _active = false; return; }
        // 重生、暫停、過場：直接收掉
        PlayerRespawnSystem rs = PlayerRespawnSystem.Instance;
        if ((rs != null && rs.IsRespawning) || !Mathf.Approximately(Time.timeScale, 1f)) { StopSweep(); return; }

        _x -= Speed * dt;
        _bobT += dt;
        float halfW = _cam.orthographic ? _cam.orthographicSize * _cam.aspect : 25f;
        float camX = _cam.transform.position.x;
        float y = _y + Mathf.Abs(Mathf.Sin(_bobT * 17f)) * 0.22f;          // 跑步的上下起伏
        _shadow.transform.position = new Vector3(_x, y, _player.position.z);

        // 進出畫面邊緣淡入淡出
        float edge = Mathf.Min(camX + halfW - _x, _x - (camX - halfW));
        float k = Mathf.Clamp01((edge + 3f) / 7f);
        k = k * k * (3f - 2f * k);
        _shadow.color = new Color(ShadowColor.r, ShadowColor.g, ShadowColor.b, MaxAlpha * k);

        // 一路帶起灰塵
        _puffT -= dt;
        if (_puffT <= 0f && k > 0.2f) { _puffT = 0.05f; EmitPuff(new Vector3(_x + 1.2f, _y - 0.15f, _player.position.z), 0.38f); }

        if (_x < _endX || _x < camX - halfW - 6f) StopSweep();
    }

    private void StopSweep()
    {
        _active = false;
        if (_shadow != null) { Destroy(_shadow.gameObject); _shadow = null; }
    }

    // ───────── 灰塵 ─────────
    private void EmitPuff(Vector3 pos, float alpha)
    {
        if (_puffs.Count > 60) return;
        GameObject g = new GameObject("灰塵");
        g.transform.position = pos + new Vector3(Random.Range(-0.4f, 0.4f), Random.Range(0f, 0.25f), 0f);
        SpriteRenderer sr = g.AddComponent<SpriteRenderer>();
        sr.sprite = _dotSprite;
        sr.sortingLayerName = "Default";
        sr.sortingOrder = 5;
        sr.color = new Color(0.5f, 0.47f, 0.42f, 0f);
        Puff p = new Puff { sr = sr, life = Random.Range(0.8f, 1.4f), rise = Random.Range(0.3f, 0.9f), size0 = Random.Range(0.5f, 0.9f), size1 = Random.Range(1.6f, 2.6f), a0 = alpha };
        g.transform.localScale = Vector3.one * p.size0;
        _puffs.Add(p);
    }

    private void UpdatePuffs(float dt)
    {
        for (int i = _puffs.Count - 1; i >= 0; i--)
        {
            Puff p = _puffs[i];
            if (p.sr == null) { _puffs.RemoveAt(i); continue; }
            p.age += dt;
            float t = p.age / p.life;
            if (t >= 1f) { Destroy(p.sr.gameObject); _puffs.RemoveAt(i); continue; }
            float a = p.a0 * Mathf.Sin(t * Mathf.PI);                 // 慢慢浮出、慢慢散掉
            p.sr.color = new Color(0.5f, 0.47f, 0.42f, a);
            p.sr.transform.localScale = Vector3.one * Mathf.Lerp(p.size0, p.size1, t);
            p.sr.transform.position += new Vector3(0f, p.rise * dt, 0f);
        }
    }

    // ───────── 出生那一瞬間 ─────────
    private void ScanNewWolves()
    {
        WolfEnemy[] ws = FindObjectsByType<WolfEnemy>(FindObjectsSortMode.None);
        for (int i = 0; i < ws.Length; i++)
        {
            WolfEnemy w = ws[i];
            if (w == null || _seenWolves.Contains(w)) continue;
            _seenWolves.Add(w);
            if (!w.name.StartsWith("Spawned_Wolf")) continue;
            if (w.enabled) continue;                                   // 剛生出來還在蓄勢（腳本關著）的才算「登場」
            Vector3 p = w.transform.position;
            for (int k = 0; k < 9; k++) EmitPuff(new Vector3(p.x + Random.Range(-1.6f, 1.6f), p.y - 0.6f, p.z), 0.45f);
        }
    }
}
