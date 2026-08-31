using UnityEngine;

/// <summary>
/// 我你他　水下操作提示探針
///
/// 掛在玩家身上（InputHint 會自動補掛）。第一次入水時亮一次
/// 「W／S　上浮與下潛」——整個遊戲從頭到尾沒有任何地方教過這件事。
///
/// 【為什麼寫「連續上浮會脫力」】水下不是無限游：PlayerMovement 有一組
///   隱形的游泳體力（maxSwimStamina／swimExhaustionCooldown），
///   一直按 W 會突然浮不動。玩家不知道就會以為是壞掉。
///
/// 【時機】被文字卡或過場蓋著時不會亮（亮了也看不到），
///   等畫面真的空出來才顯示，而且倒數只在看得見時走。
/// </summary>
[DisallowMultipleComponent]
public class InputHintSwimProbe : MonoBehaviour
{
    [Tooltip("第一次入水時顯示的字")]
    [TextArea(2, 3)]
    public string hintText = "W／S　上浮與下潛\n連續上浮會脫力，稍等再游";

    [Tooltip("顯示幾秒（只算真的看得見的秒數）")]
    public float hintSeconds = 6f;

    private PlayerMovement _pm;
    private bool _done;

    private void Awake()
    {
        _pm = GetComponent<PlayerMovement>();
    }

    private void Update()
    {
        if (_done) return;
        if (_pm == null) return;
        if (!_pm.isUnderwater) return;
        if (InputHint.IsBusy) return;      // 過場／文字卡正蓋著，等它演完再說

        _done = true;
        InputHint.Once("swim", hintText, hintSeconds);
    }
}
