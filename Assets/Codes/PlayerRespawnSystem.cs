using UnityEngine;
using System.Collections;

// Ensures this script runs correctly
[RequireComponent(typeof(Rigidbody))]
public class PlayerRespawnSystem : MonoBehaviour
{
    [Header("存檔點設定 (User Setup)")]
    public Transform currentSavePoint; // 拖入你手動設置好的存檔點 Transform (可選)
    
    [Header("相機設定 (Camera Setup)")]
    // 當相機跟隨玩家時，你希望相機離玩家多遠？
    // (例如 3D：(0, 5, -10), 2D：(0, 0, -10f))
    public Vector3 cameraOffsetFromPlayer = new Vector3(0, 5, -10f);

    private bool _isRespawning = false; // 防止連續觸發的鎖
    private Rigidbody _playerRb;
    private Camera _mainCam;

    void Start()
    {
        _playerRb = GetComponent<Rigidbody>();
        _mainCam = Camera.main;

        if (_mainCam == null)
        {
            Debug.LogError("PlayerRespawnSystem: 場景中找不到 Tag 為 'MainCamera' 的攝影機，請檢查！");
        }
    }

    // ==========================================
    // 核心邏輯：回答你的關鍵問題
    // ==========================================
    // 這個 OnTriggerExit 函數，就是「脫離 CameraBounds 就讓 tag: Camera, Player 一起更新」的地方。
    // other 就是那個掛在相機下方的偵測框 (CameraBounds)
    private void OnTriggerExit(Collider other)
    {
        // 1. 檢查離開的是不是那個隱形的相機邊界偵測框
        // (請確保該框框 Tag 叫 "CameraBounds")
        if (other.CompareTag("CameraBounds") && !_isRespawning)
        {
            Debug.Log("偵測到脫離畫面！開始刷新程序...");
            StopAllCoroutines(); // 確保不會重複執行
            StartCoroutine(RespawnSequence());
        }
    }

    IEnumerator RespawnSequence()
    {
        _isRespawning = true;

        // 2. 確定瞬移目標位置 (這就是 SavePoint 的位置)
        // 優先使用面板拖入的 currentSavePoint，如果沒有，就用 Tag "SavePoint" 找最近的
        Vector3 spawnCenterPos;
        if (currentSavePoint != null)
        {
            spawnCenterPos = currentSavePoint.position;
        }
        else
        {
            GameObject sp = GameObject.FindGameObjectWithTag("SavePoint");
            if (sp != null)
            {
                spawnCenterPos = sp.transform.position;
            }
            else
            {
                Debug.LogError("場景中找不到任何 'SavePoint' 物件或 Transform，無法刷新！");
                _isRespawning = false;
                yield break;
            }
        }

        // --- 關鍵步驟 3：TELEPORT PLAYER ---
        // 將玩家移動到存檔點的正中央
        transform.position = spawnCenterPos;

        // 【超級關鍵】立即清除玩家的速度慣性，防止刷新後直接噴出去
        // 這裡使用的是 standard 物理寫法，適用所有版本
        if (_playerRb != null)
        {
            _playerRb.linearVelocity = Vector3.zero;
            _playerRb.angularVelocity = Vector3.zero;
        }

        // --- 關鍵步驟 4：TELEPORT CAMERA (這就是你要求的同步更新部分) ---
        // 讓攝影機瞬間「也瞬移」到存檔點上方，畫面才不會看到攝影機滑過去的拉扯畫面
        if (_mainCam != null)
        {
            // 將相機放在 SavePoint 中央，並且加上你設定好的 Offset 偏移量
            _mainCam.transform.position = spawnCenterPos + cameraOffsetFromPlayer;
            
            Debug.Log($"已將 Player 與 Camera 同步更新於 SavePoint: {spawnCenterPos}");
            
            // --- 註意 (如果你有使用任何相機平滑跟隨腳本) ---
            // 如果你原本就有寫一個 SimpleCameraFollow 腳本掛在相機上，
            // 比如 cam.pos = player.pos + offset; 那它在 LateUpdate 裡
            // 會自動幫你把相機定位。此步驟 4 只是確保畫面不會有「滑回去」的感覺。
        }

        // 緩衝一段時間，防止物理系統穩定前再次連續觸發
        yield return new WaitForSeconds(0.2f); 
        _isRespawning = false;
    }
}