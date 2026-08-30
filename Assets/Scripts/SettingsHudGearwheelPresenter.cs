using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
public sealed class SettingsHudGearwheelPresenter : MonoBehaviour
{
    private const int MinimumHudSortingOrder = 1000;

    [Header("HUD Placement")]
    [SerializeField] private Vector2 viewportAnchor = new Vector2(0.95f, 0.92f);
    [SerializeField] private Vector2 referenceResolution = new Vector2(2560f, 1440f);
    [SerializeField] private Vector2 buttonSize = new Vector2(180f, 174f);

    [Header("HUD Rendering")]
    [SerializeField, Min(MinimumHudSortingOrder)] private int hudSortingOrder = 2000;

    private SpriteRenderer sourceRenderer;
    private BoxCollider2D sourceCollider;
    private CameraViewportAnchor viewportAnchorComponent;
    private SettingsPopupButton popupButton;
    private MenuSpriteHoverEffect hoverEffect;

    private GameObject hudCanvasObject;
    private GameObject ownedEventSystemObject;
    private Image hudImage;

    public bool IsHudReady =>
        hudCanvasObject != null &&
        hudImage != null &&
        hudCanvasObject.activeInHierarchy;

    private void Awake()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        sourceRenderer = GetComponent<SpriteRenderer>();
        sourceCollider = GetComponent<BoxCollider2D>();
        viewportAnchorComponent = GetComponent<CameraViewportAnchor>();
        popupButton = GetComponent<SettingsPopupButton>();
        hoverEffect = GetComponent<MenuSpriteHoverEffect>();

        if (sourceRenderer == null || popupButton == null)
        {
            Debug.LogError(
                "[SettingsHudGearwheelPresenter] gearwheel requires SpriteRenderer and SettingsPopupButton.",
                this);
            return;
        }

        if (EventSystem.current == null)
        {
            ownedEventSystemObject = new GameObject(
                "SettingsHUD_EventSystem",
                typeof(EventSystem),
                typeof(InputSystemUIInputModule));
        }

        hudCanvasObject = new GameObject(
            "GearwheelHUDCanvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));

        Canvas canvas = hudCanvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = Mathf.Max(MinimumHudSortingOrder, hudSortingOrder);

        CanvasScaler scaler = hudCanvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = referenceResolution;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        GameObject buttonObject = new GameObject(
            "GearwheelButton",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Button),
            typeof(SettingsHudGearwheelHoverProxy));
        buttonObject.transform.SetParent(hudCanvasObject.transform, false);

        RectTransform rectTransform = buttonObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = viewportAnchor;
        rectTransform.anchorMax = viewportAnchor;
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = Vector2.zero;


        Canvas buttonCanvas = buttonObject.AddComponent<Canvas>();
        buttonCanvas.overrideSorting = true;
        buttonCanvas.sortingOrder = canvas.sortingOrder + 1;
        buttonObject.AddComponent<GraphicRaycaster>();
        rectTransform.sizeDelta = buttonSize;

        hudImage = buttonObject.GetComponent<Image>();
        hudImage.sprite = sourceRenderer.sprite;
        hudImage.color = sourceRenderer.color;
        hudImage.preserveAspect = true;
        hudImage.raycastTarget = true;

        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = hudImage;
        button.transition = Selectable.Transition.None;
        Navigation navigation = button.navigation;
        navigation.mode = Navigation.Mode.None;
        button.navigation = navigation;
        button.onClick.AddListener(() => popupButton.SetPopupVisible(true));

        buttonObject
            .GetComponent<SettingsHudGearwheelHoverProxy>()
            .Initialize(hoverEffect, sourceRenderer, hudImage);

        sourceRenderer.enabled = false;

        if (sourceCollider != null)
        {
            sourceCollider.enabled = false;
        }

        if (viewportAnchorComponent != null)
        {
            viewportAnchorComponent.enabled = false;
        }
    }

    private void OnEnable()
    {
        if (hudCanvasObject != null)
        {
            hudCanvasObject.SetActive(true);
        }
    }

    private void OnDisable()
    {
        if (hudCanvasObject != null)
        {
            hudCanvasObject.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        if (hudCanvasObject != null)
        {
            Destroy(hudCanvasObject);
        }

        if (ownedEventSystemObject != null)
        {
            Destroy(ownedEventSystemObject);
        }
    }

    private void OnValidate()
    {
        viewportAnchor.x = Mathf.Clamp01(viewportAnchor.x);
        viewportAnchor.y = Mathf.Clamp01(viewportAnchor.y);
        referenceResolution.x = Mathf.Max(1f, referenceResolution.x);
        referenceResolution.y = Mathf.Max(1f, referenceResolution.y);
        buttonSize.x = Mathf.Max(1f, buttonSize.x);
        buttonSize.y = Mathf.Max(1f, buttonSize.y);
        hudSortingOrder = Mathf.Max(MinimumHudSortingOrder, hudSortingOrder);
    }


    private void Start()
    {
        if (hoverEffect != null)
        {
            hoverEffect.OnHoverExit();
        }

        if (hudImage != null && sourceRenderer != null)
        {
            hudImage.sprite = sourceRenderer.sprite;
            hudImage.color = sourceRenderer.color;
        }
    }
}

public sealed class SettingsHudGearwheelHoverProxy :
    MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler
{
    private MenuSpriteHoverEffect hoverEffect;
    private SpriteRenderer sourceRenderer;
    private Image hudImage;

    public void Initialize(
        MenuSpriteHoverEffect sourceHoverEffect,
        SpriteRenderer renderer,
        Image image)
    {
        hoverEffect = sourceHoverEffect;
        sourceRenderer = renderer;
        hudImage = image;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (hoverEffect != null)
        {
            hoverEffect.OnHoverEnter();
        }

        if (hudImage != null && sourceRenderer != null)
        {
            hudImage.sprite = sourceRenderer.sprite;
            hudImage.color = sourceRenderer.color;
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (hoverEffect != null)
        {
            hoverEffect.OnHoverExit();
        }

        if (hudImage != null && sourceRenderer != null)
        {
            hudImage.sprite = sourceRenderer.sprite;
            hudImage.color = sourceRenderer.color;
        }
    }
}
