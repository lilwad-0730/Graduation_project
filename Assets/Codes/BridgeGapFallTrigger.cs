using UnityEngine;
using System.Collections;

/// <summary>
/// 斷橋墜落重生觸發輔助組件 (BridgeGapFallTrigger)
/// </summary>
public class BridgeGapFallTrigger : MonoBehaviour
{
    public UpperCastleFloorController controller;
    private bool _triggering = false;

    private void OnTriggerEnter(Collider other)
    {
        CheckFall(other.gameObject);
    }

    private void OnTriggerStay(Collider other)
    {
        CheckFall(other.gameObject);
    }

    private void CheckFall(GameObject obj)
    {
        if (_triggering || obj == null) return;

        PlayerMovement pm = obj.GetComponent<PlayerMovement>();
        if (pm == null) pm = obj.GetComponentInParent<PlayerMovement>();
        if (pm == null && (obj.CompareTag("Player") || obj.name.Contains("Player")))
        {
            pm = Object.FindFirstObjectByType<PlayerMovement>();
        }

        if (pm != null)
        {
            _triggering = true;
            StartCoroutine(RespawnRoutine(pm));
        }
    }

    private IEnumerator RespawnRoutine(PlayerMovement pm)
    {
        Debug.Log("🕳️【斷橋墜落】玩家跌入高空深淵，觸發真實下墜演出！");

        // 依設定延遲短暫時間，讓玩家清楚體會到失重下墜感
        if (controller != null && controller.fallDelayBeforeFade > 0f)
        {
            yield return new WaitForSeconds(controller.fallDelayBeforeFade);
        }

        PlayerRespawnSystem respawnSystem = pm.GetComponent<PlayerRespawnSystem>();
        if (respawnSystem == null) respawnSystem = Object.FindFirstObjectByType<PlayerRespawnSystem>();

        Vector3 targetRespawnPos;
        if (controller != null && controller.customRespawnPoint != null)
        {
            targetRespawnPos = controller.customRespawnPoint.position;
        }
        else
        {
            // 預設重生在破洞左側 3.5 米處的平地安全區
            float respawnX = (controller != null) ? (controller.gapLeftX - 3.5f) : (transform.position.x - 4f);
            float respawnY = (controller != null) ? (controller.patioFloorSurfaceY + 0.2f) : 39.6f;
            targetRespawnPos = new Vector3(respawnX, respawnY, 0f);
        }

        if (respawnSystem != null)
        {
            respawnSystem.TriggerRespawn(targetRespawnPos);
        }
        else
        {
            pm.WarpTo(targetRespawnPos);
        }

        yield return new WaitForSeconds(2.0f);
        _triggering = false;
    }
}
