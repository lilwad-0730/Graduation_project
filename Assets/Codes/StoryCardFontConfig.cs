using TMPro;
using UnityEngine;

/// <summary>
/// 過場文字卡（StoryCardPlayer）專用字型設定：放在 Resources，讓 DontDestroyOnLoad 或執行期生成的播放器也能讀到。
/// </summary>
public class StoryCardFontConfig : ScriptableObject
{
    public TMP_FontAsset font;
}
