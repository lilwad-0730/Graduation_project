using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;


[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
public sealed class WorldSpaceMasterVolumeSlider : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    [Header("場景物件")]
    [SerializeField] private SpriteRenderer trackRenderer;
    [SerializeField] private SpriteRenderer volumePointRenderer;
    [SerializeField] private Camera inputCamera;
    [SerializeField] private TMP_Text volumePercentText;


    [Header("總音量")]
    [SerializeField] private string playerPrefsKey = "MasterVolume";
    [SerializeField, Range(0f, 1f)] private float defaultVolume = 1f;

    public float Value { get; private set; }

    private void Awake()
    {
        if (trackRenderer == null)
        {
            trackRenderer = GetComponent<SpriteRenderer>();
        }

        if (inputCamera == null)
        {
            inputCamera = Camera.main;
        }

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

    private void UpdateFromPointer(Vector2 screenPosition, Camera eventCamera)
    {
        if (trackRenderer == null || volumePointRenderer == null)
        {
            return;
        }

        Camera cameraToUse = eventCamera != null ? eventCamera : inputCamera;
        if (cameraToUse == null)
        {
            cameraToUse = Camera.main;
        }

        if (cameraToUse == null)
        {
            return;
        }

        Ray pointerRay = cameraToUse.ScreenPointToRay(screenPosition);
        Plane sliderPlane = new Plane(trackRenderer.transform.forward, trackRenderer.bounds.center);

        if (!sliderPlane.Raycast(pointerRay, out float intersectionDistance))
        {
            return;
        }

        Vector3 worldPosition = pointerRay.GetPoint(intersectionDistance);
        Bounds trackBounds = trackRenderer.bounds;
        float pointHalfWidth = Mathf.Min(volumePointRenderer.bounds.extents.x, trackBounds.extents.x);
        float minimumX = trackBounds.min.x + pointHalfWidth;
        float maximumX = trackBounds.max.x - pointHalfWidth;
        float volume = Mathf.InverseLerp(minimumX, maximumX, Mathf.Clamp(worldPosition.x, minimumX, maximumX));

        ApplyVolume(volume);
    }

    private void ApplyVolume(float volume)
    {
        if (trackRenderer == null || volumePointRenderer == null)
        {
            return;
        }

        Value = Mathf.Clamp01(volume);

        Bounds trackBounds = trackRenderer.bounds;
        float pointHalfWidth = Mathf.Min(volumePointRenderer.bounds.extents.x, trackBounds.extents.x);
        float minimumX = trackBounds.min.x + pointHalfWidth;
        float maximumX = trackBounds.max.x - pointHalfWidth;

        Vector3 pointPosition = volumePointRenderer.transform.position;
        pointPosition.x = Mathf.Lerp(minimumX, maximumX, Value);
        pointPosition.y = trackBounds.center.y;
        volumePointRenderer.transform.position = pointPosition;

        AudioListener.volume = Value;

        if (volumePercentText != null)
        {
            volumePercentText.text = Mathf.RoundToInt(Value * 100f).ToString();
        }
    }
}