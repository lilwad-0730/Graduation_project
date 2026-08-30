using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// 我你他　片尾感謝名單（電影式上捲）
///
/// 翻完最後一頁再翻一次 → 黑幕 → 名單由下往上捲 → 停在標題卡。
///
/// 【怎麼用】
///   把這支掛在 Book 物件上（跟 PageBook 同一個），其他都不用設。
///   Canvas、黑幕、文字都在執行時自己建。名單內容已內建。
///
/// 【名單來源】
///   組員與指導老師：《文字施工單_給美術與程式》《Codex 任務書》兩份交叉核對一致。
///   素材與音樂：直接掃 Assets 目錄列出來的，不是猜的。
///   ★沒有寫在專案文件裡的人名，我不會自己加。要補請自己在 Inspector 加。
/// </summary>
[DisallowMultipleComponent]
public class EndCredits : MonoBehaviour
{
    // ══════════════════════════════════════════
    // 名單資料
    // ══════════════════════════════════════════
    public enum Kind
    {
        Title,      // 大標題，置中
        Section,    // 分類標，置中，小字灰色
        Name,       // 單獨一個名字，置中
        Role,       // 兩欄：左＝職務（右對齊）　右＝名字（左對齊）
        Paragraph,  // 整段文字，置中，會自動換行
        Gap         // 空白，用 gap 控制高度
    }

    [System.Serializable]
    public class Entry
    {
        public Kind kind = Kind.Name;
        [TextArea(1, 4)] public string left = "";
        [TextArea(1, 4)] public string right = "";
        public float gap = 40f;   // 只有 Kind.Gap 會用到
    }

    [Header("名單內容")]
    [Tooltip("優先讀 Resources 裡的這個文字檔（不用寫 .txt）。找不到才用下面的 entries")]
    public string creditsResource = "Credits";
    [Tooltip("creditsResource 找不到時才用這個。留空會用內建的")]
    public List<Entry> entries = new List<Entry>();

    // ══════════════════════════════════════════
    // 觸發
    // ══════════════════════════════════════════
    [Header("觸發")]
    [Tooltip("留空會自動在同物件或場景裡找 PageBook")]
    public PageBook book;
    [Tooltip("翻到最後一頁後，再按一次「下一頁」就開始捲名單")]
    public bool armOnLastPage = true;
    [Tooltip("大於 0＝到最後一頁後等這麼多秒自動開始（展場用）。0＝只等按鍵")]
    public float autoRollAfterSeconds = 0f;

    // ══════════════════════════════════════════
    // 結局流程（玻璃館結束 → 這裡）
    // ══════════════════════════════════════════
    [Header("結局流程")]
    [Tooltip("結尾漫畫的起始頁（0 起算）。第 63 頁＝62")]
    public int endingStartPage = 62;
    [Tooltip("結局模式下，翻到最後一頁後等這麼多秒自動捲名單（不必等玩家按鍵）")]
    public float endingAutoRollSeconds = 2.5f;
    [Tooltip("名單捲完後載入的場景。留空＝停在標題卡（展場模式）")]
    public string sceneAfterCredits = "MainMenuScene";

    /// <summary>
    /// ★由玻璃館結局設 true：進 Book 場景後直接跳到結尾漫畫，
    ///   翻完自動捲名單，名單結束回主選單。
    ///   自由翻閱（從主選單「製作名單和來源」進來）不受影響。
    /// </summary>
    public static bool EndingMode;

    // ══════════════════════════════════════════
    // 版面
    // ══════════════════════════════════════════
    [Header("版面")]
    [Tooltip("#0B0C16 夜空色，與繪本背景同色")]
    public Color bgColor = new Color(0.043f, 0.047f, 0.086f, 1f);
    [Tooltip("#E8E0D2 骨白")]
    public Color textColor = new Color(0.9098f, 0.8784f, 0.8235f, 1f);
    [Tooltip("分類標的顏色，比正文暗一階")]
    public Color sectionColor = new Color(0.9098f, 0.8784f, 0.8235f, 0.55f);
    [Tooltip("留空會依序找：Resources 裡的 TMP 字型 → Resources 裡的 .ttf（執行時現做 SDF 字型）")]
    public TMP_FontAsset fontAsset;
    [Tooltip("備援 1：Resources 裡的 TMP_FontAsset 路徑（不用副檔名）")]
    public string fontTmpResource = "";
    [Tooltip("備援 2：Resources 裡的 .ttf 路徑（不用副檔名）。執行時用它現做動態 SDF 字型，中文一定有")]
    public string fontTtfResource = "Fonts/ChenYuluoyan-2.0-Thin";

    public float titleSize = 76f;
    public float sectionSize = 30f;
    public float nameSize = 44f;
    public float roleSize = 34f;
    public float paragraphSize = 28f;

    [Tooltip("大標題的字元間距。片尾標題拉開比較有分量")]
    public float titleSpacing = 34f;
    [Tooltip("分類標的字元間距")]
    public float sectionSpacing = 50f;

    [Tooltip("名單總寬。1920 畫布左右各留 360 安全邊")]
    public float contentWidth = 1200f;
    [Tooltip("Role 兩欄中間的間距")]
    public float columnGap = 60f;
    [Tooltip("每一行之間的基本行距")]
    public float rowSpacing = 16f;

    // ══════════════════════════════════════════
    // 捲動
    // ══════════════════════════════════════════
    [Header("捲動")]
    [Tooltip("每秒捲幾像素（1080 基準）。電影片尾大約 70–110")]
    public float scrollSpeed = 90f;
    [Tooltip("黑幕淡入秒數")]
    public float curtainIn = 1.5f;
    [Tooltip("名單開始捲之前先停幾秒全黑")]
    public float leadIn = 1.2f;
    [Tooltip("捲到最後一張標題卡置中後，停留幾秒")]
    public float tailHold = 5f;
    [Tooltip("最後淡出秒數。0＝停在標題卡不淡出（展場建議 0）")]
    public float fadeOut = 3f;

    // ══════════════════════════════════════════
    // 跳過
    // ══════════════════════════════════════════
    [Header("跳過")]
    public bool allowSkip = true;
    [Tooltip("開始後這麼多秒內按鍵無效，防誤觸")]
    public float minBeforeSkip = 2f;
    [Tooltip("跳過之後要做什麼")]
    public bool returnToFirstPageOnSkip = true;

    // ══════════════════════════════════════════
    // 內部
    // ══════════════════════════════════════════
    private Canvas _canvas;
    private Image _curtain;
    private RectTransform _container;
    private bool _built;
    private bool _armed;
    private bool _rolling;
    private float _armedAt;
    private float _lastEntryCenter;   // 最後一張標題卡的中心，離 container 頂端多遠
    private float _totalHeight;
    private TMP_FontAsset _font;      // 實際使用的字型（見 ResolveFont）

    public bool IsRolling { get { return _rolling; } }

    private void Awake()
    {
        LoadCredits();
        if (book == null) book = GetComponent<PageBook>();
        if (book == null) book = Object.FindObjectOfType<PageBook>();
    }

    private void Start()
    {
        // ★結局模式：從玻璃館進來，直接翻到結尾漫畫，並讓名單自動接上
        if (!EndingMode || book == null) return;
        if (endingStartPage >= 0 && endingStartPage < book.PageCount)
        {
            book.GoTo(endingStartPage, false);
        }
        if (endingAutoRollSeconds > 0f) autoRollAfterSeconds = endingAutoRollSeconds;
        armOnLastPage = true;
    }

    /// <summary>
    /// 讀名單。優先序：Resources/Credits.txt → Inspector 的 entries → 程式內建。
    /// 要改名單，改 Resources/Credits.txt 就好，不用碰程式。
    /// </summary>
    public void LoadCredits()
    {
        if (!string.IsNullOrEmpty(creditsResource))
        {
            TextAsset ta = Resources.Load<TextAsset>(creditsResource);
            if (ta != null && !string.IsNullOrEmpty(ta.text))
            {
                List<Entry> parsed = Parse(ta.text);
                if (parsed.Count > 0)
                {
                    entries = parsed;
                    return;
                }
                Debug.LogWarning("[EndCredits] Resources/" + creditsResource + " 讀到了但解析結果是空的，改用內建名單。");
            }
        }
        if (entries == null || entries.Count == 0) entries = BuildDefaultCredits();
    }

    /// <summary>
    /// 名單模板語法：
    ///   #  大標題　　　　例：# 我　你　他
    ///   &gt;  分類標　　　　例：&gt; 組　員
    ///   ~  段落（會自動換行）
    ///   |  兩欄：職務 | 名字
    ///   //  這行是註解，不會顯示
    ///   空行 一行＝一段空白，連續空行會疊加
    ///   其他 都當成一個名字，置中
    /// </summary>
    public static List<Entry> Parse(string raw)
    {
        List<Entry> L = new List<Entry>();
        if (string.IsNullOrEmpty(raw)) return L;

        string[] rows = raw.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        int blanks = 0;

        for (int i = 0; i < rows.Length; i++)
        {
            string s = rows[i].Trim();

            if (s.Length == 0) { blanks++; continue; }
            if (s.StartsWith("//")) continue;

            if (blanks > 0)
            {
                Entry g = new Entry();
                g.kind = Kind.Gap;
                g.gap = 60f * blanks;      // 一個空行＝60px
                L.Add(g);
                blanks = 0;
            }

            Entry e = new Entry();
            if (s.StartsWith("#"))
            {
                e.kind = Kind.Title; e.left = s.Substring(1).Trim();
            }
            else if (s.StartsWith(">"))
            {
                e.kind = Kind.Section; e.left = s.Substring(1).Trim();
            }
            else if (s.StartsWith("~"))
            {
                e.kind = Kind.Paragraph; e.left = s.Substring(1).Trim();
            }
            else if (s.IndexOf('|') >= 0)
            {
                int p = s.IndexOf('|');
                e.kind = Kind.Role;
                e.left = s.Substring(0, p).Trim();
                e.right = s.Substring(p + 1).Trim();
            }
            else
            {
                e.kind = Kind.Name; e.left = s;
            }
            L.Add(e);
        }
        return L;
    }

    private void OnEnable()
    {
        if (book != null) book.OnPageChanged += HandlePageChanged;
    }

    private void OnDisable()
    {
        if (book != null) book.OnPageChanged -= HandlePageChanged;
    }

    private void HandlePageChanged(int idx)
    {
        if (!armOnLastPage || book == null) return;
        bool last = idx >= book.PageCount - 1;
        if (last && !_armed && !_rolling)
        {
            _armed = true;
            _armedAt = Time.unscaledTime;
        }
        else if (!last)
        {
            _armed = false;   // 往回翻就取消
        }
    }

    private void Update()
    {
        if (_rolling || !_armed || book == null) return;
        if (book.IsBusy) return;

        if (autoRollAfterSeconds > 0f && Time.unscaledTime - _armedAt >= autoRollAfterSeconds)
        {
            StartCoroutine(Roll());
            return;
        }

#if ENABLE_LEGACY_INPUT_MANAGER
        if (NextPressed()) StartCoroutine(Roll());
#endif
    }

#if ENABLE_LEGACY_INPUT_MANAGER
    /// <summary>跟 PageBook 一樣的「下一頁」輸入。</summary>
    private static bool NextPressed()
    {
        if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D) ||
            Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.PageDown)) return true;
        if (Input.GetMouseButtonDown(0)) return true;
        if (Input.mouseScrollDelta.y < -0.01f) return true;
        return false;
    }
#endif

    // ══════════════════════════════════════════
    // 播放
    // ══════════════════════════════════════════
    public IEnumerator Roll()
    {
        if (_rolling) yield break;
        _rolling = true;
        _armed = false;

        Build();
        _canvas.enabled = true;
        SetCurtainAlpha(0f);
        SetContainerY(-540f);

        // 黑幕淡入
        yield return Fade(0f, 1f, curtainIn);
        yield return Wait(leadIn);

        // 由下往上捲，停在最後一張標題卡置中
        float from = -540f;
        float to = _lastEntryCenter;
        float dist = to - from;
        float dur = scrollSpeed > 0f ? dist / scrollSpeed : 1f;

        float t = 0f;
#if ENABLE_LEGACY_INPUT_MANAGER
        float started = Time.unscaledTime;
#endif
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            SetContainerY(Mathf.Lerp(from, to, Mathf.Clamp01(t / dur)));

#if ENABLE_LEGACY_INPUT_MANAGER
            if (allowSkip && Time.unscaledTime - started >= minBeforeSkip && Input.anyKeyDown)
            {
                yield return Finish(true);
                yield break;
            }
#endif
            yield return null;
        }
        SetContainerY(to);

        yield return Wait(tailHold);
        yield return Finish(false);
    }

    private IEnumerator Finish(bool skipped)
    {
        if (fadeOut > 0f || skipped)
        {
            yield return Fade(1f, 0f, skipped ? 0.6f : fadeOut);
            _canvas.enabled = false;
        }
        // fadeOut = 0 且沒被跳過 → 停在標題卡，展場就讓它停著
        _rolling = false;

        // ★結局模式：名單結束（含被跳過）→ 回主選單，整局收束
        if (EndingMode)
        {
            EndingMode = false;
            if (!string.IsNullOrEmpty(sceneAfterCredits))
            {
                SceneManager.LoadScene(sceneAfterCredits);
                yield break;
            }
        }

        if (returnToFirstPageOnSkip && skipped && book != null)
        {
            book.GoTo(0, false);
        }
    }

    private IEnumerator Fade(float a, float b, float dur)
    {
        if (dur <= 0f) { SetCurtainAlpha(b); yield break; }
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            SetCurtainAlpha(Mathf.Lerp(a, b, Mathf.Clamp01(t / dur)));
            yield return null;
        }
        SetCurtainAlpha(b);
    }

    private IEnumerator Wait(float s)
    {
        float t = 0f;
        while (t < s) { t += Time.unscaledDeltaTime; yield return null; }
    }

    private void SetCurtainAlpha(float a)
    {
        if (_curtain != null) _curtain.color = new Color(bgColor.r, bgColor.g, bgColor.b, a);
        if (_container != null)
        {
            // 文字跟著黑幕一起淡，避免文字浮在半透明底上
            CanvasGroup g = _container.GetComponent<CanvasGroup>();
            if (g != null) g.alpha = a;
        }
    }

    private void SetContainerY(float y)
    {
        if (_container != null) _container.anchoredPosition = new Vector2(0f, y);
    }

    /// <summary>
    /// 決定片尾用哪個字型。順序：
    ///   1  Inspector 指定的 fontAsset
    ///   2  Resources 裡的 TMP_FontAsset（fontTmpResource）
    ///   3  Resources 裡的 .ttf（fontTtfResource）→ 執行時現做動態 SDF 字型。
    ///      繪本專案沒有中文 TMP 字型資產，走的就是這條：
    ///      只要 Assets/Resources/Fonts/ 裡有 ttf，中文就一定顯示得出來。
    /// </summary>
    private void ResolveFont()
    {
        if (_font != null) return;

        _font = fontAsset;

        if (_font == null && !string.IsNullOrEmpty(fontTmpResource))
        {
            _font = Resources.Load<TMP_FontAsset>(fontTmpResource);
        }

        if (_font == null && !string.IsNullOrEmpty(fontTtfResource))
        {
            Font src = Resources.Load<Font>(fontTtfResource);
            if (src != null)
            {
                // 2048 動態圖集＋允許多張：名單 172 個不重複中文綽綽有餘
                _font = TMP_FontAsset.CreateFontAsset(
                    src, 64, 8,
                    UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,
                    2048, 2048,
                    AtlasPopulationMode.Dynamic, true);
            }
        }

        if (_font == null)
        {
            Debug.LogWarning("[EndCredits] 找不到任何中文字型，會用 TMP 預設字型（中文將顯示為方框）。" +
                             "請確認 Resources/" + fontTtfResource + ".ttf 存在。");
        }
    }

    // ══════════════════════════════════════════
    // 建畫面
    // ══════════════════════════════════════════
    private void Build()
    {
        if (_built) return;
        ResolveFont();

        GameObject canvasGo = new GameObject("EndCreditsCanvas");
        canvasGo.transform.SetParent(transform, false);
        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 10000;

        CanvasScaler sc = canvasGo.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920f, 1080f);
        sc.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        sc.matchWidthOrHeight = 0.5f;

        GameObject curtainGo = new GameObject("Curtain");
        curtainGo.transform.SetParent(canvasGo.transform, false);
        _curtain = curtainGo.AddComponent<Image>();
        _curtain.raycastTarget = true;
        _curtain.color = new Color(bgColor.r, bgColor.g, bgColor.b, 0f);
        RectTransform cr = curtainGo.AddComponent<RectTransform>();
        if (cr != null)
        {
            cr.anchorMin = new Vector2(0f, 0f);
            cr.anchorMax = new Vector2(1f, 1f);
            cr.offsetMin = new Vector2(0f, 0f);
            cr.offsetMax = new Vector2(0f, 0f);
        }

        GameObject cont = new GameObject("CreditsContent");
        cont.transform.SetParent(canvasGo.transform, false);
        _container = cont.AddComponent<RectTransform>();
        cont.AddComponent<CanvasGroup>();
        if (_container != null)
        {
            _container.anchorMin = new Vector2(0.5f, 0.5f);
            _container.anchorMax = new Vector2(0.5f, 0.5f);
            _container.pivot = new Vector2(0.5f, 1f);   // 頂端對齊，方便往上推
            _container.sizeDelta = new Vector2(contentWidth, 0f);
        }

        LayoutRows();
        _canvas.enabled = false;
        _built = true;
    }

    /// <summary>把 entries 排成一列一列，回填總高與最後一張卡的中心位置。</summary>
    private void LayoutRows()
    {
        float y = 0f;
        float lastCenter = 0f;

        for (int i = 0; i < entries.Count; i++)
        {
            Entry e = entries[i];
            if (e == null) continue;

            if (e.kind == Kind.Gap)
            {
                y += e.gap;
                continue;
            }

            float h;
            if (e.kind == Kind.Role)
            {
                float colW = (contentWidth - columnGap) * 0.5f;
                float hL = MakeText(e.left, roleSize, textColor, TextAlignmentOptions.Right,
                                    -(colW + columnGap) * 0.5f, y, colW);
                float hR = MakeText(e.right, roleSize, textColor, TextAlignmentOptions.Left,
                                    (colW + columnGap) * 0.5f, y, colW);
                h = Mathf.Max(hL, hR);
            }
            else
            {
                float size = e.kind == Kind.Title ? titleSize
                           : e.kind == Kind.Section ? sectionSize
                           : e.kind == Kind.Name ? nameSize
                           : paragraphSize;
                Color col = e.kind == Kind.Section ? sectionColor : textColor;
                float sp = e.kind == Kind.Title ? titleSpacing
                         : e.kind == Kind.Section ? sectionSpacing : 0f;
                h = MakeText(e.left, size, col, TextAlignmentOptions.Center, 0f, y, contentWidth, sp);
            }

            if (e.kind == Kind.Title) lastCenter = y + h * 0.5f;

            y += h + rowSpacing;
        }

        _totalHeight = y;
        // 沒有標題卡就停在整份名單的底
        _lastEntryCenter = lastCenter > 0f ? lastCenter : _totalHeight;
        if (_container != null) _container.sizeDelta = new Vector2(contentWidth, _totalHeight);
    }

    /// <summary>建一個文字列，回傳它的高度。</summary>
    private float MakeText(string text, float size, Color col, TextAlignmentOptions align,
                           float x, float yTop, float width, float charSpacing = 0f)
    {
        GameObject go = new GameObject("Row");
        go.transform.SetParent(_container != null ? _container.transform : transform, false);

        TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
        if (t == null) return size * 1.4f;   // Editor 外的保險

        if (_font != null) t.font = _font;
        t.text = text != null ? text : "";
        t.fontSize = size;
        t.color = col;
        t.alignment = align;
        t.enableWordWrapping = true;
        t.overflowMode = TextOverflowModes.Overflow;
        t.raycastTarget = false;
        t.lineSpacing = 20f;
        t.characterSpacing = charSpacing;

        RectTransform r = go.AddComponent<RectTransform>();
        if (r != null)
        {
            r.anchorMin = new Vector2(0.5f, 1f);
            r.anchorMax = new Vector2(0.5f, 1f);
            r.pivot = new Vector2(0.5f, 1f);
            r.sizeDelta = new Vector2(width, 0f);
            r.anchoredPosition = new Vector2(x, -yTop);
        }

        t.ForceMeshUpdate();
        float h = t.preferredHeight;
        if (h <= 0f) h = size * 1.4f;
        if (r != null) r.sizeDelta = new Vector2(width, h);
        return h;
    }

    // ══════════════════════════════════════════
    // 內建名單
    //   組員與指導老師：《文字施工單_給美術與程式》《Codex 任務書》交叉核對一致
    //   素材與音樂：掃 Assets 目錄列出來的
    //   ★沒有寫在專案文件裡的人名，這裡不會出現。要補請在 Inspector 加。
    // ══════════════════════════════════════════
    [ContextMenu("重新載入內建名單")]
    private void ReloadDefault() { entries = BuildDefaultCredits(); }

    public static List<Entry> BuildDefaultCredits()
    {
        List<Entry> L = new List<Entry>();

        Add(L, Kind.Gap, "", 420f);
        Add(L, Kind.Title, "我　你　他");
        Add(L, Kind.Gap, "", 260f);

        Add(L, Kind.Section, "組　員");
        Add(L, Kind.Name, "江啟強");
        Add(L, Kind.Name, "陳修毅");
        Add(L, Kind.Name, "王翊安");
        Add(L, Kind.Gap, "", 160f);

        Add(L, Kind.Section, "指導老師");
        Add(L, Kind.Name, "莊宗嚴");
        Add(L, Kind.Gap, "", 220f);

        Add(L, Kind.Section, "音樂與音效");
        AddRole(L, "原創音樂・音效", "團隊自製");
        Add(L, Kind.Gap, "", 60f);
        AddRole(L, "Shadows and Dust", "Scott Buckley");
        AddRole(L, "Echoes", "chosic.com");
        AddRole(L, "The Last Tears", "Ashot Danielyan");
        AddRole(L, "Introspective Sad Ambient Piano", "Ashot Danielyan");
        Add(L, Kind.Gap, "", 220f);

        Add(L, Kind.Section, "字　型");
        AddRole(L, "芫荽 ChenYuluoyan", "justfont");
        AddRole(L, "Mamelon", "—");
        AddRole(L, "UoqMunThenKhung", "—");
        Add(L, Kind.Gap, "", 220f);

        Add(L, Kind.Section, "使用素材");
        Add(L, Kind.Name, "BubbleR");
        Add(L, Kind.Name, "CloudSea");
        Add(L, Kind.Name, "Easy Transition");
        Add(L, Kind.Name, "FreeParallax");
        Add(L, Kind.Name, "MonsterMutant 7");
        Add(L, Kind.Name, "PolyOne　Rocks Stylized");
        Add(L, Kind.Name, "Rock Pack Free");
        Add(L, Kind.Name, "Stylized Environment VFX");
        Add(L, Kind.Name, "living birds");
        Add(L, Kind.Name, "EzTornado");
        Add(L, Kind.Gap, "", 220f);

        Add(L, Kind.Section, "開發工具");
        Add(L, Kind.Name, "Unity 6");
        Add(L, Kind.Name, "TextMesh Pro　Cinemachine");
        Add(L, Kind.Name, "DOTween　Demigiant");
        Add(L, Kind.Gap, "", 300f);

        // ★這一段是規格明訂不可刪的，見《文字施工單_給美術與程式》
        Add(L, Kind.Paragraph,
            "本作插圖以生成式 AI 依團隊撰寫之提示詞產生，\n" +
            "並由團隊進行構圖審核、迭代修正與後製。");
        Add(L, Kind.Gap, "", 40f);
        Add(L, Kind.Paragraph, "音樂與音效為團隊自製。");
        // ※ 若採用上面「音樂與音效」段列出的四首授權曲，
        //   這一行建議改成：「音樂與音效除上列授權曲外，為團隊自製。」
        //   由企劃拍板，我沒有自己改。

        Add(L, Kind.Gap, "", 400f);
        Add(L, Kind.Title, "我　你　他");
        Add(L, Kind.Gap, "", 400f);

        return L;
    }

    private static void Add(List<Entry> L, Kind k, string text, float gap = 40f)
    {
        Entry e = new Entry();
        e.kind = k; e.left = text; e.right = ""; e.gap = gap;
        L.Add(e);
    }

    private static void AddRole(List<Entry> L, string role, string who)
    {
        Entry e = new Entry();
        e.kind = Kind.Role; e.left = role; e.right = who; e.gap = 0f;
        L.Add(e);
    }
}
