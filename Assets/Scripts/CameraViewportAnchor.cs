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
        // Keep the serialized reference optional so this component remains reusable in prefabs.
        // Camera.main is used only as a runtime/editor fallback and is not written back to the asset.
        Camera cameraToUse = targetCamera != null ? targetCamera : Camera.main;

        if (cameraToUse == null)
        {
            return;
        }

        Vector3 anchoredPosition = cameraToUse.ViewportToWorldPoint(
            new Vector3(viewportPosition.x, viewportPosition.y, distanceFromCamera));
        bool positionChanged = transform.position != anchoredPosition;

        transform.position = anchoredPosition;

        // When the settings panel opens it pauses the game before the next
        // physics step. Keep 2D colliders aligned with the newly anchored UI.
        if (Application.isPlaying && positionChanged)
        {
            Physics2D.SyncTransforms();
        }
    }
}
