using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

namespace SpankyBoy.JuiceUI.Free
{
    [RequireComponent(typeof(RectTransform))]
    public class PanelAnimator_Free : MonoBehaviour
    {
        // --- FREE VERSION: Limited to 4 animation types ---
        public enum AnimationType
        {
            Fade,               // Simple fade in/out
            Slide,              // Slide from direction
            Scale,              // Scale from 0 to 1
            Pop                 // Bouncy pop effect
        }

        public enum Direction
        {
            Left,
            Right,
            Top,
            Bottom
        }

        [Header("Animation Settings")]
        public AnimationType inAnimation = AnimationType.Fade;
        public AnimationType outAnimation = AnimationType.Fade;
        public Direction inDirection = Direction.Bottom;
        public Direction outDirection = Direction.Top;

        [Header("Timing")]
        public float inDuration = 0.5f;
        public float outDuration = 0.4f;
        public float inDelay = 0f;
        public float outDelay = 0f;
        public Ease inEase = Ease.OutQuad;
        public Ease outEase = Ease.InQuad;

        [Header("Behavior")]
        public bool playInOnEnable = true;
        public bool disableOnOutComplete = true;
        public bool resetStateAfterOut = true;

        [Header("Audio")]
        public AudioClip inSound;
        public AudioClip outSound;
        public AudioSource customAudioSource;
        [Range(0f, 1f)]
        public float volume = 1.0f;

        private RectTransform rectTransform;
        private CanvasGroup canvasGroup;
        private Vector2 originalPosition;
        private Vector3 originalScale;
        private AudioSource audioSource;
        private bool isAnimating = false;

        void Awake()
        {
            rectTransform = GetComponent<RectTransform>();
            audioSource = customAudioSource ? customAudioSource : GetComponent<AudioSource>();
            
            originalPosition = rectTransform.anchoredPosition;
            originalScale = transform.localScale;

            canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null)
            {
                canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }
        }

        void OnEnable()
        {
            if (playInOnEnable)
            {
                AnimateIn();
            }
        }

        /// <summary>
        /// Play the In animation
        /// </summary>
        public void AnimateIn()
        {
            if (isAnimating) return;
            isAnimating = true;

            PlaySound(inSound);
            KillAllTweens();
            PrepareInAnimation();
            ExecuteInAnimation();
        }

        /// <summary>
        /// Play the Out animation
        /// </summary>
        public void AnimateOut()
        {
            if (isAnimating) return;
            isAnimating = true;

            PlaySound(outSound);
            KillAllTweens();
            ExecuteOutAnimation();
        }

        private void PrepareInAnimation()
        {
            switch (inAnimation)
            {
                case AnimationType.Fade:
                    canvasGroup.alpha = 0f;
                    break;

                case AnimationType.Slide:
                    rectTransform.anchoredPosition = GetOffscreenPosition(inDirection);
                    canvasGroup.alpha = 1f;
                    break;

                case AnimationType.Scale:
                case AnimationType.Pop:
                    transform.localScale = Vector3.zero;
                    canvasGroup.alpha = 1f;
                    break;
            }
        }

        private void ExecuteInAnimation()
        {
            Sequence sequence = DOTween.Sequence();

            switch (inAnimation)
            {
                case AnimationType.Fade:
                    sequence.Append(canvasGroup.DOFade(1f, inDuration).SetEase(inEase));
                    break;

                case AnimationType.Slide:
                    sequence.Append(rectTransform.DOAnchorPos(originalPosition, inDuration).SetEase(inEase));
                    break;

                case AnimationType.Scale:
                    sequence.Append(transform.DOScale(originalScale, inDuration).SetEase(inEase));
                    break;

                case AnimationType.Pop:
                    sequence.Append(transform.DOScale(originalScale, inDuration).SetEase(Ease.OutBack));
                    break;
            }

            sequence.SetDelay(inDelay)
                .SetUpdate(true)
                .OnComplete(() => isAnimating = false);
        }

        private void ExecuteOutAnimation()
        {
            Sequence sequence = DOTween.Sequence();

            switch (outAnimation)
            {
                case AnimationType.Fade:
                    sequence.Append(canvasGroup.DOFade(0f, outDuration).SetEase(outEase));
                    break;

                case AnimationType.Slide:
                    sequence.Append(rectTransform.DOAnchorPos(GetOffscreenPosition(outDirection), outDuration).SetEase(outEase));
                    break;

                case AnimationType.Scale:
                    sequence.Append(transform.DOScale(Vector3.zero, outDuration).SetEase(outEase));
                    break;

                case AnimationType.Pop:
                    sequence.Append(transform.DOScale(Vector3.zero, outDuration).SetEase(Ease.InBack));
                    break;
            }

            sequence.SetDelay(outDelay)
                .SetUpdate(true)
                .OnComplete(() =>
                {
                    isAnimating = false;
                    
                    if (disableOnOutComplete)
                    {
                        gameObject.SetActive(false);
                    }

                    if (resetStateAfterOut)
                    {
                        rectTransform.anchoredPosition = originalPosition;
                        transform.localScale = originalScale;
                        canvasGroup.alpha = 1f;
                    }
                });
        }

        private Vector2 GetOffscreenPosition(Direction direction)
        {
            Canvas parentCanvas = GetComponentInParent<Canvas>();
            if (parentCanvas == null) return originalPosition;

            RectTransform canvasRect = parentCanvas.GetComponent<RectTransform>();
            float canvasWidth = canvasRect.rect.width;
            float canvasHeight = canvasRect.rect.height;
            float panelWidth = rectTransform.rect.width;
            float panelHeight = rectTransform.rect.height;

            Vector2 offscreenPos = originalPosition;

            switch (direction)
            {
                case Direction.Left:
                    offscreenPos = new Vector2(-canvasWidth / 2 - panelWidth / 2, originalPosition.y);
                    break;
                case Direction.Right:
                    offscreenPos = new Vector2(canvasWidth / 2 + panelWidth / 2, originalPosition.y);
                    break;
                case Direction.Top:
                    offscreenPos = new Vector2(originalPosition.x, canvasHeight / 2 + panelHeight / 2);
                    break;
                case Direction.Bottom:
                    offscreenPos = new Vector2(originalPosition.x, -canvasHeight / 2 - panelHeight / 2);
                    break;
            }

            return offscreenPos;
        }

        private void KillAllTweens()
        {
            rectTransform.DOKill();
            transform.DOKill();
            canvasGroup.DOKill();
        }

        private void PlaySound(AudioClip clip)
        {
            if (clip != null && audioSource != null)
            {
                audioSource.PlayOneShot(clip, volume);
            }
        }

        // Public API
        public void Show() => AnimateIn();
        public void Hide() => AnimateOut();
    }
}