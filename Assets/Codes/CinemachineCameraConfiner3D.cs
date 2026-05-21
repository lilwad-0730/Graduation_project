using UnityEngine;
using Unity.Cinemachine;

[ExecuteAlways]
[AddComponentMenu("Cinemachine/User/Cinemachine Camera Confiner 3D")]
public class CinemachineCameraConfiner3D : CinemachineExtension
{
    [Header("邊界設定")]
    [Tooltip("用於限制攝影機的邊界 Collider (建議使用 BoxCollider，並勾選 Is Trigger)")]
    public Collider boundaryCollider;

    [Tooltip("是否自動尋找名字為 Background 或帶有 Background 標籤的物件作為邊界")]
    public bool autoFindBackground = true;

    protected override void Awake()
    {
        base.Awake();
        if (boundaryCollider == null && autoFindBackground)
        {
            FindBoundaryCollider();
        }
    }

    private void FindBoundaryCollider()
    {
        GameObject bg = GameObject.FindWithTag("Background");
        if (bg == null) bg = GameObject.Find("Background");
        if (bg == null) bg = GameObject.Find("BG");

        if (bg != null)
        {
            boundaryCollider = bg.GetComponent<Collider>();
            if (boundaryCollider != null)
            {
                Debug.Log($"[CinemachineCameraConfiner3D] 自動綁定背景邊界：{bg.name}");
            }
        }
    }

    protected override void PostPipelineStageCallback(
        CinemachineVirtualCameraBase vcam,
        CinemachineCore.Stage stage, ref CameraState state, float deltaTime)
    {
        // 在 Body 階段之後、相機定位完成時，進行位置邊界限制修正，這是最平滑、最符合 Cinemachine 管線的時機
        if (stage == CinemachineCore.Stage.Body)
        {
            if (boundaryCollider == null)
            {
                if (autoFindBackground) FindBoundaryCollider();
                if (boundaryCollider == null) return;
            }

            // 獲取主相機以取得 Aspect Ratio 螢幕寬高比
            Camera mainCam = Camera.main;
            if (mainCam == null) return;

            float aspect = mainCam.aspect;
            float halfHeight = 0f;
            float halfWidth = 0f;

            // 從目前的 Lens 狀態動態讀取，以完美相容「階梯攝影機動態縮放」的效果！
            // 當鏡頭動態拉遠時，邊界限制會自動內縮，確保任何縮放大小下，四邊都不會看穿背景！
            bool isOrthographic = state.Lens.Orthographic;
            float currentSize = isOrthographic ? state.Lens.OrthographicSize : state.Lens.FieldOfView;

            if (isOrthographic)
            {
                halfHeight = currentSize;
                halfWidth = halfHeight * aspect;
            }
            else
            {
                // 透視投影：根據相機到背景中心的 Z 軸距離計算實際的視野高寬
                float distance = Mathf.Abs(state.RawPosition.z - boundaryCollider.bounds.center.z);
                halfHeight = distance * Mathf.Tan(currentSize * 0.5f * Mathf.Deg2Rad);
                halfWidth = halfHeight * aspect;
            }

            Bounds bgBounds = boundaryCollider.bounds;

            // 計算相機中心點的容許範圍，確保相機四邊不會超出背景 Collider
            float minX = bgBounds.min.x + halfWidth;
            float maxX = bgBounds.max.x - halfWidth;
            float minY = bgBounds.min.y + halfHeight;
            float maxY = bgBounds.max.y - halfHeight;

            // 如果背景寬度或高度小於攝影機目前的視野，則將相機鎖定在背景中心點
            if (minX > maxX)
            {
                minX = maxX = bgBounds.center.x;
            }
            if (minY > maxY)
            {
                minY = maxY = bgBounds.center.y;
            }

            // 限制虛擬相機的原始位置
            Vector3 clampedPos = state.RawPosition;
            clampedPos.x = Mathf.Clamp(clampedPos.x, minX, maxX);
            clampedPos.y = Mathf.Clamp(clampedPos.y, minY, maxY);

            state.RawPosition = clampedPos;
        }
    }
}
