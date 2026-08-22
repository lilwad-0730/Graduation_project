using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(100)]
public sealed class CreditsScrollbarSpriteSync : MonoBehaviour
{
    [SerializeField] private Scrollbar scrollbar;
    [SerializeField] private SpriteRenderer railSprite;
    [SerializeField] private SpriteRenderer thumbSprite;

    private void Reset()
    {
        scrollbar = GetComponent<Scrollbar>();
    }

    private void Awake()
    {
        if (scrollbar == null)
        {
            scrollbar = GetComponent<Scrollbar>();
        }
    }

    private void LateUpdate()
    {
        if (scrollbar == null || railSprite == null || thumbSprite == null)
        {
            return;
        }

        Bounds railBounds = railSprite.bounds;
        float thumbHalfHeight = thumbSprite.bounds.extents.y;
        float bottomY = railBounds.min.y + thumbHalfHeight;
        float topY = railBounds.max.y - thumbHalfHeight;

        if (bottomY > topY)
        {
            bottomY = topY = railBounds.center.y;
        }

        Vector3 position = thumbSprite.transform.position;
        position.y = Mathf.Lerp(bottomY, topY, Mathf.Clamp01(scrollbar.value));
        thumbSprite.transform.position = position;
    }
}
