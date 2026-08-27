using UnityEngine;

/// <summary>
/// 畢展現場用。
/// 沒人碰的時候自己翻，翻完停在最後一頁；再更久沒人碰就回到第一頁重新開始。
/// 有人一碰（滑鼠／鍵盤／觸控）就立刻交還控制權。
/// </summary>
[RequireComponent(typeof(PageBook))]
public class ExhibitMode : MonoBehaviour
{
    [Header("開關")]
    public bool enableAutoPlay = true;

    [Header("時間")]
    [Tooltip("沒人操作幾秒之後開始自己翻")]
    public float idleBeforeAutoPlay = 25f;
    [Tooltip("自動翻頁時，每一頁停多久")]
    public float autoPageSeconds = 4.0f;
    [Tooltip("沒人操作幾秒之後回到第一頁")]
    public float idleBeforeReset = 90f;

    [Header("提示")]
    [Tooltip("自動播放時要不要在角落顯示一行小字")]
    public bool showHint = true;
    public string hintText = "點畫面翻頁";
    public Color hintColor = new Color(0.91f, 0.88f, 0.82f, 0.45f);

    PageBook book;
    float lastInput;
    float nextAuto;
    bool autoPlaying;
    GUIStyle style;

    void Awake()
    {
        book = GetComponent<PageBook>();
        lastInput = Time.unscaledTime;
    }

    void Update()
    {
        if (!enableAutoPlay) return;

        if (AnyInput())
        {
            lastInput = Time.unscaledTime;
            autoPlaying = false;
        }

        float idle = Time.unscaledTime - lastInput;

        if (idle > idleBeforeReset)
        {
            if (book.Index != 0) book.GoTo(0);
            lastInput = Time.unscaledTime - idleBeforeAutoPlay; // 重設完立刻進入自動播放
            autoPlaying = false;
            return;
        }

        if (idle > idleBeforeAutoPlay)
        {
            if (!autoPlaying) { autoPlaying = true; nextAuto = Time.unscaledTime + 0.6f; }
            if (!book.IsBusy && Time.unscaledTime >= nextAuto)
            {
                if (book.Index < book.PageCount - 1)
                {
                    book.Next();
                    nextAuto = Time.unscaledTime + autoPageSeconds;
                }
                else
                {
                    // 停在最後一頁，等 idleBeforeReset 把它送回開頭
                    nextAuto = Time.unscaledTime + autoPageSeconds;
                }
            }
        }
    }

    bool AnyInput()
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.anyKeyDown) return true;
        if (Input.GetMouseButton(0) || Input.GetMouseButton(1)) return true;
        if (Mathf.Abs(Input.mouseScrollDelta.y) > 0.01f) return true;
        if (Input.touchCount > 0) return true;
        if (Input.mousePosition != lastMouse) { lastMouse = Input.mousePosition; return true; }
#endif
        return false;
    }
#if ENABLE_LEGACY_INPUT_MANAGER
    Vector3 lastMouse;
#endif

    void OnGUI()
    {
        if (!showHint || !autoPlaying) return;
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label);
            style.fontSize = Mathf.RoundToInt(Screen.height * 0.022f);
            style.alignment = TextAnchor.LowerRight;
        }
        style.normal.textColor = hintColor;
        float pad = Screen.height * 0.03f;
        GUI.Label(new Rect(0, 0, Screen.width - pad, Screen.height - pad), hintText, style);
    }
}
