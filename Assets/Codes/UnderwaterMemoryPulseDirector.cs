using System.Collections;
using UnityEngine;

/// <summary>
/// 水下記憶回聲脈衝。
/// 收集日誌或育兒物件後，從那個位置擴出一圈無文字的水波（記憶的回聲），接著最近的光球也回應一圈柔光，
/// 順便讓玩家知道光球在哪。★1015 改成純演出：不推人、不扣氣、不出現紅邊（撿到安心的東西不該被罰）。
/// </summary>
public class UnderwaterMemoryPulseDirector : MonoBehaviour, IResettable
{
    private enum PulseKind { Note, NurseryItem }

    [Header("啟用")]
    public bool pulseOnNote = true;
    public bool pulseOnNurseryItem = true;
    public bool requireUnderwaterEffect = true;

    [Header("時序")]
    [Tooltip("收集後延遲幾秒才開始預兆。日誌文字卡會暫停 Time.timeScale，所以脈衝會自然等卡片結束。")]
    public float startDelay = 0.45f;
    [Tooltip("水壓真正打到玩家前的預兆時間。")]
    public float telegraphSeconds = 1.1f;
    [Tooltip("兩次脈衝至少間隔幾秒，避免連續收集時懲罰堆疊。")]
    public float cooldownSeconds = 2.5f;

    [Header("視覺")]
    public Color notePulseColor = new Color(0.55f, 0.9f, 1f, 0.55f);
    public Color nurseryPulseColor = new Color(1f, 0.78f, 0.45f, 0.55f);
    public Color safePulseColor = new Color(0.9f, 1f, 0.7f, 0.38f);
    public float startRingScale = 0.9f;
    public float endRingScale = 8.0f;
    public int ringSortingOrder = 35;

    [Header("音效")]
    public AudioClip telegraphSFX;
    [Range(0f, 1f)] public float sfxVolume = 0.65f;

    private static UnderwaterMemoryPulseDirector _instance;
    private float _nextPulseTime;
    private Coroutine _pulseRoutine;
    private Transform _player;
    private Rigidbody _playerRb;
    private PlayerMovement _playerMovement;

    public static UnderwaterMemoryPulseDirector Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = Object.FindAnyObjectByType<UnderwaterMemoryPulseDirector>();
            }
            if (_instance == null)
            {
                GameObject go = new GameObject("UnderwaterMemoryPulseDirector_AutoCreated");
                _instance = go.AddComponent<UnderwaterMemoryPulseDirector>();
            }
            return _instance;
        }
    }

    public static void NotifyNoteCollected(Vector3 sourcePosition)
    {
        if (UnderwaterSuffocationEffect.Instance == null) return;
        Instance.QueuePulse(sourcePosition, PulseKind.Note);
    }

    public static void NotifyNurseryItemCollected(Vector3 sourcePosition)
    {
        if (UnderwaterSuffocationEffect.Instance == null) return;
        Instance.QueuePulse(sourcePosition, PulseKind.NurseryItem);
    }

    private void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
        }
        else if (_instance != this)
        {
            Destroy(gameObject);
        }
    }

    private void QueuePulse(Vector3 sourcePosition, PulseKind kind)
    {
        if (requireUnderwaterEffect && UnderwaterSuffocationEffect.Instance == null) return;
        if (kind == PulseKind.Note && !pulseOnNote) return;
        if (kind == PulseKind.NurseryItem && !pulseOnNurseryItem) return;
        if (Time.time < _nextPulseTime) return;

        if (_pulseRoutine != null) StopCoroutine(_pulseRoutine);
        _pulseRoutine = StartCoroutine(PulseRoutine(sourcePosition, kind));
    }

    private IEnumerator PulseRoutine(Vector3 sourcePosition, PulseKind kind)
    {
        _nextPulseTime = Time.time + cooldownSeconds;

        yield return new WaitForSeconds(startDelay);

        CachePlayer();
        if (_player == null || PlayerRespawnSystem.IsAnyRespawning)
        {
            _pulseRoutine = null;
            yield break;
        }

        Color pulseColor = kind == PulseKind.Note ? notePulseColor : nurseryPulseColor;
        if (telegraphSFX != null) PlaySfx(telegraphSFX, sourcePosition, sfxVolume * 0.8f);

        yield return SpawnRing(sourcePosition, pulseColor, startRingScale, endRingScale, telegraphSeconds);

        // 光球回應：最近的光球位置也擴出一圈柔光水波（不扣氣、不推人），順便告訴玩家光球在哪
        GuidanceLight light = FindNearestLight();
        if (light != null)
        {
            yield return SpawnRing(light.transform.position, safePulseColor, 0.8f, 5.0f, 0.9f);
        }
        _pulseRoutine = null;
    }

    private IEnumerator SpawnRing(Vector3 position, Color color, float startScale, float endScale, float duration)
    {
        Sprite sprite = UnderwaterHintGlow.SoftCircle();
        GameObject ring = new GameObject("MemoryPressureRing");
        ring.transform.position = new Vector3(position.x, position.y, position.z);

        SpriteRenderer sr = ring.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = color;
        sr.sortingOrder = ringSortingOrder;

        float t = 0f;
        duration = Mathf.Max(0.01f, duration);
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / duration);
            float eased = 1f - (1f - k) * (1f - k);
            float scale = Mathf.Lerp(startScale, endScale, eased);
            ring.transform.localScale = new Vector3(scale, scale, 1f);

            Color c = color;
            c.a = color.a * (1f - k);
            sr.color = c;
            yield return null;
        }

        Destroy(ring);
    }

    private GuidanceLight FindNearestLight()
    {
        CachePlayer();
        if (_player == null) return null;

        GuidanceLight best = null;
        float bestDist = float.MaxValue;
        GuidanceLight[] lights = Object.FindObjectsByType<GuidanceLight>(FindObjectsInactive.Exclude);
        for (int i = 0; i < lights.Length; i++)
        {
            GuidanceLight light = lights[i];
            if (light == null || !light.isActiveAndEnabled) continue;
            float dist = Vector2.Distance(_player.position, light.transform.position);
            if (dist < bestDist) { bestDist = dist; best = light; }
        }
        return best;
    }

    private void CachePlayer()
    {
        if (_playerMovement == null) _playerMovement = Object.FindAnyObjectByType<PlayerMovement>();
        if (_playerMovement != null)
        {
            _player = _playerMovement.transform;
            if (_playerRb == null) _playerRb = _playerMovement.GetComponent<Rigidbody>();
            if (_playerRb == null) _playerRb = _playerMovement.GetComponentInParent<Rigidbody>();
        }
    }

    private void PlaySfx(AudioClip clip, Vector3 position, float volume)
    {
        if (clip == null) return;
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFXAt(clip, position, volume);
        else AudioSource.PlayClipAtPoint(clip, position, AudioManager.ScaleSfx(volume));
    }

    public void ResetToInitialState()
    {
        if (_pulseRoutine != null)
        {
            StopCoroutine(_pulseRoutine);
            _pulseRoutine = null;
        }
        foreach (SpriteRenderer sr in Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Exclude))
        {
            if (sr != null && sr.gameObject.name == "MemoryPressureRing") Destroy(sr.gameObject);
        }
        _nextPulseTime = Time.time + 0.8f;
    }
}
