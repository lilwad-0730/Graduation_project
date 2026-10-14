using UnityEngine;

/// <summary>
/// ★1012 棉花堡上層：後景鋪底氣球。
/// 只是飄著讓上層不單調，不能碰、沒有碰撞。越遠的越小、越淡、飄得越慢（空氣透視），
/// 擺在角色與城堡物件的後面。場景物件由 Tools ▸ 棉花堡上層 ▸ 建立氣球 建好。
/// </summary>
public class UpperCastleBackdropBalloon : MonoBehaviour
{
    public SpriteRenderer stringRenderer;

    [Header("飄動（越遠的氣球建立工具會給越小的數字）")]
    public float bobAmplitude = 0.5f;
    public float bobSpeed = 0.5f;
    public float driftAmplitude = 2.2f;
    public float driftSpeed = 0.12f;
    public float swayDegrees = 3f;

    private Vector3 _home;
    private float _phase;

    private void Start()
    {
        _home = transform.position;
        _phase = Random.value * 100f;
    }

    private void Update()
    {
        float t = Time.time + _phase;
        transform.position = new Vector3(
            _home.x + Mathf.Sin(t * driftSpeed) * driftAmplitude,
            _home.y + Mathf.Sin(t * bobSpeed) * bobAmplitude,
            _home.z);
        transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 0.55f) * swayDegrees);
        if (stringRenderer != null)
            stringRenderer.transform.localRotation = Quaternion.Euler(0f, 0f, -Mathf.Sin(t * 0.55f - 0.6f) * swayDegrees * 1.6f);
    }
}
