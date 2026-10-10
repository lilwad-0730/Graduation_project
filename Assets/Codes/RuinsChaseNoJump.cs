using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ★1011 暫時：廢墟「石牆砸碎之後的推巨石階段」不能跳（玩家被狼追、推巨石時跳沒有意義）。
///
/// 判斷方式（事件 ＋ 位置，兩個同時成立才封鎖）：
///   1. 事件：場景裡的石牆（RuinsDoor 的 Destructible）已經砸碎。
///   2. 位置：玩家在石牆右邊（x ≥ 石牆 x − marginBeforeDoor）、而且還沒到風暴那邊（x &lt; 光球 P14 的 x）。
///   3. 沒在重生轉場、沒被劇情鎖住（那些時候本來就不能動）。
/// 為什麼不是單純「位置」或「碰撞框」：
///   ・只看位置：還沒砸石牆就走到那一段（或測試時直接把玩家放過去）也會被鎖，跳不過去就卡關。
///   ・碰撞框：重生、傳送時進出框的事件容易漏接（卡在框裡沒被放開）。
///   ・事件＋位置：重生到石牆左邊（或任何 bug 把她放回石牆前）→ 位置不成立，馬上能跳，不會卡死。
///   這個每幀重新判斷，沒有「記住狀態」，所以不會漏放開。
///
/// 場景不用改：載入有 RuinsDoor 的場景時自動啟動。
/// 關掉：RuinsChaseNoJump.Enabled = false（或把 PlayerMovement.JumpBlockedByRuinsChase 放開）。
/// </summary>
public class RuinsChaseNoJump : MonoBehaviour
{
    public static bool Enabled = true;

    /// <summary>石牆左邊多少米開始算（石牆本體的寬度要涵蓋進來）。</summary>
    public static float marginBeforeDoor = 1f;

    /// <summary>風暴路徑點名稱（推巨石階段的終點）。</summary>
    public static string endWaypointName = "P14";

    /// <summary>找不到終點路徑點時，從石牆往右算這麼遠為止。</summary>
    public static float fallbackLengthAfterDoor = 150f;

    public static bool logEvents = true;

    PlayerMovement _pm;
    Destructible _door;
    float _doorX;
    float _endX;
    bool _active;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Attach();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Attach();
    }

    static void Attach()
    {
        PlayerMovement.JumpBlockedByRuinsChase = false;   // 換場景一律先放開
        if (!Enabled) return;
        if (FindAnyObjectByType<RuinsChaseNoJump>() != null) return;
        if (FindAnyObjectByType<RuinsDoor>() == null) return;
        GameObject go = new GameObject("[RuinsChaseNoJump]");
        go.AddComponent<RuinsChaseNoJump>();
    }

    void Start()
    {
        Resolve();
    }

    void Resolve()
    {
        RuinsDoor door = FindAnyObjectByType<RuinsDoor>();
        if (door != null)
        {
            _door = door.GetComponent<Destructible>();
            _doorX = door.transform.position.x;
        }

        _endX = _doorX + fallbackLengthAfterDoor;
        GuidanceLight orb = FindAnyObjectByType<GuidanceLight>();
        if (orb != null && orb.waypoints != null)
        {
            foreach (Transform wp in orb.waypoints)
            {
                if (wp != null && wp.name == endWaypointName) { _endX = wp.position.x; break; }
            }
        }
    }

    void Update()
    {
        if (!Enabled || _door == null)
        {
            Release();
            if (_door == null && Time.frameCount % 120 == 0) Resolve();   // 石牆被重置／重建時重新找
            return;
        }
        if (_pm == null)
        {
            _pm = FindAnyObjectByType<PlayerMovement>();
            if (_pm == null) { Release(); return; }
        }

        float x = _pm.transform.position.x;
        bool block = _door.HasShattered
                     && x >= _doorX - marginBeforeDoor
                     && x < _endX
                     && !PlayerRespawnSystem.IsAnyRespawning;

        if (block != _active)
        {
            _active = block;
            PlayerMovement.JumpBlockedByRuinsChase = block;
            if (logEvents) Debug.Log("[RuinsChaseNoJump] " + (block ? "石牆已砸碎、她在推巨石的路段：不能跳" : "離開推巨石路段（或石牆還沒砸／重生）：可以跳了") + "（x " + x.ToString("F1") + "）");
        }
    }

    void Release()
    {
        if (_active)
        {
            _active = false;
            PlayerMovement.JumpBlockedByRuinsChase = false;
        }
    }

    void OnDisable()
    {
        PlayerMovement.JumpBlockedByRuinsChase = false;
    }
}
