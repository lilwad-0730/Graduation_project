using UnityEngine;

/// <summary>
/// ★1109k 近景虛化前景層的視差：每個子物件的 x = 錨點 x − (攝影機 x − 錨點 x) × factor。
/// 前景比世界移動得更快（factor 0.3~0.6），製造「靠近鏡頭」的景深感。
///
/// 做法：子物件在場景裡擺的位置就是「錨點」（攝影機正好在那個 x 時，它就在原處）。
/// 執行時每幀（LateUpdate，排在攝影機之後）才去改 transform；編輯模式預設完全不動場景，
/// 只有勾 previewInEditMode 才會預覽，取消勾選就把位置還原成錨點，所以不會弄髒場景。
/// 純視覺：沒有碰撞、不跟任何遊戲邏輯互動。
/// </summary>
[ExecuteAlways]
[DefaultExecutionOrder(10001)]
public class ForegroundParallax : MonoBehaviour
{
    [Tooltip("視差強度。0.3~0.6：數字越大，前景相對畫面移動越快")]
    [Range(0f, 1f)] public float factor = 0.45f;

    [Tooltip("要跟著動的物件；留空 = 自動抓自己底下所有子物件")]
    public Transform[] items;

    [Tooltip("編輯模式預覽（勾掉會把位置還原成錨點）。不勾就完全不動場景")]
    public bool previewInEditMode = false;

    [Tooltip("預覽用的攝影機 x")]
    public float previewCamX = 0f;

    private Transform[] _items;
    private float[] _anchorX;
    private bool _applied;

    private void CaptureAnchors()
    {
        if (items != null && items.Length > 0) _items = items;
        else
        {
            _items = new Transform[transform.childCount];
            for (int i = 0; i < _items.Length; i++) _items[i] = transform.GetChild(i);
        }
        _anchorX = new float[_items.Length];
        for (int i = 0; i < _items.Length; i++) _anchorX[i] = _items[i] != null ? _items[i].position.x : 0f;
    }

    /// <summary>把所有物件還原成錨點位置（編輯模式預覽關掉時用）。</summary>
    public void RestoreAnchors()
    {
        if (_items == null || _anchorX == null) return;
        for (int i = 0; i < _items.Length; i++)
        {
            if (_items[i] == null) continue;
            Vector3 p = _items[i].position; p.x = _anchorX[i]; _items[i].position = p;
        }
        _applied = false;
        _items = null; _anchorX = null;
    }

    /// <summary>照指定的攝影機 x 立刻套用一次（執行時由 LateUpdate 呼叫，截圖腳本也會用）。</summary>
    public void Apply(float camX)
    {
        if (_items == null || _anchorX == null) CaptureAnchors();
        for (int i = 0; i < _items.Length; i++)
        {
            if (_items[i] == null) continue;
            Vector3 p = _items[i].position;
            p.x = _anchorX[i] - (camX - _anchorX[i]) * factor;
            _items[i].position = p;
        }
        _applied = true;
    }

    private void LateUpdate()
    {
        if (Application.isPlaying)
        {
            Camera cam = Camera.main;
            if (cam != null) Apply(cam.transform.position.x);
            return;
        }
        if (previewInEditMode) Apply(previewCamX);
        else if (_applied) RestoreAnchors();
    }

    private void OnDisable()
    {
        // 編輯模式離開 / 重新編譯前，把位置還原，避免弄髒場景
        if (!Application.isPlaying) RestoreAnchors();
    }

    private void OnValidate()
    {
        if (!previewInEditMode && !Application.isPlaying && _applied) RestoreAnchors();
    }
}
