using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ★1125 浮在「對應物件上方」的操作提示（翊安的方向：除了 WASD，其他按鍵提示都顯示在物件頭上）。
///
///   ・鍵鼠：貼紙風的按鍵帽（Shift、S、D…），會被按下去、上面三個小箭頭往下流動
///   ・手把：A／X／Y 按鈕，或 3D 搖桿（會從中立推到方向再回來）
///   ・依 InputPrompt 自動切換；玩家走近才淡入，離開淡出，隨物件移動（例如滾動的巨石）
///   ・「學會就不再煩」：玩家在提示出現時真的按了對應的鍵 learnAfter 次，同一種提示整個遊戲就不再出現
///   ・播過場、被鎖定時自動讓路
///
/// 不用手掛：WorldKeyPromptDirector 在場景載入時幫推石、鏡箱、巨石、燭火裝上。
/// 總開關：WorldKeyPrompt.Enabled＝false → 全部不出現。
/// </summary>
public class WorldKeyPrompt : MonoBehaviour
{
    public static bool Enabled = true;

    /// <summary>已經學會的提示種類（整個遊戲共用，重生、換場景都不重置）。</summary>
    private static readonly HashSet<string> Learned = new HashSet<string>();
    private static readonly Dictionary<string, int> PressCount = new Dictionary<string, int>();

    // ── 設定（由 Attach 填）──
    private string _id;
    private Transform _target;
    private string[] _kb, _pad;
    private float _range = 6f;
    private float _size = 1.0f;
    private float _lift = 0.8f;
    private int _learnAfter = 2;
    private Func<bool> _learnPress;
    private Func<bool> _hideWhen;

    private Collider[] _cols;
    private Renderer[] _rends;
    private Transform _player;
    private float _nextLookup;
    private float _alpha;
    private float _phase;
    private bool _builtPad;
    private bool _built;
    private readonly List<Glyph> _glyphs = new List<Glyph>();

    private class Glyph
    {
        public SpriteRenderer sr;
        public PromptSheets.Sheet sheet;
        public float x;
    }

    /// <summary>在 target 上方放一個提示。kb／pad 是素材名稱清單（例如 {"key_Shift","plus","key_D"}）。</summary>
    public static WorldKeyPrompt Attach(string id, Transform target, string[] kb, string[] pad,
                                        float range, float size, float lift, int learnAfter,
                                        Func<bool> learnPress, Func<bool> hideWhen)
    {
        if (target == null) return null;
        GameObject go = new GameObject("[提示:" + id + "]");
        WorldKeyPrompt p = go.AddComponent<WorldKeyPrompt>();
        p._id = id; p._target = target; p._kb = kb; p._pad = pad;
        p._range = range; p._size = size; p._lift = lift; p._learnAfter = learnAfter;
        p._learnPress = learnPress; p._hideWhen = hideWhen;
        p._cols = target.GetComponentsInChildren<Collider>();
        p._rends = target.GetComponentsInChildren<Renderer>();
        p._phase = UnityEngine.Random.Range(0f, 6.28f);
        return p;
    }

    private Vector3 AnchorPoint()
    {
        Bounds b = new Bounds(_target.position, Vector3.zero);
        bool has = false;
        if (_cols != null)
            foreach (Collider c in _cols) { if (c == null || c.isTrigger) continue; if (!has) { b = c.bounds; has = true; } else b.Encapsulate(c.bounds); }
        if (!has && _rends != null)
            foreach (Renderer r in _rends) { if (r == null) continue; if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds); }
        return new Vector3(b.center.x, b.max.y + _lift, _target.position.z - 0.5f);
    }

    private float PlayerDistance(Vector3 anchor)
    {
        if (_player == null) return float.MaxValue;
        Vector3 p = _player.position;
        float dx = Mathf.Abs(p.x - anchor.x);
        float dy = Mathf.Abs(p.y - (anchor.y - _lift));
        if (dy > 7f) return float.MaxValue;
        return dx;
    }

    private void Build(bool pad)
    {
        foreach (Glyph g in _glyphs) if (g.sr != null) Destroy(g.sr.gameObject);
        _glyphs.Clear();
        string[] names = pad ? _pad : _kb;
        _built = true;
        _builtPad = pad;
        if (names == null) return;
        float total = 0f;
        List<float> adv = new List<float>();
        foreach (string n in names) { float a = Advance(n) * _size; adv.Add(a); total += a; }
        float x = -total * 0.5f;
        for (int i = 0; i < names.Length; i++)
        {
            PromptSheets.Sheet sh = PromptSheets.Get(names[i]);
            float cx = x + adv[i] * 0.5f;
            x += adv[i];
            if (sh == null) continue;
            GameObject go = new GameObject(names[i]);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(cx, 0f, 0f);
            go.transform.localScale = Vector3.one * _size;
            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = 9400;
            sr.sprite = sh.frames[0];
            sr.color = new Color(1f, 1f, 1f, 0f);
            _glyphs.Add(new Glyph { sr = sr, sheet = sh, x = cx });
        }
    }

    /// <summary>每個圖示在一排裡佔的寬度（一格寬度的比例）。</summary>
    private static float Advance(string n)
    {
        if (n == "plus") return 0.42f;
        if (n.StartsWith("stick")) return 0.95f;
        if (n == "key_Shift" || n == "key_Space") return 0.84f;
        return 0.62f;
    }

    private void LateUpdate()
    {
        if (_target == null) { Destroy(gameObject); return; }
        if (!Enabled || Learned.Contains(_id))
        {
            _alpha = 0f;
            foreach (Glyph g in _glyphs) if (g.sr != null) g.sr.enabled = false;
            return;
        }

        if (_player == null && Time.unscaledTime >= _nextLookup)
        {
            _nextLookup = Time.unscaledTime + 0.5f;
            PlayerMovement pm = FindAnyObjectByType<PlayerMovement>();
            if (pm != null) _player = pm.transform;
        }

        Vector3 anchor = AnchorPoint();
        float dist = PlayerDistance(anchor);
        float want = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(_range * 0.7f, _range, dist));
        bool hide = false;
        if (_hideWhen != null) { try { hide = _hideWhen(); } catch { hide = false; } }
        if (hide || PlayerMovement.IsHardCutsceneLocked || _target == null || !_target.gameObject.activeInHierarchy) want = 0f;

        // 學會了嗎：提示看得見的時候，玩家真的按了對應的鍵
        if (_alpha > 0.5f && _learnPress != null)
        {
            bool pressed = false;
            try { pressed = _learnPress(); } catch { }
            if (pressed)
            {
                int n; PressCount.TryGetValue(_id, out n); n++;
                PressCount[_id] = n;
                if (n >= _learnAfter) { Learned.Add(_id); Debug.Log("【操作提示】玩家已學會「" + _id + "」，之後不再顯示這種浮動提示"); }
            }
        }

        Fade(want);

        bool pad = InputPrompt.IsPad;
        if (!_built || pad != _builtPad) Build(pad);

        // 位置：物件上方、微微漂浮；面向鏡頭
        float bob = Mathf.Sin(Time.unscaledTime * 2.1f + _phase) * 0.06f;
        transform.position = anchor + new Vector3(0f, bob, 0f);
        Camera cam = Camera.main;
        if (cam != null) transform.rotation = cam.transform.rotation;

        // 動畫
        float t = Time.unscaledTime + _phase;
        foreach (Glyph g in _glyphs)
        {
            if (g.sr == null) continue;
            int f = g.sheet.frames.Length > 1 ? (int)(t * g.sheet.fps) % g.sheet.frames.Length : 0;
            g.sr.sprite = g.sheet.frames[f];
            g.sr.color = new Color(1f, 1f, 1f, _alpha);
            g.sr.enabled = _alpha > 0.01f;
        }
    }

    private void Fade(float target)
    {
        _alpha = Mathf.MoveTowards(_alpha, target, Time.unscaledDeltaTime / 0.35f);
    }
}
