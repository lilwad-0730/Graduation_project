using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// ★1108 全關卡脫困：玩家不管卡在哪一關的哪裡，都一定有辦法出來。
///
/// 兩種觸發：
///  ・手動：按住 Backspace 約 1.2 秒（畫面下方會出現進度）。任何時候、任何關卡都能用。
///  ・自動：①身體插在固體碰撞體裡、而且幾乎沒移動 1.2 秒；②上下左右四面都被擋住、按著方向鍵 1.5 秒沒移動。
///        水下已有 UnderwaterPenetrationProbe（石頭專用）時，自動偵測讓給它，只保留手動。
///
/// 救援是「逐級加重」，同一個 40 秒內連續卡住就往下一級：
///  1. 放回最近一個「確定沒插在東西裡」且離卡住處至少 1 公尺的安全位置
///  2. 放回安全位置清單裡最遠（最早）的那一個
///  3. 回存檔點救援：先「只搬人」（不死、不重置任何機關，已做過的事不會被洗掉）；
///     40 秒內再卡才走完整重生（存檔點之後的機關重置）；再不行搬回這一關起點。
///     （階梯在 PlayerRespawnSystem.RescueToCheckpoint，石頭救援也走同一條）
/// 第 3 級之後會留一份「需要人看」的錯誤回報（不會叫 AI 自動改，因為通常是場景碰撞的問題）。
/// 關掉：PlayerUnstuck.Enabled = false。
/// </summary>
public class PlayerUnstuck : MonoBehaviour
{
    public static bool Enabled = true;
    public static bool AutoDetect = true;

    [Header("手動")]
    public KeyCode manualKey = KeyCode.Backspace;
    public float manualHoldSeconds = 1.2f;

    [Header("自動偵測")]
    public float embeddedSeconds = 1.2f;
    public float pinnedSeconds = 1.5f;
    [Tooltip("插進去判定：玩家碰撞盒往內縮多少")]
    public float shrink = 0.25f;
    [Tooltip("這段時間內移動少於這麼多公尺才算沒動")]
    public float stationaryDistance = 0.4f;
    public float pinnedProbeDistance = 0.12f;

    [Header("救援")]
    public float safeRecordInterval = 0.25f;
    public int safeCapacity = 60;
    public float minRescueDistance = 1f;
    public float escalateWindow = 40f;

    private static PlayerUnstuck _inst;

    private PlayerMovement _pm;
    private Rigidbody _rb;
    private Collider _col;
    private bool _hasProbe;

    private readonly List<Vector3> _safe = new List<Vector3>();
    private float _nextRecord, _nextCheck;
    private float _embeddedT, _pinnedT, _hold;
    private Vector3 _anchor;
    private float _anchorT;
    private readonly List<float> _rescues = new List<float>();
    private string _lastEmbeddedName = "";

    private float _hintUntil;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { _inst = null; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Boot()
    {
        if (_inst != null) return;
        GameObject go = new GameObject("[脫困系統]");
        DontDestroyOnLoad(go);
        _inst = go.AddComponent<PlayerUnstuck>();
    }

    private void OnEnable() { SceneManager.sceneLoaded += OnSceneLoaded; }
    private void OnDisable() { SceneManager.sceneLoaded -= OnSceneLoaded; }

    private void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        if (m != LoadSceneMode.Single) return;
        _pm = null; _rb = null; _col = null; _hasProbe = false;
        _safe.Clear(); _rescues.Clear();
        ResetTimers();
    }

    private void ResetTimers()
    {
        _embeddedT = 0f; _pinnedT = 0f; _hold = 0f;
        if (_col != null) _anchor = _col.bounds.center;
        _anchorT = Time.unscaledTime;
    }

    private bool Bind()
    {
        if (_pm != null && _col != null) return true;
        _pm = FindFirstObjectByType<PlayerMovement>();
        if (_pm == null) return false;
        _rb = _pm.GetComponent<Rigidbody>();
        _col = _pm.GetComponent<Collider>();
        if (_col == null) _col = _pm.GetComponentInChildren<Collider>();
        _hasProbe = _pm.GetComponent<UnderwaterPenetrationProbe>() != null;
        ResetTimers();
        return _col != null;
    }

    private bool Inactive()
    {
        string n = SceneManager.GetActiveScene().name;
        if (n == GameFlow.MenuSceneName || n == "Book") return true;
        if (Time.timeScale < 0.01f) return true;
        if (PlayerRespawnSystem.IsAnyRespawning) return true;
        PlayerRespawnSystem rs = PlayerRespawnSystem.Instance;
        if (rs != null && rs.IsBusy) return true;
        if (PlayerCutsceneHold.IsHeld) return true;
        if (_pm != null && _pm.isCutsceneFrozen) return true;
        return false;
    }

    private void Update()
    {
        if (!Enabled || !Bind()) { HideUi(); return; }
        if (Inactive()) { ResetTimers(); HideUi(); return; }

        // ───── 手動：按住鍵 ─────
        if (Input.GetKey(manualKey))
        {
            _hold += Time.unscaledDeltaTime;     // 按住時的提示面板由 HangRescue 統一畫（含後面的「回主選單」倒數）
            if (_hold >= manualHoldSeconds)
            {
                _hold = 0f;
                Rescue("玩家手動脫困（按住 " + manualKey + "）", manual: true);
                return;
            }
        }
        else
        {
            _hold = 0f;
            if (Time.unscaledTime > _hintUntil) HideUi();
        }

        if (Time.unscaledTime < _nextCheck) return;
        float dt = 0.1f;
        _nextCheck = Time.unscaledTime + dt;

        // 記錄安全位置：沒插在東西裡、離上一個記錄點有一段距離
        bool embedded = IsEmbedded(out Collider ec);
        if (ec != null) _lastEmbeddedName = ec.transform.parent != null ? ec.transform.parent.name + "/" + ec.name : ec.name;
        if (!embedded && Time.unscaledTime >= _nextRecord)
        {
            _nextRecord = Time.unscaledTime + safeRecordInterval;
            Vector3 p = _pm.transform.position;
            if (_safe.Count == 0 || Vector3.Distance(_safe[_safe.Count - 1], p) > 0.3f)
            {
                _safe.Add(p);
                while (_safe.Count > safeCapacity) _safe.RemoveAt(0);
            }
        }

        // 移動量：以「錨點」為準，1.2 秒內動超過門檻就重新計時
        Vector3 c = _col.bounds.center;
        if (Vector3.Distance(c, _anchor) > stationaryDistance) { _anchor = c; _anchorT = Time.unscaledTime; _embeddedT = 0f; _pinnedT = 0f; }
        bool stationary = Time.unscaledTime - _anchorT >= 0.5f;

        if (!AutoDetect) { _embeddedT = 0f; _pinnedT = 0f; return; }
        // 水下已有石頭專用的救援（UnderwaterPenetrationProbe）：讓它先動，我這邊等久一點才補上（它救成功、位置一動這邊的計時就歸零）
        float slow = _hasProbe ? 1.7f : 1f;

        // A. 插在固體裡又沒動
        _embeddedT = (embedded && stationary) ? _embeddedT + dt : 0f;

        // B. 四面被擋 + 按著方向鍵 + 沒動
        bool pressing = Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.S)
                        || Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.DownArrow);
        _pinnedT = (pressing && stationary && IsPinned()) ? _pinnedT + dt : 0f;

        if (_embeddedT > embeddedSeconds * 0.5f * slow || _pinnedT > pinnedSeconds * 0.5f * slow)
        {
            _hintUntil = Time.unscaledTime + 0.4f;
            if (_hold <= 0f) RescuePrompt.Show("似乎被卡住了", "按住 " + manualKey + " 嘗試脫困");
        }

        if (_embeddedT >= embeddedSeconds * slow) { Rescue("身體插在「" + _lastEmbeddedName + "」裡面超過 " + (embeddedSeconds * slow).ToString("F1") + " 秒", false); return; }
        if (_pinnedT >= pinnedSeconds * slow) { Rescue("上下左右四面都被擋住、按著方向鍵 " + (pinnedSeconds * slow).ToString("F1") + " 秒沒移動", false); return; }
    }

    // ───────────── 偵測 ─────────────
    private bool Ignorable(Collider c)
    {
        if (c == null || c == _col || c.isTrigger || !c.enabled) return true;
        if (c.transform.IsChildOf(_pm.transform)) return true;
        if (Physics.GetIgnoreCollision(_col, c)) return true;                                           // 一次性平台之類，被明確設定「不碰」的
        if (Physics.GetIgnoreLayerCollision(_col.gameObject.layer, c.gameObject.layer)) return true;
        if (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic) return true;               // 會被推開的動態物體（箱子、敵人）不算卡牆
        return false;
    }

    /// <summary>
    /// 往內縮一圈的半尺寸。每個軸至少留原本的 40%：
    /// 這個專案的玩家碰撞盒在 Z 軸只有約 0.22 的半厚，直接減 0.25 會變成負數、偵測永遠失效（實測抓到的）。
    /// </summary>
    private Vector3 ShrunkHalf(Bounds b)
    {
        Vector3 e = b.extents;
        return new Vector3(Mathf.Max(e.x - shrink, e.x * 0.4f), Mathf.Max(e.y - shrink, e.y * 0.4f), Mathf.Max(e.z - shrink, e.z * 0.4f));
    }

    private bool IsEmbedded(out Collider hit)
    {
        hit = null;
        Bounds b = _col.bounds;
        Vector3 half = ShrunkHalf(b);
        foreach (Collider c in Physics.OverlapBox(b.center, half, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
        {
            if (Ignorable(c)) continue;
            hit = c;
            return true;
        }
        return false;
    }

    private static readonly Vector3[] Sides = { Vector3.up, Vector3.down, Vector3.left, Vector3.right };

    private bool IsPinned()
    {
        Bounds b = _col.bounds;
        Vector3 half = b.extents - Vector3.one * 0.02f;
        half.x = Mathf.Max(0.05f, half.x); half.y = Mathf.Max(0.05f, half.y); half.z = Mathf.Max(0.05f, half.z);
        for (int i = 0; i < Sides.Length; i++)
        {
            bool hit = false;
            foreach (RaycastHit h in Physics.BoxCastAll(b.center, half, Sides[i], Quaternion.identity, pinnedProbeDistance, ~0, QueryTriggerInteraction.Ignore))
            {
                if (Ignorable(h.collider)) continue;
                hit = true; break;
            }
            if (!hit) return false;   // 只要有一面是空的，她按另一個方向就走得出來，不算被夾住
        }
        return true;
    }

    // ───────────── 救援 ─────────────
    private int Stage()
    {
        float now = Time.unscaledTime;
        _rescues.RemoveAll(t => now - t > escalateWindow);
        return _rescues.Count + 1;
    }

    private bool Safe(Vector3 p)
    {
        // 候選點本身不能插在東西裡（用同一套判定，把碰撞盒搬到那裡測）
        Vector3 offset = p - _pm.transform.position;
        Bounds b = _col.bounds;
        Vector3 half = ShrunkHalf(b);
        foreach (Collider c in Physics.OverlapBox(b.center + offset, half, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
            if (!Ignorable(c)) return false;
        return true;
    }

    private void Rescue(string reason, bool manual)
    {
        int stage = Stage();
        _rescues.Add(Time.unscaledTime);
        Vector3 cur = _pm.transform.position;
        string how;
        bool done = false;

        if (stage == 1)
        {
            for (int i = _safe.Count - 1; i >= 0 && !done; i--)
                if (Vector3.Distance(_safe[i], cur) >= minRescueDistance && Safe(_safe[i])) { Warp(_safe[i]); how = "放回最近的安全位置"; Report(stage, reason, how, manual, cur); return; }
            // 沒有離 1 公尺以上的：只要有離 0.4 公尺以上的安全位置，也先用（放回去總比直接走整套重生好）
            float far = 0f; int fi = -1;
            for (int i = 0; i < _safe.Count; i++)
            {
                float d = Vector3.Distance(_safe[i], cur);
                if (d >= 0.4f && d > far && Safe(_safe[i])) { far = d; fi = i; }
            }
            if (fi >= 0) { Warp(_safe[fi]); how = "放回附近的安全位置（" + far.ToString("F1") + "m）"; Report(stage, reason, how, manual, cur); return; }
            stage = 2;   // 真的沒有，直接升一級
        }
        if (stage == 2)
        {
            float best = -1f; int bi = -1;
            for (int i = 0; i < _safe.Count; i++)
            {
                float d = Vector3.Distance(_safe[i], cur);
                if (d >= minRescueDistance && d > best && Safe(_safe[i])) { best = d; bi = i; }
            }
            if (bi >= 0) { Warp(_safe[bi]); how = "放回較早的安全位置（" + best.ToString("F1") + "m）"; Report(stage, reason, how, manual, cur); return; }
            stage = 3;
        }

        PlayerRespawnSystem rs = PlayerRespawnSystem.Instance;
        // 第 3 級起交給重生系統的救援階梯（只搬到存檔點 → 完整重生 → 搬回起點），升級邏輯只在那邊有一份
        if (rs != null)
        {
            how = "回存檔點救援（先只搬人、不重置世界；再卡才走完整重生）";
            Report(Mathf.Max(stage, 3), reason, how, manual, cur);
            rs.RescueToCheckpoint("脫困系統：" + reason);
            return;
        }
        how = "沒有重生系統，往上抬 3 公尺";
        Report(4, reason, how, manual, cur);
        Warp(cur + Vector3.up * 3f);
    }

    private void Warp(Vector3 target)
    {
        Vector3 bad = _pm.transform.position;
        _pm.WarpTo(target);
        if (_rb != null && !_rb.isKinematic) { _rb.linearVelocity = Vector3.zero; _rb.angularVelocity = Vector3.zero; }
        _safe.RemoveAll(p => Vector3.Distance(p, bad) < 0.8f);   // 卡住那一帶的記錄不能再拿來當安全點
        ResetTimers();
    }

    private void Report(int stage, string reason, string how, bool manual, Vector3 at)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("【脫困】第 ").Append(stage).Append(" 級：").Append(how).Append("\n  原因：").Append(reason).Append("\n  卡住位置：").Append(at.ToString("F2"));
        if (!string.IsNullOrEmpty(_lastEmbeddedName)) sb.Append("\n  附近碰撞體：").Append(_lastEmbeddedName);
        Debug.LogWarning(sb.ToString());
        RescuePrompt.Show("已嘗試脫困", how);
        _hintUntil = Time.unscaledTime + 1.2f;
        if (stage >= 3 && !manual)
            GameRecoveryGuard.ReportSoftLock("StuckPlayer", sb.ToString() + "\n  （前兩級自動脫困沒用，通常是場景碰撞或石頭殼的問題，需要人看）");
        ResetTimers();
    }

    private void HideUi() { RescuePrompt.Hide(); }
}
