using UnityEngine;

[DisallowMultipleComponent]
public sealed class PauseGameWhileActive : MonoBehaviour
{
    private bool isPausing;
    private float previousTimeScale = 1f;

    private void OnEnable()
    {
        BeginPause();
    }

    private void OnDisable()
    {
        RestoreTimeScale();
    }

    private void OnDestroy()
    {
        RestoreTimeScale();
    }

    private void OnApplicationQuit()
    {
        RestoreTimeScale();
    }

    // ★1016 設定開著的時候，如果別人（例如文字卡／照片卡播完）把時間還原成 > 0，
    //   記下它要的值、維持暫停；關設定時再還給它。原本這種情況會在設定底下偷偷繼續跑，
    //   關設定時又把開設定那一刻的 0 還回去 → 整個遊戲停住、只能重開。
    private void Update()
    {
        if (!isPausing || !Application.isPlaying) return;
        if (Time.timeScale > 0f)
        {
            previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
        }
    }

    private void BeginPause()
    {
        if (!Application.isPlaying || isPausing)
        {
            return;
        }

        previousTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        isPausing = true;
    }

    private void RestoreTimeScale()
    {
        if (!isPausing)
        {
            return;
        }

        if (Application.isPlaying)
        {
            // ★1016 開設定那一刻時間本來就是 0（文字卡／照片卡正在播）：
            //   卡片還在播就維持 0（卡片播完會自己還原）；卡片已經播完就還原成 1，不能把別人的 0 當成常態還回去。
            float restoreTo = previousTimeScale;
            if (restoreTo <= 0f)
            {
                StoryCardPlayer cards = FindAnyObjectByType<StoryCardPlayer>();   // 不用 Instance：它找不到會自己生一個
                bool cardStillPlaying = cards != null && cards.IsPlaying;
                restoreTo = cardStillPlaying ? 0f : 1f;
            }
            Time.timeScale = restoreTo;
        }

        isPausing = false;
    }
}
