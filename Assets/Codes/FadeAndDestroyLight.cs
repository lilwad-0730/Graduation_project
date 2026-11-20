using UnityEngine;

/// <summary>★1116 點光源在 duration 秒內從原本亮度退到 0，然後刪掉自己（玻璃館燭火被吃時的爆光用）。</summary>
public class FadeAndDestroyLight : MonoBehaviour
{
    public float duration = 1f;
    private Light _light;
    private float _start, _startIntensity;

    private void Start()
    {
        _light = GetComponent<Light>();
        _start = Time.time;
        _startIntensity = _light != null ? _light.intensity : 0f;
    }

    private void Update()
    {
        float t = (Time.time - _start) / Mathf.Max(0.05f, duration);
        if (_light != null) _light.intensity = _startIntensity * (1f - t) * (1f - t);
        if (t >= 1f) Destroy(gameObject);
    }
}
