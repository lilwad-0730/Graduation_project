using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ★1125 操作提示的動畫素材庫：讀 Resources/Prompts/index.json 與同名 PNG 逐格圖（Tools/make_prompt_frames.py 產生）。
///   PromptSheets.Get("stick_right") → 一組 Sprite（每格 192×192，每公尺 100 像素）＋每秒幾格。
///   名稱：stick_left/right/up/down、key_Shift/S/D/A/W/Space/E、btn_A/X/Y/B、plus（貼紙版，浮在物件上方用）
///         line_stick_*／line_light_stick_*（線稿版，背景線條畫的搖桿用；light＝淺色線）
/// 找不到就回傳 null，呼叫的人自己略過，不會報錯。
/// </summary>
public static class PromptSheets
{
    public const float PixelsPerUnit = 100f;

    [Serializable] private class Entry { public string name; public int frames; public int w; public int h; public float fps; }
    [Serializable] private class Index { public Entry[] sheets; }

    public class Sheet
    {
        public Sprite[] frames;
        public float fps;
        public float size;      // 一格的世界大小（公尺，縮放 1 時）
    }

    private static Dictionary<string, Entry> _index;
    private static readonly Dictionary<string, Sheet> _cache = new Dictionary<string, Sheet>();

    private static void LoadIndex()
    {
        if (_index != null) return;
        _index = new Dictionary<string, Entry>();
        TextAsset ta = Resources.Load<TextAsset>("Prompts/index");
        if (ta == null) { Debug.LogWarning("【操作提示】找不到 Resources/Prompts/index.json"); return; }
        try
        {
            Index idx = JsonUtility.FromJson<Index>(ta.text);
            if (idx != null && idx.sheets != null)
                foreach (Entry e in idx.sheets) _index[e.name] = e;
        }
        catch (Exception ex) { Debug.LogWarning("【操作提示】index.json 讀取失敗：" + ex.Message); }
    }

    public static Sheet Get(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        Sheet s;
        if (_cache.TryGetValue(name, out s)) return s;
        LoadIndex();
        Entry e;
        if (!_index.TryGetValue(name, out e)) { _cache[name] = null; return null; }
        Texture2D tex = Resources.Load<Texture2D>("Prompts/" + name);
        if (tex == null) { _cache[name] = null; return null; }
        s = new Sheet { frames = new Sprite[e.frames], fps = e.fps, size = e.w / PixelsPerUnit };
        for (int i = 0; i < e.frames; i++)
            s.frames[i] = Sprite.Create(tex, new Rect(i * e.w, 0, e.w, e.h), new Vector2(0.5f, 0.5f), PixelsPerUnit);
        _cache[name] = s;
        return s;
    }
}
