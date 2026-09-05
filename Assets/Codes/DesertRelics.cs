using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ★0905 荒原散落物（GDD §6-2「每一段路面放一件有人留下來的東西」＋ Q5-S3 手套 ＋ 門框＝章節句號）。
/// 美術把圖放到 Assets/Resources/Desert/Relics/ 底下（shoe.png／glove.png／cup.png／doorframe.png），
/// 進遊戲就會自動出現在對的位置；缺哪張就跳過哪張，Console 會列出來。不動場景檔。
///
/// 五條規矩（GDD）：屬於某個人／半埋進地裡（這裡埋 1/3）／單獨一件／顏色跟世界一樣、不打光不描邊／不解釋是誰的。
/// 「看得清楚」靠夠大、在動線上、輪廓可讀——所以放在地面上、主角會經過的 x，尺寸由 height 控制。
/// 紅線：伴侶手一律做成「一隻空手套」——沒有身體、沒有傷、沒有墳。
/// </summary>
[DisallowMultipleComponent]
public class DesertRelics : MonoBehaviour
{
    [System.Serializable]
    public class Relic
    {
        public string resource;                 // Resources/Desert/Relics/<resource>.png
        public float x;                         // 世界 x（atFirstFakeShelterLee 開著時忽略）
        public float height = 1.6f;             // 世界高度（整張圖）
        [Range(0f, 0.9f)] public float bury = 0.33f;   // 埋進地裡的比例
        public int sortingOrder = 2;            // 地面 1、掩體 2：跟掩體同層，比地面高
        public bool atFirstFakeShelterLee = false;     // 放在拍一之後第一座假掩體的背風面（手套）
        public float offsetFromShelter = -1.4f;        // 相對假掩體中心（負＝左＝背風面）
    }

    public string resourceFolder = "Desert/Relics/";
    public float zPosition = 0f;
    public List<Relic> relics = new List<Relic>
    {
        new Relic { resource = "shoe",      x = 30f,  height = 1.4f, bury = 0.30f, sortingOrder = 2 },
        new Relic { resource = "glove",     x = 62.5f, height = 1.5f, bury = 0.33f, sortingOrder = 2, atFirstFakeShelterLee = true, offsetFromShelter = -1.4f },
        new Relic { resource = "cup",       x = 160f, height = 1.2f, bury = 0.33f, sortingOrder = 2 },
        new Relic { resource = "doorframe", x = 226f, height = 6.0f, bury = 0.35f, sortingOrder = 2 },
    };

    private readonly List<GameObject> _spawned = new List<GameObject>();

    public static DesertRelics Install()
    {
        DesertRelics existing = FindFirstObjectByType<DesertRelics>();
        if (existing != null) return existing;
        GameObject go = new GameObject("DesertRelics (自動生成)");
        DesertRelics r = go.AddComponent<DesertRelics>();
        r.Build();
        return r;
    }

    public void Build()
    {
        foreach (GameObject g in _spawned) if (g != null) Destroy(g);
        _spawned.Clear();

        List<string> placed = new List<string>();
        List<string> missing = new List<string>();

        foreach (Relic r in relics)
        {
            if (r == null || string.IsNullOrEmpty(r.resource)) continue;

            Sprite sprite = LoadSprite(resourceFolder + r.resource);
            if (sprite == null) { missing.Add(r.resource); continue; }

            float x = r.x;
            if (r.atFirstFakeShelterLee && DesertBeatDirector.Instance != null)
            {
                WindShelter fake = DesertBeatDirector.Instance.FirstFakeShelter();
                if (fake != null) x = fake.transform.position.x + r.offsetFromShelter;
            }

            float groundY = DesertBeatDirector.Instance != null ? DesertBeatDirector.Instance.GroundYAt(x) : -6.3f;

            GameObject go = new GameObject("Relic_" + r.resource);
            go.transform.SetParent(transform, false);
            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = r.sortingOrder;
            sr.color = Color.white;   // 不打光、不描邊、不加光環：顏色交給圖本身

            // 圖的世界高度＝height；pivot 在圖的正中央，所以中心放在「地面 ＋ 露出部分的一半」
            float ppuHeight = sprite.bounds.size.y;   // 以目前 ppu 算出的世界高度
            float scale = ppuHeight > 0.0001f ? r.height / ppuHeight : 1f;
            go.transform.localScale = new Vector3(scale, scale, 1f);
            float visible = r.height * (1f - r.bury);
            go.transform.position = new Vector3(x, groundY + visible - r.height * 0.5f, zPosition);

            _spawned.Add(go);
            placed.Add(r.resource + "@" + x.ToString("F1"));
        }

        Debug.Log("[DesertRelics] 散落物已放 " + placed.Count + " 件" + (placed.Count > 0 ? "（" + string.Join("、", placed.ToArray()) + "）" : "")
                  + (missing.Count > 0 ? "；缺圖 " + string.Join("、", missing.ToArray()) + " → 放到 Assets/Resources/" + resourceFolder + "<名稱>.png 即出現" : ""));
    }

    private static Sprite LoadSprite(string path)
    {
        Sprite s = Resources.Load<Sprite>(path);
        if (s != null) return s;
        Texture2D t = Resources.Load<Texture2D>(path);
        if (t == null) return null;
        return Sprite.Create(t, new Rect(0f, 0f, t.width, t.height), new Vector2(0.5f, 0.5f), 100f);
    }
}
