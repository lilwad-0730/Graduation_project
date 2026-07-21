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

    [Tooltip("葉片水流拉拽形變幅度")]
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
    private bool _initialized = false;

    void Start()
    {
        Initialize();
    }

    void OnEnable()
    {
        Initialize();
    }

    private void Initialize()
    {
        if (!_initialized)
        {
            _initialWorldPos = transform.position;
            _initialRotation = transform.localRotation;
            _initialScale = transform.localScale;
            _spriteRenderer = GetComponent<SpriteRenderer>();

            // 計算 Sprite 的底部中心點作為根部支點 (Root Pivot)
            if (_spriteRenderer != null && _spriteRenderer.sprite != null)
            {
                Bounds spriteBounds = _spriteRenderer.sprite.bounds;
                _rootPivotOffsetLocal = new Vector3(spriteBounds.center.x, spriteBounds.min.y, 0f);
            }
            else
            {
                _rootPivotOffsetLocal = new Vector3(0f, -0.5f, 0f);
            }

            // 若未指定 phaseOffset，根據座標自動生成穩定隨機值
            if (phaseOffset == 0f)
            {
                phaseOffset = (transform.position.x * 12.3f + transform.position.y * 7.7f) % (Mathf.PI * 2f);
            }

            _initialized = true;
        }
    }

    void Update()
    {
        if (pauseSway) return;

        if (!_initialized)
        {
            Initialize();
        }

        float time = Application.isPlaying ? Time.time : (float)UnityEditor.EditorApplication.timeSinceStartup;
        
        // 正規化時間 (0 ~ 1)，包含 phaseOffset 確保擺動非同步且 100% 無縫銜接
        float normalizedTime = ((time + phaseOffset) % cycleDuration) / cycleDuration;
        float tau = normalizedTime * Mathf.PI * 2.0f; // 2 * PI * t / T

        // 1. 水流運動方程：包含基頻與諧波延遲，呈現 S 型流體感
        float mainWave = Mathf.Sin(tau);
        float lagWave = Mathf.Sin(tau * 2.0f - tipInertiaLag);
        float currentAngle = (mainWave + 0.25f * lagWave) * maxSwayAngle;

        // 2. 繞根部支點 (Root Pivot) 旋轉，確保根部固定不動
        Quaternion currentRot = _initialRotation * Quaternion.Euler(0f, 0f, currentAngle);

        if (lockRootPivot)
        {
            Vector3 worldPivot = transform.TransformPoint(_rootPivotOffsetLocal);
            transform.localRotation = currentRot;
            Vector3 rotatedOffset = transform.TransformPoint(_rootPivotOffsetLocal);
            transform.position += (worldPivot - rotatedOffset);
        }
        else
        {
            transform.localRotation = currentRot;
        }

        // 3. 葉片水流伸縮與慣性形變
        float stretchFactor = Mathf.Abs(mainWave) * stretchAmount;
        Vector3 newScale = _initialScale;
        newScale.y = _initialScale.y * (1.0f + stretchFactor);
        newScale.x = _initialScale.x * (1.0f - stretchFactor * 0.4f);
        transform.localScale = newScale;
    }
}
