using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ★1019 換場景進來時，設定介面（SETTING）一律先關著。
///
/// Root Cause：InGameSettingsSystem 預製物件裡「SETTING」（設定介面本體）原本預設是開的，
///   棉花堡／廢墟／水下的場景實例都有各自蓋掉成關閉（m_IsActive=0），只有荒原（desert）沒蓋，
///   所以轉場進荒原一開始就跳出設定介面，還會因為 PauseGameWhileActive 把遊戲暫停。
///   隊友已經把預製物件預設改成關閉，荒原場景也補了蓋掉；這支是第三道保險：
///   不管哪個場景、不管預製物件預設怎麼被改回去，載入完成的那一刻只要設定介面是開的就關掉。
///   玩家自己按齒輪開啟設定介面不受影響（只在「載入場景」那一刻檢查一次）。
/// </summary>
public static class SettingsPopupSceneStartGuard
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        SceneManager.sceneLoaded -= OnSceneLoadedHook;
        SceneManager.sceneLoaded += OnSceneLoadedHook;
        CloseAll();
    }

    private static void OnSceneLoadedHook(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single) return;
        CloseAll();
    }

    private static void CloseAll()
    {
        SettingsPopupOverlayPresenter[] popups = Object.FindObjectsByType<SettingsPopupOverlayPresenter>(FindObjectsInactive.Exclude);
        for (int i = 0; i < popups.Length; i++)
        {
            SettingsPopupOverlayPresenter p = popups[i];
            if (p == null || !p.gameObject.activeSelf) continue;
            Debug.Log("【設定介面】換場景進來時設定介面是開著的，先關起來（" + p.gameObject.scene.name + "）。");
            p.gameObject.SetActive(false);
        }
    }
}
