using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public class LockTransform : MonoBehaviour
{
    [Header("鎖定控制")]
    [Tooltip("勾選後將鎖定此物件且 Z 軸移至 100 (背景隱藏)；取消勾選則還原且 Z 軸設為 -10")]
    public bool isLocked = true;

    [HideInInspector]
    [SerializeField]
    private Vector3 lockedPosition;
    
    [HideInInspector]
    [SerializeField]
    private Quaternion lockedRotation;
    
    [HideInInspector]
    [SerializeField]
    private Vector3 lockedScale;
    
    [HideInInspector]
    [SerializeField]
    private bool hasLockedValues = false;

    private bool _previousLockState = false;

    void Start()
    {
        _previousLockState = isLocked;
        UpdateLockState(true);
    }

    void OnEnable()
    {
        _previousLockState = isLocked;
        UpdateLockState(true);
    }

    void OnValidate()
    {
        UpdateLockState(false);
    }

    void Update()
    {
        if (isLocked != _previousLockState)
        {
            UpdateLockState(false);
        }

        if (isLocked && hasLockedValues)
        {
            if (transform.position != lockedPosition)
            {
                transform.position = lockedPosition;
            }
            if (transform.rotation != lockedRotation)
            {
                transform.rotation = lockedRotation;
            }
            if (transform.localScale != lockedScale)
            {
                transform.localScale = lockedScale;
            }
        }
    }

    public void UpdateLockState(bool forceReset)
    {
        _previousLockState = isLocked;

        if (isLocked)
        {
            if (!hasLockedValues || forceReset)
            {
                lockedPosition = new Vector3(transform.position.x, transform.position.y, 100f);
                lockedRotation = transform.rotation;
                lockedScale = transform.localScale;
                hasLockedValues = true;
            }

            transform.position = lockedPosition;

#if UNITY_EDITOR
            SceneVisibilityManager.instance.DisablePicking(gameObject, true);
#endif
        }
        else
        {
            hasLockedValues = false;
            
            Vector3 currentPos = transform.position;
            currentPos.z = -10f;
            transform.position = currentPos;

#if UNITY_EDITOR
            SceneVisibilityManager.instance.EnablePicking(gameObject, true);
#endif
        }
    }

    void OnDisable()
    {
#if UNITY_EDITOR
        SceneVisibilityManager.instance.EnablePicking(gameObject, true);
#endif
    }
}
