using UnityEngine;
using System.Collections;

public enum BirdState { Patrol, Attack, PostAttack, Hidden, ConvergeCamera, DiveToGround }

[RequireComponent(typeof(Rigidbody))]
// 【重要修正】這裡的名字必須跟你的檔案名稱 (FlyingEnemy) 完全一樣
public class FlyingEnemy : MonoBehaviour 
{
    [Header("1. 基本設定")]
    public float attackSpeed = 8f;
    public float aggroRadius = 6f;
    public float maxChaseRange = 15f;
    public float knockbackForce = 20f;

    [Header("2. 盤旋軌跡設定")]
    public float patrolSpeed = 1f;
    public float patrolRadiusX = 3f;
    public float patrolRadiusZ = 2f;
    public float patrolRadiusY = 1.2f;
    private float randomOffset;
    private int patrolMode;
    private float patrolFreqMultiplier;

    [Header("3. 消失漸出設定")]
    public float fadeDuration = 1.5f;

    private Transform player;
    private Rigidbody rb;
    private Collider birdCollider;
    private Vector3 startPos;
    private Transform skyBackground;
    private Camera mainCam;

    public BirdState currentState = BirdState.Patrol;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        birdCollider = GetComponent<Collider>();
        startPos = transform.position;
        mainCam = Camera.main;

        rb.constraints = RigidbodyConstraints.FreezeRotation;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        randomOffset = Random.Range(0f, Mathf.PI * 2f);
        patrolMode = Random.Range(0, 3);
        patrolFreqMultiplier = Random.Range(0.7f, 1.4f);

        GameObject p = GameObject.FindGameObjectWithTag("Player");
        if (p != null) player = p.transform;

        GameObject bg = GameObject.FindGameObjectWithTag("Background");
        if (bg != null) skyBackground = bg.transform;

        SetVisible(true);
    }

    void Update()
    {
        if (player == null) return;

        float distToStartPos = Vector3.Distance(startPos, player.position);

        if (currentState == BirdState.Hidden)
        {
            if (distToStartPos <= aggroRadius)
                WakeUp();
            return;
        }

        if (currentState == BirdState.PostAttack) return;

        if (currentState == BirdState.Attack && distToStartPos > maxChaseRange)
        {
            HideToBackground();
            return;
        }

        if (currentState == BirdState.Patrol && distToStartPos <= aggroRadius)
            currentState = BirdState.Attack;

        switch (currentState)
        {
            case BirdState.Patrol:         PatrolBehavior();   break;
            case BirdState.Attack:         AttackBehavior();   break;
            case BirdState.ConvergeCamera: ConvergeToCamera(); break;
            case BirdState.DiveToGround:   DiveBehavior();     break;
        }
    }

    // ==========================================
    // 巡邏：三種隨機模式 + Y軸起伏
    // ==========================================
    void PatrolBehavior()
    {
        float t = Time.time * patrolSpeed * patrolFreqMultiplier + randomOffset;
        float x, z;

        switch (patrolMode)
        {
            case 0: // 標準橢圓
                x = startPos.x + Mathf.Cos(t) * patrolRadiusX;
                z = startPos.z + Mathf.Sin(t) * patrolRadiusZ;
                break;
            case 1: // 8字形
                x = startPos.x + Mathf.Sin(t * 2f) * patrolRadiusX;
                z = startPos.z + Mathf.Sin(t) * patrolRadiusZ;
                break;
            default: // 不規則疊加
                x = startPos.x + Mathf.Cos(t) * patrolRadiusX + Mathf.Cos(t * 1.7f) * (patrolRadiusX * 0.3f);
                z = startPos.z + Mathf.Sin(t) * patrolRadiusZ + Mathf.Sin(t * 2.3f) * (patrolRadiusZ * 0.3f);
                break;
        }

        float y = startPos.y
            + Mathf.Sin(t * 1.3f) * patrolRadiusY
            + Mathf.Sin(t * 0.7f + 1.5f) * (patrolRadiusY * 0.5f);

        rb.MovePosition(new Vector3(x, y, z));
    }

    void AttackBehavior()
    {
        Vector3 direction = (player.position - transform.position).normalized;
        rb.linearVelocity = direction * attackSpeed;
    }

    void ConvergeToCamera()
    {
        if (mainCam != null)
        {
            Vector3 camCenter = mainCam.transform.position + mainCam.transform.forward * 10f;
            Vector3 dir = (camCenter - transform.position).normalized;
            rb.linearVelocity = dir * (attackSpeed * 1.5f);
        }
    }

    void DiveBehavior()
    {
        rb.linearVelocity = Vector3.down * attackSpeed;
    }

    // ==========================================
    // 碰撞偵測
    // ==========================================
    private void OnCollisionEnter(Collision collision)
    {
        if (currentState == BirdState.Hidden || currentState == BirdState.PostAttack) return;

        if (collision.gameObject.CompareTag("Player") && currentState == BirdState.Attack)
            HandlePlayerHit(collision.gameObject);

        if (collision.gameObject.CompareTag("Ground") && currentState == BirdState.DiveToGround)
        {
            rb.linearVelocity = Vector3.zero;
            rb.isKinematic = true;
            StartCoroutine(FadeOutThenHide(fadeDuration));
        }
    }

    // 【重要修正】恢復 OnTriggerEnter，並徹底移除 EventObject 的檢查
    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("StopAttackObject"))
        {
            currentState = BirdState.DiveToGround;
            
            // 達成穿透：改為 Trigger 後，就會直接穿過玩家與地面，且不會觸發 OnCollisionEnter(不造成擊退)
            if (birdCollider != null)
            {
                birdCollider.isTrigger = true;
            }

            // 1秒後消失：直接呼叫原本寫好的協程
            StartCoroutine(WaitThenHide(1f));
        }
    }

    IEnumerator IgnorePlayerCollision()
    {
        GameObject p = GameObject.FindGameObjectWithTag("Player");
        if (p != null)
        {
            Collider playerCol = p.GetComponent<Collider>();
            if (playerCol != null)
            {
                Physics.IgnoreCollision(birdCollider, playerCol, true);
                yield return new WaitForSeconds(5f);
                if (playerCol != null)
                    Physics.IgnoreCollision(birdCollider, playerCol, false);
            }
        }
    }

    // ==========================================
    // 擊中玩家：觸發帶有轉場的直接重新刷新
    // ==========================================
    void HandlePlayerHit(GameObject playerObj)
    {
        currentState = BirdState.PostAttack;
        rb.linearVelocity = Vector3.zero; // 鳥撞到後停在原地

        Debug.Log("被鳥攻擊，觸發轉場並重新刷新場景！");
        
        PlayerRespawnSystem respawnSys = playerObj.GetComponent<PlayerRespawnSystem>();
        if (respawnSys != null)
        {
            // 特殊規則：如果是教學關卡的鳥，則強制傳送到教學重生點
            if (this.gameObject.name == "TutorialBirdEnemy")
            {
                GameObject tutorialPoint = GameObject.Find("TutorialRespawnPoint");
                if (tutorialPoint != null)
                {
                    respawnSys.TriggerRespawn(tutorialPoint.transform.position);
                }
                else
                {
                    Debug.LogWarning("找不到 TutorialRespawnPoint 物件，退回一般重生點");
                    respawnSys.TriggerRespawn();
                }
            }
            else
            {
                // 一般鳥：呼叫支援黑畫面與文字轉場的「無縫傳送重生」
                respawnSys.TriggerRespawn();
            }
            
            StartCoroutine(WaitThenHide(1f));
        }
        else
        {
            // 若沒有掛載重生系統，則作為備案直接重載場景（無轉場）
            UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
        }
    }

    IEnumerator WaitThenHide(float waitTime)
    {
        yield return new WaitForSeconds(waitTime);
        HideToBackground();
    }

    // ==========================================
    // 漸出協程：落地後慢慢消失
    // ==========================================
    IEnumerator FadeOutThenHide(float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            SetAlpha(Mathf.Lerp(1f, 0f, elapsed / duration));
            yield return null;
        }
        HideToBackground();
    }

    // ==========================================
    // URP 2D 通用透明度控制
    // ==========================================
    void SetAlpha(float alpha)
    {
        SpriteRenderer[] sprites = GetComponentsInChildren<SpriteRenderer>();
        foreach (SpriteRenderer sr in sprites)
        {
            Color c = sr.color;
            c.a = alpha;
            sr.color = c;
        }

        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        foreach (Renderer r in renderers)
        {
            if (r is SpriteRenderer) continue;
            foreach (Material mat in r.materials)
            {
                if (mat.HasProperty("_BaseColor"))
                {
                    Color c = mat.GetColor("_BaseColor");
                    c.a = alpha;
                    mat.SetColor("_BaseColor", c);
                }
                else if (mat.HasProperty("_Color"))
                {
                    Color c = mat.GetColor("_Color");
                    c.a = alpha;
                    mat.SetColor("_Color", c);
                }
            }
        }
    }

    void HideToBackground()
    {
        currentState = BirdState.Hidden;
        rb.linearVelocity = Vector3.zero;
        rb.isKinematic = false;

        if (birdCollider != null)
        {
            birdCollider.isTrigger = false;
        }

        SetVisible(false);

        if (skyBackground != null)
            transform.position = new Vector3(skyBackground.position.x, 15f, skyBackground.position.z);
    }

    void WakeUp()
    {
        transform.position = startPos;
        SetAlpha(1f);   
        SetVisible(true);
        currentState = BirdState.Patrol;
    }

    void SetVisible(bool isVisible)
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        foreach (Renderer r in renderers)
            r.enabled = isVisible;

        if (birdCollider != null)
            birdCollider.enabled = isVisible;
    }
}
