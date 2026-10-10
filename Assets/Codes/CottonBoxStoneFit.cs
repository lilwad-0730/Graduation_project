using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ★1010 1008 會議 棉花堡 b「方塊石頭碰撞範圍縮小」（修毅清單待完成 1）。
///
/// 現況：方塊石頭（貼圖「ChatGPT Image 2026年5月27日 下午09_42_43」，場景裡 3 顆）的碰撞箱跟貼圖差不多大，
///       但她自己的碰撞箱比身體寬（寬 2.13 米、往右偏 0.3 米），靠近石頭時看起來人和石頭中間隔了一段，像石頭的碰撞箱太大。
/// 做法：不改場景。進到有方塊石頭的場景時，把方塊石頭的碰撞箱左右各比貼圖窄 shrinkPerSide 米；
///       上下不動（站在石頭上、石頭落地的高度都不變）。用貼圖大小算，重複套用結果一樣。
/// 整包關掉：CottonBoxStoneFit.Enabled = false。
/// </summary>
public static class CottonBoxStoneFit
{
    public static bool Enabled = true;

    /// <summary>碰撞箱左右各比貼圖窄幾米（世界單位）。</summary>
    public static float shrinkPerSide = 0.25f;

    /// <summary>縮完至少留這麼寬（米），避免小石頭被縮沒了。</summary>
    public static float minWidth = 0.8f;

    private const string SpriteKey = "09_42_43";   // 方塊石頭貼圖名稱裡的一段

    // 縮之前的大小（Restore 用：測試時切回改動前）
    private static readonly System.Collections.Generic.Dictionary<BoxCollider, Vector3[]> Originals =
        new System.Collections.Generic.Dictionary<BoxCollider, Vector3[]>();

    /// <summary>把縮過的方塊石頭碰撞箱放回原本大小（測試時切回改動前用）。</summary>
    public static void Restore()
    {
        foreach (var kv in Originals)
        {
            if (kv.Key == null) continue;
            kv.Key.size = kv.Value[0];
            kv.Key.center = kv.Value[1];
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Apply();
    }

    private static void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        // 換場景：清掉已經不在的石頭（還在的保留原本大小，重複套用不會把縮過的當成原本）
        var gone = new System.Collections.Generic.List<BoxCollider>();
        foreach (var kv in Originals) if (kv.Key == null) gone.Add(kv.Key);
        foreach (var k in gone) Originals.Remove(k);
        Apply();
    }

    public static void Apply()
    {
        if (!Enabled) return;
        int count = 0;
        System.Text.StringBuilder sb = null;

        foreach (SpriteRenderer sr in Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Include))
        {
            if (sr == null || sr.sprite == null) continue;
            bool isBoxStone = sr.sprite.name.IndexOf(SpriteKey, System.StringComparison.Ordinal) >= 0
                              || (sr.sprite.texture != null && sr.sprite.texture.name.IndexOf(SpriteKey, System.StringComparison.Ordinal) >= 0);
            if (!isBoxStone) continue;

            BoxCollider bc = sr.GetComponent<BoxCollider>();
            if (bc == null || bc.isTrigger) continue;

            float scaleX = Mathf.Abs(sr.transform.lossyScale.x);
            if (scaleX < 0.0001f) continue;

            Bounds sb0 = sr.sprite.bounds;   // 貼圖的本地範圍（不含縮放）
            float spriteWorldW = sb0.size.x * scaleX;
            float targetWorldW = Mathf.Max(minWidth, spriteWorldW - 2f * shrinkPerSide);
            if (targetWorldW >= spriteWorldW) continue;

            if (!Originals.ContainsKey(bc)) Originals[bc] = new Vector3[] { bc.size, bc.center };
            Vector3[] orig = Originals[bc];
            float oldWorldW = orig[0].x * scaleX;
            Vector3 size = bc.size;
            Vector3 center = bc.center;
            size.x = targetWorldW / scaleX;
            center.x = sb0.center.x;            // 左右置中在貼圖上
            bc.size = size;
            bc.center = center;
            count++;

            if (sb == null) sb = new System.Text.StringBuilder();
            sb.Append(" ").Append(sr.gameObject.name).Append(" 寬 ").Append(oldWorldW.ToString("F2"))
              .Append("→").Append(targetWorldW.ToString("F2")).Append(" 米；");
        }

        if (count > 0)
            Debug.Log("【方塊石頭】碰撞箱左右各比貼圖窄 " + shrinkPerSide.ToString("F2") + " 米（上下不動）：" + count + " 顆。" + sb);
    }
}
