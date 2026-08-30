using UnityEngine;

/// <summary>
/// 我你他　拉桿提示探針
///
/// 掛在組員的 LeverSystem 身上（InputHint 會自動補掛，不用手拉）。
/// 拉桿的 BoxCollider 就在同一個物件上，所以這支收得到同一批 OnTrigger 事件，
/// 完全不用改組員的 LeverSystem 一行字。
///
/// 【行為】走近 → 畫面下方亮「按下 E　拉動拉桿」；離開或拉下後收掉，之後不再出現。
/// 【例外】拉桿設成 triggerOnEnter（碰到就自動拉）時不出提示，因為沒有按鍵可按。
/// </summary>
[DisallowMultipleComponent]
public class InputHintLeverProbe : MonoBehaviour
{
    [Tooltip("走近拉桿時畫面下方顯示的字")]
    public string hintText = "按下 E　拉動拉桿";

    private LeverSystem _lever;
    private string _key = "lever";
    private bool _inZone;
    private bool _done;

    private static System.Reflection.FieldInfo _pulledField;
    private static bool _pulledFieldChecked;

    private void Awake()
    {
        _lever = GetComponent<LeverSystem>();
        _key = "lever:" + GetInstanceID();
        if (_lever != null && _lever.interactKey != KeyCode.E)
        {
            // 組員若改了按鍵，提示跟著改，不會說謊
            hintText = hintText.Replace("E", _lever.interactKey.ToString());
        }
    }

    private void OnDisable()
    {
        InputHint.Hide(_key);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsPlayer(other)) return;
        _inZone = true;
        Refresh();
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsPlayer(other)) return;
        _inZone = false;
        InputHint.Hide(_key);
    }

    private void Update()
    {
        if (_done) return;
        if (!_inZone) return;

        if (IsPulled())
        {
            Finish();
            return;
        }

#if ENABLE_LEGACY_INPUT_MANAGER
        KeyCode k = _lever != null ? _lever.interactKey : KeyCode.E;
        if (Input.GetKeyDown(k))
        {
            Finish();
            return;
        }
#endif
        Refresh();
    }

    private void Refresh()
    {
        if (_done) return;
        if (_lever != null && _lever.triggerOnEnter) return;   // 碰到就自動拉，沒東西可提示
        if (IsPulled()) { Finish(); return; }
        if (_inZone) InputHint.Show(_key, hintText);
    }

    private void Finish()
    {
        _done = true;
        InputHint.Hide(_key);
    }

    /// <summary>
    /// LeverSystem.isPulled 是私有的。用反射讀一次當保險（讀不到就當沒拉，
    /// 反正上面已經有「按下去就收」跟「離開就收」兩道，提示不會賴著不走）。
    /// </summary>
    private bool IsPulled()
    {
        if (_lever == null) return false;
        if (!_pulledFieldChecked)
        {
            _pulledFieldChecked = true;
            try
            {
                _pulledField = typeof(LeverSystem).GetField("isPulled",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            }
            catch { _pulledField = null; }
        }
        if (_pulledField == null) return false;
        try { return (bool)_pulledField.GetValue(_lever); }
        catch { return false; }
    }

    private static bool IsPlayer(Collider other)
    {
        if (other == null) return false;
        if (other.CompareTag("Player")) return true;
        return other.GetComponentInParent<PlayerMovement>() != null;
    }
}
