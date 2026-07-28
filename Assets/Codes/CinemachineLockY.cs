using UnityEngine;
using Unity.Cinemachine;

[ExecuteAlways]
[SaveDuringPlay]
[AddComponentMenu("Cinemachine/Cinemachine Lock Y")]
public class CinemachineLockY : CinemachineExtension
{
    [Tooltip("The fixed Y position to lock the camera to")]
    public float lockedY = 5.29f;

    protected override void PostPipelineStageCallback(
        CinemachineVirtualCameraBase vcam,
        CinemachineCore.Stage stage, ref CameraState state, float deltaTime)
    {
        if (stage == CinemachineCore.Stage.Body)
        {
            Vector3 pos = state.RawPosition;
            pos.y = lockedY;
            state.RawPosition = pos;
        }
    }
}
