namespace PixeLadder.EasyTransition
{
    using System;
    using System.Collections;
    using UnityEngine;
    using UnityEngine.SceneManagement;
    using UnityEngine.UI;
    using UnityEngine.Events;

    /// <summary>
    /// The global singleton manager that controls the scene transition lifecycle.
    /// Handles asynchronous scene loading, automatic UI blocking, and VR camera updates.
    /// </summary>
    [DisallowMultipleComponent]
    public class SceneTransitioner : MonoBehaviour
    {
        public static SceneTransitioner Instance { get; private set; }

        [Header("Canvas Configuration")]
        [Tooltip("The UI Image prefab that will be instantiated to cover the screen.")]
        [SerializeField] private Image transitionImagePrefab;

        [Tooltip("Use ScreenSpaceOverlay for standard screens. Use ScreenSpaceCamera for VR.")]
        [SerializeField] private RenderMode canvasRenderMode = RenderMode.ScreenSpaceOverlay;

        [Tooltip("Required if RenderMode is ScreenSpaceCamera. Leave empty to automatically use Camera.main.")]
        [SerializeField] private Camera targetCamera;

        [Tooltip("The sorting order of the Canvas. Ensure this is higher than your game's UI.")]
        [SerializeField] private int canvasSortingOrder = 999;

        [Tooltip("If true, automatically creates a CanvasGroup to block all player raycasts/clicks during the transition.")]
        [SerializeField] private bool blockUIInteraction = true;

        [Header("Default Settings")]
        [Tooltip("The effect used if a transition is called without explicitly passing one.")]
        [SerializeField] private TransitionEffect defaultTransition;

        [Header("Events")]
        public UnityEvent OnTransitionStart = new UnityEvent();
        public UnityEvent OnFadeOutComplete = new UnityEvent();
        public UnityEvent OnSceneLoaded = new UnityEvent();
        public UnityEvent OnFadeInStart = new UnityEvent();
        public UnityEvent OnTransitionFinished = new UnityEvent();

        private Canvas transitionCanvas;
        private Image transitionImageInstance;
        private CanvasGroup canvasGroup;

        /// <summary>
        /// Returns true if a transition is currently in progress.
        /// </summary>
        public bool IsTransitioning { get; private set; }

        /// <summary>★1017 Inspector 設定的 Canvas 模式（Canvas.renderMode 在還沒指定相機時會回報成 Overlay，不能拿來還原）。</summary>
        public RenderMode ConfiguredRenderMode => canvasRenderMode;

        /// <summary>★1017 Inspector 設定的 Canvas 排序。</summary>
        public int ConfiguredSortingOrder => canvasSortingOrder;

        private static readonly int RectSizeID = Shader.PropertyToID("_RectSize");

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
                InitializeCanvas();
                InitializeEvents();
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void InitializeEvents()
        {
            OnTransitionStart ??= new UnityEvent();
            OnFadeOutComplete ??= new UnityEvent();
            OnSceneLoaded ??= new UnityEvent();
            OnFadeInStart ??= new UnityEvent();
            OnTransitionFinished ??= new UnityEvent();
        }

        private void InitializeCanvas()
        {
            GameObject canvasGO = new GameObject("TransitionCanvas");
            canvasGO.transform.SetParent(transform);

            transitionCanvas = canvasGO.AddComponent<Canvas>();
            transitionCanvas.renderMode = canvasRenderMode;
            transitionCanvas.sortingOrder = canvasSortingOrder;

            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();

            canvasGroup = canvasGO.AddComponent<CanvasGroup>();
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;

            transitionImageInstance = Instantiate(transitionImagePrefab, canvasGO.transform);

            RectTransform rectT = transitionImageInstance.rectTransform;
            rectT.anchorMin = Vector2.zero;
            rectT.anchorMax = Vector2.one;
            // Extremely large base scale ensures coverage during Canvas scaling calculations
            rectT.sizeDelta = new Vector2(6000f, 6000f);
            rectT.anchoredPosition = Vector2.zero;

            transitionImageInstance.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            UpdateVRCamera();
        }

        /// <summary>
        /// Starts a transition and loads a new scene asynchronously by its string name.
        /// </summary>
        public void LoadScene(string sceneName, TransitionEffect effect = null)
        {
            StartTransitionRoutine(sceneName, -1, null, effect);
        }

        /// <summary>
        /// Starts a transition and loads a new scene asynchronously by its Build Index.
        /// </summary>
        public void LoadScene(int buildIndex, TransitionEffect effect = null)
        {
            StartTransitionRoutine(null, buildIndex, null, effect);
        }

        /// <summary>
        /// Plays a transition without loading a scene. Executes a custom C# Action (like teleportation) at the midpoint.
        /// </summary>
        public void PlayTransition(Action onMidPoint = null, TransitionEffect effect = null)
        {
            StartTransitionRoutine(null, -1, onMidPoint, effect);
        }

        // ★1011 防呆：轉場被打斷（例如直接 LoadScene、timeScale 卡 0）時，IsTransitioning 會永遠是 true，
        //   之後所有 LoadScene 都被這行吃掉＝「按鈕按了沒反應」。超過 stuckTimeoutSeconds 就當作卡死，強制放行。
        private float transitionStartRealtime = -999f;
        private const float StuckTimeoutSeconds = 20f;

        /// <summary>★1011 強制結束目前的轉場狀態（回主選單時呼叫）：停掉協程、蓋住畫面的圖關掉、不再擋點擊。</summary>
        public void ForceReset()
        {
            StopAllCoroutines();
            IsTransitioning = false;
            if (transitionImageInstance != null) transitionImageInstance.gameObject.SetActive(false);
            if (canvasGroup != null) canvasGroup.blocksRaycasts = false;
        }

        /// <summary>★1011 轉場已經進行超過 minAgeSeconds 秒還沒結束，才強制重置（正常的轉場收尾不會被打斷）。</summary>
        public void ResetIfStuck(float minAgeSeconds)
        {
            // ★1017 效果本身的長度（淡出＋中間停頓＋淡入）也要算進去，否則比較長的轉場（例如 CircleEffect 6 秒）
            //   會在載入完、正在淡入時被誤判卡死、直接砍掉。
            if (IsTransitioning && Time.unscaledTime - transitionStartRealtime > minAgeSeconds + currentEffectSeconds) ForceReset();
        }

        private float currentEffectSeconds;

        private void StartTransitionRoutine(string sceneName, int sceneIndex, Action midPointAction, TransitionEffect effect)
        {
            if (IsTransitioning)
            {
                if (Time.unscaledTime - transitionStartRealtime < StuckTimeoutSeconds) return;
                Debug.LogWarning("[SceneTransitioner] 上一次轉場超過 " + StuckTimeoutSeconds + " 秒還沒結束，判定卡死，強制重置後繼續。", this);
                ForceReset();
            }
            transitionStartRealtime = Time.unscaledTime;

            TransitionEffect usedEffect = effect != null ? effect : defaultTransition;
            if (usedEffect == null)
            {
                Debug.LogError("[SceneTransitioner] No transition effect assigned.", this);
                return;
            }

            currentEffectSeconds = usedEffect.Duration + usedEffect.MiddleDelay;

            StartCoroutine(TransitionRoutine(sceneName, sceneIndex, midPointAction, usedEffect));
        }

        private IEnumerator TransitionRoutine(string sceneName, int sceneIndex, Action midPointAction, TransitionEffect effect)
        {
            IsTransitioning = true;
            transitionImageInstance.gameObject.SetActive(true);

            if (canvasGroup != null && blockUIInteraction)
                canvasGroup.blocksRaycasts = true;

            // Reset image scale to standard screen size for accurate shader UV calculations
            transitionImageInstance.rectTransform.sizeDelta = new Vector2(600f, 600f);

            OnTransitionStart?.Invoke();

            Material materialInstance = new Material(effect.transitionMaterial);

            Canvas.ForceUpdateCanvases();
            Rect rect = transitionImageInstance.rectTransform.rect;
            materialInstance.SetVector(RectSizeID, new Vector4(rect.width, rect.height, 0, 0));

            effect.SetEffectProperties(materialInstance);
            transitionImageInstance.material = materialInstance;

            // --- FADE OUT ---
            yield return effect.AnimateOut(transitionImageInstance);
            OnFadeOutComplete?.Invoke();

            // Keep the outgoing scene completely covered while the target scene
            // is loaded and fully activated.
            transitionImageInstance.rectTransform.sizeDelta = new Vector2(6000f, 6000f);

            // --- MIDPOINT ---
            midPointAction?.Invoke();

            if (effect.MiddleDelay > 0)
                yield return new WaitForSecondsRealtime(effect.MiddleDelay);

            // Load and activate the target while the screen is fully covered.
            // Fade In starts only after the new scene is active.
            if (!string.IsNullOrEmpty(sceneName) || sceneIndex >= 0)
            {
                AsyncOperation sceneLoadOperation = !string.IsNullOrEmpty(sceneName)
                    ? SceneManager.LoadSceneAsync(sceneName)
                    : SceneManager.LoadSceneAsync(sceneIndex);

                if (sceneLoadOperation != null)
                {
                    yield return sceneLoadOperation;

                    OnSceneLoaded?.Invoke();
                    UpdateVRCamera();
                }
            }

            transitionImageInstance.rectTransform.sizeDelta = new Vector2(600f, 600f);

            OnFadeInStart?.Invoke();

            // --- FADE IN ---
            yield return effect.AnimateIn(transitionImageInstance);

            // The new scene is fully visible once Fade In completes.
            transitionImageInstance.gameObject.SetActive(false);

            // --- CLEANUP ---
            if (canvasGroup != null) canvasGroup.blocksRaycasts = false;
            IsTransitioning = false;

            Destroy(materialInstance);
            OnTransitionFinished?.Invoke();
        }


        /// <summary>
        /// Ensures the Canvas remains attached to the current main camera, crucial for VR setups after a scene loads.
        /// </summary>
        private void UpdateVRCamera()
        {
            if (transitionCanvas != null && (canvasRenderMode == RenderMode.ScreenSpaceCamera || canvasRenderMode == RenderMode.WorldSpace))
            {
                Camera cam = targetCamera != null ? targetCamera : Camera.main;
                if (cam != null && transitionCanvas.worldCamera != cam)
                {
                    transitionCanvas.worldCamera = cam;
                    // Push the canvas just beyond the near clip plane to prevent eye-clipping in VR
                    transitionCanvas.planeDistance = cam.nearClipPlane + 0.01f;
                    Canvas.ForceUpdateCanvases();
                }
            }
        }

        /// <summary>
        /// A helper method to easily play a one-shot audio clip globally, ensuring it isn't destroyed by scene loads.
        /// </summary>
        public void PlayGlobalSound(AudioClip clip)
        {
            if (clip != null && TryGetComponent(out AudioSource source)) source.PlayOneShot(clip);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
        }
    }
}