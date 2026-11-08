using UnityEngine;

/// <summary>
/// ★1108 「這個位置能不能站人」的共用判斷，給重生與救援用。
/// 存檔點或救援點如果剛好被包在石頭／牆裡，玩家被送過去就會再卡一次（卡石重生迴圈的來源之一）。
/// 這裡找「離原本位置最近、沒插在固體裡、也沒被包住」的空位。原本位置本來就沒問題時，原樣回傳（不改變現有行為）。
/// </summary>
public static class PlayerSafeSpot
{
    private const float Shrink = 0.08f;          // 碰撞盒往內縮多少再測（只貼著不算插進去）
    private const float OpenProbe = 0.8f;        // 往外探幾公尺看有沒有空間
    private const int OpenSidesNeeded = 3;       // 8 個方向至少要有幾個是通的，才算「不是被包住」

    private static readonly Vector3[] Dirs =
    {
        Vector3.up, Vector3.down, Vector3.left, Vector3.right,
        new Vector3(1, 1, 0).normalized, new Vector3(-1, 1, 0).normalized, new Vector3(1, -1, 0).normalized, new Vector3(-1, -1, 0).normalized
    };

    private static bool Ignorable(Collider c, Collider self, Transform player)
    {
        if (c == null || c == self || c.isTrigger || !c.enabled) return true;
        if (c.transform.IsChildOf(player)) return true;
        if (Physics.GetIgnoreCollision(self, c)) return true;
        if (Physics.GetIgnoreLayerCollision(self.gameObject.layer, c.gameObject.layer)) return true;
        if (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic) return true;   // 會被推開的動態物體不算
        return false;
    }

    private static Vector3 Half(Bounds b, float shrink)
    {
        Vector3 e = b.extents;
        // 每個軸至少留 40%：玩家碰撞盒 Z 很薄，直接減會變負數
        return new Vector3(Mathf.Max(e.x - shrink, e.x * 0.4f), Mathf.Max(e.y - shrink, e.y * 0.4f), Mathf.Max(e.z - shrink, e.z * 0.4f));
    }

    /// <summary>把玩家碰撞盒搬到 pos，測有沒有插進固體、有沒有被包在石頭殼裡。</summary>
    public static bool IsFree(Collider self, Transform player, Vector3 pos)
    {
        if (self == null || player == null) return true;
        Bounds b = self.bounds;
        Vector3 center = b.center + (pos - player.position);
        foreach (Collider c in Physics.OverlapBox(center, Half(b, Shrink), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
            if (!Ignorable(c, self, player)) return false;

        // 水下石頭是空心的殼：包在殼裡面不會碰到任何東西（上面測不出來），但出不去。用石頭救援的同一套判斷補上。
        UnderwaterPenetrationProbe probe = player.GetComponent<UnderwaterPenetrationProbe>();
        if (probe != null && probe.IsInsideRockShell(center)) return false;
        return true;
    }

    /// <summary>8 個方向裡有幾個方向能往外走一小段（被包在洞裡或夾縫裡的位置會很少）。</summary>
    private static int OpenSides(Collider self, Transform player, Vector3 pos)
    {
        Bounds b = self.bounds;
        Vector3 center = b.center + (pos - player.position);
        Vector3 half = Half(b, 0.1f);
        int open = 0;
        for (int i = 0; i < Dirs.Length; i++)
        {
            bool blocked = false;
            foreach (RaycastHit h in Physics.BoxCastAll(center, half, Dirs[i], Quaternion.identity, OpenProbe, ~0, QueryTriggerInteraction.Ignore))
            {
                if (Ignorable(h.collider, self, player)) continue;
                blocked = true; break;
            }
            if (!blocked) open++;
        }
        return open;
    }

    /// <summary>
    /// pos 本身可以站就原樣回傳；被包住就由近到遠一圈一圈找最近的空位（最遠 8 公尺）。
    /// 全部都不行才回傳原位（交給後面的脫困機制）。
    /// </summary>
    public static Vector3 FindFree(Collider self, Transform player, Vector3 pos, out bool moved)
    {
        moved = false;
        if (self == null || player == null || IsFree(self, player, pos)) return pos;
        for (float r = 0.5f; r <= 8f; r += 0.5f)
        {
            for (int i = 0; i < 16; i++)
            {
                float a = i * Mathf.PI * 2f / 16f;
                Vector3 p = pos + new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f);
                if (IsFree(self, player, p) && OpenSides(self, player, p) >= OpenSidesNeeded) { moved = true; return p; }
            }
        }
        return pos;
    }
}
