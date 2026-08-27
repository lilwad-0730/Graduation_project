using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 繪本的聲音：翻頁音效 ＋ 依章節交叉淡化的 BGM。
/// 掛在跟 PageBook 同一個物件上就好，AudioSource 會自己建。
/// </summary>
[RequireComponent(typeof(PageBook))]
public class BookAudio : MonoBehaviour
{
    [System.Serializable]
    public class Chapter
    {
        public string name = "章節";
        [Tooltip("這一章從第幾頁開始（0 起算）")]
        public int startPage;
        public AudioClip bgm;
        [Tooltip("Bgm 留空時，從 Resources/Audio/ 用這個檔名載入")]
        public string bgmResource = "";
        [Range(0f, 1f)] public float volume = 0.7f;
    }

    [Header("章節音樂")]
    public Chapter[] chapters;
    public float crossfadeSeconds = 2.2f;

    [Header("翻頁音效")]
    public AudioClip[] pageTurnClips;
    [Range(0f, 1f)] public float sfxVolume = 0.55f;
    [Range(0f, 0.5f)] public float pitchJitter = 0.12f;

    [Header("巨石落地後的全靜音")]
    [Tooltip("翻到這一頁時，音樂整個消失 N 秒 —— 全片唯一一次")]
    public int silencePage = 26;          // 廢-6「街空了」
    public float silenceSeconds = 0.8f;

    PageBook book;
    AudioSource a, b, sfx;
    AudioSource cur, nxt;
    int curChapter = -1;
    float fade = 1f;                      // 1 = 完全在 cur 上
    float silenceUntil = -1f;
    float duckMul = 1f;

    void Awake()
    {
        book = GetComponent<PageBook>();
        LoadMissingClips();
        a = NewSource("BGM_A");
        b = NewSource("BGM_B");
        sfx = NewSource("SFX");
        sfx.loop = false;
        cur = a; nxt = b;
    }

    /// <summary>
    /// Inspector 沒拖檔案的話，自己去 Resources/Audio/ 撈。
    /// 跟 PageBook 撈頁面同一個道理 —— 不用手拖欄位。
    /// </summary>
    void LoadMissingClips()
    {
        if (chapters != null)
        {
            foreach (var c in chapters)
            {
                if (c.bgm == null && !string.IsNullOrEmpty(c.bgmResource))
                {
                    c.bgm = Resources.Load<AudioClip>("Audio/" + c.bgmResource);
                    if (c.bgm == null)
                        Debug.LogWarning("[BookAudio] 找不到 Resources/Audio/" + c.bgmResource);
                }
            }
        }

        if (pageTurnClips == null || pageTurnClips.Length == 0)
        {
            var found = Resources.LoadAll<AudioClip>("Audio");
            var list = new List<AudioClip>();
            foreach (var c in found)
                if (c != null && c.name.StartsWith("SFX_翻頁")) list.Add(c);
            list.Sort((x, y) => string.CompareOrdinal(x.name, y.name));
            pageTurnClips = list.ToArray();
            Debug.Log("[BookAudio] 自動載入 " + pageTurnClips.Length + " 個翻頁音效");
        }
    }

    AudioSource NewSource(string n)
    {
        var go = new GameObject(n);
        go.transform.SetParent(transform, false);
        var s = go.AddComponent<AudioSource>();
        s.playOnAwake = false;
        s.loop = true;
        s.spatialBlend = 0f;
        s.volume = 0f;
        return s;
    }

    void OnEnable()
    {
        book.OnPageChanged += HandlePage;
        book.OnTurnStarted += HandleTurn;
    }

    void OnDisable()
    {
        book.OnPageChanged -= HandlePage;
        book.OnTurnStarted -= HandleTurn;
    }

    void Start() { HandlePage(book.Index); }

    void HandleTurn(int dir)
    {
        if (pageTurnClips == null || pageTurnClips.Length == 0) return;
        var clip = pageTurnClips[Random.Range(0, pageTurnClips.Length)];
        if (clip == null) return;
        sfx.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
        sfx.PlayOneShot(clip, sfxVolume);
    }

    void HandlePage(int page)
    {
        if (page == silencePage && silenceSeconds > 0f)
            silenceUntil = Time.unscaledTime + silenceSeconds;

        int ch = ChapterOf(page);
        if (ch < 0 || ch == curChapter) return;

        var c = chapters[ch];
        if (c.bgm == null) { curChapter = ch; return; }

        // 換手：新的接到 nxt，淡過去
        nxt.clip = c.bgm;
        nxt.volume = 0f;
        nxt.time = 0f;
        nxt.Play();
        fade = 0f;
        curChapter = ch;
    }

    int ChapterOf(int page)
    {
        if (chapters == null || chapters.Length == 0) return -1;
        int best = -1;
        for (int i = 0; i < chapters.Length; i++)
            if (page >= chapters[i].startPage) best = i;
        return best;
    }

    void Update()
    {
        // 全靜音
        float wantDuck = (Time.unscaledTime < silenceUntil) ? 0f : 1f;
        duckMul = Mathf.MoveTowards(duckMul, wantDuck, Time.unscaledDeltaTime / 0.06f);

        if (fade < 1f)
        {
            fade = Mathf.MoveTowards(fade, 1f, Time.unscaledDeltaTime / Mathf.Max(0.05f, crossfadeSeconds));
            if (fade >= 1f)
            {
                cur.Stop();
                var tmp = cur; cur = nxt; nxt = tmp;
            }
        }

        float vol = (curChapter >= 0 && curChapter < chapters.Length) ? chapters[curChapter].volume : 0.7f;
        if (fade < 1f)
        {
            cur.volume = (1f - fade) * vol * duckMul;
            nxt.volume = fade * vol * duckMul;
        }
        else
        {
            cur.volume = vol * duckMul;
            nxt.volume = 0f;
        }
    }

    /// <summary>Inspector 沒填的話，用這一組預設章節分頁（對應 74 頁的順序）。</summary>
    [ContextMenu("填入預設章節分頁")]
    public void FillDefaultChapters()
    {
        chapters = new[]
        {
            new Chapter { name = "棉花堡", startPage = 0, bgmResource = "BGM_棉花堡_原型_loop", volume = 0.62f },
            new Chapter { name = "廢墟", startPage = 14, bgmResource = "BGM_廢墟_原型_loop", volume = 0.68f },
            new Chapter { name = "荒原", startPage = 30, bgmResource = "BGM_荒原_原型_loop", volume = 0.62f },
            new Chapter { name = "水下", startPage = 41, bgmResource = "BGM_水下_原型_loop", volume = 0.60f },
            new Chapter { name = "星空玻璃館", startPage = 56, bgmResource = "BGM_玻璃館_原型_loop", volume = 0.66f },
            new Chapter { name = "結局", startPage = 62, bgmResource = "BGM_結局_原型", volume = 0.58f },
        };
        Debug.Log("[BookAudio] 已填入 6 章，BGM 會自動從 Resources/Audio/ 載入。");
    }
}
