using PixeLadder.EasyTransition;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
public sealed class StartMenuSceneTransition : MonoBehaviour
{
    [SerializeField] private string targetSceneName = "SampleScene";
    [SerializeField] private TransitionEffect transitionEffect;
    private bool transitionRequested;

    private void OnMouseDown()
    {
        BeginTransition();
    }

    public void BeginTransition()
    {
        if (transitionRequested)
            return;

        SceneTransitioner transitioner = SceneTransitioner.Instance;
        if (transitioner == null)
        {
            Debug.LogError(
                "[StartMenuSceneTransition] SceneTransitioner is not available in the scene.",
                this);
            return;
        }

        if (transitionEffect == null)
        {
            Debug.LogError(
                "[StartMenuSceneTransition] No transition effect has been assigned.",
                this);
            return;
        }

        transitionRequested = true;
        PrepareNewGameRun();

        Collider2D buttonCollider = GetComponent<Collider2D>();
        if (buttonCollider != null)
            buttonCollider.enabled = false;

        transitioner.LoadScene(targetSceneName, transitionEffect);
    }

    private static void PrepareNewGameRun()
    {
        Time.timeScale = 1f;
        BookTransitionManager.ResetTransientState();
        EndCredits.EndingMode = false;
        PlayerRespawnSystem.IsAnyRespawning = false;
        MirrorWallAbsorbCutscene.IsAnyCutsceneRunning = false;

        StoryCardPlayer storyCardPlayer = FindAnyObjectByType<StoryCardPlayer>();
        if (storyCardPlayer != null)
        {
            storyCardPlayer.ReleaseCurtain();
        }
    }
}
