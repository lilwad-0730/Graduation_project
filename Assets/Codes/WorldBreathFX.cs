using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ★1109 世界呼吸：遠景（有 ParallaxBackground 的背景）極慢、極輕地放大縮小，約 0.25%，眼睛幾乎察覺不到，
/// 但整個畫面會有「活著」的感覺。節奏跟著情緒走（讀 EmotionDirector）：
///   平靜／安心 → 很慢、很深的呼吸（約 9 秒一次）
///   緊張（狼逼近、缺氧）→ 變快、變淺（約 3.5 秒一次），像憋著氣
/// 規則：
///  • 只「放大」不「縮小」（縮小會在畫面邊緣露出空隙），最小值就是原本大小。
///  • 只動有 ParallaxBackground 的物件、不動有碰撞體／剛體／動畫的、不動雲朵（棉花堡雲朵呼吸是 EmotionAtmosphereFX 的）。
///  • 縮放被別的腳本動過（跟我上次寫的不一樣）就放棄那個物件，不跟人搶。
///  • 關掉或離開時全部還原成原本大小。
/// 關掉：WorldBreathFX.Enabled = false。
/// </summary>
public class WorldBreathFX : MonoBehaviour
{
    public static bool Enabled = true;

    // 企劃可調
    public static float Amplitude = 0.0025f;          // 最大放大比例（0.25%）
    public static float CalmPeriod = 9f, TensePeriod = 3.5f;   // 一次呼吸幾秒

    private class Item { public Transform t; public Vector3 baseScale, lastWritten; public float phase; public bool dead; }

    private readonly List<Item> _items = new List<Item>();
    private float _phase;            // 全體共用的呼吸相位（速度會變，所以用累加，不會跳拍）
    private float _amp;              // 目前深度（平滑）
    private float _collectAt;
    private bool _collected;
    /// <summary>目前被我動著的物件數量（驗收／除錯用）。</summary>
    public static int ActiveCount;

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
        string n = s.name.ToLowerInvariant();
        if (n.Contains("mainmenu") || n.Contains("book")) return;      // 選單與繪本不用
        if (FindFirstObjectByType<WorldBreathFX>() != null) return;
        new GameObject("[世界呼吸]").AddComponent<WorldBreathFX>();
    }

    private void Start() { _collectAt = Time.unscaledTime + 1.0f; }

    private void Collect()
    {
        _collected = true;
        ParallaxBackground[] all = FindObjectsByType<ParallaxBackground>(FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            ParallaxBackground pb = all[i];
            if (pb == null) continue;
            Transform t = pb.transform;
            if (t.name.ToLowerInvariant().Contains("cloudplatform")) continue;               // 雲朵是 EmotionAtmosphereFX 的
            if (t.GetComponentInParent<Collider>() != null || t.GetComponentInChildren<Collider>() != null) continue;
            if (t.GetComponentInParent<Collider2D>() != null || t.GetComponentInChildren<Collider2D>() != null) continue;
            if (t.GetComponentInParent<Rigidbody>() != null || t.GetComponent<Animator>() != null) continue;
            if (t.parent != null && t.parent.GetComponentInParent<ParallaxBackground>() != null) continue;   // 巢狀視差只動最外層
            float h = Mathf.Abs(t.position.x * 0.37f + t.position.y * 0.61f);
            _items.Add(new Item { t = t, baseScale = t.localScale, lastWritten = t.localScale, phase = (h % 1f) * 0.8f });
        }
    }

    private void Update()
    {
        if (!Enabled)
        {
            if (_items.Count > 0) Restore();
            _collected = false;
            _collectAt = Time.unscaledTime + 1f;
            return;
        }
        if (!_collected)
        {
            if (Time.unscaledTime >= _collectAt) Collect();
            return;
        }

        float tension = EmotionDirector.Enabled ? EmotionDirector.Tension : 0f;
        float period = Mathf.Lerp(CalmPeriod, TensePeriod, Mathf.SmoothStep(0f, 1f, tension));
        float depth = Mathf.Lerp(1f, 0.6f, tension);                       // 緊張時變淺
        float dt = Time.unscaledDeltaTime;
        _phase += dt / period;                                              // 累加相位：節奏變快變慢不跳拍
        _amp = Mathf.MoveTowards(_amp, Amplitude * depth, dt * 0.002f);

        int active = 0;
        for (int i = 0; i < _items.Count; i++)
        {
            Item it = _items[i];
            if (it.dead) continue;
            if (it.t == null) { it.dead = true; continue; }
            // 縮放被別人動過：放棄這個物件（不還原、不再碰）
            if ((it.t.localScale - it.lastWritten).sqrMagnitude > 1e-8f) { it.dead = true; continue; }
            float s = 0.5f + 0.5f * Mathf.Sin((_phase + it.phase) * Mathf.PI * 2f);   // 0～1，只放大
            it.t.localScale = it.baseScale * (1f + _amp * s);
            it.lastWritten = it.t.localScale;
            active++;
        }
        ActiveCount = active;
    }

    private void Restore()
    {
        for (int i = 0; i < _items.Count; i++)
        {
            Item it = _items[i];
            if (it.dead || it.t == null) continue;
            if ((it.t.localScale - it.lastWritten).sqrMagnitude <= 1e-8f) it.t.localScale = it.baseScale;
        }
        _items.Clear();
        _amp = 0f;
        ActiveCount = 0;
    }

    private void OnDestroy() { Restore(); }
}
