using UnityEngine;

/// <summary>
/// 讓玩家穿過這個物件上的 Collider（其他物件照常碰撞）。
/// 用在玻璃館怪物走的隱形地板：它在死亡區上方，玩家掉下去會卡在畫面外的隱形地板上不會死。
/// </summary>
public class IgnorePlayerCollision : MonoBehaviour
{
    public string playerTag = "Player";

    void Start()
    {
        GameObject player = GameObject.FindWithTag(playerTag);
        if (player == null) return;

        Collider[] mine = GetComponentsInChildren<Collider>(true);
        foreach (Collider pc in player.GetComponentsInChildren<Collider>(true))
            foreach (Collider c in mine)
                Physics.IgnoreCollision(pc, c, true);
    }
}
