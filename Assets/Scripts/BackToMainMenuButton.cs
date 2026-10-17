using PixeLadder.EasyTransition;
using UnityEngine;

/// <summary>
/// 設定畫面的「回主選單」按鈕：按下後用 Easy Transition 的轉場效果回到主選單。
/// 設定畫面維持開著（遊戲維持暫停），轉場動畫用的是不受暫停影響的時間。
/// 回到主選單後 GameFlow 會自動把這一輪的狀態清掉。
///
/// 設定畫面與齒輪 HUD 畫在 Screen Space - Overlay 的 Canvas 上，一般的轉場（Screen Space - Camera）蓋不住它們。
/// 所以這次轉場期間，把轉場的 Canvas 暫時改成 Overlay、放到最上層，並換成能在 Overlay 上畫的圓形材質
/// （overlayMaterial，計算跟原本的 CircleEffect 一樣）；轉場結束後還原，不影響其他轉場。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
public sealed class BackToMainMenuButton : MonoBehaviour
{
    [SerializeField] private string targetSceneName = GameFlow.MenuSceneName;
    [SerializeField] private TransitionEffect transitionEffect;
    [Tooltip("能畫在 Overlay Canvas 上的圓形轉場材質（UI/EasyTransition/CircleOverlay）。")]
    [SerializeField] private Material overlayMaterial;
    [Tooltip("轉場 Canvas 在這次轉場期間的排序（越大越上層，最大 32767）。")]
    [SerializeField] private int overlaySortingOrder = 32767;
    [Tooltip("場景裡還沒有轉場器時（例如在編輯器直接從關卡按 Play）用這個生一個。")]
    [SerializeField] private SceneTransitioner transitionerPrefab;

    private bool requested;

    private void OnEnable()
    {
        requested = false;
        Collider2D col = GetComponent<Collider2D>();
        if (col != null) col.enabled = true;
    }

    private void OnMouseDown()
    {
        BackToMenu();
    }

    public void BackToMenu()
    {
        if (requested) return;

        SceneTransitioner transitioner = SceneTransitioner.Instance;
        if (transitioner == null && transitionerPrefab != null)
            transitioner = Instantiate(transitionerPrefab);   // Awake 會自己設成 Instance、跨場景保留

        if (transitioner == null || transitionEffect == null)
        {
            Debug.LogWarning("[BackToMainMenuButton] 沒有轉場器或轉場效果，直接回主選單。", this);
            requested = true;
            GameFlow.ReturnToMainMenu("設定畫面按回主選單（無轉場）");
            return;
        }

        TransitionEffect effect = transitionEffect;
        Canvas canvas = transitioner.GetComponentInChildren<Canvas>(true);
        bool raised = canvas != null && overlayMaterial != null;
        RenderMode oldMode = RenderMode.ScreenSpaceOverlay;
        int oldOrder = 0;

        if (raised)
        {
            // 用 CircleEffect 的複本（時間、曲線都一樣），只把材質換成能畫在 Overlay 上的版本
            effect = Instantiate(transitionEffect);
            effect.transitionMaterial = overlayMaterial;

            // 用轉場器自己的設定還原（Canvas.renderMode 在還沒指定相機時會回報成 Overlay）
            oldMode = transitioner.ConfiguredRenderMode;
            oldOrder = transitioner.ConfiguredSortingOrder;
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = overlaySortingOrder;
        }

        transitioner.LoadScene(targetSceneName, effect);

        if (!transitioner.IsTransitioning)
        {
            Debug.LogWarning("[BackToMainMenuButton] 轉場器沒有開始轉場（可能正在別的轉場中），可以再按一次。", this);
            if (raised) Restore(canvas, oldMode, oldOrder, effect);
            return;
        }

        if (raised)
        {
            // 這個按鈕換場景時就被銷毀了，還原交給掛在轉場器（跨場景保留）上的小元件：
            // 每幀檢查，轉場一結束（包含被 GameFlow 強制重置、不會發 OnTransitionFinished 的情況）就還原。
            TransitionCanvasRestorer restorer = transitioner.gameObject.AddComponent<TransitionCanvasRestorer>();
            restorer.Init(transitioner, canvas, oldMode, oldOrder, effect);
        }

        requested = true;
        Collider2D col = GetComponent<Collider2D>();
        if (col != null) col.enabled = false;
    }

    private static void Restore(Canvas canvas, RenderMode mode, int order, TransitionEffect effectCopy)
    {
        if (canvas != null)
        {
            canvas.renderMode = mode;
            canvas.sortingOrder = order;
        }
        if (effectCopy != null) Destroy(effectCopy);
    }

    /// <summary>轉場結束後把轉場 Canvas 的模式、排序還原，並清掉效果複本，然後自己移除。</summary>
    private sealed class TransitionCanvasRestorer : MonoBehaviour
    {
        private SceneTransitioner transitioner;
        private Canvas canvas;
        private RenderMode mode;
        private int order;
        private TransitionEffect effectCopy;

        public void Init(SceneTransitioner t, Canvas c, RenderMode m, int o, TransitionEffect e)
        {
            transitioner = t; canvas = c; mode = m; order = o; effectCopy = e;
        }

        private void LateUpdate()
        {
            if (transitioner != null && transitioner.IsTransitioning) return;
            Restore(canvas, mode, order, effectCopy);
            Destroy(this);
        }
    }
}
