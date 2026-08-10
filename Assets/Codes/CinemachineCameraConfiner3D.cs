using UnityEngine;
using Unity.Cinemachine;

[ExecuteAlways]
[AddComponentMenu("Cinemachine/User/Cinemachine Camera Confiner 3D")]
public class CinemachineCameraConfiner3D : CinemachineExtension
{
    [Header("全局背景控制器 (推薦使用)")]
    [Tooltip("請把您所有當作「背景邊界」的物件 (例如 connect_0, RuinedBackground) 拖曳到這個陣列裡！只要放在這裡，攝影機絕對不會超過它們！")]
    public Collider[] explicitBackgrounds;

    [Header("邊界設定")]
    [Tooltip("是否自動尋找名字為 Background 或帶有 Background/RuinedBackground 標籤的物件作為邊界 (如果您有在上面設定，這個可以不用管)")]
    public bool autoFindBackground = true;

    [Tooltip("當某個背景比螢幕還小的時候，是否強制把攝影機鎖定在該背景的正中央？(打勾可避免破圖)")]
    public bool lockToCenterIfTooSmall = true;

    private Collider boundaryCollider;
    private Collider[] _cachedBackgroundColliders;
    private Bounds _currentClusterBounds;

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
        
        // 1. 優先使用全局手動設定的邊界
        if (explicitBackgrounds != null && explicitBackgrounds.Length > 0)
        {
            foreach (var col in explicitBackgrounds)
            {
                if (col != null) colliders.Add(col);
            }
        }
        
        // 2. 如果有開自動尋找，再把標籤加進去
        if (autoFindBackground)
        {
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
                            if (col != null && !colliders.Contains(col))
                            {
                                colliders.Add(col);
                            }
                        }
                    }
                }
                catch { }
            }
        }

        _cachedBackgroundColliders = colliders.ToArray();
    }

    private void FindClosestBoundaryCollider()
    {
        if (_cachedBackgroundColliders == null || _cachedBackgroundColliders.Length == 0)
        {
            CacheBackgroundColliders();
            if (_cachedBackgroundColliders == null || _cachedBackgroundColliders.Length == 0) return;
        }

        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj == null)
        {
            boundaryCollider = null;
            return;
        }

        Vector3 targetPos = playerObj.transform.position;

        // ★★★ 核心：只有主角踩到地面 (isGrounded) 時才允許開啟 confiner
        // 在空中墜落時，boundaryCollider 一律保持 null，攝影機完全自由跟隨主角！
        PlayerMovement pm = playerObj.GetComponent<PlayerMovement>();
        bool playerIsGrounded = (pm != null) ? pm.isGrounded : false;

        if (!playerIsGrounded)
        {
            // 主角在空中（墜落/跳躍），立刻解鎖所有邊界，鏡頭 100% 跟緊主角
            boundaryCollider = null;
            _currentClusterBounds = new Bounds();
            return;
        }

        // 主角已落地，尋找主角腳底所在的背景區域（只比對 X 軸範圍，Y 軸交給踩地保證）
        Collider activeCol = null;
        foreach (var col in _cachedBackgroundColliders)
        {
            if (col == null) continue;
            Bounds b = col.bounds;

            // 只判斷主角的 X 是否在該背景的 X 範圍內（踩到地即代表 Y 已在正確位置）
            bool isInsideX = targetPos.x >= b.min.x && targetPos.x <= b.max.x;
            // Y 也要在背景範圍內（防止主角雖落地但踩到其他背景）
            bool isInsideY = targetPos.y >= b.min.y && targetPos.y <= b.max.y;

            if (isInsideX && isInsideY)
            {
                activeCol = col;
                break;
            }
        }

        if (activeCol != null)
        {
            if (boundaryCollider != activeCol)
            {
                boundaryCollider = activeCol;
                _currentClusterBounds = CalculateClusterBounds(activeCol);
                Debug.Log($"[CinemachineCameraConfiner3D] ✅ 主角落地於區域，開啟邊界防穿幫：{activeCol.gameObject.name}");
            }
            else if (_currentClusterBounds.size == Vector3.zero)
            {
                _currentClusterBounds = CalculateClusterBounds(boundaryCollider);
            }
        }
        else
        {
            boundaryCollider = null;
            _currentClusterBounds = new Bounds();
        }
    }



    // 將互相連接的背景合併成一個超大邊界，解決背景切換時瞬間傳送的問題！
    private Bounds CalculateClusterBounds(Collider seed)
    {
        Bounds b = seed.bounds;
        bool changed = true;
        System.Collections.Generic.List<Collider> included = new System.Collections.Generic.List<Collider>() { seed };

        while (changed)
        {
            changed = false;
            foreach (var col in _cachedBackgroundColliders)
            {
                if (col == null || included.Contains(col)) continue;
                
                // 稍微擴大邊界來檢查是否相鄰 (容錯率)
                Bounds expandedB = b;
                expandedB.Expand(2f); 
                
                if (expandedB.Intersects(col.bounds))
                {
                    b.Encapsulate(col.bounds);
                    included.Add(col);
                    changed = true;
                }
            }
        }
        return b;
    }

    protected override void PostPipelineStageCallback(
        CinemachineVirtualCameraBase vcam,
        CinemachineCore.Stage stage, ref CameraState state, float deltaTime)
    {
        // 在 Body 階段之後、相機定位完成時，進行位置邊界限制修正，這是最平滑、最符合 Cinemachine 管線的時機
        if (stage == CinemachineCore.Stage.Body)
        {
            // 在運行時，動態尋找最接近玩家的背景，防止鏡頭切換拉扯與多場景邊界錯亂！
            if (Application.isPlaying && autoFindBackground)
            {
                FindClosestBoundaryCollider();
            }

            if (boundaryCollider == null) return;

            // 【新增】：如果是 FallingBackground，則不做邊界限制，讓鏡頭強制跟隨墜落的玩家！
            if (boundaryCollider.CompareTag("FallingBackground"))
            {
                return;
            }

            // 獲取主相機以取得 Aspect Ratio 螢幕寬高比
            Camera mainCam = Camera.main;
            if (mainCam == null) return;

            float aspect = mainCam.aspect;
            float halfHeight = 0f;
            float halfWidth = 0f;

            // 從目前的 Lens 狀態動態讀取，以完美相容「階梯攝影機動態縮放」的效果！
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

            Bounds bgBounds = _currentClusterBounds;

            // 計算相機中心點 of 容許範圍，確保相機四邊不會超出背景 Collider
            float minX = bgBounds.min.x + halfWidth;
            float maxX = bgBounds.max.x - halfWidth;
            float minY = bgBounds.min.y + halfHeight;
            float maxY = bgBounds.max.y - halfHeight;

            // 限制虛擬相機的原始位置
            Vector3 clampedPos = state.RawPosition;
            
            // X 軸邊界限制
            if (minX <= maxX) 
            {
                clampedPos.x = Mathf.Clamp(clampedPos.x, minX, maxX);
            }
            else if (lockToCenterIfTooSmall)
            {
                clampedPos.x = bgBounds.center.x; // 若背景太窄，強制鎖定在中心
            }

            // Y 軸動態邊界限制（活化判斷：掉落從上方進入時不擋鏡頭）
            if (minY <= maxY) 
            {
                if (state.RawPosition.y > maxY)
                {
                    // 鏡頭尚在背景上方時，僅限制 bottom 下邊界 (minY)，允許鏡頭順暢跟隨玩家從上方滑入！
                    clampedPos.y = Mathf.Max(clampedPos.y, minY);
                }
                else
                {
                    // 已進入背景內部，完整進行上下邊界限制防露空
                    clampedPos.y = Mathf.Clamp(clampedPos.y, minY, maxY);
                }
            }
            else if (lockToCenterIfTooSmall)
            {
                // 若玩家已在背景高度範圍內才置中鎖定；掉落過程不強行擋住鏡頭
                if (state.RawPosition.y <= bgBounds.max.y)
                {
                    clampedPos.y = bgBounds.center.y;
                }
            }

            state.RawPosition = clampedPos;
        }
    }

}
