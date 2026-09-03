using UnityEngine;

public sealed class EnableAnimatorOnPlayerStep : MonoBehaviour
{
    
    private bool activationScheduled;
[SerializeField] private Animator targetAnimator;

    private void Awake()
    {
        if (targetAnimator != null)
            targetAnimator.enabled = false;
    }

private void OnCollisionEnter(Collision collision)
    {
        if (activationScheduled || targetAnimator == null)
            return;

        GameObject hit = collision.gameObject;
        if (!hit.CompareTag("Player") &&
            hit.GetComponentInParent<PlayerMovement>() == null &&
            !hit.name.ToLowerInvariant().Contains("player"))
            return;

        activationScheduled = true;
        Invoke(nameof(EnableTargetAnimator), 1f);
    }

private void EnableTargetAnimator()
    {
        if (targetAnimator != null)
            targetAnimator.enabled = true;

        enabled = false;
    }

}
