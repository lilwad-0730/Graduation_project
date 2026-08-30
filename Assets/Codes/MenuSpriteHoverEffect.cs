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
    
    [Header("音效設置")]
    [SerializeField] private AudioClip rolloverSound;
    [SerializeField, Range(0f, 1f)] private float rolloverVolume = 1f;

    [Header("透明度設置（可選）")]
    [SerializeField] private bool useCustomAlpha = false;
    [SerializeField, Range(0f, 1f)] private float defaultAlpha = 1f;
    [SerializeField, Range(0f, 1f)] private float hoverAlpha = 1f;


    private AudioSource audioSource;
    private SpriteRenderer spriteRenderer;
    private bool isSelected;

private void Awake()
    {
        uiImage = GetComponent<Image>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        audioSource = GetComponent<AudioSource>();

        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }

        audioSource.playOnAwake = false;
        audioSource.loop = false;
        audioSource.spatialBlend = 0f;

        // 自動載入預設圖片
        if (defaultSprite == null)
        {
            if (uiImage != null && uiImage.sprite != null) defaultSprite = uiImage.sprite;
            else if (spriteRenderer != null && spriteRenderer.sprite != null) defaultSprite = spriteRenderer.sprite;
        }

        // 初始化為預設 Sprite，不播放音效
        SetSprite(defaultSprite, false, false); 
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
        SetSprite(hoverSprite, true, true);
    }

public void OnHoverExit()
    {
        // 已選中的分類必須維持選取 Sprite。
        SetSprite(isSelected ? hoverSprite : defaultSprite, false, isSelected);
    }

public void SetSelected(bool selected)
    {
        isSelected = selected;
        SetSprite(isSelected ? hoverSprite : defaultSprite, false, isSelected);
    }


private void SetSprite(Sprite sprite, bool playSound, bool hoverState)
    {
        if (sprite == null)
        {
            return;
        }

        bool spriteChanged = false;

        if (uiImage != null && uiImage.sprite != sprite)
        {
            uiImage.sprite = sprite;
            spriteChanged = true;
        }

        if (spriteRenderer != null && spriteRenderer.sprite != sprite)
        {
            spriteRenderer.sprite = sprite;
            spriteChanged = true;
        }

        ApplyAlpha(hoverState ? hoverAlpha : defaultAlpha);

        if (spriteChanged && playSound && rolloverSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(rolloverSound, rolloverVolume);
        }
    }


private void ApplyAlpha(float alpha)
    {
        if (!useCustomAlpha)
        {
            return;
        }

        if (uiImage != null)
        {
            Color imageColor = uiImage.color;
            imageColor.a = alpha;
            uiImage.color = imageColor;
        }

        if (spriteRenderer != null)
        {
            Color rendererColor = spriteRenderer.color;
            rendererColor.a = alpha;
            spriteRenderer.color = rendererColor;
        }
    }
}
