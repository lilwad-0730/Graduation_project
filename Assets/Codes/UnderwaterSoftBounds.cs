using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ★1016 水下：把「開放水域中間看不見的死亡線」改成看不見的底（碰到不會死，只是游不下去）。
///
/// 實玩時發現：水下右邊看起來是開放水域，其實 y −49（x 25.8～67.7）有一條看不見的 DeathZone，
/// 游過去直接死；左邊 y −48（x −36～5.5）也有一條。第一次玩的人完全看不出來。
/// 這裡只改「水平的帶狀 DeathZone、而且在中層（y 高於 maxConvertDepthY）」這兩條：
/// 把它們的觸發框改成實體（不再是觸發），她會被擋住、浮在上面，不會死。
/// 最深處的底線、左右兩側的直立死亡牆照舊（那些在石頭裡或地圖外，防穿模用）。
///
/// 程式自動掛上（只在場景名含 underwater 的關卡），不用改場景；enableInBuild = false 整個關掉。
/// </summary>
public class UnderwaterSoftBounds : MonoBehaviour
{
    public static bool enableInBuild = true;

    [Tooltip("只改 y 高於這個值的帶狀死亡線（中層）；更深的照舊會死")]
    public float maxConvertDepthY = -60f;

    [Tooltip("寬度至少是高度的幾倍才算「水平的帶」（直立的死亡牆不改）")]
    public float minAspect = 8f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Install(SceneManager.GetActiveScene());
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Install(scene);
    }

    private static void Install(Scene scene)
    {
        if (!enableInBuild || !scene.IsValid()) return;
        if (!scene.name.ToLower().Contains("underwater")) return;
        if (FindAnyObjectByType<UnderwaterSoftBounds>() != null) return;
        GameObject go = new GameObject("★1016_UnderwaterSoftBounds");
        SceneManager.MoveGameObjectToScene(go, scene);
        go.AddComponent<UnderwaterSoftBounds>();
    }

    private void Start()
    {
        int converted = 0;
        foreach (Collider c in FindObjectsByType<Collider>())
        {
            if (c == null || !c.isTrigger) continue;
            GameObject g = c.gameObject;
            bool isDeath = g.CompareTag("DeathZone") || g.name.Contains("DeathZone");
            if (!isDeath) continue;

            Bounds b = c.bounds;
            if (b.size.y <= 0.001f) continue;
            if (b.size.x < b.size.y * minAspect) continue;   // 直立的牆不改
            if (b.center.y < maxConvertDepthY) continue;      // 最深的底線不改

            c.isTrigger = false;   // 變成實體：擋住、不觸發死亡
            converted++;
            Debug.Log($"【水下邊界】★1016 '{g.name}'（x {b.min.x:F1}～{b.max.x:F1}，y {b.center.y:F1}）改成看不見的底：碰到不會死，只是游不下去");
        }
        if (converted == 0) Debug.Log("【水下邊界】★1016 沒有找到中層的帶狀死亡線，沒有改動");
    }
}
