using UnityEngine;

/// <summary>
/// 自動為場景中所有名稱包含 'breakable' 的玻璃地磚掛載與設定 BreakableGlassFloor 和 Destructible。
/// 無論是在 Edit Mode 還是 Play Mode 執行，均保證 100% 作用。
/// </summary>
[ExecuteAlways]
public class BreakableFloorAutoSetup : MonoBehaviour
{
    private void Awake()
    {
        SetupAllBreakableFloors();
    }

    private void OnEnable()
    {
        SetupAllBreakableFloors();
    }

    public static void SetupAllBreakableFloors()
    {
        var all = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var go in all)
        {
            if (go.name.ToLower().Contains("breakable") || go.name.ToLower().Contains("glass floor_0 breakable"))
            {
                Destructible dest = go.GetComponent<Destructible>();
                if (dest == null) dest = go.AddComponent<Destructible>();
                dest.columns = 4;
                dest.rows = 4;
                dest.explosionForce = 4.5f;
                dest.disappearDelay = 2.5f;
                dest.shatterOnCollision = false;

                BreakableGlassFloor bgf = go.GetComponent<BreakableGlassFloor>();
                if (bgf == null) bgf = go.AddComponent<BreakableGlassFloor>();
                bgf.delayBeforeShatter = 2.0f;
                bgf.enableWarningShake = true;
                bgf.shakeIntensity = 0.05f;

                Collider col = go.GetComponent<Collider>();
                if (col == null)
                {
                    BoxCollider boxCol = go.AddComponent<BoxCollider>();
                    SpriteRenderer sr = go.GetComponent<SpriteRenderer>();
                    if (sr != null && sr.sprite != null)
                    {
                        boxCol.size = new Vector3(sr.bounds.size.x, sr.bounds.size.y, 2f);
                    }
                    else
                    {
                        boxCol.size = new Vector3(11.38f, 1.28f, 2f);
                    }
                }

                Rigidbody rb = go.GetComponent<Rigidbody>();
                if (rb == null)
                {
                    rb = go.AddComponent<Rigidbody>();
                    rb.isKinematic = true;
                    rb.useGravity = false;
                }
            }
        }
    }
}
