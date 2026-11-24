using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ★1125 水下「走到一個新範圍就自動存檔」。
///
/// 起因：水下原本只有「撿到日誌／育兒物品／吸收光絮」那一瞬間才會設重生點，之間游很遠、還沒撿到東西就溺斃，
/// 全都回到上一個撿東西的地方（開頭就是出生點），看起來像「整個水下只有一個重生點」。
///
/// 做法：玩家離上一個重生點超過 AreaMeters（預設 22 公尺）、而且現在氣還夠（窒息危險度低）、沒被演出鎖著，
/// 就把重生點更新到她現在的位置。氣快沒的時候不存（不然重生在快溺斃的地方，會一直死）。
/// 不用手掛：換到水下場景自動裝上；總開關 UnderwaterAutoCheckpoint.Enabled＝false。
/// </summary>
public class UnderwaterAutoCheckpoint : MonoBehaviour
{
    public static bool Enabled = true;
    /// <summary>離上一個重生點多遠（公尺）才換新的。</summary>
    public static float AreaMeters = 22f;
    /// <summary>窒息危險度（0＝氣滿、1＝溺斃）超過這個就不存。</summary>
    public static float MaxDanger = 0.35f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Install();
    }

    private static void OnSceneLoaded(Scene s, LoadSceneMode m) { if (m == LoadSceneMode.Single) Install(); }

    private static void Install()
    {
        if (!Enabled) return;
        if (UnderwaterSuffocationEffect.Instance == null) return;   // 不是水下
        if (FindAnyObjectByType<UnderwaterAutoCheckpoint>() != null) return;
        GameObject go = new GameObject("[水下自動存檔]");
        go.AddComponent<UnderwaterAutoCheckpoint>();
    }

    private PlayerMovement _pm;
    private PlayerRespawnSystem _rs;
    private float _next;
    private Vector3 _last;
    private bool _hasLast;

    private void Update()
    {
        if (!Enabled || Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 0.5f;
        UnderwaterSuffocationEffect fx = UnderwaterSuffocationEffect.Instance;
        if (fx == null) return;
        if (_pm == null) _pm = FindAnyObjectByType<PlayerMovement>();
        if (_pm == null) return;
        if (_rs == null) _rs = _pm.GetComponent<PlayerRespawnSystem>();
        if (_rs == null) _rs = FindAnyObjectByType<PlayerRespawnSystem>();
        if (_rs == null) return;

        if (PlayerRespawnSystem.IsAnyRespawning || _pm.IsControlLocked || _pm.isCutsceneFrozen) return;
        if (fx.Danger01 > MaxDanger) return;

        Vector3 p = _pm.transform.position;
        Vector3 from = PlayerRespawnSystem.ActiveRespawnPosition;
        if (from == Vector3.zero) from = _hasLast ? _last : p;
        if (Vector2.Distance(new Vector2(p.x, p.y), new Vector2(from.x, from.y)) < AreaMeters) return;

        _rs.SetSafeGroundPosition(p);
        _last = p; _hasLast = true;
        Debug.Log($"🚩【水下自動存檔】離上一個重生點超過 {AreaMeters:F0} 公尺、氣還夠（危險度 {fx.Danger01:F2}）→ 重生點更新到 {p}");
    }
}
