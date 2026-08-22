using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace SpankyBoy.JuiceUI.Free
{
    public class PanelDemoController_Free : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The panel with PanelAnimator_Free component")]
        public PanelAnimator_Free panelAnimator;

        [Header("Dropdowns")]
        public TMP_Dropdown inAnimationDropdown;
        public TMP_Dropdown inDirectionDropdown;
        public TMP_Dropdown outAnimationDropdown;
        public TMP_Dropdown outDirectionDropdown;

        [Header("Buttons")]
        public Button animateButton;
        public Button closeButton;

        private bool isAnimationInProgress = false;

        void Start()
        {
            if (panelAnimator == null)
            {
                Debug.LogError("PanelAnimator_Free reference is missing!");
                return;
            }

            // Setup dropdowns
            SetupDropdowns();

            // Add button listeners
            if (animateButton != null)
            {
                animateButton.onClick.AddListener(OnAnimateButtonClicked);
            }

            if (closeButton != null)
            {
                closeButton.onClick.AddListener(OnCloseButtonClicked);
            }

            // Add listeners to update panel settings when dropdowns change
            inAnimationDropdown.onValueChanged.AddListener(delegate { UpdatePanelSettings(); });
            inDirectionDropdown.onValueChanged.AddListener(delegate { UpdatePanelSettings(); });
            outAnimationDropdown.onValueChanged.AddListener(delegate { UpdatePanelSettings(); });
            outDirectionDropdown.onValueChanged.AddListener(delegate { UpdatePanelSettings(); });

            // Disable auto-play on enable since we want manual control
            panelAnimator.playInOnEnable = false;

            // Set initial settings from dropdowns
            UpdatePanelSettings();

            // Update direction dropdown states
            UpdateDirectionDropdownStates();
        }

        void SetupDropdowns()
        {
            // Setup In Animation Dropdown
            inAnimationDropdown.ClearOptions();
            inAnimationDropdown.AddOptions(new System.Collections.Generic.List<string>
            {
                "Fade", "Slide", "Scale", "Pop"
            });

            // Setup Out Animation Dropdown
            outAnimationDropdown.ClearOptions();
            outAnimationDropdown.AddOptions(new System.Collections.Generic.List<string>
            {
                "Fade", "Slide", "Scale", "Pop"
            });

            // Setup Direction Dropdowns
            inDirectionDropdown.ClearOptions();
            inDirectionDropdown.AddOptions(new System.Collections.Generic.List<string>
            {
                "Left", "Right", "Top", "Bottom"
            });

            outDirectionDropdown.ClearOptions();
            outDirectionDropdown.AddOptions(new System.Collections.Generic.List<string>
            {
                "Left", "Right", "Top", "Bottom"
            });
        }

        void UpdatePanelSettings()
        {
            // Update In Animation
            panelAnimator.inAnimation = (PanelAnimator_Free.AnimationType)inAnimationDropdown.value;

            // Update In Direction
            panelAnimator.inDirection = (PanelAnimator_Free.Direction)inDirectionDropdown.value;

            // Update Out Animation
            panelAnimator.outAnimation = (PanelAnimator_Free.AnimationType)outAnimationDropdown.value;

            // Update Out Direction
            panelAnimator.outDirection = (PanelAnimator_Free.Direction)outDirectionDropdown.value;

            // Update direction dropdown interactability
            UpdateDirectionDropdownStates();
        }

        void UpdateDirectionDropdownStates()
        {
            // In Direction is only relevant when In Animation is Slide
            bool inIsSlide = panelAnimator.inAnimation == PanelAnimator_Free.AnimationType.Slide;
            inDirectionDropdown.interactable = inIsSlide;

            // Out Direction is only relevant when Out Animation is Slide
            bool outIsSlide = panelAnimator.outAnimation == PanelAnimator_Free.AnimationType.Slide;
            outDirectionDropdown.interactable = outIsSlide;

            // Optional: Visual feedback for disabled state
            if (inDirectionDropdown.transform.parent != null)
            {
                var inDirGroup = inDirectionDropdown.transform.parent.GetComponent<CanvasGroup>();
                if (inDirGroup != null)
                {
                    inDirGroup.alpha = inIsSlide ? 1f : 0.5f;
                }
            }

            if (outDirectionDropdown.transform.parent != null)
            {
                var outDirGroup = outDirectionDropdown.transform.parent.GetComponent<CanvasGroup>();
                if (outDirGroup != null)
                {
                    outDirGroup.alpha = outIsSlide ? 1f : 0.5f;
                }
            }
        }

        void OnAnimateButtonClicked()
        {
            if (isAnimationInProgress) return;

            // Update settings one more time before animating
            UpdatePanelSettings();

            // Start the animation sequence: Close then Open
            StartCoroutine(AnimateSequence());
        }

        void OnCloseButtonClicked()
        {
            if (isAnimationInProgress) return;

            panelAnimator.AnimateOut();
        }

        System.Collections.IEnumerator AnimateSequence()
        {
            isAnimationInProgress = true;

            // Disable animate button during animation
            if (animateButton != null)
            {
                animateButton.interactable = false;
            }

            // First, close the panel (if it's open)
            panelAnimator.AnimateOut();

            // Wait for out animation to complete
            yield return new WaitForSeconds(panelAnimator.outDuration + panelAnimator.outDelay + 0.1f);

            // Make sure panel is active
            panelAnimator.gameObject.SetActive(true);

            // Then open it with the new animation
            panelAnimator.AnimateIn();

            // Wait for in animation to complete
            yield return new WaitForSeconds(panelAnimator.inDuration + panelAnimator.inDelay + 0.1f);

            isAnimationInProgress = false;

            // Re-enable animate button
            if (animateButton != null)
            {
                animateButton.interactable = true;
            }
        }
    }
}