using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// ★1021 線條畫提示（企劃的 LineHintSign／LineHintDirector 放的那些教學圖）的「可拖動標記」。
///
/// 為什麼要它：企劃那套線條畫提示的位置是執行時用程式算出來的（例如「水下出生點右邊 6.5 米」），要微調只能改程式。
/// 這個標記放在場景裡，拖它就能改位置；遊戲執行時，只要場景裡有跟某張提示同名的標記，那張提示就改放在標記的位置（大小也可以覆蓋）。
/// 沒有標記的提示照舊用程式算的位置，所以不影響任何現有行為。
///
/// 用法（選單 Tools ▸ 提示圖示 ▸ 建立線條畫提示的可拖動標記）：
///   1. 在 Unity 按 Play、進到要調的關卡（提示一生出來，位置就會被自動記下，不用做別的）
///   2. 停掉 Play
///   3. 選單 Tools ▸ 提示圖示 ▸ 建立線條畫提示的可拖動標記 → 場景裡會冒出一個一個金色框的標記
///   4. 在 Scene 視窗拖標記（金色框＝圖的範圍）；想改大小就改 Inspector 的 World Width
///   5. 存檔（Ctrl+S）。標記存在場景檔裡，commit 之後組員 pull 下來就有，位置也一樣
///   不想要了：刪掉那個標記，提示就回到程式算的位置。
/// </summary>
[DisallowMultipleComponent]
public class LineHintAnchor : MonoBehaviour
{
    [Tooltip("對應哪一張提示（物件名稱，例如 LineHint_水下WS）。由選單自動填，別亂改。")]
    public string signName = "";
    [Tooltip("圖的路徑（只用來在 Scene 視窗畫範圍框）。")]
    public string resourcePath = "";
    [Tooltip("圖在世界裡的寬度（公尺）。0＝照程式原本算的大小。")]
    public float worldWidth = 0f;

    private static readonly List<LineHintAnchor> _all = new List<LineHintAnchor>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { _all.Clear(); }

    private void OnEnable() { if (!_all.Contains(this)) _all.Add(this); }
    private void OnDisable() { _all.Remove(this); }

    /// <summary>找場景裡跟這張提示同名、而且啟用的標記。</summary>
    public static bool TryGet(string name, out LineHintAnchor anchor)
    {
        anchor = null;
        if (string.IsNullOrEmpty(name)) return false;
        // 場景剛載入時 OnEnable 可能還沒跑到，保險起見再掃一次場景
        for (int i = 0; i < _all.Count; i++)
        {
            LineHintAnchor a = _all[i];
            if (a != null && a.signName == name) { anchor = a; return true; }
        }
        foreach (LineHintAnchor a in FindObjectsByType<LineHintAnchor>(FindObjectsInactive.Exclude))
        {
            if (a != null && a.signName == name) { anchor = a; return true; }
        }
        return false;
    }

    /// <summary>LineHintSign.Create 呼叫：只在編輯器裡把「這張提示放在哪」記下來，給選單建立標記用（打包版不做事）。</summary>
    public static void Record(string name, string res, Vector3 pos, float width)
    {
#if UNITY_EDITOR
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        string key = "LineHintRec_" + scene;
        string old = EditorPrefs.GetString(key, "");
        List<string> lines = new List<string>();
        foreach (string l in old.Split('\n'))
        {
            if (string.IsNullOrEmpty(l)) continue;
            if (l.StartsWith(name + "|")) continue;      // 同名的換成新的
            lines.Add(l);
        }
        lines.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}|{1}|{2:F2}|{3:F2}|{4:F2}|{5:F2}", name, res, pos.x, pos.y, pos.z, width));
        EditorPrefs.SetString(key, string.Join("\n", lines.ToArray()));
#endif
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        float w = worldWidth > 0f ? worldWidth : 8.5f;
        float h = w * 0.38f;
        if (!string.IsNullOrEmpty(resourcePath))
        {
            Texture2D tex = Resources.Load<Texture2D>(resourcePath);
            if (tex != null) h = w * tex.height / Mathf.Max(1, tex.width);
        }
        Gizmos.color = new Color(1f, 0.82f, 0.25f, 0.95f);
        Gizmos.DrawWireCube(transform.position, new Vector3(w, h, 0.1f));
        Gizmos.DrawSphere(transform.position, 0.25f);
        Handles.Label(transform.position + new Vector3(-w * 0.5f, h * 0.5f + 0.4f, 0f), signName);
    }
#endif
}
