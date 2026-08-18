using UnityEngine;
using System.Collections;

/// <summary>
/// 主選單角色動態控制器
/// 狀態管理: Walking (行走), Stopping (減速中), LookingAtMenu (注視選單), Resuming (恢復行走)
/// 當選單項目 Hover / Select 時，角色自然減速 (0.2~0.5s) 並轉向左側選單；
/// 當無操作或焦點解除時，短暫維持注視後再恢復向右緩慢行走
/// </summary>
public class MenuCharacterController : MonoBehaviour
{
    public enum CharacterState
    {
        Walking,
        Stopping,
        LookingAtMenu,
        Resuming
    }

    [Header("狀態與屬性")]
    public CharacterState currentState = CharacterState.Walking;

    [Header("元件引用")]
    public Animator characterAnimator;
    public Transform characterTransform;

    [Header("移動與轉向參數")]
    public float walkSpeed = 1.5f;
    public float minX = 0f;
    public float maxX = 8f;
    public float decelerationDuration = 0.35f;
    public float gazeDurationBeforeResume = 2.0f;

    private float _currentSpeed = 0f;
    private int _walkDirection = 1; // 1: 右, -1: 左
    private Coroutine _stateCoroutine;
    private float _idleTimer = 0f;
    private bool _hasActiveFocus = false;

    private static readonly int AnimRunState = Animator.StringToHash("Run");
    private static readonly int AnimIdleState = Animator.StringToHash("Idle");

    private void Start()
    {
        if (characterTransform == null) characterTransform = transform;
        if (characterAnimator == null) characterAnimator = GetComponentInChildren<Animator>();

        _currentSpeed = walkSpeed;
        SetAnimatorWalk(true);
    }

    private void Update()
    {
        switch (currentState)
        {
            case CharacterState.Walking:
                MoveCharacter();
                break;

            case CharacterState.LookingAtMenu:
                _idleTimer += Time.deltaTime;
                if (!_hasActiveFocus && _idleTimer >= gazeDurationBeforeResume)
                {
                    ResumeWalking();
                }
                break;
        }
    }

    private void MoveCharacter()
    {
        if (characterTransform == null) return;

        Vector3 pos = characterTransform.position;
        pos.x += _walkDirection * _currentSpeed * Time.deltaTime;

        // 抵達 X 邊界時自動折返
        if (pos.x >= maxX)
        {
            pos.x = maxX;
            _walkDirection = -1;
            SetFacingDirection(-1);
        }
        else if (pos.x <= minX)
        {
            pos.x = minX;
            _walkDirection = 1;
            SetFacingDirection(1);
        }

        characterTransform.position = pos;
    }

    /// <summary>
    /// 當玩家 hover / select 任何選單選項時呼叫
    /// </summary>
    public void OnMenuHoverOrSelect()
    {
        _hasActiveFocus = true;
        _idleTimer = 0f;

        if (currentState == CharacterState.Walking || currentState == CharacterState.Resuming)
        {
            if (_stateCoroutine != null) StopCoroutine(_stateCoroutine);
            _stateCoroutine = StartCoroutine(StopAndLookAtMenuRoutine());
        }
    }

    /// <summary>
    /// 當選擇焦點解除時呼叫
    /// </summary>
    public void OnMenuFocusLost()
    {
        _hasActiveFocus = false;
        _idleTimer = 0f; // 開始倒數 2.0s 後 Resume
    }

    private IEnumerator StopAndLookAtMenuRoutine()
    {
        currentState = CharacterState.Stopping;

        // 0.2~0.5 秒自然減速
        float elapsed = 0f;
        float initialSpeed = _currentSpeed;

        while (elapsed < decelerationDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / decelerationDuration;
            _currentSpeed = Mathf.Lerp(initialSpeed, 0f, t);

            // 邊減速邊繼續小幅度向前移動
            if (characterTransform != null)
            {
                characterTransform.position += new Vector3(_walkDirection * _currentSpeed * Time.deltaTime, 0f, 0f);
            }
            yield return null;
        }

        _currentSpeed = 0f;
        SetAnimatorWalk(false);

        // 轉向左側選單 (-1 方向)
        SetFacingDirection(-1);

        currentState = CharacterState.LookingAtMenu;
    }

    public void ResumeWalking()
    {
        if (currentState == CharacterState.LookingAtMenu || currentState == CharacterState.Stopping)
        {
            if (_stateCoroutine != null) StopCoroutine(_stateCoroutine);
            _stateCoroutine = StartCoroutine(ResumeWalkingRoutine());
        }
    }

    private IEnumerator ResumeWalkingRoutine()
    {
        currentState = CharacterState.Resuming;

        // 轉回原本行走方向 (通常向右 +1)
        _walkDirection = 1;
        SetFacingDirection(1);
        yield return new WaitForSeconds(0.2f);

        SetAnimatorWalk(true);

        // 漸進加速恢復行走
        float elapsed = 0f;
        while (elapsed < 0.4f)
        {
            elapsed += Time.deltaTime;
            _currentSpeed = Mathf.Lerp(0f, walkSpeed, elapsed / 0.4f);
            yield return null;
        }

        _currentSpeed = walkSpeed;
        currentState = CharacterState.Walking;
    }

    private void SetFacingDirection(int dir)
    {
        if (characterTransform == null) return;
        Vector3 scale = characterTransform.localScale;
        scale.x = Mathf.Abs(scale.x) * (dir >= 0 ? 1f : -1f);
        characterTransform.localScale = scale;
    }

    private void SetAnimatorWalk(bool isWalking)
    {
        if (characterAnimator == null) return;
        try
        {
            characterAnimator.CrossFade(isWalking ? AnimRunState : AnimIdleState, 0.2f);
        }
        catch { }
    }
}
