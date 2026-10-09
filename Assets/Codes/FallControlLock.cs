using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ★1009 墜落時不能操作（1008 會議：「廢墟 a. 墜落過程不要讓玩家操作」）。
///
/// 改動前：碰到墜落通道（Tag FallingBackground）時 PlayerMovement 會鎖左右（freezeHorizontal），
/// 但同一支程式 Update 開頭的「防卡死」（一幀移動超過 5 米就解鎖）在跳崖瞬移的那幾幀也會把這個鎖解開；
/// 碰觸事件和防卡死誰先跑看當下的幀數，順序不巧時整段墜落都能左右移動。
/// 跳崖觸發區（SkyDiveTeleportTrigger）在白霧淡出的 0.3 秒內也沒有鎖她。
///
/// 改動後：她在跳崖觸發區或墜落通道裡、而且還沒落地時，每一幀最後都把左右鎖補回去（左右、跳都不行）；
/// 落地時 PlayerMovement 本來就會解鎖，這裡也就停手。不改 PlayerMovement、不改場景。
/// 整包關掉：FallControlLock.Enabled = false。
/// </summary>
[DisallowMultipleComponent]
public class FallControlLock : MonoBehaviour
{
    public static bool Enabled = true;

    private PlayerMovement _pm;
    private Collider[] _zones = new Collider[0];
    private bool _locking;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Attach();
    }

    private static void OnSceneLoaded(Scene s, LoadSceneMode m) { Attach(); }

    private static void Attach()
    {
        if (!Enabled) return;
        PlayerMovement pm = FindAnyObjectByType<PlayerMovement>();
        if (pm == null || pm.GetComponent<FallControlLock>() != null) return;
        if (FindAnyObjectByType<SkyDiveTeleportTrigger>(FindObjectsInactive.Include) == null) return;   // 只有有跳崖的場景（SampleScene）
        pm.gameObject.AddComponent<FallControlLock>();
    }

    private void Start()
    {
        _pm = GetComponent<PlayerMovement>();
        CollectZones();
    }

    private void CollectZones()
    {
        System.Collections.Generic.List<Collider> list = new System.Collections.Generic.List<Collider>();
        foreach (SkyDiveTeleportTrigger s in FindObjectsByType<SkyDiveTeleportTrigger>(FindObjectsInactive.Exclude))
            foreach (Collider c in s.GetComponents<Collider>()) if (c != null) list.Add(c);
        GameObject[] falls = new GameObject[0];
        try { falls = GameObject.FindGameObjectsWithTag("FallingBackground"); } catch (UnityException) { }
        foreach (GameObject g in falls)
            foreach (Collider c in g.GetComponents<Collider>()) if (c != null && c.isTrigger) list.Add(c);
        _zones = list.ToArray();
    }

    private bool InZone(Vector3 p)
    {
        for (int i = 0; i < _zones.Length; i++)
        {
            Collider c = _zones[i];
            if (c == null || !c.enabled || !c.gameObject.activeInHierarchy) continue;
            Bounds b = c.bounds;   // 用執行時的範圍（CottonStairsGuard 會把墜落通道的上緣降下來）
            if (p.x >= b.min.x && p.x <= b.max.x && p.y >= b.min.y && p.y <= b.max.y) return true;
        }
        return false;
    }

    private void LateUpdate()
    {
        if (!Enabled || _pm == null) return;
        if (PlayerRespawnSystem.IsAnyRespawning) { _locking = false; return; }

        bool falling = !_pm.isGrounded && !_pm.isUnderwater && InZone(transform.position);
        if (falling)
        {
            if (!_pm.freezeHorizontal) _pm.freezeHorizontal = true;   // 防卡死解開了就補回去
            if (!_locking) { _locking = true; Debug.Log("【墜落鎖】跳崖／墜落中：左右與跳都鎖住，落地才放開"); }
        }
        else if (_locking)
        {
            _locking = false;   // 落地（PlayerMovement 自己會解鎖）或離開通道
        }
    }
}
