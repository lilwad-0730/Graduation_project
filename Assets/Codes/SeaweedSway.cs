using UnityEngine;

[ExecuteAlways]
public class SeaweedSway : MonoBehaviour
{
    [Header("編輯與控制")]
    [Tooltip("勾選即可暫停海草搖擺 (方便編輯其他物件)")]
    public bool pauseSway = false;

    [Header("無縫水下擺動設定")]
    [Tooltip("無縫循環週期的秒數 (首尾 100% 完全銜接)")]
    public float cycleDuration = 6.0f;

    [Tooltip("最大擺動角度 (以根部為支點)")]
    public float maxSwayAngle = 10.0f;

    [Tooltip("水流推動與葉尖慣性延遲 (S型彎曲感)")]
    public float tipInertiaLag = 0.35f;

    [Tooltip("葉片水流拉拽形變幅度（以 Z 軸縮放值作為 Y 軸絕對變化量參考）")]
    public float stretchAmount = 0.035f;

    [Tooltip("自動鎖定根部底端為旋轉支點 (確保根部固定不動)")]
    public bool lockRootPivot = true;

    [Tooltip("隨機相位偏移 (避免多株海草同步搖擺)")]
    public float phaseOffset = 0f;

    private Vector3 _initialWorldPos;
    private Quaternion _initialRotation;
    private Vector3 _initialScale;
    private Vector3 _rootPivotOffsetLocal;
    private SpriteRenderer _spriteRenderer;
    private bool _initialized;

    private void Start()
    {
        Initialize();
    }

    private void OnEnable()
    {
        Initialize();

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            UnityEditor.EditorApplication.update -= EditorTick;
            UnityEditor.EditorApplication.update += EditorTick;
        }
#endif
    }

    private void OnDisable()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.update -= EditorTick;
#endif

        if (_initialized)
        {
            transform.position = _initialWorldPos;
            transform.localRotation = _initialRotation;
            transform.localScale = _initialScale;
        }

        _initialized = false;
    }

    private void OnDestroy()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.update -= EditorTick;
#endif

        if (_initialized)
        {
            transform.position = _initialWorldPos;
            transform.localRotation = _initialRotation;
            transform.localScale = _initialScale;
        }
    }

    private void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        _initialWorldPos = transform.position;
        _initialRotation = transform.localRotation;
        _initialScale = transform.localScale;
        _spriteRenderer = GetComponent<SpriteRenderer>();

        if (_spriteRenderer != null && _spriteRenderer.sprite != null)
        {
            Bounds spriteBounds = _spriteRenderer.sprite.bounds;
            _rootPivotOffsetLocal = new Vector3(spriteBounds.center.x, spriteBounds.min.y, 0f);
        }
        else
        {
            _rootPivotOffsetLocal = new Vector3(0f, -0.5f, 0f);
        }

        if (phaseOffset == 0f)
        {
            phaseOffset = (transform.position.x * 12.3f + transform.position.y * 7.7f) % (Mathf.PI * 2f);
        }

        _initialized = true;
    }

#if UNITY_EDITOR
    private void EditorTick()
    {
        if (Application.isPlaying)
        {
            return;
        }

        UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
        UnityEditor.SceneView.RepaintAll();
    }
#endif

    private void Update()
    {
        if (pauseSway)
        {
            return;
        }

        if (!_initialized)
        {
            Initialize();
        }

        float duration = Mathf.Max(0.01f, cycleDuration);
#if UNITY_EDITOR
        float time = Application.isPlaying
            ? Time.time
            : (float)UnityEditor.EditorApplication.timeSinceStartup;
#else
        float time = Time.time;
#endif
        float normalizedTime = ((time + phaseOffset) % duration) / duration;
        float tau = normalizedTime * Mathf.PI * 2.0f;

        float mainWave = Mathf.Sin(tau);
        float lagWave = Mathf.Sin(tau * 2.0f - tipInertiaLag);
        float currentAngle = (mainWave + 0.25f * lagWave) * maxSwayAngle;

        Quaternion currentRot = _initialRotation * Quaternion.Euler(0f, 0f, currentAngle);

        if (lockRootPivot)
        {
            Vector3 worldPivot = transform.TransformPoint(_rootPivotOffsetLocal);
            transform.localRotation = currentRot;
            Vector3 rotatedOffset = transform.TransformPoint(_rootPivotOffsetLocal);
            transform.position += worldPivot - rotatedOffset;
        }
        else
        {
            transform.localRotation = currentRot;
        }

        float stretchFactor = Mathf.Abs(mainWave) * stretchAmount;
        Vector3 newScale = _initialScale;
        float zScaleReference = Mathf.Abs(_initialScale.z);
        float yScaleSign = Mathf.Sign(_initialScale.y);
        newScale.y = yScaleSign * zScaleReference * (1.0f + stretchFactor);
        newScale.x = _initialScale.x * (1.0f - stretchFactor * 0.4f);
        transform.localScale = newScale;
    }
}
