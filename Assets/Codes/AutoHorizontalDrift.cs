using UnityEngine;

/// <summary>
/// 自動水平漂移 (Auto Horizontal Drift)。
/// 讓物件 (例如背景雲朵) 自動往左或往右持續移動。
/// 可選擇移動到指定距離後的處理方式：一直走下去、回到起點重新循環、或來回擺盪。
/// </summary>
public class AutoHorizontalDrift : MonoBehaviour
{
    public enum Direction { Left, Right }
    public enum EndMode
    {
        [InspectorName("一直移動 (不回頭)")] Endless,
        [InspectorName("循環 (走完距離後回到起點)")] Loop,
        [InspectorName("來回 (走完距離後反向)")] PingPong
    }

    [Header("移動 (Movement)")]
    [Tooltip("移動方向")]
    public Direction direction = Direction.Left;
    [Tooltip("移動速度 (單位/秒)")]
    public float speed = 0.5f;
    [Tooltip("速度隨機浮動比例 (0 = 固定速度，0.2 = 每次開始時在 ±20% 內隨機，讓多朵雲速度不一致)")]
    [Range(0f, 1f)]
    public float speedRandomness = 0f;
    [Tooltip("開始移動前的延遲 (秒)")]
    public float startDelay = 0f;
    [Tooltip("暫停移動")]
    public bool paused = false;

    [Header("範圍 (Range)")]
    [Tooltip("走完指定距離後的處理方式")]
    public EndMode endMode = EndMode.Loop;
    [Tooltip("移動距離 (從起點算起，Endless 模式下無作用)")]
    public float travelDistance = 30f;

    [Header("其他 (Other)")]
    [Tooltip("勾選後使用不受 Time.timeScale 影響的時間 (遊戲暫停時仍會移動)")]
    public bool useUnscaledTime = false;

    private Vector3 _startLocalPos;
    private float _currentSpeed;
    private float _offset;      // 目前相對起點的位移量 (沿移動方向，恆為正)
    private int _pingPongSign = 1;
    private float _delayTimer;

    private void Start()
    {
        _startLocalPos = transform.localPosition;
        _currentSpeed = speed * (1f + Random.Range(-speedRandomness, speedRandomness));
        _delayTimer = startDelay;
    }

    private void Update()
    {
        if (paused) return;

        float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        if (_delayTimer > 0f)
        {
            _delayTimer -= dt;
            return;
        }

        float step = Mathf.Abs(_currentSpeed) * dt;

        switch (endMode)
        {
            case EndMode.Endless:
                _offset += step;
                break;

            case EndMode.Loop:
                _offset += step;
                if (travelDistance > 0f && _offset >= travelDistance) _offset -= travelDistance;
                break;

            case EndMode.PingPong:
                _offset += step * _pingPongSign;
                if (_offset >= travelDistance) { _offset = travelDistance; _pingPongSign = -1; }
                else if (_offset <= 0f) { _offset = 0f; _pingPongSign = 1; }
                break;
        }

        float dirSign = direction == Direction.Left ? -1f : 1f;
        transform.localPosition = _startLocalPos + new Vector3(dirSign * _offset, 0f, 0f);
    }

    /// <summary>讓物件回到起點並重新開始移動。</summary>
    public void ResetToStart()
    {
        _offset = 0f;
        _pingPongSign = 1;
        _delayTimer = startDelay;
        transform.localPosition = _startLocalPos;
    }
}
