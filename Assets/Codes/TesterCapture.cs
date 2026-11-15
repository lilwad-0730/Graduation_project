using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// ★1020 測試用截圖／錄影（給發表會的海報、20 秒與一分鐘影片取畫面）。
/// 只在測試模式開著時存在（TesterMode：編輯器、Development Build、或正式版旁邊放了測試檔）；展場沒有測試檔就沒有這個功能。
///
///   F12：截一張圖（螢幕原解析度 PNG；測試檔 shotscale=2 可放大）。
///   R（或 F11）：開始／停止錄影——逐格存成 JPG（預設每秒 30 格、寬 1920；測試檔 recfps=、recwidth= 可改；
///        recsec=8 ＝錄滿 8 秒遊戲時間自動停）。★1020 Mac 的 F11 預設是「顯示桌面」，按了遊戲收不到，所以改用 R。
///        錄影時用 Time.captureFramerate 固定遊戲時間：存檔比較慢時遊戲會變慢，但存下來的畫面是平順的 30 格。
///   存到遊戲 log 的資料夾（Mac：~/Library/Logs/FKNM/我你他/shots/、rec/月日_時分秒/），不會跳 macOS 的資料夾詢問。
///   截圖、錄影時把右上角的齒輪（設定）與 GOD MODE 字樣藏起來（測試檔 hideui=0 就不藏）。
///   ★1111 測試檔 reccursor=1：錄下來的畫面畫出滑鼠游標，按左鍵時多一圈淡金色的圈（遊戲畫面本身抓不到系統游標；
///        給實機測試影片看得出滑鼠在哪、點了哪裡）。
/// </summary>
public class TesterCapture : MonoBehaviour
{
    private static TesterCapture _inst;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (!TesterMode.Enabled || _inst != null) return;
        GameObject go = new GameObject("★1020_TesterCapture");
        DontDestroyOnLoad(go);
        _inst = go.AddComponent<TesterCapture>();
    }

    private bool _rec;
    private int _frame;
    private string _recDir;
    private int _outW;
    private int _autoStopFrames;
    private RenderTexture _full, _small;
    private Texture2D _tex;
    private bool _busyShot;
    private bool _drawCursor;              // ★1111 reccursor=1
    private int _clickFrame = -9999;       // ★1111 最近一次按左鍵是第幾格（畫點擊圈用）
    private static readonly Color RingColor = new Color(1f, 0.82f, 0.35f);

    private readonly List<Behaviour> _hiddenUi = new List<Behaviour>();
    private readonly List<PlayerPetrification> _hiddenGod = new List<PlayerPetrification>();

    private void Update()
    {
        if (_rec && Input.GetMouseButtonDown(0)) _clickFrame = _frame;   // ★1111 點擊圈
        if (Input.GetKeyDown(KeyCode.F12) && !_busyShot) StartCoroutine(Shot());
        if (Input.GetKeyDown(KeyCode.F11) || Input.GetKeyDown(KeyCode.R))
        {
            if (_rec) StopRec();
            else StartRec();
        }
    }

    private static string BaseDir()
    {
        string d = null;
        try { d = Path.GetDirectoryName(Application.consoleLogPath); } catch { }
        if (string.IsNullOrEmpty(d)) d = Application.persistentDataPath;
        return d;
    }

    private IEnumerator Shot()
    {
        _busyShot = true;
        HideUi(true);
        yield return null;                       // 藏起來的東西下一格才生效
        yield return new WaitForEndOfFrame();
        string dir = Path.Combine(BaseDir(), "shots");
        try
        {
            Directory.CreateDirectory(dir);
            int scale = Mathf.Clamp(Mathf.RoundToInt(TesterMode.OptionFloat("shotscale", 1f)), 1, 4);
            string path = Path.Combine(dir, "shot_" + System.DateTime.Now.ToString("MMdd_HHmmss_fff") + ".png");
            if (scale > 1) ScreenCapture.CaptureScreenshot(path, scale);
            else
            {
                Texture2D t = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(path, t.EncodeToPNG());
                Destroy(t);
            }
            Debug.LogWarning("【截圖】★1020 F12：" + path + "（" + Screen.width + "x" + Screen.height + (scale > 1 ? " ×" + scale : "") + "）");
        }
        catch (System.Exception e) { Debug.LogWarning("【截圖】失敗：" + e.Message); }
        yield return null;                       // shotscale>1 時截圖在這一格結束才寫檔，多等一格再把 UI 還原
        if (!_rec) HideUi(false);
        _busyShot = false;
    }

    private void StartRec()
    {
        float fps = Mathf.Clamp(TesterMode.OptionFloat("recfps", 30f), 10f, 60f);
        _outW = Mathf.Clamp(Mathf.RoundToInt(TesterMode.OptionFloat("recwidth", 1920f)), 640, 3840);
        _recDir = Path.Combine(BaseDir(), "rec", System.DateTime.Now.ToString("MMdd_HHmmss"));
        try { Directory.CreateDirectory(_recDir); }
        catch (System.Exception e) { Debug.LogWarning("【錄影】建資料夾失敗：" + e.Message); return; }
        Time.captureFramerate = Mathf.RoundToInt(fps);
        _rec = true;
        _frame = 0;
        _autoStopFrames = Mathf.RoundToInt(TesterMode.OptionFloat("recsec", 0f) * Time.captureFramerate);
        string rc = TesterMode.Option("reccursor");
        _drawCursor = rc != null && rc.Trim() == "1";
        _clickFrame = -9999;
        HideUi(true);
        StartCoroutine(RecLoop());
        Debug.LogWarning("【錄影】★1020 開始：" + _recDir + "（每秒 " + Time.captureFramerate + " 格、寬 " + _outW + (_drawCursor ? "、畫游標" : "") + "）");
    }

    private void StopRec()
    {
        _rec = false;
        Time.captureFramerate = 0;
        HideUi(false);
        Debug.LogWarning("【錄影】★1020 停止：" + _recDir + "，共 " + _frame + " 格");
    }

    private IEnumerator RecLoop()
    {
        yield return null;                       // 藏 UI 下一格才生效
        while (_rec)
        {
            yield return new WaitForEndOfFrame();
            if (!_rec) break;
            try { CaptureFrame(); }
            catch (System.Exception e)
            {
                Debug.LogWarning("【錄影】這一格失敗，停止：" + e.Message);
                StopRec();
                yield break;
            }
        }
    }

    private void CaptureFrame()
    {
        int sw = Screen.width, sh = Screen.height;
        int outH = Mathf.RoundToInt(_outW * (float)sh / Mathf.Max(1, sw));
        outH -= outH % 2;
        if (_full == null || _full.width != sw || _full.height != sh)
        {
            if (_full != null) _full.Release();
            _full = new RenderTexture(sw, sh, 0, RenderTextureFormat.ARGB32);
        }
        if (_small == null || _small.width != _outW || _small.height != outH)
        {
            if (_small != null) _small.Release();
            _small = new RenderTexture(_outW, outH, 0, RenderTextureFormat.ARGB32);
            _tex = new Texture2D(_outW, outH, TextureFormat.RGB24, false);
        }
        ScreenCapture.CaptureScreenshotIntoRenderTexture(_full);
        // 有些繪圖 API（Metal）抓下來是上下顛倒的
        if (SystemInfo.graphicsUVStartsAtTop) Graphics.Blit(_full, _small, new Vector2(1f, -1f), new Vector2(0f, 1f));
        else Graphics.Blit(_full, _small);
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = _small;
        _tex.ReadPixels(new Rect(0, 0, _outW, outH), 0, 0);
        if (_drawCursor) DrawCursor(sw, sh, outH);   // ★1111
        _tex.Apply(false);
        RenderTexture.active = prev;
        File.WriteAllBytes(Path.Combine(_recDir, "f_" + _frame.ToString("00000") + ".jpg"), _tex.EncodeToJPG(90));
        _frame++;
        if (_frame % 150 == 0) Debug.Log("【錄影】" + _frame + " 格");
        if (_autoStopFrames > 0 && _frame >= _autoStopFrames) StopRec();
    }

    // ★1111 經典箭頭游標（X＝黑邊、.＝白），尖端在第一列第一格；往右、往下長
    private static readonly string[] CursorMask =
    {
        "X",
        "XX",
        "X.X",
        "X..X",
        "X...X",
        "X....X",
        "X.....X",
        "X......X",
        "X.......X",
        "X........X",
        "X.........X",
        "X......XXXXX",
        "X...X..X",
        "X..XX..X",
        "X.X  X..X",
        "XX   X..X",
        "X     X..X",
        "      X..X",
        "       XX",
    };

    /// <summary>★1111 把滑鼠游標（和剛按下左鍵時的一圈）畫進這一格（_tex 已讀好、左下角為原點）。</summary>
    private void DrawCursor(int sw, int sh, int outH)
    {
        if (!Cursor.visible) return;
        Vector3 m = Input.mousePosition;
        if (m.x < 0f || m.y < 0f || m.x > sw || m.y > sh) return;
        int cx = Mathf.RoundToInt(m.x * _outW / Mathf.Max(1, sw));
        int cy = Mathf.RoundToInt(m.y * outH / Mathf.Max(1, sh));
        float u = outH / 1080f;

        // 點擊圈：按下後 14 格內，從小圈擴大、淡掉
        int since = _frame - _clickFrame;
        if (since >= 0 && since < 14)
        {
            float t = since / 14f;
            float r = Mathf.Lerp(8f, 38f, t) * u;
            float th = 3.5f * u;
            float a = 0.9f * (1f - t);
            int R = Mathf.CeilToInt(r + th + 1f);
            int x0 = Mathf.Max(0, cx - R), y0 = Mathf.Max(0, cy - R);
            int x1 = Mathf.Min(_outW, cx + R + 1), y1 = Mathf.Min(outH, cy + R + 1);
            if (x1 > x0 && y1 > y0)
            {
                int bw = x1 - x0, bh = y1 - y0;
                Color[] px = _tex.GetPixels(x0, y0, bw, bh);
                for (int j = 0; j < bh; j++)
                    for (int i = 0; i < bw; i++)
                    {
                        float dx = x0 + i - cx, dy = y0 + j - cy;
                        float d = Mathf.Abs(Mathf.Sqrt(dx * dx + dy * dy) - r);
                        float cov = Mathf.Clamp01(th * 0.5f + 0.5f - d);
                        if (cov <= 0f) continue;
                        int k = j * bw + i;
                        px[k] = Color.Lerp(px[k], RingColor, cov * a);
                    }
                _tex.SetPixels(x0, y0, bw, bh, px);
            }
        }

        // 游標：尖端對準滑鼠位置
        int s = Mathf.Max(1, Mathf.RoundToInt(outH / 540f));
        int rows = CursorMask.Length, cols = 12;
        int cx0 = Mathf.Max(0, cx), cx1 = Mathf.Min(_outW, cx + cols * s);
        int cy0 = Mathf.Max(0, cy - rows * s + 1), cy1 = Mathf.Min(outH, cy + 1);
        if (cx1 <= cx0 || cy1 <= cy0) return;
        int w = cx1 - cx0, h = cy1 - cy0;
        Color[] cp = _tex.GetPixels(cx0, cy0, w, h);
        for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++)
            {
                int row = (cy - (cy0 + j)) / s, col = (cx0 + i - cx) / s;
                if (row < 0 || row >= rows) continue;
                string line = CursorMask[row];
                if (col < 0 || col >= line.Length) continue;
                char c = line[col];
                if (c == 'X') cp[j * w + i] = Color.black;
                else if (c == '.') cp[j * w + i] = Color.white;
            }
        _tex.SetPixels(cx0, cy0, w, h, cp);
    }

    /// <summary>藏起／還原右上角的齒輪（設定）與 GOD MODE 字樣。</summary>
    private void HideUi(bool hide)
    {
        string opt = TesterMode.Option("hideui");
        if (opt != null && opt.Trim() == "0") return;
        if (hide)
        {
            foreach (PlayerPetrification p in FindObjectsByType<PlayerPetrification>(FindObjectsInactive.Exclude))
                if (p != null && p.showOnScreenIndicator) { p.showOnScreenIndicator = false; _hiddenGod.Add(p); }
            foreach (Canvas c in FindObjectsByType<Canvas>(FindObjectsInactive.Exclude))
            {
                if (c == null || !c.enabled) continue;
                string root = c.transform.root.name;
                // 齒輪是 SettingsHudGearwheelPresenter 在執行時自己生的根物件 GearwheelHUDCanvas
                if (root.Contains("InGameSettings") || root.StartsWith("SETTING") || root.Contains("Gearwheel"))
                { c.enabled = false; _hiddenUi.Add(c); }
            }
        }
        else
        {
            foreach (PlayerPetrification p in _hiddenGod) if (p != null) p.showOnScreenIndicator = true;
            foreach (Behaviour b in _hiddenUi) if (b != null) b.enabled = true;
            _hiddenGod.Clear();
            _hiddenUi.Clear();
        }
    }

    private void OnDestroy()
    {
        if (_rec) Time.captureFramerate = 0;
        if (_full != null) _full.Release();
        if (_small != null) _small.Release();
    }
}
