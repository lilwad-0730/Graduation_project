using UnityEngine;
using System.Collections;

/// <summary>
/// 掛載於真掩體與假掩體上。
/// 核心機制：
/// 1. 【保留實體物理碰撞】：石柱/掩體自身保持實體物理碰撞體 (isTrigger = false)，主角可踩踏、跳躍、倚靠，保有真實世界物件質感。
/// 2. 【背風面防風保護區】：程式在石柱「背風面 (後面/左側)」生成防風保護範圍，迎風面 (前面/右側) 則不保護。
/// 3. 【高度可自訂】：Y 軸高度與垂直偏移量可在 Inspector 自由調整，Scene 視窗綠色 Gizmos 與實際代碼 100% 一致。
/// 4. 【假掩體吹風崩解】：假掩體在吹風時延遲碎裂，碎裂後實體碰撞與防風區域同步消失。
/// </summary>
public class WindShelter : MonoBehaviour, IResettable
{
    [Header("掩體屬性")]
    [Tooltip("是否為真掩體。若為 false，玩家在裡面吹風時掩體會崩解碎裂。")]
    public bool isTrueShelter = true;

    [Tooltip("【假掩體限定】開始崩解碎裂的延遲時間 (秒，預設 0.8)")]
    public float collapseDelay = 0.8f;

    [Tooltip("【假掩體限定】碎裂瞬間給玩家的逃跑免疫時間 (秒，預設 1.5)。\n碎裂跟失去掩體保護是同一幀，設 0 的話玩家會在石柱爆開的當下直接被石化、反應時間是 0。\n給一段時間讓她跑向下一座掩體；跑不到還是會被石化，懲罰照舊")]
    [Range(0f, 5f)]
    public float collapseEscapeGrace = 1.5f;

    [Header("★1101 碎前預兆（52 章 B1：假岩柱碎前 0.8 秒毫無預兆）")]
    [Tooltip("假掩體開始崩解到碎裂的這段時間（collapseDelay），柱子抖動、顏色變暗，讓玩家看得出它要碎了。\n她離開保護區倒數取消時會還原。關掉＝原本行為（倒數期間畫面毫無變化）")]
    public bool collapseWarning = true;
    [Tooltip("抖動幅度（公尺），越接近碎裂越大。只抖沒有碰撞框的純視覺子物件；渲染器跟碰撞框在同一個物件上時只變色不抖，不會推到她")]
    public float collapseWarningShake = 0.06f;
    [Tooltip("碎裂前顏色壓暗到幾成（1＝不變色）")]
    [Range(0.3f, 1f)] public float collapseWarningDarken = 0.72f;

    [Header("防風保護區域設定 (背風面躲避範圍)")]
    [Tooltip("【背風面/後面】保護延伸寬度 (公尺，預設 2.0，向左側/背風面延伸)")]
    public float protectBehindDistance = 2.0f;

    [Tooltip("【迎風面/前面】保護延伸寬度 (公尺，預設 0.0，不向右側/迎風面延伸，防止在前面被保護)")]
    public float protectFrontDistance = 0.0f;

    [Tooltip("【垂直保護高度】(公尺，預設 4.5，可直接在 Inspector 調整)")]
    public float protectHeight = 4.5f;

    [Tooltip("【垂直高度偏移】(公尺，預設 0.0，可上下微調保護框底部位置)")]
    public float offsetY = 0.0f;

    private bool isPlayerInside = false;
    private bool hasCollapsed = false;
    private Destructible destructible;
    private WindGustSystem windSystem;
    private Coroutine collapseCoroutine;
    private Transform playerTrans;
    private Collider[] shelterColliders;
    private Renderer[] shelterRenderers;

    // ★1101 碎前預兆用：純視覺子物件的原始位置、變色用的 PropertyBlock
    private Transform[] _warnShakeTargets;
    private Vector3[] _warnShakeBase;
    private bool _warnVisualOn = false;
    private static readonly int _ColorId = Shader.PropertyToID("_Color");
    private static readonly int _BaseColorId = Shader.PropertyToID("_BaseColor");
    private Color[] _warnBaseColors;

    private void Start()
    {
        // 獲取掩體實體碰撞體與渲染器 (保留原有的 isTrigger 設定，不破壞實體物理碰撞！)
        shelterColliders = GetComponentsInChildren<Collider>(true);
        shelterRenderers = GetComponentsInChildren<Renderer>(true);

        destructible = GetComponent<Destructible>();
        if (destructible == null) destructible = GetComponentInChildren<Destructible>();
        if (destructible == null) destructible = GetComponentInParent<Destructible>();

        windSystem = FindFirstObjectByType<WindGustSystem>();

        EnsurePlayer();

        if (!isTrueShelter && destructible == null)
        {
            Debug.LogWarning($"[WindShelter] '{gameObject.name}' 被設為假掩體，但找不到 Destructible 元件，將無法正常崩解！");
        }
    }

    private void EnsurePlayer()
    {
        if (playerTrans == null)
        {
            GameObject playerObj = GameObject.FindWithTag("Player");
            if (playerObj != null)
            {
                playerTrans = playerObj.transform;
            }
            else
            {
                PlayerMovement pm = FindFirstObjectByType<PlayerMovement>();
                if (pm != null) playerTrans = pm.transform;
            }
        }
    }

    private void Update()
    {
        EnsurePlayer();

        // 核心運作：即時判定主角是否處於石柱背風面的防風保護範圍內
        if (playerTrans != null && !hasCollapsed && gameObject.activeInHierarchy)
        {
            bool inBounds = CheckPlayerInShelterZone(playerTrans.position);
            if (inBounds && !isPlayerInside)
            {
                SetPlayerInside(true);
            }
            else if (!inBounds && isPlayerInside)
            {
                SetPlayerInside(false);
            }
        }

        // 假掩體崩解邏輯 (若正在重生轉場或破曉緩衝中，絕對不執行崩解倒數)
        if (isTrueShelter || hasCollapsed || !isPlayerInside || windSystem == null || PlayerRespawnSystem.IsAnyRespawning) return;

        // 如果正值吹風狀態，且未啟動崩解協程，立即開始倒數
        if (windSystem.CurrentState == WindState.Blowing && collapseCoroutine == null)
        {
            collapseCoroutine = StartCoroutine(CollapseSequence());
        }
    }

    /// <summary>
    /// 計算保護區的精確世界座標邊界 (minX, maxX, minY, maxY)
    /// </summary>
    private void GetShelterZoneBounds(out float minX, out float maxX, out float minY, out float maxY)
    {
        float baseY = transform.position.y;
        float pillarLeft = transform.position.x - 0.5f;
        float pillarRight = transform.position.x + 0.5f;

        if (shelterColliders != null && shelterColliders.Length > 0 && shelterColliders[0] != null)
        {
            Bounds b = shelterColliders[0].bounds;
            pillarLeft = b.min.x;
            pillarRight = b.max.x;
            baseY = b.min.y;
        }
        else if (shelterRenderers != null && shelterRenderers.Length > 0 && shelterRenderers[0] != null)
        {
            Bounds b = shelterRenderers[0].bounds;
            pillarLeft = b.min.x;
            pillarRight = b.max.x;
            baseY = b.min.y;
        }

        // 逆風由右向左吹：
        // 左側 (後面/背風面) = pillarLeft - protectBehindDistance
        // 右側 (前面/迎風面) = pillarRight + protectFrontDistance
        minX = pillarLeft - protectBehindDistance;
        maxX = pillarRight + protectFrontDistance;

        // Y 軸精確高度控制
        minY = baseY + offsetY;
        maxY = baseY + offsetY + protectHeight;
    }

    /// <summary>
    /// 計算 2D 空間中玩家是否位於掩體的保護範圍內
    /// </summary>
    private bool CheckPlayerInShelterZone(Vector3 playerPos)
    {
        GetShelterZoneBounds(out float minX, out float maxX, out float minY, out float maxY);
        return (playerPos.x >= minX && playerPos.x <= maxX && playerPos.y >= minY && playerPos.y <= maxY);
    }

    private void SetPlayerInside(bool inside)
    {
        if (isPlayerInside == inside) return;

        isPlayerInside = inside;
        if (inside)
        {
            if (!hasCollapsed)
            {
                WindGustSystem.RegisterPlayerShelter();
                Debug.Log($"🛡️【掩體保護】玩家進入掩體 '{gameObject.name}' 背風安全區域。(isTrueShelter: {isTrueShelter})");
            }
        }
        else
        {
            if (!hasCollapsed)
            {
                WindGustSystem.UnregisterPlayerShelter();
                Debug.Log($"💨【掩體離開】玩家離開掩體 '{gameObject.name}'。");
            }

            if (!isTrueShelter && collapseCoroutine != null)
            {
                StopCoroutine(collapseCoroutine);
                collapseCoroutine = null;
                RestoreWarningVisual();   // ★1101 她跑開、倒數取消：柱子回到原樣
            }
        }
    }

    private IEnumerator CollapseSequence()
    {
        Debug.LogWarning($"💥【假掩體警報】'{gameObject.name}' 開始崩解！於 {collapseDelay} 秒後碎裂！");
        if (collapseWarning && collapseDelay > 0.01f)
        {
            // ★1101 碎前預兆：倒數期間柱子越抖越厲害、越來越暗（原本這 0.8 秒畫面毫無變化，她沒有資訊可以跑）
            BeginWarningVisual();
            float t = 0f;
            while (t < collapseDelay)
            {
                t += Time.deltaTime;
                UpdateWarningVisual(Mathf.Clamp01(t / collapseDelay));
                yield return null;
            }
            RestoreWarningVisual();   // 碎片效果照原本的樣子出
        }
        else
        {
            yield return new WaitForSeconds(collapseDelay);
        }

        hasCollapsed = true;

        // 1. 優先觸發 2D 碎石爆破散落效果！
        if (destructible != null)
        {
            destructible.Shatter();
        }
        else
        {
            gameObject.SetActive(false);
        }

        // 2. 碎石爆開的同時，掩體保護與物理實體同步宣告失效！
        if (isPlayerInside)
        {
            WindGustSystem.UnregisterPlayerShelter();

            // 3. ★碎裂跟失去保護是同一幀，中間那 0.8 秒她還在保護中、畫面上也沒有任何預兆
            //    （Destructible.Shatter() 是被呼叫的當下直接爆，沒有碎裂前的搖晃）。
            //    不補這段免疫的話，玩家的體驗是「躲得好好的 → 石柱突然爆炸 → 自己同時變石頭」，
            //    她沒有任何資訊可以做出不同的選擇。給時間讓她跑，跑不掉還是會石化。
            GrantEscapeGrace();
        }
        collapseCoroutine = null;
    }

    // ══════════════════════════════════════════
    // ★1101 碎前預兆（抖動＋壓暗）
    // ══════════════════════════════════════════
    private void BeginWarningVisual()
    {
        if (_warnVisualOn) return;
        if (shelterRenderers == null || shelterRenderers.Length == 0) shelterRenderers = GetComponentsInChildren<Renderer>(true);

        // 只抖「沒有碰撞框」的純視覺子物件（抖到碰撞框會推她）；根物件本身不抖
        var list = new System.Collections.Generic.List<Transform>();
        for (int i = 0; i < shelterRenderers.Length; i++)
        {
            Renderer r = shelterRenderers[i];
            if (r == null) continue;
            Transform tr = r.transform;
            if (tr == transform) continue;
            if (tr.GetComponent<Collider>() != null) continue;
            if (!list.Contains(tr)) list.Add(tr);
        }
        _warnShakeTargets = list.ToArray();
        _warnShakeBase = new Vector3[_warnShakeTargets.Length];
        for (int i = 0; i < _warnShakeTargets.Length; i++) _warnShakeBase[i] = _warnShakeTargets[i].localPosition;

        _warnBaseColors = new Color[shelterRenderers.Length];
        for (int i = 0; i < shelterRenderers.Length; i++)
        {
            Renderer r = shelterRenderers[i];
            if (r == null) continue;
            SpriteRenderer sr = r as SpriteRenderer;
            if (sr != null) _warnBaseColors[i] = sr.color;
            else
            {
                MaterialPropertyBlock mpb = new MaterialPropertyBlock();
                r.GetPropertyBlock(mpb);
                Color c = Color.white;
                if (r.sharedMaterial != null)
                {
                    if (r.sharedMaterial.HasProperty(_BaseColorId)) c = r.sharedMaterial.GetColor(_BaseColorId);
                    else if (r.sharedMaterial.HasProperty(_ColorId)) c = r.sharedMaterial.GetColor(_ColorId);
                }
                _warnBaseColors[i] = c;
            }
        }
        _warnVisualOn = true;
    }

    private void UpdateWarningVisual(float u)
    {
        if (!_warnVisualOn) return;
        // 抖：幅度隨倒數加大（0.3 → 1 倍），每幀換方向
        float amp = collapseWarningShake * Mathf.Lerp(0.3f, 1f, u);
        if (_warnShakeTargets != null)
        {
            for (int i = 0; i < _warnShakeTargets.Length; i++)
            {
                if (_warnShakeTargets[i] == null) continue;
                Vector2 o = Random.insideUnitCircle * amp;
                _warnShakeTargets[i].localPosition = _warnShakeBase[i] + new Vector3(o.x, o.y * 0.5f, 0f);
            }
        }
        // 暗：線性壓到 collapseWarningDarken，最後 0.15 秒再閃一下亮回來（像裂開前的最後一下）
        float dark = Mathf.Lerp(1f, collapseWarningDarken, u);
        if (u > 0.85f) dark = Mathf.Lerp(dark, 1f, (u - 0.85f) / 0.15f * 0.6f);
        ApplyWarningTint(dark);
    }

    private void ApplyWarningTint(float mul)
    {
        if (shelterRenderers == null || _warnBaseColors == null) return;
        for (int i = 0; i < shelterRenderers.Length && i < _warnBaseColors.Length; i++)
        {
            Renderer r = shelterRenderers[i];
            if (r == null) continue;
            Color b = _warnBaseColors[i];
            Color c = new Color(b.r * mul, b.g * mul, b.b * mul, b.a);
            SpriteRenderer sr = r as SpriteRenderer;
            if (sr != null) { sr.color = c; continue; }
            MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            if (mul >= 0.999f) { mpb.Clear(); r.SetPropertyBlock(mpb); continue; }
            mpb.SetColor(_ColorId, c);
            mpb.SetColor(_BaseColorId, c);
            r.SetPropertyBlock(mpb);
        }
    }

    private void RestoreWarningVisual()
    {
        if (!_warnVisualOn) return;
        if (_warnShakeTargets != null)
        {
            for (int i = 0; i < _warnShakeTargets.Length; i++)
                if (_warnShakeTargets[i] != null) _warnShakeTargets[i].localPosition = _warnShakeBase[i];
        }
        ApplyWarningTint(1f);
        _warnVisualOn = false;
    }

    /// <summary>假掩體碎在玩家頭上時，補一段免疫讓她跑得掉。</summary>
    private void GrantEscapeGrace()
    {
        if (collapseEscapeGrace <= 0f) return;

        EnsurePlayer();
        if (playerTrans == null) return;

        PlayerPetrification petr = playerTrans.GetComponent<PlayerPetrification>();
        if (petr == null) petr = playerTrans.GetComponentInParent<PlayerPetrification>();
        if (petr == null) petr = playerTrans.GetComponentInChildren<PlayerPetrification>();
        if (petr == null) return;

        petr.GrantGrace(collapseEscapeGrace);
        Debug.Log($"🏃【假掩體】'{gameObject.name}' 碎了，給玩家 {collapseEscapeGrace} 秒逃跑免疫，快找下一座掩體。");
    }

    // 在 Scene 視窗繪製綠色防風保護範圍，便於關卡編輯檢視 (與代碼計算 100% 嚴格一致)
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = isTrueShelter ? new Color(0f, 1f, 0.2f, 0.4f) : new Color(1f, 0.8f, 0f, 0.4f);
        GetShelterZoneBounds(out float minX, out float maxX, out float minY, out float maxY);

        Vector3 center = new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, transform.position.z);
        Vector3 size = new Vector3(maxX - minX, maxY - minY, 2.0f);

        Gizmos.DrawWireCube(center, size);
    }

    // --- IResettable 實作 ---
    public void ResetToInitialState()
    {
        if (collapseCoroutine != null)
        {
            StopCoroutine(collapseCoroutine);
            collapseCoroutine = null;
        }
        RestoreWarningVisual();   // ★1101
        if (isPlayerInside && !hasCollapsed)
        {
            WindGustSystem.UnregisterPlayerShelter();
        }
        isPlayerInside = false;
        hasCollapsed = false;

        // 重新喚醒與刷新可能崩解的假掩體
        gameObject.SetActive(true);
        if (destructible != null)
        {
            destructible.ResetToInitialState();
        }
        else
        {
            SpriteRenderer[] allSrs = GetComponentsInChildren<SpriteRenderer>(true);
            foreach (var sr in allSrs) if (sr != null) sr.enabled = true;

            Collider[] allCols = GetComponentsInChildren<Collider>(true);
            foreach (var col in allCols) if (col != null) col.enabled = true;
        }
    }
}
