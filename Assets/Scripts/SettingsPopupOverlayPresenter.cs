using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Presents the world-space settings artwork in a dedicated Screen Space Overlay
/// canvas while leaving the authored settings hierarchy and its input components
/// untouched.  The original SpriteRenderers and world-space canvases remain the
/// source of truth for hover, collider, slider, and back-button behaviour.
/// </summary>
[DefaultExecutionOrder(10001)]
[DisallowMultipleComponent]
public sealed class SettingsPopupOverlayPresenter : MonoBehaviour
{
    private const int MinimumOverlaySortingOrder = 2002;
    private const int MaximumOverlaySortingOrder = 32767;
    private const float Epsilon = 0.0001f;

    [Header("Overlay Rendering")]
    [SerializeField, Min(MinimumOverlaySortingOrder)]
    private int minimumOverlaySortingOrder = MinimumOverlaySortingOrder;

    [Header("Viewport Sizing")]
    [SerializeField]
    private Vector2 targetViewportSize = new Vector2(0.75f, 0.75f);

    private readonly List<SpriteVisual> spriteVisuals = new List<SpriteVisual>();
    private readonly List<TextVisual> textVisuals = new List<TextVisual>();

    private GameObject overlayCanvasObject;
    private Canvas overlayCanvas;
    private RectTransform overlayRect;
    private SpriteRenderer[] sourceSprites;
    private TMP_Text[] sourceTexts;
    private Camera targetCamera;
    private Vector3 originalLocalScale;
    private bool originalLocalScaleCaptured;

    /// <summary>Whether the runtime overlay and all cached visual entries exist.</summary>
    public bool IsOverlayReady =>
        overlayCanvas != null &&
        overlayCanvasObject != null &&
        overlayCanvasObject.activeInHierarchy &&
        sourceSprites != null &&
        sourceTexts != null;

    /// <summary>The final Canvas sorting order used by the settings overlay.</summary>
    public int OverlaySortingOrder => overlayCanvas != null ? overlayCanvas.sortingOrder : -1;

    /// <summary>Number of SpriteRenderer surrogates currently presented.</summary>
    public int SpriteSurrogateCount => spriteVisuals.Count;

    /// <summary>Number of TMP text surrogates currently presented.</summary>
    public int TextSurrogateCount => textVisuals.Count;

    private void Awake()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        targetCamera = Camera.main;
    }

    private void OnEnable()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        CaptureOriginalScale();
        EnsureOverlay();
        CacheSources();
        ApplyViewportRelativeScale();
        SyncVisuals();
    }

    private void LateUpdate()
    {
        if (!Application.isPlaying || !gameObject.activeInHierarchy)
        {
            return;
        }

        EnsureOverlay();

        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        ApplyViewportRelativeScale();
        SyncVisuals();
    }

    private void OnDisable()
    {
        RestoreSourceRendering();
        RestoreOriginalScale();

        if (overlayCanvasObject != null)
        {
            overlayCanvasObject.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        RestoreSourceRendering();
        RestoreOriginalScale();
        DestroyVisuals();

        if (overlayCanvasObject != null)
        {
            Destroy(overlayCanvasObject);
            overlayCanvasObject = null;
            overlayCanvas = null;
            overlayRect = null;
        }
    }

    private void EnsureOverlay()
    {
        if (overlayCanvasObject == null)
        {
            overlayCanvasObject = new GameObject(
                "SettingsPopupOverlayCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler));

            // Keep the overlay independent from SETTING's world-space position,
            // scale, and CameraViewportAnchor component.
            overlayCanvasObject.transform.SetParent(null, false);

            overlayCanvas = overlayCanvasObject.GetComponent<Canvas>();
            overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            overlayCanvas.overrideSorting = true;

            CanvasScaler scaler = overlayCanvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;

            overlayRect = overlayCanvasObject.GetComponent<RectTransform>();
        }

        if (!overlayCanvasObject.activeSelf)
        {
            overlayCanvasObject.SetActive(true);
        }

        overlayCanvas.sortingOrder = ResolveOverlaySortingOrder();
    }

    private int ResolveOverlaySortingOrder()
    {
        int requestedMinimum = Mathf.Max(MinimumOverlaySortingOrder, minimumOverlaySortingOrder);
        int highestOrder = requestedMinimum - 1;

        Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas canvas = canvases[i];
            if (canvas == null || canvas == overlayCanvas || !canvas.enabled || !canvas.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                highestOrder = Mathf.Max(highestOrder, canvas.sortingOrder);
            }
        }

        return Mathf.Clamp(highestOrder + 1, requestedMinimum, MaximumOverlaySortingOrder);
    }

    private void CacheSources()
    {
        SpriteRenderer[] latestSprites = GetComponentsInChildren<SpriteRenderer>(true);
        TMP_Text[] latestTexts = GetComponentsInChildren<TMP_Text>(true);

        if (!SameSources(sourceSprites, latestSprites) || !SameSources(sourceTexts, latestTexts))
        {
            sourceSprites = latestSprites;
            sourceTexts = latestTexts;
            RebuildVisuals();
        }
    }

    private static bool SameSources<T>(T[] current, T[] latest) where T : UnityEngine.Object
    {
        if (current == null || latest == null || current.Length != latest.Length)
        {
            return false;
        }

        for (int i = 0; i < current.Length; i++)
        {
            if (current[i] != latest[i])
            {
                return false;
            }
        }

        return true;
    }

    private void RebuildVisuals()
    {
        DestroyVisuals();

        if (overlayRect == null)
        {
            return;
        }

        if (sourceSprites != null)
        {
            for (int i = 0; i < sourceSprites.Length; i++)
            {
                SpriteRenderer source = sourceSprites[i];
                if (source == null)
                {
                    continue;
                }

                GameObject surrogateObject = new GameObject(
                    source.gameObject.name + " (Overlay)",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image));
                surrogateObject.transform.SetParent(overlayRect, false);

                Image image = surrogateObject.GetComponent<Image>();
                image.raycastTarget = false;
                image.maskable = false;
                image.type = Image.Type.Simple;

                spriteVisuals.Add(new SpriteVisual(source, image));
            }
        }

        if (sourceTexts != null)
        {
            for (int i = 0; i < sourceTexts.Length; i++)
            {
                TMP_Text source = sourceTexts[i];
                if (source == null)
                {
                    continue;
                }

                GameObject surrogateObject = Instantiate(source.gameObject, overlayRect, false);
                surrogateObject.name = source.gameObject.name + " (Overlay)";

                TMP_Text text = surrogateObject.GetComponent<TMP_Text>();
                if (text == null)
                {
                    Destroy(surrogateObject);
                    continue;
                }

                text.raycastTarget = false;
                text.maskable = false;
                if (text.canvasRenderer != null)
                {
                    text.canvasRenderer.cull = false;
                }
                text.text = source.text;

                textVisuals.Add(new TextVisual(source, text));
            }
        }

        SortSurrogates();
    }

    private void SortSurrogates()
    {
        spriteVisuals.Sort(CompareSprites);
        textVisuals.Sort(CompareTexts);

        for (int i = 0; i < spriteVisuals.Count; i++)
        {
            if (spriteVisuals[i].image != null)
            {
                spriteVisuals[i].image.transform.SetSiblingIndex(i);
            }
        }

        for (int i = 0; i < textVisuals.Count; i++)
        {
            if (textVisuals[i].text != null)
            {
                textVisuals[i].text.transform.SetSiblingIndex(spriteVisuals.Count + i);
            }
        }
    }

    private static int CompareSprites(SpriteVisual left, SpriteVisual right)
    {
        if (left.source == null)
        {
            return right.source == null ? 0 : -1;
        }

        if (right.source == null)
        {
            return 1;
        }

        int layerCompare = left.source.sortingLayerID.CompareTo(right.source.sortingLayerID);
        if (layerCompare != 0)
        {
            return layerCompare;
        }

        int orderCompare = left.source.sortingOrder.CompareTo(right.source.sortingOrder);
        if (orderCompare != 0)
        {
            return orderCompare;
        }

        return CompareHierarchy(left.source.transform, right.source.transform);
    }

    private static int CompareTexts(TextVisual left, TextVisual right)
    {
        if (left.source == null)
        {
            return right.source == null ? 0 : -1;
        }

        if (right.source == null)
        {
            return 1;
        }

        Canvas leftCanvas = left.source.GetComponentInParent<Canvas>();
        Canvas rightCanvas = right.source.GetComponentInParent<Canvas>();
        int leftOrder = leftCanvas != null ? leftCanvas.sortingOrder : 0;
        int rightOrder = rightCanvas != null ? rightCanvas.sortingOrder : 0;
        int orderCompare = leftOrder.CompareTo(rightOrder);
        if (orderCompare != 0)
        {
            return orderCompare;
        }

        return CompareHierarchy(left.source.transform, right.source.transform);
    }

    private static int CompareHierarchy(Transform left, Transform right)
    {
        if (left == right)
        {
            return 0;
        }

        List<int> leftPath = BuildSiblingPath(left);
        List<int> rightPath = BuildSiblingPath(right);
        int count = Mathf.Min(leftPath.Count, rightPath.Count);

        for (int i = 0; i < count; i++)
        {
            int comparison = leftPath[i].CompareTo(rightPath[i]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return leftPath.Count.CompareTo(rightPath.Count);
    }

    private static List<int> BuildSiblingPath(Transform value)
    {
        List<int> path = new List<int>();
        Transform current = value;

        while (current != null)
        {
            path.Insert(0, current.GetSiblingIndex());
            current = current.parent;
        }

        return path;
    }

    private void CaptureOriginalScale()
    {
        originalLocalScale = transform.localScale;
        originalLocalScaleCaptured = true;
    }

    private void RestoreOriginalScale()
    {
        if (!originalLocalScaleCaptured)
        {
            return;
        }

        if (transform.localScale != originalLocalScale)
        {
            transform.localScale = originalLocalScale;
            if (Application.isPlaying)
            {
                Physics2D.SyncTransforms();
            }
        }

        originalLocalScaleCaptured = false;
    }

    private void ApplyViewportRelativeScale()
    {
        if (!originalLocalScaleCaptured || targetCamera == null)
        {
            return;
        }

        if (sourceSprites == null)
        {
            CacheSources();
        }

        if (!TryGetProjectedSourceSizeAtOriginalScale(out Vector2 projectedSize))
        {
            return;
        }

        Rect pixelRect = targetCamera.pixelRect;
        float viewportWidth = pixelRect.width;
        float viewportHeight = pixelRect.height;
        if (viewportWidth < Epsilon || viewportHeight < Epsilon)
        {
            viewportWidth = Screen.width;
            viewportHeight = Screen.height;
        }

        float targetWidthFraction = Mathf.Clamp(targetViewportSize.x, 0.01f, 1f);
        float targetHeightFraction = Mathf.Clamp(targetViewportSize.y, 0.01f, 1f);
        float targetWidth = viewportWidth * targetWidthFraction;
        float targetHeight = viewportHeight * targetHeightFraction;
        if (targetWidth < Epsilon || targetHeight < Epsilon ||
            projectedSize.x < Epsilon || projectedSize.y < Epsilon)
        {
            return;
        }

        float uniformScale = Mathf.Min(
            targetWidth / projectedSize.x,
            targetHeight / projectedSize.y);
        if (float.IsNaN(uniformScale) || float.IsInfinity(uniformScale) || uniformScale <= 0f)
        {
            return;
        }

        uniformScale = Mathf.Clamp(uniformScale, 0.0001f, 1000f);
        Vector3 desiredScale = originalLocalScale * uniformScale;
        if (!ApproximatelyEqual(transform.localScale, desiredScale))
        {
            transform.localScale = desiredScale;
            if (Application.isPlaying)
            {
                Physics2D.SyncTransforms();
            }
        }
    }

    private bool TryGetProjectedSourceSizeAtOriginalScale(out Vector2 projectedSize)
    {
        projectedSize = Vector2.zero;
        if (targetCamera == null || sourceSprites == null || sourceSprites.Length == 0)
        {
            return false;
        }

        // Measure from the authored scale, then restore the last applied scale before
        // returning. This makes perspective-camera sizing deterministic and avoids
        // feedback oscillation from measuring an already-scaled popup.
        Vector3 previousScale = transform.localScale;
        bool normalizedScale = !ApproximatelyEqual(previousScale, originalLocalScale);
        if (normalizedScale)
        {
            transform.localScale = originalLocalScale;
        }

        bool hasProjectedBounds = false;
        float minX = 0f;
        float maxX = 0f;
        float minY = 0f;
        float maxY = 0f;

        for (int i = 0; i < sourceSprites.Length; i++)
        {
            SpriteRenderer source = sourceSprites[i];
            if (source == null || !source.enabled || source.sprite == null ||
                !source.gameObject.activeInHierarchy)
            {
                continue;
            }

            Rect projected = ProjectedBoundsRect(source.bounds, targetCamera);
            if (!hasProjectedBounds)
            {
                minX = projected.xMin;
                maxX = projected.xMax;
                minY = projected.yMin;
                maxY = projected.yMax;
                hasProjectedBounds = true;
            }
            else
            {
                minX = Mathf.Min(minX, projected.xMin);
                maxX = Mathf.Max(maxX, projected.xMax);
                minY = Mathf.Min(minY, projected.yMin);
                maxY = Mathf.Max(maxY, projected.yMax);
            }
        }

        if (normalizedScale)
        {
            transform.localScale = previousScale;
        }

        if (!hasProjectedBounds)
        {
            return false;
        }

        projectedSize = new Vector2(maxX - minX, maxY - minY);
        return projectedSize.x >= Epsilon && projectedSize.y >= Epsilon;
    }

    private static bool ApproximatelyEqual(Vector3 left, Vector3 right)
    {
        return Mathf.Abs(left.x - right.x) < Epsilon &&
               Mathf.Abs(left.y - right.y) < Epsilon &&
               Mathf.Abs(left.z - right.z) < Epsilon;
    }

    private void SyncVisuals()
    {
        if (overlayCanvas == null || overlayRect == null)
        {
            return;
        }

        CacheSources();
        HideSourceRendering();

        for (int i = 0; i < spriteVisuals.Count; i++)
        {
            SyncSprite(spriteVisuals[i]);
        }

        for (int i = 0; i < textVisuals.Count; i++)
        {
            SyncText(textVisuals[i]);
        }
    }

    private void SyncSprite(SpriteVisual visual)
    {
        if (visual.source == null || visual.image == null || targetCamera == null)
        {
            if (visual.image != null)
            {
                visual.image.gameObject.SetActive(false);
            }

            return;
        }

        bool active = visual.source.gameObject.activeInHierarchy &&
                      visual.source.enabled &&
                      visual.source.sprite != null;
        visual.image.gameObject.SetActive(active);

        if (!active)
        {
            return;
        }

        visual.image.sprite = visual.source.sprite;
        visual.image.color = visual.source.color;
        visual.image.enabled = true;

        Bounds bounds = visual.source.bounds;
        Vector2 projectedSize = ProjectedBoundsSize(bounds, targetCamera);
        Vector2 localSize = visual.source.drawMode == SpriteDrawMode.Simple
            ? visual.source.sprite.bounds.size
            : visual.source.size;

        if (localSize.x < Epsilon || localSize.y < Epsilon || projectedSize.x < Epsilon || projectedSize.y < Epsilon)
        {
            visual.image.gameObject.SetActive(false);
            return;
        }

        RectTransform rect = visual.image.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = WorldToOverlayPoint(bounds.center);
        rect.sizeDelta = localSize;
        rect.localScale = new Vector3(
            projectedSize.x / localSize.x * (visual.source.flipX ? -1f : 1f),
            projectedSize.y / localSize.y * (visual.source.flipY ? -1f : 1f),
            1f);
        rect.localRotation = Quaternion.identity;
    }

    private void SyncText(TextVisual visual)
    {
        if (visual.source == null || visual.text == null || targetCamera == null)
        {
            if (visual.text != null)
            {
                visual.text.gameObject.SetActive(false);
            }

            return;
        }

        bool active = visual.source.gameObject.activeInHierarchy && visual.source.enabled;
        visual.text.gameObject.SetActive(active);

        if (!active)
        {
            return;
        }

        // Text content and colour are allowed to change at runtime (for example,
        // the volume percentage), while all authored TMP formatting stays on the
        // cloned component.
        if (visual.text.text != visual.source.text)
        {
            visual.text.text = visual.source.text;
        }

        if (visual.text.canvasRenderer != null)
        {
            visual.text.canvasRenderer.cull = false;
        }

        visual.text.color = visual.source.color;
        visual.text.enabled = visual.source.enabled;

        RectTransform sourceRect = visual.source.rectTransform;
        RectTransform targetRect = visual.text.rectTransform;
        Vector2 localSize = sourceRect.rect.size;
        Vector2 projectedSize = ProjectedRectSize(sourceRect, targetCamera);

        if (localSize.x < Epsilon || localSize.y < Epsilon || projectedSize.x < Epsilon || projectedSize.y < Epsilon)
        {
            visual.text.gameObject.SetActive(false);
            return;
        }

        targetRect.anchorMin = new Vector2(0.5f, 0.5f);
        targetRect.anchorMax = new Vector2(0.5f, 0.5f);
        targetRect.pivot = sourceRect.pivot;
        targetRect.anchoredPosition = WorldToOverlayPoint(sourceRect.position);
        targetRect.sizeDelta = localSize;
        targetRect.localScale = new Vector3(
            projectedSize.x / localSize.x,
            projectedSize.y / localSize.y,
            1f);
        targetRect.localRotation = Quaternion.identity;
    }

    private Vector2 WorldToOverlayPoint(Vector3 worldPoint)
    {
        Vector2 screenPoint = targetCamera.WorldToScreenPoint(worldPoint);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            overlayRect,
            screenPoint,
            null,
            out Vector2 localPoint);
        return localPoint;
    }

    private static Rect ProjectedBoundsRect(Bounds bounds, Camera camera)
    {
        Vector3[] corners = new Vector3[8];
        Vector3 min = bounds.min;
        Vector3 max = bounds.max;
        corners[0] = new Vector3(min.x, min.y, min.z);
        corners[1] = new Vector3(min.x, min.y, max.z);
        corners[2] = new Vector3(min.x, max.y, min.z);
        corners[3] = new Vector3(min.x, max.y, max.z);
        corners[4] = new Vector3(max.x, min.y, min.z);
        corners[5] = new Vector3(max.x, min.y, max.z);
        corners[6] = new Vector3(max.x, max.y, min.z);
        corners[7] = new Vector3(max.x, max.y, max.z);

        Vector3 first = camera.WorldToScreenPoint(corners[0]);
        float minX = first.x;
        float maxX = first.x;
        float minY = first.y;
        float maxY = first.y;

        for (int i = 1; i < corners.Length; i++)
        {
            Vector3 point = camera.WorldToScreenPoint(corners[i]);
            minX = Mathf.Min(minX, point.x);
            maxX = Mathf.Max(maxX, point.x);
            minY = Mathf.Min(minY, point.y);
            maxY = Mathf.Max(maxY, point.y);
        }

        return Rect.MinMaxRect(minX, minY, maxX, maxY);
    }

    private static Vector2 ProjectedBoundsSize(Bounds bounds, Camera camera)
    {
        Vector3[] corners = new Vector3[8];
        Vector3 min = bounds.min;
        Vector3 max = bounds.max;
        corners[0] = new Vector3(min.x, min.y, min.z);
        corners[1] = new Vector3(min.x, min.y, max.z);
        corners[2] = new Vector3(min.x, max.y, min.z);
        corners[3] = new Vector3(min.x, max.y, max.z);
        corners[4] = new Vector3(max.x, min.y, min.z);
        corners[5] = new Vector3(max.x, min.y, max.z);
        corners[6] = new Vector3(max.x, max.y, min.z);
        corners[7] = new Vector3(max.x, max.y, max.z);

        Vector3 first = camera.WorldToScreenPoint(corners[0]);
        float minX = first.x;
        float maxX = first.x;
        float minY = first.y;
        float maxY = first.y;

        for (int i = 1; i < corners.Length; i++)
        {
            Vector3 point = camera.WorldToScreenPoint(corners[i]);
            minX = Mathf.Min(minX, point.x);
            maxX = Mathf.Max(maxX, point.x);
            minY = Mathf.Min(minY, point.y);
            maxY = Mathf.Max(maxY, point.y);
        }

        return new Vector2(maxX - minX, maxY - minY);
    }

    private static Vector2 ProjectedRectSize(RectTransform rect, Camera camera)
    {
        Vector3[] corners = new Vector3[4];
        rect.GetWorldCorners(corners);

        Vector3 first = camera.WorldToScreenPoint(corners[0]);
        float minX = first.x;
        float maxX = first.x;
        float minY = first.y;
        float maxY = first.y;

        for (int i = 1; i < corners.Length; i++)
        {
            Vector3 point = camera.WorldToScreenPoint(corners[i]);
            minX = Mathf.Min(minX, point.x);
            maxX = Mathf.Max(maxX, point.x);
            minY = Mathf.Min(minY, point.y);
            maxY = Mathf.Max(maxY, point.y);
        }

        return new Vector2(maxX - minX, maxY - minY);
    }

    private void DestroyVisuals()
    {
        RestoreSourceRendering();

        for (int i = 0; i < spriteVisuals.Count; i++)
        {
            if (spriteVisuals[i].image != null)
            {
                Destroy(spriteVisuals[i].image.gameObject);
            }
        }

        for (int i = 0; i < textVisuals.Count; i++)
        {
            if (textVisuals[i].text != null)
            {
                Destroy(textVisuals[i].text.gameObject);
            }
        }

        spriteVisuals.Clear();
        textVisuals.Clear();
    }

    private void HideSourceRendering()
    {
        for (int i = 0; i < spriteVisuals.Count; i++)
        {
            SpriteRenderer source = spriteVisuals[i].source;
            if (source != null)
            {
                source.forceRenderingOff = true;
            }
        }

        for (int i = 0; i < textVisuals.Count; i++)
        {
            TMP_Text source = textVisuals[i].source;
            if (source != null && source.canvasRenderer != null)
            {
                source.canvasRenderer.cull = true;
            }
        }
    }

    private void RestoreSourceRendering()
    {
        for (int i = 0; i < spriteVisuals.Count; i++)
        {
            SpriteVisual visual = spriteVisuals[i];
            if (visual.source != null)
            {
                visual.source.forceRenderingOff = visual.originalForceRenderingOff;
            }
        }

        for (int i = 0; i < textVisuals.Count; i++)
        {
            TextVisual visual = textVisuals[i];
            if (visual.source != null && visual.source.canvasRenderer != null)
            {
                visual.source.canvasRenderer.cull = visual.originalCanvasRendererCull;
            }
        }
    }

    private void OnValidate()
    {
        minimumOverlaySortingOrder = Mathf.Max(
            MinimumOverlaySortingOrder,
            minimumOverlaySortingOrder);
        targetViewportSize.x = Mathf.Clamp(targetViewportSize.x, 0.01f, 1f);
        targetViewportSize.y = Mathf.Clamp(targetViewportSize.y, 0.01f, 1f);
    }

    private sealed class SpriteVisual
    {
        public readonly SpriteRenderer source;
        public readonly Image image;
        public readonly bool originalForceRenderingOff;

        public SpriteVisual(SpriteRenderer source, Image image)
        {
            this.source = source;
            this.image = image;
            originalForceRenderingOff = source != null && source.forceRenderingOff;
        }
    }

    private sealed class TextVisual
    {
        public readonly TMP_Text source;
        public readonly TMP_Text text;
        public readonly bool originalCanvasRendererCull;

        public TextVisual(TMP_Text source, TMP_Text text)
        {
            this.source = source;
            this.text = text;
            originalCanvasRendererCull = source != null && source.canvasRenderer != null && source.canvasRenderer.cull;
        }
    }
}
