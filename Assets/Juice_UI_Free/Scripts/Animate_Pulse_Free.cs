using UnityEngine;
using DG.Tweening;

namespace SpankyBoy.JuiceUI.Free
{
    public class Animate_Pulse_Free : MonoBehaviour
    {
        // --- FREE VERSION: Limited to 2 pulse types ---
        public enum PulseType
        {
            Scale,           // Classic scale pulse (universal)
            Breathe          // Slow, gentle pulse (attention)
        }

        [Header("Pulse Type")]
        public PulseType pulseType = PulseType.Scale;

        [Header("Scale Settings")]
        [Tooltip("The target scale multiplier for the pulse effect. 1.1 = 110% of original size.")]
        public float pulseScale = 1.1f;

        [Header("Timing")]
        [Tooltip("The duration of one full pulse cycle (scale up and down).")]
        public float duration = 1.0f;

        [Tooltip("The easing for the pulse. 'InOutSine' is smooth and gentle.")]
        public Ease easeType = Ease.InOutSine;

        [Header("Behavior")]
        [Tooltip("If true, the pulse animation will start automatically when the object is enabled.")]
        public bool playOnEnable = true;

        private Vector3 originalScale;
        private Tween pulseTween;

        void Awake()
        {
            originalScale = transform.localScale;
        }

        void OnEnable()
        {
            if (playOnEnable)
            {
                StartPulse();
            }
        }

        void OnDisable()
        {
            StopPulse();
        }

        /// <summary>
        /// Starts the continuous pulse animation.
        /// </summary>
        public void StartPulse()
        {
            StopPulse();

            switch (pulseType)
            {
                case PulseType.Scale:
                    pulseTween = transform.DOScale(originalScale * pulseScale, duration)
                        .SetEase(easeType)
                        .SetLoops(-1, LoopType.Yoyo)
                        .SetUpdate(true);
                    break;

                case PulseType.Breathe:
                    // Slower, gentler pulse
                    float breatheScale = 1f + (pulseScale - 1f) * 0.5f; // Half the intensity
                    pulseTween = transform.DOScale(originalScale * breatheScale, duration * 1.5f)
                        .SetEase(Ease.InOutSine)
                        .SetLoops(-1, LoopType.Yoyo)
                        .SetUpdate(true);
                    break;
            }
        }

        /// <summary>
        /// Stops the pulse animation and returns the object to its original scale.
        /// </summary>
        public void StopPulse()
        {
            if (pulseTween != null && pulseTween.IsActive())
            {
                pulseTween.Kill();
            }
            transform.localScale = originalScale;
        }
    }
}