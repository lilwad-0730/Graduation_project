using System;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// ★除錯探針 —— 問題查完之後可以整個檔案刪掉。
///
/// 把玩家與演出的實際狀態寫進 &lt;專案資料夾&gt;/Logs/wonita_debug.log，
/// 這樣不用靠 Console 截圖（Console 只顯示訊息的前兩行，長的會被截掉）
/// 也能看到跑起來當下的真實數值。
///
/// 狀態只在「有變化」時才寫一行，所以檔案不會爆掉。
/// 不需要掛在任何物件上，會自己啟動。
/// </summary>
public class WoNiTa_DebugProbe : MonoBehaviour
{
    private static WoNiTa_DebugProbe _inst;
    private static string _path;
    private static readonly StringBuilder _buf = new StringBuilder();

    private PlayerMovement _pm;
    private float _nextLookup;
    private string _last = "";
    private float _nextFlush;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (_inst != null) return;

        GameObject go = new GameObject("~WoNiTa_DebugProbe");
        _inst = go.AddComponent<WoNiTa_DebugProbe>();
        DontDestroyOnLoad(go);

        try
        {
            string dir = Path.Combine(Application.dataPath, "../Logs");
            Directory.CreateDirectory(dir);
            _path = Path.Combine(dir, "wonita_debug.log");
            File.AppendAllText(_path,
                "\n===== 開始 " + DateTime.Now.ToString("MM-dd HH:mm:ss")
                + "  場景=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name + " =====\n");
        }
        catch (Exception e)
        {
            Debug.LogWarning("【探針】開檔失敗：" + e.Message);
            _path = null;
        }

        Application.logMessageReceived += OnLog;
    }

    private static void OnLog(string msg, string stack, LogType type)
    {
        if (_path == null || msg == null) return;

        bool keep = type == LogType.Error || type == LogType.Exception || type == LogType.Warning
                    || msg.Contains("跳不了") || msg.Contains("鏡牆") || msg.Contains("影子怪物")
                    || msg.Contains("重生") || msg.Contains("死亡") || msg.Contains("觸發點");
        if (!keep) return;

        Write("LOG|" + type + "|" + msg.Replace("\n", " / "));
    }

    private static void Write(string line)
    {
        lock (_buf)
        {
            _buf.Append(DateTime.Now.ToString("HH:mm:ss.ff")).Append(' ').Append(line).Append('\n');
            if (_buf.Length > 60000) _buf.Length = 0;   // 保險，別無限長
        }
    }

    private void Update()
    {
        string snap = Snapshot();
        if (snap != _last)
        {
            Write("ST |" + snap);
            _last = snap;
        }

        if (Time.unscaledTime >= _nextFlush)
        {
            Flush();
            _nextFlush = Time.unscaledTime + 1f;
        }
    }

    private void OnApplicationQuit() { Flush(); }
    private void OnDisable() { Flush(); }

    private static void Flush()
    {
        if (_path == null) return;
        string s;
        lock (_buf)
        {
            if (_buf.Length == 0) return;
            s = _buf.ToString();
            _buf.Length = 0;
        }
        try { File.AppendAllText(_path, s); } catch { }
    }

    private static string B(bool b) { return b ? "T" : "F"; }

    private string Snapshot()
    {
        if (_pm == null && Time.unscaledTime >= _nextLookup)
        {
            _pm = FindFirstObjectByType<PlayerMovement>();
            _nextLookup = Time.unscaledTime + 1f;
        }

        string pos = "-";
        string grounded = "?", frozen = "?", water = "?";
        if (_pm != null)
        {
            Vector3 p = _pm.transform.position;
            pos = p.x.ToString("F1") + "," + p.y.ToString("F1");
            grounded = B(_pm.isGrounded);
            frozen = B(_pm.isCutsceneFrozen);
            water = B(_pm.isUnderwater);
        }

        string monster = "無";
        if (ShadowMonsterController.Instance != null)
            monster = ShadowMonsterController.Instance.currentState.ToString();

        // 鏡牆：GameObject.Find 只找得到「啟用中」的物件
        GameObject wall = GameObject.Find("mirror wall_001");
        string wallState = "不在場上";
        if (wall != null)
        {
            int on = 0, all = 0;
            foreach (var r in wall.GetComponentsInChildren<Renderer>(true)) { all++; if (r.enabled) on++; }
            int cols = 0;
            foreach (var c in wall.GetComponentsInChildren<Collider>(true)) { if (c.enabled) cols++; }
            wallState = "在場上 Renderer開=" + on + "/" + all + " Collider開=" + cols;
        }

        return "位置=" + pos
             + " 地=" + grounded
             + " froz=" + frozen
             + " hard=" + B(PlayerMovement.IsHardCutsceneLocked)
             + "(生" + B(PlayerRespawnSystem.IsAnyRespawning)
             + " 鏡" + B(MirrorWallAbsorbCutscene.IsAnyCutsceneRunning)
             + " 怪" + B(ShadowMonsterController.IsRevealRunning)
             + " 卡" + B(StoryCardPlayer.Instance != null && StoryCardPlayer.Instance.IsPlaying) + ")"
             + " 水=" + water
             + " 怪物=" + monster
             + " timeScale=" + Time.timeScale.ToString("F2")
             + " 鏡牆=" + wallState;
    }
}
