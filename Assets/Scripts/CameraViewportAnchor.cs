using UnityEngine;

[ExecuteAlways]
[DefaultExecutionOrder(10000)]
[DisallowMultipleComponent]
public sealed class CameraViewportAnchor : MonoBehaviour
{
    [SerializeField] private Camera targetCamera;
    [SerializeField] private Vector2 viewportPosition = new Vector2(0.95f, 0.92f);
    [SerializeField, Min(0.01f)] private float distanceFromCamera = 10f;

    private void OnEnable()
    {
        UpdatePosition();
    }

    private void LateUpdate()
    {
        UpdatePosition();
    }

    private void UpdatePosition()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        if (targetCamera == null)
        {
            return;
        }

        Vector3 anchoredPosition = targetCamera.ViewportToWorldPoint(
            new Vector3(viewportPosition.x, viewportPosition.y, distanceFromCamera));

        transform.position = anchoredPosition;
    }
}
