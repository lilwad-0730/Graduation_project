using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// 控制滑鼠接觸與離開時切換物件的 Sprite (支援 UGUI Image 與 2D SpriteRenderer)
/// </summary>
public class MenuSpriteHoverEffect : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Sprite 設置")]
    public Sprite defaultSprite;   // 未選取/預設 Sprite (如 menu_slection)
    public Sprite hoverSprite;     // 接觸/選取時 Sprite (如 menu_selcted)

    private Image uiImage;
    private SpriteRenderer spriteRenderer;

    private void Awake()
    {
        uiImage = GetComponent<Image>();
        spriteRenderer = GetComponent<SpriteRenderer>();

        // 自動載入預設圖片
        if (defaultSprite == null)
        {
            if (uiImage != null && uiImage.sprite != null) defaultSprite = uiImage.sprite;
            else if (spriteRenderer != null && spriteRenderer.sprite != null) defaultSprite = spriteRenderer.sprite;
        }

        // 初始化為預設 Sprite
        SetSprite(defaultSprite);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        OnHoverEnter();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        OnHoverExit();
    }

    private void OnMouseEnter()
    {
        OnHoverEnter();
    }

    private void OnMouseExit()
    {
        OnHoverExit();
    }

    public void OnHoverEnter()
    {
        if (hoverSprite != null)
        {
            SetSprite(hoverSprite);
        }
    }

    public void OnHoverExit()
    {
        if (defaultSprite != null)
        {
            SetSprite(defaultSprite);
        }
    }

    private void SetSprite(Sprite s)
    {
        if (s == null) return;

        if (uiImage != null)
        {
            uiImage.sprite = s;
        }
        if (spriteRenderer != null)
        {
            spriteRenderer.sprite = s;
        }
    }
}
