using UnityEngine;

/// <summary>
/// ★1118c 星空視差層（Parallax_StarfieldNear/Mid/Far）是 Main Camera 的子物件，離鏡頭固定 20～30 單位，
/// 執行時鏡頭在 z=-70，星星就跑到世界 z=-50～-40，蓋在怪物（z 約 -36～+10）前面，白點會畫在怪物身上。
/// 這個元件每幀（在鏡頭移動、視差腳本之後）把這一層的「世界 z」釘在固定值：比怪物、玻璃路面遠，比背景大圖（z=20）近。
/// 正交鏡頭下 z 不影響大小與視差位移，只影響前後遮擋，所以看起來跟原本一樣。
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(1000)]
public class StarfieldDepthPin : MonoBehaviour
{
    [Tooltip("固定的世界 z（要大於怪物最遠處、小於背景大圖 dark bluebg_0 的 z=20）")]
    public float worldZ = 14f;

    private void LateUpdate()
    {
        Vector3 p = transform.position;
        if (!Mathf.Approximately(p.z, worldZ)) { p.z = worldZ; transform.position = p; }
    }
}
