using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public sealed class SettingsCategoryTab : MonoBehaviour
{
    [SerializeField] private SettingsCategorySwitcher.Category category;

    private SettingsCategorySwitcher categorySwitcher;
    private MenuSpriteHoverEffect hoverEffect;

    public SettingsCategorySwitcher.Category Category => category;

private void Awake()
    {
        categorySwitcher = GetComponentInParent<SettingsCategorySwitcher>();
        hoverEffect = GetComponent<MenuSpriteHoverEffect>();
    }

public void SetSelected(bool selected)
    {
        if (hoverEffect == null)
            hoverEffect = GetComponent<MenuSpriteHoverEffect>();

        if (hoverEffect != null)
            hoverEffect.SetSelected(selected);
    }


    private void OnMouseDown()
    {
        if (categorySwitcher == null)
            categorySwitcher = GetComponentInParent<SettingsCategorySwitcher>();

        if (categorySwitcher != null)
            categorySwitcher.ShowCategory(category);
    }
}
