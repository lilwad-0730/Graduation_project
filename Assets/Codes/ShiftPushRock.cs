using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ★1115 棉花堡的石頭（Pushable）改成「要按住 Shift 才推得動、拉得動」。
///
///  規則（跟 PlayerMovement 的抓取機制一致，沒有第二套）：
///   ・沒按 Shift：石頭水平方向鎖死（Freeze Position X），玩家頂著它走＝撞牆，不會有推動動畫、石頭也不會動。重力照樣作用（掉下去、站上去都正常）。
///   ・按住 Shift：PlayerMovement 本來的抓取（TryGrabObject）會把石頭變 kinematic 掛在玩家身上一起走，這時候我不插手。
///   ・放開 Shift：石頭回到非 kinematic，這支腳本馬上又把水平鎖回去，所以放手後不會多滑一段。
///  PlayerMovement 用 IsShiftOnly() 判斷「這顆要 Shift 才能推」：沒抓著時，它不算「前方可推物」
///  （不播推動動畫、不被當成可穿過的東西），避免「動畫在推、石頭不動」的矛盾。
///
///  範圍：只有棉花堡（場景名含 CottonCastle）。廢墟的巨石（RollingRockVisual）是另一套推法，不受影響。
///  其他場景要用：手動把這個元件掛到石頭上即可。
///  純程式自動掛上，不用改場景檔。
/// </summary>
[DisallowMultipleComponent]
public class ShiftPushRock : MonoBehaviour
{
    [Tooltip("取消勾選＝這顆石頭回到不用 Shift 也能推")]
    public bool requireShift = true;
    [Tooltip("頂著石頭走超過這麼多秒、卻沒按 Shift，就跳出提示")]
    public float hintAfterSeconds = 0.6f;

    private Rigidbody _rb;
    private float _bumpTime;
    private float _restTime;
    private bool _weFroze;   // 這顆是被我固定住的（不是本來就 kinematic）

    /// <summary>目前這顆石頭需要按住 Shift 才能推嗎？</summary>
    public bool NeedsShift => enabled && requireShift;

    /// <summary>給 PlayerMovement 用：這個物件（或它的父層）是不是「要 Shift 才能推」的石頭。</summary>
    public static bool IsShiftOnly(GameObject go, Collider col)
    {
        ShiftPushRock r = null;
        if (col != null && col.attachedRigidbody != null) r = col.attachedRigidbody.GetComponent<ShiftPushRock>();
        if (r == null && go != null) r = go.GetComponentInParent<ShiftPushRock>();
        if (r == null && col != null) r = col.GetComponentInParent<ShiftPushRock>();
        return r != null && r.NeedsShift;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        AttachAll(SceneManager.GetActiveScene());
    }

    private static void OnSceneLoaded(Scene s, LoadSceneMode m) { AttachAll(s); }

    private static void AttachAll(Scene s)
    {
        if (s.name.IndexOf("CottonCastle", System.StringComparison.OrdinalIgnoreCase) < 0) return;

        // 已經是 Pushable 的石頭
        foreach (GameObject go in GameObject.FindGameObjectsWithTag("Pushable"))
            Attach(go);
        // 之後才會被解鎖變成 Pushable 的石頭（光球解鎖型）
        foreach (UnlockableMovableObject u in FindObjectsByType<UnlockableMovableObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            Attach(u.gameObject);
    }

    private static void Attach(GameObject go)
    {
        if (go == null || go.GetComponent<ShiftPushRock>() != null) return;
        if (go.GetComponent<RollingRockVisual>() != null) return;   // 廢墟那套巨石不碰
        go.AddComponent<ShiftPushRock>();
    }

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        if (_rb == null) _rb = GetComponentInParent<Rigidbody>();
    }

    private void FixedUpdate()
    {
        if (_rb == null || !requireShift) return;

        bool grabbed = IsGrabbed();
        if (grabbed != _wasGrabbed)
        {
            _wasGrabbed = grabbed;
            if (grabbed) OnGrabStart(); else OnGrabEnd();
        }
        if (grabbed) return;   // 被玩家抓著（掛在玩家身上、kinematic）時不插手

        // ★ 不能用 Freeze Position X：實測（玩家 6 公尺/秒撞上去）鎖 X 的石頭，玩家會直接穿過去（石頭很薄、玩家碰撞框很深，物理引擎改往 Z 方向分開）。
        //   改成：石頭站穩在地上就設成 kinematic（固定不動的實心障礙，玩家撞上去是牆）；按 Shift 抓取時 PlayerMovement 本來就會設 kinematic 掛到玩家身上。
        //   放手後這裡下一個物理步又把它固定住，所以不會多滑一段。還在空中／掉落中就維持一般剛體讓它落下，橫向速度歸零。
        if (_rb.isKinematic) return;
        // 「站穩」＝速度幾乎是 0 維持一小段時間（空中的石頭有重力，速度一定不是 0；原本用往下射線判斷，石頭壓在斜面／別條 Z 上時射不到，會誤判成沒站穩）
        _restTime = _rb.linearVelocity.sqrMagnitude < 0.0004f ? _restTime + Time.fixedDeltaTime : 0f;
        if (_restTime >= 0.15f)
        {
            _restTime = 0f;
            _rb.linearVelocity = Vector3.zero;
            _rb.isKinematic = true;
            _weFroze = true;
        }
        else if (Mathf.Abs(_rb.linearVelocity.x) > 0.001f)
        {
            _rb.linearVelocity = new Vector3(0f, _rb.linearVelocity.y, _rb.linearVelocity.z);
        }
    }

    // ───────── 抓著拖動時的順暢處理 ─────────
    // 抓著的石頭是掛在玩家身上（成了玩家碰撞的一部分），拖動時石頭底面貼著地面磨：
    //  ・石頭預設有摩擦力 → 拖起來一頓一頓；
    //  ・地面是好幾塊碰撞框拼的，石頭底邊會卡在接縫上（鬼碰撞）→ 突然卡住。
    // 做法：抓起來的當下換成零摩擦材質、整顆抬高一點點；放手時往下貼回地面、換回原本的材質並固定住。
    [Tooltip("抓著拖動時石頭抬高多少公尺（避免底邊卡在地面接縫）")]
    public float carryLift = 0.04f;
    private bool _wasGrabbed;
    private PhysicsMaterial _origMat;
    private Collider _matCol;
    private static PhysicsMaterial _slick;

    private void OnGrabStart()
    {
        if (_slick == null)
        {
            _slick = new PhysicsMaterial("ShiftRockSlick")
            {
                dynamicFriction = 0f, staticFriction = 0f, bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum, bounceCombine = PhysicsMaterialCombine.Minimum
            };
        }
        _matCol = GetComponent<Collider>();
        if (_matCol == null) _matCol = GetComponentInChildren<Collider>();
        if (_matCol != null) { _origMat = _matCol.sharedMaterial; _matCol.sharedMaterial = _slick; }
        transform.position += Vector3.up * carryLift;
    }

    private void OnGrabEnd()
    {
        if (_matCol != null) _matCol.sharedMaterial = _origMat;
        _matCol = null;
        _restTime = 0f;
        // 往下貼回地面：腳下 0.6 公尺內有實體就貼上去並固定；沒有（被拉到邊緣外）就讓它當一般剛體掉下去
        Collider col = GetComponent<Collider>();
        if (col == null) col = GetComponentInChildren<Collider>();
        if (col == null) return;
        Bounds b = col.bounds;
        Vector3 half = new Vector3(b.extents.x * 0.9f, 0.02f, Mathf.Max(0.05f, b.extents.z * 0.9f));
        float best = float.MaxValue;
        foreach (RaycastHit h in Physics.BoxCastAll(new Vector3(b.center.x, b.min.y + 0.05f, b.center.z), half, Vector3.down, Quaternion.identity, 0.6f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (h.collider == null || h.collider.transform.IsChildOf(transform)) continue;
            if (h.collider.GetComponentInParent<PlayerMovement>() != null) continue;
            if (h.distance < best) best = h.distance;
        }
        if (best < float.MaxValue)
        {
            transform.position += Vector3.down * Mathf.Max(0f, best - 0.04f);
            Physics.SyncTransforms();
            _rb.linearVelocity = Vector3.zero;
            _rb.isKinematic = true;
            _weFroze = true;
        }
    }

    private bool IsGrabbed()
    {
        Transform p = transform.parent;
        return p != null && p.GetComponentInParent<PlayerMovement>() != null;
    }

    private void OnDisable()
    {
        // 這個規則被關掉（或物件被移除）：把我固定住的石頭放回去，變回可以直接推
        if (_rb != null && _weFroze && !IsGrabbed()) _rb.isKinematic = false;
        _weFroze = false;
    }

    // 玩家頂著石頭、又沒按 Shift：一段時間後提示一次
    private void OnCollisionStay(Collision c)
    {
        if (!requireShift || _rb == null || IsGrabbed()) return;
        PlayerMovement pm = c.gameObject.GetComponent<PlayerMovement>();
        if (pm == null) pm = c.gameObject.GetComponentInParent<PlayerMovement>();
        if (pm == null || !pm.isGrounded) { _bumpTime = 0f; return; }

        float input = pm.CurrentMoveInput;
        float toRock = Mathf.Sign(transform.position.x - pm.transform.position.x);
        if (Mathf.Abs(input) > 0.05f && Mathf.Sign(input) == toRock && !Input.GetKey(KeyCode.LeftShift))
        {
            _bumpTime += Time.deltaTime;
            if (_bumpTime >= hintAfterSeconds) InputHint.Once("shift_push_rock", "按住 Shift　推拉石頭", 4f);
        }
        else
        {
            _bumpTime = 0f;
        }
    }

    private void OnCollisionExit(Collision c) { _bumpTime = 0f; }
}
