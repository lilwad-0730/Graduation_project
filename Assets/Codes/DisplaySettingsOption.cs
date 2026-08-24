using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public sealed class DisplaySettingsOption : MonoBehaviour
{
    public enum OptionKind
    {
        FullScreen = 0,
        Windowed = 1,
        Resolution = 2
    }

    [SerializeField] private OptionKind kind;
    [SerializeField] private int width;
    [SerializeField] private int height;

    private DisplaySettingsController controller;
    private MenuSpriteHoverEffect hoverEffect;

    public OptionKind Kind => kind;
    public int Width => width;
    public int Height => height;

    private void Awake()
    {
        controller = GetComponentInParent<DisplaySettingsController>();
        hoverEffect = GetComponent<MenuSpriteHoverEffect>();
    }

    private void OnMouseDown()
    {
        if (controller == null)
            controller = GetComponentInParent<DisplaySettingsController>();

        if (controller != null)
            controller.SelectOption(this);
    }

    public void SetSelected(bool selected)
    {
        if (hoverEffect == null)
            hoverEffect = GetComponent<MenuSpriteHoverEffect>();

        if (hoverEffect != null)
            hoverEffect.SetSelected(selected);
    }
}
