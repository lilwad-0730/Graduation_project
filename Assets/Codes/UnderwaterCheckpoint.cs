using UnityEngine;

/// <summary>
/// 水下自動存檔點。
/// 水下沒有固定的存檔方塊，溺斃後原本只能從最上面重游一遍。
/// 現在撿到日誌、紙條、育兒物品、吸收光絮的當下，重生點就設在那裡——每個回憶都是一個錨點。
/// 只在有 UnderwaterSuffocationEffect 的場景（水下）作用，其他關卡呼叫等於沒事。
/// </summary>
public static class UnderwaterCheckpoint
{
    private static PlayerRespawnSystem _respawn;

    public static void MarkHere(Component near, string reason)
    {
        if (UnderwaterSuffocationEffect.Instance == null) return;   // 不是水下關
        if (PlayerRespawnSystem.IsAnyRespawning) return;

        PlayerMovement pm = null;
        if (near != null)
        {
            pm = near.GetComponentInParent<PlayerMovement>();
            if (pm == null) pm = near.GetComponentInChildren<PlayerMovement>();
        }
        if (pm == null) pm = Object.FindFirstObjectByType<PlayerMovement>();
        if (pm == null) return;

        if (_respawn == null || !_respawn.isActiveAndEnabled)
        {
            _respawn = pm.GetComponent<PlayerRespawnSystem>();
            if (_respawn == null) _respawn = pm.GetComponentInParent<PlayerRespawnSystem>();
            if (_respawn == null) _respawn = Object.FindFirstObjectByType<PlayerRespawnSystem>();
        }
        if (_respawn == null) return;

        // 帶前進保護：回頭撿漏掉的日誌不會讓重生點倒退回更前面的位置
        _respawn.TrySetProgressCheckpoint(pm.transform.position, "水下存檔點：" + reason);
    }
}
