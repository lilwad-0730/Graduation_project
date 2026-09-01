using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// ★除錯／監測探針 —— 問題查完之後可以整個檔案刪掉。
///
/// 1. 把玩家與演出的實際狀態寫進 <專案資料夾>/Logs/wonita_debug.log
///    （Console 只顯示訊息前兩行，長訊息會被截掉，寫檔才看得到全貌）。
/// 2. 每隔幾秒把遊戲畫面縮小存成 JPG 到 <專案資料夾>/Logs/frames/，
///    給遠端監測用，跑完整輪也就幾十 MB。
///
/// 狀態行只在「有變化」時寫，另外每 2 秒補一行心跳（帶座標）。
/// 不需要掛在任何物件上，會自己啟動。
/// </summary>
public class WoNiTa_DebugProbe : MonoBehaviour
{
    private const float FrameEverySeconds = 6f;   // 畫面快照間隔
    private const float HeartbeatSeconds  = 2f;   // 狀態心跳間隔

    private static WoNiTa_DebugProbe _inst;
    private static string _path;
    private static string _frameDir;
    private static readonly StringBuilder _buf = new StringBuilder();

    private PlayerMovement _pm;
    private float _nextLookup;
    private string _lastCore = "";
    private float _nextBeat;
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
            _frameDir = Path.Combine(dir, "frames");
            Directory.CreateDirectory(_frameDir);
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
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode m)
    {
        Write("SCN|載入場景 " + s.name);
        Flush();
    }

    private static void OnLog(string msg, string stack, LogType type)
    {
        if (_path == null || msg == null) return;

        // 警告以上全收；一般 Log 只收帶遊戲標籤的（【…】或 [xxx] 開頭）與關鍵字
        bool keep = type == LogType.Error || type == LogType.Exception || type == LogType.Warning
                    || msg.Contains("【") || msg.StartsWith("[")
                    || msg.Contains("跳不了") || msg.Contains("鏡牆") || msg.Contains("重生")
                    || msg.Contains("死亡") || msg.Contains("燭火") || msg.Contains("結局")
                    || msg.Contains("轉場") || msg.Contains("章") || msg.Contains("繪本");
        if (!keep) return;

        string line = "LOG|" + type + "|" + msg.Replace("\n", " / ");
        if ((type == LogType.Error || type == LogType.Exception) && !string.IsNullOrEmpty(stack))
        {
            string st = stack.Replace("\n", " ⇐ ");
            if (st.Length > 900) st = st.Substring(0, 900);
            line += " ◆堆疊: " + st;
        }
        Write(line);
    }

    private static void Write(string line)
    {
        lock (_buf)
        {
            _buf.Append(DateTime.Now.ToString("HH:mm:ss.ff")).Append(' ').Append(line).Append('\n');
            if (_buf.Length > 120000) _buf.Length = 0;   // 保險，別無限長
        }
    }

    private void Start()
    {
        StartCoroutine(FrameLoop());
    }

    private void Update()
    {
        string core = SnapshotCore();
        bool changed = core != _lastCore;
        if (changed || Time.unscaledTime >= _nextBeat)
        {
            Write("ST |" + PosText() + " " + core);
            _lastCore = core;
            _nextBeat = Time.unscaledTime + HeartbeatSeconds;
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

    // ── 畫面快照 ─────────────────────────────────

    private IEnumerator FrameLoop()
    {
        var eof = new WaitForEndOfFrame();
        while (true)
        {
            yield return new WaitForSecondsRealtime(FrameEverySeconds);
            yield return eof;
            try { SaveFrame(); }
            catch (Exception e) { Write("FRM|失敗 " + e.Message); }
        }
    }

    private void SaveFrame()
    {
        if (_frameDir == null) return;

        Texture2D full = ScreenCapture.CaptureScreenshotAsTexture();
        int dw = 960;
        int dh = Mathf.Max(1, Mathf.RoundToInt(full.height * (float)dw / Mathf.Max(1, full.width)));

        RenderTexture rt = RenderTexture.GetTemporary(dw, dh, 0);
        Graphics.Blit(full, rt);
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        Texture2D small = new Texture2D(dw, dh, TextureFormat.RGB24, false);
        small.ReadPixels(new Rect(0, 0, dw, dh), 0, 0);
        small.Apply(false);
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);

        byte[] jpg = small.EncodeToJPG(62);
        Destroy(full);
        Destroy(small);

        string name = "f_" + DateTime.Now.ToString("HHmmss") + ".jpg";
        File.WriteAllBytes(Path.Combine(_frameDir, name), jpg);
        Write("FRM|" + name);
    }

    // ── 狀態組字 ─────────────────────────────────

    private static string B(bool b) { return b ? "T" : "F"; }

    private string PosText()
    {
        if (_pm == null) return "位置=-";
        Vector3 p = _pm.transform.position;
        return "位置=" + p.x.ToString("F1") + "," + p.y.ToString("F1");
    }

    /// <summary>不含座標的狀態（座標另外印，免得移動時每幀都算「有變化」）。</summary>
    private string SnapshotCore()
    {
        if (_pm == null && Time.unscaledTime >= _nextLookup)
        {
            _pm = FindFirstObjectByType<PlayerMovement>();
            _nextLookup = Time.unscaledTime + 1f;
        }

        string grounded = "?", frozen = "?", water = "?";
        if (_pm != null)
        {
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

        return "場景=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name
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
