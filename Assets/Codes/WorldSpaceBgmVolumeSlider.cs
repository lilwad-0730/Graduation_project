using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider2D))]
public sealed class WorldSpaceBgmVolumeSlider : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler, IWorldVolumeSlider
{
    [Header("場景物件")]
    [SerializeField] private BoxCollider2D interactionCollider;
    [SerializeField] private SpriteRenderer trackRenderer;
    [SerializeField] private SpriteRenderer volumePointRenderer;
    [SerializeField] private Camera inputCamera;
    [SerializeField] private TMP_Text volumePercentText;

    [Header("BGM 音量")]
    [SerializeField] private string playerPrefsKey = "BGMVolume";
    [SerializeField, Range(0f, 1f)] private float defaultVolume = 0.75f;

    public float Value { get; private set; }

    private void Awake()
    {
        if (trackRenderer == null)
            trackRenderer = GetComponent<SpriteRenderer>();

        if (interactionCollider == null)
            interactionCollider = GetComponent<BoxCollider2D>();

        if (inputCamera == null)
            inputCamera = Camera.main;

        float savedVolume = PlayerPrefs.GetFloat(playerPrefsKey, defaultVolume);
        ApplyVolume(savedVolume);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        UpdateFromPointer(eventData.position, eventData.pressEventCamera);
    }

    public void OnDrag(PointerEventData eventData)
    {
        UpdateFromPointer(eventData.position, eventData.pressEventCamera);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        PlayerPrefs.SetFloat(playerPrefsKey, Value);
        PlayerPrefs.Save();
    }

        private void OnMouseDown()
    {
        if (NeedsMouseFallback())
            UpdateFromPointer(Input.mousePosition, inputCamera);
    }

    private void OnMouseDrag()
    {
        if (NeedsMouseFallback())
            UpdateFromPointer(Input.mousePosition, inputCamera);
    }

    private void OnMouseUp()
    {
        if (!NeedsMouseFallback())
            return;

        PlayerPrefs.SetFloat(playerPrefsKey, Value);
        PlayerPrefs.Save();
    }

    private bool NeedsMouseFallback()
    {
        Camera cameraToUse = inputCamera != null ? inputCamera : Camera.main;
        return EventSystem.current == null || cameraToUse == null ||
               cameraToUse.GetComponent<Physics2DRaycaster>() == null;
    }

private void UpdateFromPointer(Vector2 screenPosition, Camera eventCamera, float worldOffsetX = 0f)
    {
        if (trackRenderer == null || interactionCollider == null || volumePointRenderer == null)
            return;

        if (!TryGetPointerWorldX(screenPosition, eventCamera, out float pointerX))
            return;

        Bounds movementBounds = interactionCollider.bounds;
        float minimumX = movementBounds.min.x;
        float maximumX = movementBounds.max.x;
        float volume = Mathf.InverseLerp(
            minimumX,
            maximumX,
            Mathf.Clamp(pointerX + worldOffsetX, minimumX, maximumX));

        ApplyVolume(volume);
    }

    private bool TryGetPointerWorldX(Vector2 screenPosition, Camera eventCamera, out float worldX)
    {
        worldX = 0f;
        if (trackRenderer == null)
            return false;

        Camera cameraToUse = eventCamera != null ? eventCamera : inputCamera;
        if (cameraToUse == null)
            cameraToUse = Camera.main;

        if (cameraToUse == null)
            return false;

        Ray pointerRay = cameraToUse.ScreenPointToRay(screenPosition);
        Plane sliderPlane = new Plane(trackRenderer.transform.forward, trackRenderer.bounds.center);

        if (!sliderPlane.Raycast(pointerRay, out float intersectionDistance))
            return false;

        worldX = pointerRay.GetPoint(intersectionDistance).x;
        return true;
    }

    // ★1017 按住控點（volumepoint，見 VolumeSliderHandle）拖曳
    private float handleGrabOffsetX;

    public void BeginHandleDrag(Vector2 screenPosition, Camera eventCamera)
    {
        handleGrabOffsetX = 0f;
        if (volumePointRenderer != null && TryGetPointerWorldX(screenPosition, eventCamera, out float pointerX))
            handleGrabOffsetX = volumePointRenderer.transform.position.x - pointerX;
    }

    public void DragHandle(Vector2 screenPosition, Camera eventCamera)
    {
        UpdateFromPointer(screenPosition, eventCamera, handleGrabOffsetX);
    }

    public void EndHandleDrag()
    {
        PlayerPrefs.SetFloat(playerPrefsKey, Value);
        PlayerPrefs.Save();
    }

    public void ApplyVolume(float volume)
    {
        if (trackRenderer == null || interactionCollider == null || volumePointRenderer == null)
            return;

        Value = Mathf.Clamp01(volume);

        Bounds movementBounds = interactionCollider.bounds;
        Vector3 pointPosition = volumePointRenderer.transform.position;
        pointPosition.x = Mathf.Lerp(movementBounds.min.x, movementBounds.max.x, Value);
        pointPosition.y = trackRenderer.bounds.center.y;
        volumePointRenderer.transform.position = pointPosition;

        if (volumePercentText != null)
            volumePercentText.text = Mathf.RoundToInt(Value * 100f).ToString();

        AudioManager.SetBgmVolume(Value);
    }
}
