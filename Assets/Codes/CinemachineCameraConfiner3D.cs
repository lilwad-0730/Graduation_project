using UnityEngine;
using Unity.Cinemachine;

[ExecuteAlways]
[AddComponentMenu("Cinemachine/User/Cinemachine Camera Confiner 3D")]
public class CinemachineCameraConfiner3D : CinemachineExtension
{
    [Header("邊界設定")]
    [Tooltip("用於限制攝影機的邊界 Collider (建議使用 BoxCollider，並勾選 Is Trigger)")]
    public Collider boundaryCollider;

    [Tooltip("是否自動尋找名字為 Background 或帶有 Background/FallingBackground/RuinedBackground 標籤的物件作為邊界")]
    public bool autoFindBackground = true;

    // 快取場景中所有的背景 Collider，提高效能並支援多區域切換
    private Collider[] _cachedBackgroundColliders;

    protected override void Awake()
    {
        base.Awake();
        if (Application.isPlaying)
        {
            CacheBackgroundColliders();
            FindClosestBoundaryCollider();
        }
    }

    private void CacheBackgroundColliders()
    {
        System.Collections.Generic.List<Collider> colliders = new System.Collections.Generic.List<Collider>();
        
        // 支援多種背景標籤，以相容關卡的不同區塊
        string[] tags = { "Background", "FallingBackground", "RuinedBackground" };
        foreach (string tag in tags)
        {
            try
            {
                GameObject[] bgs = GameObject.FindGameObjectsWithTag(tag);
                if (bgs != null)
                {
                    foreach (GameObject bg in bgs)
                    {
                        Collider col = bg.GetComponent<Collider>();
                        if (col != null)
                        {
                            colliders.Add(col);
                        }
                    }
                }
            }
            catch { }
        }

        if (colliders.Count == 0)
        {
            // 備用尋找
            GameObject bg = GameObject.Find("Background");
            if (bg == null) bg = GameObject.Find("BG");
            if (bg != null)
            {
                Collider col = bg.GetComponent<Collider>();
                if (col != null) colliders.Add(col);
            }
        }

        _cachedBackgroundColliders = colliders.ToArray();
    }

    private void FindClosestBoundaryCollider()
    {
        // 如果未啟用自動尋找，或者已手動指定了固定邊界，直接使用手動指定的
        if (boundaryCollider != null && !autoFindBackground) return;

        if (_cachedBackgroundColliders == null || _cachedBackgroundColliders.Length == 0)
        {
            CacheBackgroundColliders();
            if (_cachedBackgroundColliders == null || _cachedBackgroundColliders.Length == 0) return;
        }

        GameObject player = GameObject.FindWithTag("Player");
        Vector3 targetPos = player != null ? player.transform.position : transform.position;

        Collider closestCol = null;
        float minDistance = float.MaxValue;

        foreach (var col in _cachedBackgroundColliders)
        {
            if (col != null)
            {
                // 使用 ClosestPoint 計算玩家到背景碰撞體的最短物理距離，極度精準！
                Vector3 closestPoint = col.bounds.ClosestPoint(targetPos);
                float dist = Vector3.Distance(closestPoint, targetPos);
                if (dist < minDistance)
                {
                    minDistance = dist;
                    closestCol = col;
                }
            }
        }

        if (closestCol != null && boundaryCollider != closestCol)
        {
            boundaryCollider = closestCol;
            Debug.Log($"[CinemachineCameraConfiner3D] 已動態尋找並綁定最接近玩家的背景邊界：{closestCol.gameObject.name}");
        }
    }

    protected override void PostPipelineStageCallback(
        CinemachineVirtualCameraBase vcam,
        CinemachineCore.Stage stage, ref CameraState state, float deltaTime)
    {
        // 在 Body 階段之後、相機定位完成時，進行位置邊界限制修正，這是最平滑、最符合 Cinemachine 管線的時機
        if (stage == CinemachineCore.Stage.Body)
        {
            // 取得玩家的 PlayerMovement 元件以確認是否正在墜落
            GameObject player = GameObject.FindWithTag("Player");
            PlayerMovement pm = player != null ? player.GetComponent<PlayerMovement>() : null;
            
            if (pm != null && pm.freezeHorizontal)
            {
                // 如果正在 FallingBackground 墜落中，直接略過邊界限制，讓相機能毫無阻礙地跟隨玩家往下掉！
                return;
            }

            // 在運行時，動態尋找最接近玩家的背景，防止鏡頭切換拉扯與多場景邊界錯亂！
            if (Application.isPlaying && autoFindBackground)
            {
                FindClosestBoundaryCollider();
            }

            if (boundaryCollider == null) return;

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

            // 計算相機中心點 of 容許範圍，確保相機四邊不會超出背景 Collider
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
