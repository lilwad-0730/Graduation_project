using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
public sealed class CameraFocusOnClick : MonoBehaviour
{
    [SerializeField] private Camera targetCamera;
    [SerializeField] private SpriteRenderer focusTarget;
    [SerializeField, Min(1f)] private float padding = 1f;

    private void OnMouseUpAsButton()
    {
        if (targetCamera == null || focusTarget == null)
        {
            return;
        }

        Bounds targetBounds = focusTarget.bounds;
        Vector3 cameraPosition = targetCamera.transform.position;
        cameraPosition.x = targetBounds.center.x;
        cameraPosition.y = targetBounds.center.y;
        targetCamera.transform.position = cameraPosition;

        targetCamera.orthographic = true;
        float safeAspect = Mathf.Max(targetCamera.aspect, 0.0001f);
        float verticalSize = targetBounds.extents.y;
        float horizontalSize = targetBounds.extents.x / safeAspect;
        targetCamera.orthographicSize = Mathf.Max(verticalSize, horizontalSize) * padding;
    }
}