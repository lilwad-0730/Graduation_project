using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// ★1115 手把支援（只讀手把，沒接手把＝全部回傳「沒按」，鍵盤操作完全不受影響）。
///
///  用 Unity 的 Input System（Gamepad.current）讀，所以 Xbox、PlayStation、Switch Pro 的按鈕位置都一樣，不用管各家編號不同。
///  專案的輸入設定是「兩套都開」（Both），原本的鍵盤程式照舊，這裡只是「另外多一條路」，各處用 || 接上去。
///
///  對照（以 Xbox 命名，PlayStation 對應位置）：
///   ・左搖桿／十字鍵左右　　　走（原本 Horizontal 軸就吃搖桿，這裡補十字鍵）
///   ・A（叉）　　　　　　　　 跳／游泳往上／選單確定／對話下一頁
///   ・X（方塊）　　　　　　　 抓住拖動（＝鍵盤左 Shift）、也算「按住 Shift 推石」
///   ・B（圈）　　　　　　　　 盾（＝鍵盤 Q）
///   ・Y（三角）　　　　　　　 點燭火（＝鍵盤 S 那個動作）
///   ・搖桿／十字鍵 下　　　　 游泳往下、硬撐（＝鍵盤 S／下）
///   ・搖桿／十字鍵 上　　　　 游泳往上、選單往上
///   ・Start　　　　　　　　　 選單返回（＝Esc）
/// </summary>
public static class PadInput
{
    private const float Dead = 0.5f;

    private static Gamepad Pad => Gamepad.current;

    public static bool Connected => Pad != null;

    // ── 走 ──
    /// <summary>十字鍵左右（搖桿已由 Input.GetAxis("Horizontal") 處理，這裡只補十字鍵）。-1／0／1。</summary>
    public static float DpadHorizontal
    {
        get
        {
            Gamepad p = Pad; if (p == null) return 0f;
            if (p.dpad.right.isPressed) return 1f;
            if (p.dpad.left.isPressed) return -1f;
            return 0f;
        }
    }

    // ── 跳／確定 ──
    // 新輸入系統認得手把就讀 buttonSouth；另外多接舊輸入的 joystick button 0（Xbox 的 A），認不到標準手把時也能跳
    public static bool JumpDown => (Pad != null && Pad.buttonSouth.wasPressedThisFrame) || Input.GetKeyDown(KeyCode.JoystickButton0);
    public static bool JumpHeld => (Pad != null && Pad.buttonSouth.isPressed) || Input.GetKey(KeyCode.JoystickButton0);

    // ── 抓取（＝Shift）──
    // 同跳躍：另外多接舊輸入的 joystick button 2（Xbox 的 X）
    public static bool GrabDown => (Pad != null && Pad.buttonWest.wasPressedThisFrame) || Input.GetKeyDown(KeyCode.JoystickButton2);
    public static bool GrabUp => (Pad != null && Pad.buttonWest.wasReleasedThisFrame) || Input.GetKeyUp(KeyCode.JoystickButton2);
    public static bool GrabHeld => (Pad != null && Pad.buttonWest.isPressed) || Input.GetKey(KeyCode.JoystickButton2);

    // ── 其他動作 ──
    public static bool ShieldDown => Pad != null && Pad.buttonEast.wasPressedThisFrame;
    public static bool CandleDown => Pad != null && Pad.buttonNorth.wasPressedThisFrame;
    public static bool BackDown => Pad != null && Pad.startButton.wasPressedThisFrame;

    // ── 上下（游泳、硬撐）──
    public static bool DownHeld
    {
        get { Gamepad p = Pad; return p != null && (p.leftStick.y.ReadValue() < -Dead || p.dpad.down.isPressed); }
    }
    public static bool UpHeld
    {
        get { Gamepad p = Pad; return p != null && (p.leftStick.y.ReadValue() > Dead || p.dpad.up.isPressed); }
    }

    /// <summary>選單上下：搖桿或十字鍵往下／往上「推一下」回傳 +1／-1（邊緣觸發，按住不連跳），其他回傳 0。</summary>
    public static int MenuStep
    {
        get
        {
            Gamepad p = Pad; if (p == null) return 0;
            if (Time.frameCount != _stepFrame)   // 同一幀被問幾次都是同一個答案
            {
                _stepFrame = Time.frameCount;
                float y = p.leftStick.y.ReadValue();
                int dir = (y < -Dead || p.dpad.down.isPressed) ? 1 : ((y > Dead || p.dpad.up.isPressed) ? -1 : 0);
                _stepResult = (dir != 0 && dir != _prevDir) ? dir : 0;
                _prevDir = dir;
            }
            return _stepResult;
        }
    }
    private static int _prevDir, _stepResult, _stepFrame = -1;

    /// <summary>任何手把動作正在進行（給「玩家有沒有在操作」這類判斷用）。</summary>
    public static bool AnyHeld
    {
        get
        {
            Gamepad p = Pad; if (p == null) return false;
            return p.buttonSouth.isPressed || p.buttonWest.isPressed || p.buttonEast.isPressed || p.buttonNorth.isPressed
                || Mathf.Abs(p.leftStick.x.ReadValue()) > 0.2f || Mathf.Abs(p.leftStick.y.ReadValue()) > 0.2f
                || p.dpad.ReadValue().sqrMagnitude > 0.01f;
        }
    }
}
