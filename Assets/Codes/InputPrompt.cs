using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// ★1124 操作提示依「玩家現在用的輸入裝置」切換：鍵盤滑鼠 → 顯示 WASD／Shift／空白鍵；手把 → 顯示搖桿與對應按鈕。
///
/// 做法（只看「最後一次有動作的是哪一種」，不看有沒有接手把——手把接著、卻用鍵盤玩，一樣顯示鍵盤）：
///   ・按任何鍵盤鍵、滑鼠鍵、滑鼠大幅移動  → 鍵鼠
///   ・按任何手把按鈕、搖桿／十字鍵推超過 0.5   → 手把
///   同一幀兩種都有，以手把為準（手把不會自己產生輸入，鍵盤也不會）。
///   一開始（還沒任何輸入）是鍵鼠。
///
/// 用法：
///   InputPrompt.IsPad       目前是不是手把
///   InputPrompt.Changed     切換時通知（參數：是不是手把）
///   InputPrompt.Pick(鍵盤文字, 手把文字)  依目前裝置挑文字
///   InputPrompt.SetPad(bool)  測試用，直接指定
///
/// 自己在場景載入前生成、跨場景保留；用 unscaledTime，所以暫停（timeScale＝0）時也照常偵測。
/// 只多「讀」輸入，不影響任何操作。
/// </summary>
public static class InputPrompt
{
    /// <summary>總開關。關＝永遠是鍵鼠（提示全部照舊）。</summary>
    public static bool Enabled = true;

    private static bool _isPad;
    public static bool IsPad { get { return Enabled && _isPad; } }

    /// <summary>切換時呼叫（true＝改成手把）。</summary>
    public static event Action<bool> Changed;

    private const float StickThreshold = 0.5f;
    private const float MouseMoveThreshold = 12f;   // 一幀移動這麼多像素才算（避免桌面微震誤判）

    public static string Pick(string keyboardText, string padText)
    {
        return IsPad ? padText : keyboardText;
    }

    public static void SetPad(bool pad)
    {
        if (_isPad == pad) return;
        _isPad = pad;
        Debug.Log("【輸入提示】改成顯示" + (pad ? "手把" : "鍵盤滑鼠"));
        if (Changed != null)
        {
            try { Changed(pad); }
            catch (Exception e) { Debug.LogWarning("【輸入提示】通知失敗：" + e.Message); }
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Boot()
    {
        _isPad = false;
        GameObject go = new GameObject("[輸入提示偵測]");
        UnityEngine.Object.DontDestroyOnLoad(go);
        go.AddComponent<InputPromptRunner>();
    }

    private class InputPromptRunner : MonoBehaviour
    {
        private void Update()
        {
            if (!Enabled) return;
            bool kb = false, pad = false;
            try { kb = KeyboardMouseActive(); } catch { }
            try { pad = PadActive(); } catch { }
            if (pad) SetPad(true);
            else if (kb) SetPad(false);
        }

        private static bool KeyboardMouseActive()
        {
            Keyboard k = Keyboard.current;
            if (k != null && k.anyKey.wasPressedThisFrame) return true;
            Mouse m = Mouse.current;
            if (m != null)
            {
                if (m.leftButton.wasPressedThisFrame || m.rightButton.wasPressedThisFrame || m.middleButton.wasPressedThisFrame) return true;
                if (m.delta.ReadValue().sqrMagnitude > MouseMoveThreshold * MouseMoveThreshold) return true;
            }
            return false;
        }

        private static bool PadActive()
        {
            // 舊輸入：認不到標準手把時（跟 PadInput 一樣）joystick 按鈕還是進得來
            for (int i = 0; i < 12; i++)
                if (Input.GetKeyDown(KeyCode.JoystickButton0 + i)) return true;

            Gamepad p = Gamepad.current;
            if (p == null) return false;
            if (p.buttonSouth.wasPressedThisFrame || p.buttonEast.wasPressedThisFrame ||
                p.buttonWest.wasPressedThisFrame || p.buttonNorth.wasPressedThisFrame ||
                p.startButton.wasPressedThisFrame || p.selectButton.wasPressedThisFrame ||
                p.leftShoulder.wasPressedThisFrame || p.rightShoulder.wasPressedThisFrame ||
                p.leftTrigger.wasPressedThisFrame || p.rightTrigger.wasPressedThisFrame ||
                p.dpad.up.wasPressedThisFrame || p.dpad.down.wasPressedThisFrame ||
                p.dpad.left.wasPressedThisFrame || p.dpad.right.wasPressedThisFrame) return true;
            if (p.leftStick.ReadValue().magnitude > StickThreshold) return true;
            if (p.rightStick.ReadValue().magnitude > StickThreshold) return true;
            return false;
        }
    }
}
