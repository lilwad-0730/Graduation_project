using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 統一的換場景入口：一律用非同步載入，視窗載入時不會凍住、跳「沒有回應」。
/// 呼叫端原本的淡黑幕照舊，這裡只負責把場景讀進來。
/// </summary>
public static class SceneLoader
{
    private static bool _loading;

    /// <summary>非同步載入場景。載入中重複呼叫會被忽略，避免同時讀兩個場景。</summary>
    public static AsyncOperation Load(string sceneName)
    {
        if (_loading)
        {
            Debug.LogWarning("【載入】已經在載入場景中，忽略重複要求：" + sceneName);
            return null;
        }

        AsyncOperation op = SceneManager.LoadSceneAsync(sceneName);
        if (op == null)
        {
            // 場景名稱不在 Build Settings：交給原本的同步載入去報錯，行為跟以前一樣
            SceneManager.LoadScene(sceneName);
            return null;
        }

        _loading = true;
        op.completed += _ => _loading = false;
        return op;
    }
}
