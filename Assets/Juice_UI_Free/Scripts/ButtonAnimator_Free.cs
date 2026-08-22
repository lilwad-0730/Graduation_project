using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using DG.Tweening;

namespace SpankyBoy.JuiceUI.Free
{
    [RequireComponent(typeof(Button))]
    public class ButtonAnimator_Free : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        // --- FREE VERSION: Limited to 5 hover + 5 click effects ---
        public enum HoverEffect 
        { 
            None,
            Scale,           // Universal - smooth grow
            BounceScale,     // Playful - bouncy grow
            ColorTint,       // Subtle - color only
            Glow,            // Modern - outline glow
            Pulse            // Attention - breathing effect
        }
        
        public enum ClickEffect 
        { 
            None,
            Punch,           // Universal - quick shrink
            Squeeze,         // Responsive - squash/stretch
            Flash,           // Visual - color flash
            Shake,           // Impact - position shake
            Jello            // Fun - jiggly wobble
        }

        // --- HOVER ---
        [Header("Hover Animation")]
        public HoverEffect hoverEffect = HoverEffect.Scale;
        public float hoverDuration = 0.2f;
        public Ease hoverEase = Ease.OutQuad;
        
        [Header("--- Hover Settings ---")]
        public float hoverScale = 1.1f;
        public Color hoverColor = new Color(0.9f, 0.9f, 0.9f, 1f);
        public float hoverGlowSize = 5f;

        // --- CLICK ---
        [Header("Click Animation")]
        public ClickEffect clickEffect = ClickEffect.Punch;
        public float clickDuration = 0.2f;
        public Ease clickEase = Ease.OutQuad;
        
        [Header("--- Click Settings ---")]
        public Vector3 punchStrength = new Vector3(-0.1f, -0.1f, 0);
        public float shakeStrength = 10f;
        public Color flashColor = Color.white;
        public float squeezeAmount = 0.85f;

        // --- AUDIO ---
        [Header("Audio (Optional)")]
        public AudioClip hoverSound;
        public AudioClip clickSound;
        [Tooltip("If left unassigned, will use an AudioSource on this GameObject.")]
        public AudioSource customAudioSource;
        [Range(0f, 1f)]
        public float audioVolume = 1.0f;

        // --- Private References ---
        private Button button;
        private Graphic targetGraphic;
        private AudioSource audioSource;
        private Vector3 originalScale;
        private Color originalColor;
        private bool isPointerDown = false;
        private Outline glowOutline;
        private Tween hoverTween;

        void Awake()
        {
            button = GetComponent<Button>();
            targetGraphic = GetComponent<Graphic>();
            audioSource = customAudioSource ? customAudioSource : GetComponent<AudioSource>();

            originalScale = transform.localScale;
            
            if (targetGraphic != null)
            {
                originalColor = targetGraphic.color;
            }
        }

        void OnDestroy()
        {
            CleanupHoverEffects();
        }

        // --- Event Handlers ---

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!button.interactable) return;
            isPointerDown = false;
            PlaySound(hoverSound);
            AnimateHover(true);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (!button.interactable) return;
            if (!isPointerDown)
            {
                AnimateHover(false);
            }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!button.interactable) return;
            isPointerDown = true;
            PlaySound(clickSound);
            AnimateClick();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!button.interactable) return;
            isPointerDown = false;
            AnimateHover(false);
        }

        // --- Animation Logic ---

        private void AnimateHover(bool isHovering)
        {
            if (hoverEffect == HoverEffect.None) return;
            
            CleanupHoverEffects();

            float duration = isHovering ? hoverDuration : hoverDuration * 1.5f;

            if (isHovering)
            {
                switch (hoverEffect)
                {
                    case HoverEffect.Scale:
                        hoverTween = transform.DOScale(originalScale * hoverScale, duration).SetEase(hoverEase);
                        break;

                    case HoverEffect.BounceScale:
                        hoverTween = transform.DOScale(originalScale * hoverScale, duration).SetEase(Ease.OutBounce);
                        break;

                    case HoverEffect.ColorTint:
                        if (targetGraphic) 
                            hoverTween = targetGraphic.DOColor(hoverColor, duration).SetEase(hoverEase);
                        break;

                    case HoverEffect.Glow:
                        CreateGlowEffect(true, duration);
                        break;

                    case HoverEffect.Pulse:
                        hoverTween = transform.DOScale(originalScale * 1.05f, duration / 2)
                            .SetEase(Ease.InOutSine)
                            .SetLoops(-1, LoopType.Yoyo);
                        break;
                }
            }
            else // Return to normal
            {
                switch (hoverEffect)
                {
                    case HoverEffect.Scale:
                    case HoverEffect.BounceScale:
                    case HoverEffect.Pulse:
                        hoverTween = transform.DOScale(originalScale, duration).SetEase(hoverEase);
                        break;

                    case HoverEffect.ColorTint:
                        if (targetGraphic) 
                            hoverTween = targetGraphic.DOColor(originalColor, duration).SetEase(hoverEase);
                        break;

                    case HoverEffect.Glow:
                        CreateGlowEffect(false, duration);
                        break;
                }
            }
        }

        private void AnimateClick()
        {
            if (clickEffect == ClickEffect.None) return;

            Vector3 currentScale = transform.localScale;

            switch (clickEffect)
            {
                case ClickEffect.Punch:
                    transform.DOPunchScale(punchStrength, clickDuration, 1, 0.5f);
                    break;

                case ClickEffect.Shake:
                    transform.DOShakePosition(clickDuration, strength: shakeStrength, vibrato: 20, randomness: 90, fadeOut: true);
                    break;

                case ClickEffect.Flash:
                    if (targetGraphic)
                    {
                        Color currentColor = targetGraphic.color;
                        DOTween.Sequence()
                            .Append(targetGraphic.DOColor(flashColor, clickDuration / 2))
                            .Append(targetGraphic.DOColor(currentColor, clickDuration / 2));
                    }
                    break;

                case ClickEffect.Squeeze:
                    Vector3 squeezeScale = new Vector3(currentScale.x * squeezeAmount, currentScale.y * squeezeAmount, 1f);
                    DOTween.Sequence()
                        .Append(transform.DOScale(squeezeScale, clickDuration / 2).SetEase(Ease.InQuad))
                        .Append(transform.DOScale(currentScale, clickDuration / 2).SetEase(Ease.OutElastic));
                    break;

                case ClickEffect.Jello:
                    DOTween.Sequence()
                        .Append(transform.DOScale(new Vector3(currentScale.x * 1.25f, currentScale.y * 0.75f, 1f), clickDuration / 4))
                        .Append(transform.DOScale(new Vector3(currentScale.x * 0.75f, currentScale.y * 1.25f, 1f), clickDuration / 4))
                        .Append(transform.DOScale(new Vector3(currentScale.x * 1.15f, currentScale.y * 0.85f, 1f), clickDuration / 4))
                        .Append(transform.DOScale(currentScale, clickDuration / 4));
                    break;
            }
        }

        // --- Helper Methods ---

        private void CleanupHoverEffects()
        {
            if (hoverTween != null)
            {
                hoverTween.Kill();
                hoverTween = null;
            }
            
            if (glowOutline != null)
            {
                Destroy(glowOutline);
                glowOutline = null;
            }
        }

        private void CreateGlowEffect(bool enable, float duration)
        {
            if (enable)
            {
                if (glowOutline == null)
                {
                    glowOutline = gameObject.AddComponent<Outline>();
                    glowOutline.effectColor = hoverColor;
                    glowOutline.effectDistance = Vector2.zero;
                }
                
                DOTween.To(() => glowOutline.effectDistance, 
                    x => glowOutline.effectDistance = x,
                    new Vector2(hoverGlowSize, -hoverGlowSize),
                    duration);
            }
            else
            {
                if (glowOutline != null)
                {
                    DOTween.To(() => glowOutline.effectDistance,
                        x => glowOutline.effectDistance = x,
                        Vector2.zero,
                        duration)
                        .OnComplete(() => 
                        {
                            if (glowOutline != null)
                            {
                                Destroy(glowOutline);
                                glowOutline = null;
                            }
                        });
                }
            }
        }

        private void PlaySound(AudioClip clip)
        {
            if (clip != null && audioSource != null)
            {
                audioSource.PlayOneShot(clip, audioVolume);
            }
        }
    }
}