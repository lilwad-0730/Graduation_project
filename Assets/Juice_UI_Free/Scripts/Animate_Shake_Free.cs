using UnityEngine;
using DG.Tweening;

namespace SpankyBoy.JuiceUI.Free
{
    public class Animate_Shake_Free : MonoBehaviour
    {
        // --- FREE VERSION: Limited to 3 shake types ---
        public enum ShakeType
        {
            Position,        // Classic position shake (horizontal + vertical)
            Horizontal,      // Only X-axis (great for "no" feedback)
            Rotation         // Rotation wobble (subtle but effective)
        }

        [Header("Shake Type")]
        public ShakeType shakeType = ShakeType.Position;

        [Header("Animation Settings")]
        public float duration = 0.5f;

        [Tooltip("The intensity/distance of the shake.")]
        public float strength = 15f;

        [Tooltip("How much 'jaggedness' the shake has. Higher values are more rapid.")]
        public int vibrato = 10;

        [Tooltip("How much randomness is applied to the shake. (0 = no randomness).")]
        [Range(0, 180)]
        public float randomness = 90;

        [Tooltip("The delay before the animation starts.")]
        public float delay = 0f;

        [Header("Audio (Optional)")]
        public AudioClip audioClip;
        [Tooltip("If left unassigned, will try to get AudioSource on this GameObject.")]
        public AudioSource customAudioSource;
        [Range(0f, 1f)]
        public float volume = 1.0f;

        private Vector3 originalPosition;
        private Vector3 originalRotation;

        void Awake()
        {
            originalPosition = transform.localPosition;
            originalRotation = transform.localEulerAngles;
        }

        /// <summary>
        /// Plays the shake animation.
        /// </summary>
        public void Animate()
        {
            transform.DOKill();
            PlaySound();

            switch (shakeType)
            {
                case ShakeType.Position:
                    transform.DOShakePosition(duration, strength, vibrato, randomness, fadeOut: true)
                        .SetDelay(delay)
                        .SetUpdate(true)
                        .OnComplete(() => transform.localPosition = originalPosition);
                    break;

                case ShakeType.Horizontal:
                    transform.DOShakePosition(duration, new Vector3(strength, 0, 0), vibrato, randomness, fadeOut: true)
                        .SetDelay(delay)
                        .SetUpdate(true)
                        .OnComplete(() => transform.localPosition = originalPosition);
                    break;

                case ShakeType.Rotation:
                    transform.DOShakeRotation(duration, strength, vibrato, randomness, fadeOut: true)
                        .SetDelay(delay)
                        .SetUpdate(true)
                        .OnComplete(() => transform.localEulerAngles = originalRotation);
                    break;
            }
        }

        private void PlaySound()
        {
            if (audioClip == null) return;
            AudioSource source = customAudioSource ? customAudioSource : GetComponent<AudioSource>();
            if (source != null)
            {
                source.PlayOneShot(audioClip, volume);
            }
        }
    }
}