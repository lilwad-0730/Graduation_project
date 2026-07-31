using UnityEngine;
using System.Collections;

/// <summary>
/// 掛載於可踩空/可碎裂的玻璃地磚 (例如 'glass floor_0 breakable')。
/// 當玩家接觸或踩上地磚時，無條件觸發鏡牆切片碎裂與重力崩塌特效。
/// </summary>
[RequireComponent(typeof(Destructible))]
public class BreakableGlassFloor : MonoBehaviour, IResettable
{
    [Header("踩踏碎裂設定")]
    [Tooltip("踩上地磚後，過幾秒開始向下下沉與碎裂 (秒)")]
    public float delayBeforeShatter = 2.0f;

    [Tooltip("踩上時是否有些微震動/下沉預兆")]
    public bool enableWarningShake = true;

    [Tooltip("震動幅度")]
    public float shakeIntensity = 0.05f;

    private bool isTriggered = false;
    private Vector3 originalPosition;
    private Destructible destructible;

    private void Awake()
    {
        originalPosition = transform.position;
        destructible = GetComponent<Destructible>();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (IsPlayer(collision.gameObject))
        {
            TriggerBreakSequence();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (IsPlayer(other.gameObject))
        {
            TriggerBreakSequence();
        }
    }

    private bool IsPlayer(GameObject go)
    {
        if (go == null) return false;
        if (go.CompareTag("Player")) return true;
        if (go.name.ToLower().Contains("player") || go.GetComponent<PlayerMovement>() != null) return true;
        return false;
    }

    public void TriggerBreakSequence()
    {
        if (isTriggered) return;
        isTriggered = true;
        StartCoroutine(ShatterRoutine());
    }

    private IEnumerator ShatterRoutine()
    {
        float timer = 0f;

        while (timer < delayBeforeShatter)
        {
            timer += Time.deltaTime;
            if (enableWarningShake)
            {
                Vector3 shakeOffset = Random.insideUnitSphere * shakeIntensity;
                shakeOffset.z = 0f;
                transform.position = originalPosition + Vector3.down * (timer * 0.4f) + shakeOffset;
            }
            yield return null;
        }

        if (destructible != null)
        {
            destructible.Shatter();
        }
        else
        {
            gameObject.SetActive(false);
        }
    }

    public void ResetToInitialState()
    {
        StopAllCoroutines();
        isTriggered = false;
        transform.position = originalPosition;
        if (destructible != null)
        {
            destructible.ResetToInitialState();
        }
        gameObject.SetActive(true);
    }
}
