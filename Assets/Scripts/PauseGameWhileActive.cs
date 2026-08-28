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
            Time.timeScale = previousTimeScale;
        }

        isPausing = false;
    }
}
