using PixeLadder.EasyTransition;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
public sealed class StartMenuSceneTransition : MonoBehaviour
{
    [SerializeField] private string targetSceneName = "CottonCastle";   // ★1019
    [SerializeField] private TransitionEffect transitionEffect;
    private bool transitionRequested;

    private void Awake()
    {
        transitionRequested = false;
        Collider2D buttonCollider = GetComponent<Collider2D>();
        if (buttonCollider != null)
            buttonCollider.enabled = true;
    }

    private void OnEnable()
    {
        transitionRequested = false;
        Collider2D buttonCollider = GetComponent<Collider2D>();
        if (buttonCollider != null)
            buttonCollider.enabled = true;
    }

    private void OnMouseDown()
    {
        if (MainMenuController.OwnsClicks) return;   // ★1011 點擊由 MainMenuController 統一偵測
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
        GameFlow.StartNewRun();   // ★1011 新的一輪：上一輪留下來的狀態統一在這裡清掉

        Collider2D buttonCollider = GetComponent<Collider2D>();
        if (buttonCollider != null)
            buttonCollider.enabled = false;

        // ★1016 測試模式（遊戲旁邊放了 WoNiTa_TESTER.txt、裡面寫 start=場景名）：直接進指定的關卡
        string sceneToLoad = targetSceneName;
        string testerStart = TesterMode.StartScene;
        if (!string.IsNullOrEmpty(testerStart))
        {
            if (Application.CanStreamedLevelBeLoaded(testerStart))
            {
                sceneToLoad = testerStart;
                Debug.LogWarning($"[StartMenuSceneTransition] 測試模式：直接進「{testerStart}」", this);
            }
            else
            {
                Debug.LogWarning($"[StartMenuSceneTransition] 測試檔指定的場景「{testerStart}」不在打包清單裡，照常進 {targetSceneName}", this);
            }
        }

        transitioner.LoadScene(sceneToLoad, transitionEffect);

        // ★1011 轉場器如果正「卡在轉場中」會直接忽略這次呼叫，按鈕卻已經被關掉＝永遠按不動。
        //   沒有真的開始轉場就把按鈕還原，讓玩家可以再按一次。
        if (!transitioner.IsTransitioning)
        {
            Debug.LogWarning("[StartMenuSceneTransition] 轉場器沒有開始轉場（可能被忽略），按鈕還原，可以再按一次。", this);
            transitionRequested = false;
            if (buttonCollider != null) buttonCollider.enabled = true;
        }
    }
}
