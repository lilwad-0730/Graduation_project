namespace PixeLadder.EasyTransition.Effects
{
    using System.Collections;
    using UnityEngine;
    using UnityEngine.UI;

    /// <summary>
    /// A soft, procedural cloud bank that sweeps across the screen.
    /// </summary>
    [CreateAssetMenu(fileName = "NewCloudEffect", menuName = "Easy Transition/Cloud Effect")]
    public class CloudEffect : TransitionEffect
    {
        [Header("Cloud Settings")]
        [Range(0.005f, 0.25f)]
        [SerializeField] private float smoothness = 0.06f;

        [Range(1f, 12f)]
        [SerializeField] private float cloudScale = 4.5f;

        [SerializeField] private Vector2 drift = new Vector2(0.08f, 0.025f);
        [SerializeField] private Color cloudColor = Color.black;

        public override float Smoothness
        {
            get => smoothness;
            set => smoothness = Mathf.Clamp(value, 0.005f, 0.25f);
        }

        private static readonly int SmoothnessProperty = Shader.PropertyToID("_Smoothness");
        private static readonly int CloudScaleProperty = Shader.PropertyToID("_CloudScale");
        private static readonly int DriftProperty = Shader.PropertyToID("_Drift");
        private static readonly int CloudColorProperty = Shader.PropertyToID("_CloudColor");

        public override void SetEffectProperties(Material materialInstance)
        {
            materialInstance.SetFloat(SmoothnessProperty, smoothness);
            materialInstance.SetFloat(CloudScaleProperty, cloudScale);
            materialInstance.SetVector(DriftProperty, drift);
            materialInstance.SetColor(CloudColorProperty, cloudColor);
        }

        public override IEnumerator AnimateOut(Image transitionImage)
        {
            yield return AnimateCutoff(transitionImage.material, 0f, 1f, duration / 2f);
        }

        public override IEnumerator AnimateIn(Image transitionImage)
        {
            yield return AnimateCutoff(transitionImage.material, 1f, 0f, duration / 2f);
        }
    }
}
