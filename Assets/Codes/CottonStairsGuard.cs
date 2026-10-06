using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// ★1006 棉花堡金色台階：掉下去不會卡住、走在台階下面不會跑出墜落畫面。
/// M：「現在在棉花堡，人掉下台階就會卡住」「在台階下行走會觸發墜落的背景圖，但按下左鍵後，視角會回到棉花堡，主角向左移動」。
///
/// 原因（讀 SampleScene）：
/// 1. 往廢墟的墜落通道 FALLING/connect_0 (1)（Tag FallingBackground，觸發框 x 26.6～76.9、y -67.8～0.8）上緣高過棉花堡地面
///    （FloorObject/Cube (1) 頂端 y -1.69），左緣碰到第二個金色台階下的隱形牆（AllAirWall/airwall，x 26.67～27.03）。
///    走到牆邊、或掉到牆後，PlayerMovement 就當成開始墜落：鎖左右、關掉重生系統、換成墜落音樂，
///    而且「這次墜落已進過通道」的旗標要到廢墟才重設，之後真的從上層城堡跳崖時就不會再鎖。
///    真正的墜落是 SkyDiveTeleportTrigger 把她傳到 DropTarget_Clouds（50, -10.9），通道裡比落點高的部分用不到。
///    → 進場景時把 FallingBackground 觸發框的上緣降到「跳崖落點上方 fallingTopAboveDrop 米」。只改執行中的碰撞框，場景檔不動。
/// 2. 第二個金色台階（stair decided_0 (1)）右半邊伸出隱形牆外；牆後有一段地面（到 x 38.6），但第三個台階（stair decided_0 (2)，y 8.8）
///    從那裡跳不上去、隱形牆也翻不回來：從第二個台階掉下去或沒跳上第三個台階，就困在那裡。
///    → 掉進「牆的右邊、第二個台階底面以下」這一區，就白光一閃，把她放回第二個台階上重跳
///      （教學字：「過程中不會死亡／但失誤過多依然／會重新挑戰」；用白光，和第三個台階傳送上層一樣，不用死亡的黑幕）。
/// 走在第二個台階下方，鏡頭被切成墜落畫面：在 CameraTargetXFollower.GetActiveFallingBounds（fallingEntryDepth，同日修改）。
///
/// 場景不用改：載入有棉花堡金色台階（或跳崖傳送）的場景時，自動掛到 Player。
/// </summary>
[DisallowMultipleComponent]
public class CottonStairsGuard : MonoBehaviour
{
    /// <summary>整個關掉（例如要測原本的行為）：CottonStairsGuard.Enabled = false。</summary>
    public static bool Enabled = true;

    const string DefaultRetryStair = "stair decided_0 (1)";

    [Header("場景物件（照名字找；找不到就不啟用重新挑戰）")]
    [Tooltip("掉下去之後放回的台階：棉花堡第二個金色台階")]
    public string retryStairName = DefaultRetryStair;
    [Tooltip("要跳上去的下一個台階：第三個金色台階（踩到會傳送到上層城堡）")]
    public string nextStairName = "stair decided_0 (2)";
    [Tooltip("第二個台階下方的隱形牆名字（死角在它右邊）")]
    public string wallName = "airwall";

    [Header("重新挑戰區（牆的右邊、第二個台階底面以下）")]
    [Tooltip("比第二個台階的底面再低多少（米）才算掉下去")]
    public float dropBelowStair = 2f;
    [Tooltip("區域往右延伸到第三個台階右端再過去幾米（跳過頭、掉出地面右端也算）")]
    public float zoneExtraRight = 8f;
    [Tooltip("放回第二個台階時，離台階左端幾米")]
    public float retryInsetX = 1.2f;

    [Header("白光")]
    public float flashIn = 0.12f;
    public float flashHold = 0.12f;
    public float flashOut = 0.3f;
    [Tooltip("重生黑幕 Canvas 是 999、重生漫畫 1200、文字卡 10000")]
    public int sortingOrder = 1100;

    [Header("墜落通道")]
    [Tooltip("FallingBackground 觸發框的上緣，最多留到跳崖落點上方幾米（再上面的部分會碰到棉花堡地面）")]
    public float fallingTopAboveDrop = 2f;

    public bool logEvents = true;

    PlayerMovement _pm;
    Rigidbody _rb;
    Collider _body;
    bool _zoneReady;
    float _zoneMinX, _zoneMaxX, _zoneMinY, _zoneMaxY;
    Bounds _retryStair;
    bool _busy;
    float _cooldownUntil;
    int _retries;
    GameObject _flashRoot;
    Image _flash;

    // ── 自動掛載 ─────────────────────────────────────────────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        AutoAttach();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        AutoAttach();
    }

    public static bool AutoAttach()
    {
        if (!Enabled) return false;
        PlayerMovement pm = FindAnyObjectByType<PlayerMovement>();
        if (pm == null) return false;
        CottonStairsGuard existing = pm.GetComponent<CottonStairsGuard>();
        if (existing != null)
        {
            existing.Setup();   // 玩家跨場景留著：換場景時重找一次
            return false;
        }
        if (GameObject.Find(DefaultRetryStair) == null && FindAnyObjectByType<SkyDiveTeleportTrigger>() == null) return false;
        pm.gameObject.AddComponent<CottonStairsGuard>();
        Debug.Log("[CottonStairsGuard] 已自動掛到 " + pm.gameObject.name);
        return true;
    }

    // ── 本體 ─────────────────────────────────────────────────
    void Awake()
    {
        _pm = GetComponent<PlayerMovement>();
        _rb = GetComponent<Rigidbody>();
        foreach (Collider c in GetComponents<Collider>())
        {
            if (c != null && !c.isTrigger) { _body = c; break; }
        }
        if (_body == null)
        {
            foreach (Collider c in GetComponentsInChildren<Collider>())
            {
                if (c != null && !c.isTrigger) { _body = c; break; }
            }
        }
    }

    void Start()
    {
        Setup();
    }

    public void Setup()
    {
        if (!Enabled) { _zoneReady = false; return; }
        LowerFallingTops();
        BuildZone();
    }

    void OnDestroy()
    {
        if (_flashRoot != null) Destroy(_flashRoot);
    }

    // ── 1. 墜落通道的上緣降到跳崖落點附近 ─────────────────────
    void LowerFallingTops()
    {
        SkyDiveTeleportTrigger[] dives = FindObjectsByType<SkyDiveTeleportTrigger>(FindObjectsInactive.Exclude);
        if (dives == null || dives.Length == 0) return;
        foreach (BoxCollider box in FindObjectsByType<BoxCollider>(FindObjectsInactive.Exclude))
        {
            if (box == null || !box.enabled || !box.isTrigger || !box.CompareTag("FallingBackground")) continue;
            Bounds b = box.bounds;
            float limit = float.NegativeInfinity;
            foreach (SkyDiveTeleportTrigger d in dives)
            {
                if (d == null || d.targetDropPoint == null) continue;
                Vector3 p = d.targetDropPoint.position;
                if (p.x < b.min.x || p.x > b.max.x || p.y < b.min.y || p.y > b.max.y) continue;   // 落點不在這個通道裡
                limit = Mathf.Max(limit, p.y + fallingTopAboveDrop);
            }
            if (float.IsNegativeInfinity(limit) || b.max.y <= limit + 0.01f) continue;
            float z = box.transform.eulerAngles.z % 180f;
            if (z > 0.5f && z < 179.5f)
            {
                if (logEvents) Debug.LogWarning("[CottonStairsGuard] " + box.name + " 有旋轉，不調整墜落通道上緣");
                continue;
            }
            float oldTop = b.max.y;
            SetWorldTop(box, limit);
            if (logEvents) Debug.Log("[CottonStairsGuard] 墜落通道 " + box.name + " 的上緣 y " + oldTop.ToString("F2") + " → " + box.bounds.max.y.ToString("F2") + "（跳崖落點上方 " + fallingTopAboveDrop.ToString("F1") + " 米；走在棉花堡地上不會再碰到）");
        }
    }

    static void SetWorldTop(BoxCollider box, float worldTop)
    {
        Transform t = box.transform;
        Vector3 c = box.center, s = box.size;
        float la = c.y + s.y * 0.5f, lb = c.y - s.y * 0.5f;
        Vector3 wc = t.TransformPoint(c);
        float wa = t.TransformPoint(new Vector3(c.x, la, c.z)).y;
        float wb = t.TransformPoint(new Vector3(c.x, lb, c.z)).y;
        float bottomLocal = wa >= wb ? lb : la;
        float topLocal = t.InverseTransformPoint(new Vector3(wc.x, worldTop, wc.z)).y;
        float lo = Mathf.Min(bottomLocal, topLocal), hi = Mathf.Max(bottomLocal, topLocal);
        box.center = new Vector3(c.x, (lo + hi) * 0.5f, c.z);
        box.size = new Vector3(s.x, hi - lo, s.z);
    }

    // ── 2. 隱形牆後面的死角：白光放回第二個台階 ───────────────
    void BuildZone()
    {
        _zoneReady = false;
        Collider stair = FindSolid(retryStairName);
        Collider next = FindSolid(nextStairName);
        if (stair == null || next == null) return;
        Bounds s1 = stair.bounds, s2 = next.bounds;

        // 第二個台階下方的隱形牆：右緣在台階範圍內、頂端剛好在台階底面下
        Collider wall = null;
        foreach (Collider c in FindObjectsByType<Collider>(FindObjectsInactive.Exclude))
        {
            if (c == null || !c.enabled || c.isTrigger || c.gameObject.name != wallName) continue;
            Bounds w = c.bounds;
            if (w.max.x >= s1.min.x && w.max.x <= s1.max.x + 0.5f && w.max.y <= s1.min.y + 0.5f && w.max.y > s1.min.y - 3f)
            {
                wall = c;
                break;
            }
        }
        if (wall == null)
        {
            if (logEvents) Debug.Log("[CottonStairsGuard] 找不到第二個台階下方的隱形牆，不啟用重新挑戰");
            return;
        }

        // 下緣：牆底再低 2 米，但一定在跳崖落點上方（不碰到真正的墜落）
        float minY = wall.bounds.min.y - 2f;
        foreach (SkyDiveTeleportTrigger d in FindObjectsByType<SkyDiveTeleportTrigger>(FindObjectsInactive.Exclude))
        {
            if (d != null && d.targetDropPoint != null) minY = Mathf.Max(minY, d.targetDropPoint.position.y + 1f);
        }

        _zoneMinX = wall.bounds.max.x + 0.02f;
        _zoneMaxX = s2.max.x + zoneExtraRight;
        _zoneMaxY = s1.min.y - dropBelowStair;
        _zoneMinY = minY;
        _retryStair = s1;
        _zoneReady = _zoneMaxX > _zoneMinX && _zoneMaxY > _zoneMinY;
        if (logEvents && _zoneReady)
            Debug.Log("[CottonStairsGuard] 重新挑戰區 x " + _zoneMinX.ToString("F2") + "～" + _zoneMaxX.ToString("F2") + "、y " + _zoneMinY.ToString("F2") + "～" + _zoneMaxY.ToString("F2") + "；掉進去會放回 " + stair.name);
    }

    static Collider FindSolid(string objectName)
    {
        GameObject go = GameObject.Find(objectName);
        if (go == null) return null;
        foreach (Collider c in go.GetComponents<Collider>())
        {
            if (c != null && c.enabled && !c.isTrigger) return c;
        }
        return null;
    }

    void Update()
    {
        if (!_zoneReady || _busy || _pm == null || Time.time < _cooldownUntil) return;
        if (PlayerRespawnSystem.IsAnyRespawning || PlayerMovement.IsHardCutsceneLocked) return;
        Vector3 p = _pm.transform.position;
        if (p.x < _zoneMinX || p.x > _zoneMaxX || p.y < _zoneMinY || p.y > _zoneMaxY) return;
        StartCoroutine(RetryRoutine(p));
    }

    IEnumerator RetryRoutine(Vector3 fellAt)
    {
        _busy = true;
        _retries++;
        if (logEvents) Debug.Log("[CottonStairsGuard] 掉進第二個台階後面的死角（" + fellAt.x.ToString("F1") + ", " + fellAt.y.ToString("F1") + "），白光放回台階重跳（第 " + _retries + " 次）");

        // 先停住：不再往下掉，也就不會在白光期間掉進墜落通道
        bool wasKinematic = _rb != null && _rb.isKinematic;
        if (_rb != null && !_rb.isKinematic)
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.isKinematic = true;
        }
        _pm.isCutsceneFrozen = true;

        EnsureFlash();
        yield return Fade(0f, 1f, flashIn);
        yield return new WaitForSecondsRealtime(flashHold * 0.5f);

        if (_rb != null) _rb.isKinematic = wasKinematic;
        _pm.WarpTo(RetryPoint());
        _pm.freezeHorizontal = false;
        _pm.isCutsceneFrozen = true;   // WarpTo 會解鎖；白光退掉前先不讓她動
        PlayerRespawnSystem rs = _pm.GetComponent<PlayerRespawnSystem>();
        if (rs != null && !rs.enabled) rs.enabled = true;
        CameraTargetXFollower.ReacquireCamera();

        yield return new WaitForSecondsRealtime(flashHold * 0.5f);
        yield return Fade(1f, 0f, flashOut);
        if (_flashRoot != null) _flashRoot.SetActive(false);

        if (!PlayerRespawnSystem.IsAnyRespawning) _pm.isCutsceneFrozen = false;
        _cooldownUntil = Time.time + 0.5f;
        _busy = false;
    }

    Vector3 RetryPoint()
    {
        float feetOffset = 0f;
        if (_body != null) feetOffset = _pm.transform.position.y - _body.bounds.min.y;
        float x = Mathf.Min(_retryStair.min.x + retryInsetX, _retryStair.center.x);
        return new Vector3(x, _retryStair.max.y + feetOffset + 0.05f, _retryStair.center.z);
    }

    void EnsureFlash()
    {
        if (_flashRoot == null)
        {
            _flashRoot = new GameObject("[CottonStairsGuard Flash]");
            Canvas canvas = _flashRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            GameObject img = new GameObject("White");
            img.transform.SetParent(_flashRoot.transform, false);
            _flash = img.AddComponent<Image>();
            _flash.raycastTarget = false;
            RectTransform rt = _flash.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
        _flashRoot.SetActive(true);
        _flash.color = new Color(1f, 1f, 1f, 0f);
    }

    IEnumerator Fade(float from, float to, float duration)
    {
        float t = 0f;
        duration = Mathf.Max(0.01f, duration);
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            if (_flash != null) _flash.color = new Color(1f, 1f, 1f, Mathf.Lerp(from, to, t / duration));
            yield return null;
        }
        if (_flash != null) _flash.color = new Color(1f, 1f, 1f, to);
    }
}
