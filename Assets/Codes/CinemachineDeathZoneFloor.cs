using UnityEngine;
using Unity.Cinemachine;

/// <summary>
/// 鏡頭最低只能看到 DeathZone 頂面上方 viewBottomAboveDeathZone 的位置。
/// 角色整個掉出畫面底邊時剛好碰到 DeathZone 判定死亡（死亡判定仍由 PlayerRespawnSystem 處理）。
/// 頂面 Y 和畫面半高（OrthographicSize）每幀即時讀取，移動 DeathZone 或改鏡頭大小都會自動同步。
/// </summary>
[ExecuteAlways]
[SaveDuringPlay]
[AddComponentMenu("Cinemachine/Cinemachine Death Zone Floor")]
public class CinemachineDeathZoneFloor : CinemachineExtension
{
    [Tooltip("當作鏡頭下限的 DeathZone。留空會自動找名為 deathZoneName 的物件")]
    public Collider deathZone;

    [Tooltip("自動尋找時用的物件名稱")]
    public string deathZoneName = "DeathZone_main";

    [Tooltip("畫面底邊比 DeathZone 頂面高多少。設成角色高度，角色要整個掉出畫面才碰到 DeathZone")]
    public float viewBottomAboveDeathZone = 3.88f;

    [Tooltip("只在鏡頭 X 位於 DeathZone 左右範圍內時才限制（範圍外照原本跟隨）")]
    public bool onlyWithinDeathZoneX = true;

    protected override void PostPipelineStageCallback(
        CinemachineVirtualCameraBase vcam,
        CinemachineCore.Stage stage, ref CameraState state, float deltaTime)
    {
        if (stage != CinemachineCore.Stage.Body) return;

        if (deathZone == null)
        {
            GameObject go = GameObject.Find(deathZoneName);
            if (go != null) deathZone = go.GetComponent<Collider>();
            if (deathZone == null) return;
        }

        Bounds b = deathZone.bounds;
        Vector3 pos = state.RawPosition;
        if (onlyWithinDeathZoneX && (pos.x < b.min.x || pos.x > b.max.x)) return;

        // 鏡頭中心最低 = 頂面 + 間隔 + 畫面半高 → 畫面底邊在 DeathZone 頂面上方 viewBottomAboveDeathZone
        float minCamY = b.max.y + viewBottomAboveDeathZone + state.Lens.OrthographicSize;
        if (pos.y < minCamY)
        {
            pos.y = minCamY;
            state.RawPosition = pos;
        }
    }
}
