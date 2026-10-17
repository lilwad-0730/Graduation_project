using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>音量滑桿（總音量／BGM／SFX）讓控點可以被拖曳用的共同介面。</summary>
public interface IWorldVolumeSlider
{
    /// <summary>按下控點：記住按的位置跟控點中心的差距，拖曳時控點不會跳。</summary>
    void BeginHandleDrag(Vector2 screenPosition, Camera eventCamera);
    /// <summary>拖曳控點：依滑鼠位置更新音量。</summary>
    void DragHandle(Vector2 screenPosition, Camera eventCamera);
    /// <summary>放開控點：存檔。</summary>
    void EndHandleDrag();
}

/// <summary>
/// 掛在音量滑桿的控點（volumepoint）上：玩家按住控點就能左右拖曳調整音量。
/// 實際的音量計算、存檔都交給滑桿本身（slider 欄位，對應 volumestick 上的 WorldSpace*VolumeSlider）。
/// 跟滑桿一樣支援兩種輸入：EventSystem + Physics2DRaycaster，沒有的話用 OnMouse 系列。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
public sealed class VolumeSliderHandle : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    [Tooltip("這個控點所屬的滑桿（volumestick 上的 WorldSpaceMaster/Bgm/SfxVolumeSlider）。")]
    [SerializeField] private MonoBehaviour slider;
    [SerializeField] private Camera inputCamera;

    private IWorldVolumeSlider Target => slider as IWorldVolumeSlider;

    private void Awake()
    {
        if (inputCamera == null) inputCamera = Camera.main;
        if (Target == null) Debug.LogWarning("[VolumeSliderHandle] 沒有指定滑桿，或指定的元件不是音量滑桿。", this);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        Target?.BeginHandleDrag(eventData.position, eventData.pressEventCamera);
    }

    public void OnDrag(PointerEventData eventData)
    {
        Target?.DragHandle(eventData.position, eventData.pressEventCamera);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        Target?.EndHandleDrag();
    }

    private void OnMouseDown()
    {
        if (NeedsMouseFallback()) Target?.BeginHandleDrag(Input.mousePosition, inputCamera);
    }

    private void OnMouseDrag()
    {
        if (NeedsMouseFallback()) Target?.DragHandle(Input.mousePosition, inputCamera);
    }

    private void OnMouseUp()
    {
        if (NeedsMouseFallback()) Target?.EndHandleDrag();
    }

    private bool NeedsMouseFallback()
    {
        Camera cameraToUse = inputCamera != null ? inputCamera : Camera.main;
        return EventSystem.current == null || cameraToUse == null ||
               cameraToUse.GetComponent<Physics2DRaycaster>() == null;
    }
}
