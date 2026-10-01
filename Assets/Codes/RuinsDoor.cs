using UnityEngine;

/// <summary>
/// 掛載於廢墟門上，與 Destructible 協同運作。
/// 當巨石碰撞到該門且速度達到閥值時，門會崩塌碎裂以供通過。
/// </summary>
[RequireComponent(typeof(Destructible))]
public class RuinsDoor : MonoBehaviour
{
    [Header("碰撞偵測設定")]
    [Tooltip("指定只能被此特定物件撞壞 (例如：把 rock-new 拖進來，主角或其他物體碰觸就絕對不會破壞門)。若為空則使用 Tag/名字判定。")]
    public GameObject specificDestructionObject;

    [Tooltip("可撞壞此門的物件 Tag。預設為 RollingRock。")]
    public string targetTag = "RollingRock";

    [Tooltip("撞擊門的最低速度，若速度太慢則不會撞開。可設為 0 以便任何微弱碰觸皆能撞開。")]
    public float minImpactSpeed = 0.1f;

    [Header("★1001 Air Wall 相容（只影響「什麼算撞到牆」，不改碎裂邏輯）")]
    [Tooltip("只有撞在『看得見的那段牆』上才算有效撞擊；在可見牆頂以上的那段（手動拉高出來的 Air Wall）\n" +
             "不參與撞擊判定，而且巨石在那段高度時會暫時忽略與牆的物理碰撞。\n" +
             "\n" +
             "為什麼需要：Air Wall 是把這面牆【自己的 BoxCollider】往上拉高做出來的，\n" +
             "拉高後 collider 頂端到了 Y −106.058，而巨石靜止時底部在 Y −108.515，\n" +
             "兩者在巨石還沒掉下來之前就已經互相穿插 2.46 公尺。於是拉桿一解鎖：\n" +
             "  ・PhysX 第一個物理幀就產生接觸 → minImpactSpeed（場景值 0）直接通過 → 牆當場碎裂\n" +
             "  ・而且分離方向是 +Y，巨石會被往上頂，根本不會往下掉\n" +
             "玩家看到的就是「拉下拉桿，牆先爆，巨石才掉下來」。\n" +
             "\n" +
             "開啟後行為回到原本的設計：巨石自由落體約 15.96 公尺 → 以約 17.7 m/s 砸到可見牆頂 → 碎裂。\n" +
             "關掉＝完全回到修改前的判定（含上述時間差）。")]
    public bool ignoreBoulderAboveVisibleWall = true;

    [Tooltip("可見牆頂的容許誤差 (公尺)。巨石底部低於『可見牆頂 + 這個值』才算碰到牆")]
    public float visibleWallTopTolerance = 0.3f;

    private Destructible destructible;
    private Collider _wallCol;
    private Collider _boulderCol;
    private float _visibleTopY;
    private bool _hasVisibleTop = false;
    private bool _ignoringBoulder = false;

    private void Start()
    {
        destructible = GetComponent<Destructible>();
        _wallCol = GetComponent<Collider>();

        // 可見牆頂＝Sprite 的實際上緣（不是被拉高的 collider 上緣）
        SpriteRenderer sr = GetComponent<SpriteRenderer>();
        if (sr == null) sr = GetComponentInChildren<SpriteRenderer>();
        if (sr != null && sr.sprite != null)
        {
            _visibleTopY = sr.bounds.max.y;
            _hasVisibleTop = true;
        }

        if (specificDestructionObject != null)
        {
            _boulderCol = specificDestructionObject.GetComponent<Collider>();
            if (_boulderCol == null) _boulderCol = specificDestructionObject.GetComponentInChildren<Collider>();
        }

        if (ignoreBoulderAboveVisibleWall && !_hasVisibleTop)
        {
            Debug.LogWarning($"[廢墟機關門] '{gameObject.name}' 找不到 SpriteRenderer，無法判斷可見牆頂，Air Wall 相容判定已自動停用。");
        }
    }

    /// <summary>
    /// 維持「巨石還在可見牆頂上方時，暫時不與牆發生物理碰撞」的狀態。
    /// 寫成每幀維持而不是只在 Start 設定一次，是為了讓死亡重生／場景重置自動復原——
    /// 重置時巨石會被 LeverSystem 放回原位（又變成穿插狀態），而 IResettable 的執行順序不保證，
    /// 這裡每幀比對一次 bounds 就不需要依賴順序。成本是一次 bounds 讀取與一次浮點比較，
    /// 而且只有狀態真的改變時才呼叫 Physics.IgnoreCollision。
    /// </summary>
    private void Update()
    {
        if (!ignoreBoulderAboveVisibleWall || !_hasVisibleTop) return;
        if (_wallCol == null || _boulderCol == null) return;

        // 牆已經碎了就把忽略解除（碎裂後 Destructible 會關掉所有 Collider，這裡只是不留殘留狀態）
        if (destructible != null && destructible.HasShattered)
        {
            if (_ignoringBoulder)
            {
                Physics.IgnoreCollision(_wallCol, _boulderCol, false);
                _ignoringBoulder = false;
            }
            return;
        }

        bool shouldIgnore = _boulderCol.bounds.min.y > _visibleTopY + visibleWallTopTolerance;
        if (shouldIgnore != _ignoringBoulder)
        {
            Physics.IgnoreCollision(_wallCol, _boulderCol, shouldIgnore);
            _ignoringBoulder = shouldIgnore;
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        CheckShatter(collision.gameObject, collision.relativeVelocity.magnitude);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        CheckShatter(collision.gameObject, collision.relativeVelocity.magnitude);
    }

    private void CheckShatter(GameObject hitObject, float relativeVelocityMagnitude)
    {
        bool isTarget = false;

        // 1. 如果有指定特定破壞物件，只認該物件 (進行名稱防呆比對，相容場景實例與專案預製體拖曳)
        if (specificDestructionObject != null)
        {
            string hitName = hitObject.name.Replace("(Clone)", "").Trim();
            string targetName = specificDestructionObject.name.Replace("(Clone)", "").Trim();

            if (hitObject == specificDestructionObject || 
                hitObject.transform.IsChildOf(specificDestructionObject.transform) ||
                hitName == targetName)
            {
                isTarget = true;
            }
        }
        else
        {
            // 2. 沒有指定特定物件時，才使用 Tag 與名稱匹配邏輯
            if (!string.IsNullOrEmpty(targetTag) && hitObject.CompareTag(targetTag))
            {
                isTarget = true;
            }
            else if (hitObject.GetComponent<RollingRockVisual>() != null || hitObject.name.ToLower().Contains("rock"))
            {
                isTarget = true;
            }
        }

        if (isTarget)
        {
            // ★1001 只有撞在「看得見的那段牆」上才算：擋掉被拉高出來的 Air Wall 區段造成的假撞擊
            if (ignoreBoulderAboveVisibleWall && _hasVisibleTop)
            {
                Collider hitCol = hitObject.GetComponent<Collider>();
                if (hitCol == null) hitCol = hitObject.GetComponentInChildren<Collider>();
                if (hitCol != null && hitCol.bounds.min.y > _visibleTopY + visibleWallTopTolerance)
                {
                    Debug.Log($"【廢墟機關門】'{hitObject.name}' 碰到的是可見牆頂以上的 Air Wall 區段 " +
                              $"(物件底部 {hitCol.bounds.min.y:F2} > 可見牆頂 {_visibleTopY:F2})，不算撞擊，等它真的砸到牆。");
                    return;
                }
            }

            // 嘗試取得 Rigidbody 來獲取真實速度，否則使用相對碰撞速度
            Rigidbody rb = hitObject.GetComponent<Rigidbody>();
            float speed = rb != null ? rb.linearVelocity.magnitude : relativeVelocityMagnitude;

            if (speed >= minImpactSpeed)
            {
                Debug.Log($"【廢墟機關門】偵測到巨石 '{hitObject.name}' 撞擊，撞擊速度：{speed:F2}。觸發門的碎裂崩塌！");
                destructible.Shatter();
            }
            else
            {
                Debug.Log($"【廢墟機關門】巨石 '{hitObject.name}' 碰撞速度過低 ({speed:F2} < {minImpactSpeed:F2})，未達崩塌閥值。");
            }
        }
    }
}
