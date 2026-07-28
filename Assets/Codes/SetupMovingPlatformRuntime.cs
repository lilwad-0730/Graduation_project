using UnityEngine;

[ExecuteAlways]
public class SetupMovingPlatformRuntime : MonoBehaviour
{
    private static bool configured = false;

    void Awake()
    {
        ConfigurePlatform();
    }

    void Start()
    {
        ConfigurePlatform();
    }

    void OnEnable()
    {
        ConfigurePlatform();
    }

    public static void ConfigurePlatform()
    {
        if (configured) return;

        GameObject go = GameObject.Find("glass platform_001");
        if (go == null)
        {
            var all = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var o in all)
            {
                if (o.name.Equals("glass platform_001", System.StringComparison.OrdinalIgnoreCase))
                {
                    go = o;
                    break;
                }
            }
        }

        if (go != null)
        {
            var comp = go.GetComponent<HorizontalMovingPlatform>();
            if (comp == null)
            {
                comp = go.AddComponent<HorizontalMovingPlatform>();
            }

            comp.minX = 22.36f;
            comp.maxX = 46.0f;
            comp.cycleDuration = 6.0f;
            comp.fixedY = go.transform.position.y;
            comp.smoothMovement = true;
            comp.parentPlayerOnRide = true;

            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (rb == null) rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            configured = true;
            Debug.Log($"[SetupMovingPlatformRuntime] 成功設定 '{go.name}' (minX=22.36, maxX=46.0, duration=6.0s)！");
        }
    }
}
