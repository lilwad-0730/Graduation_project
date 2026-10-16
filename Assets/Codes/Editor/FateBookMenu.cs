using System.Collections;
using UnityEditor;
using UnityEngine;

/// <summary>
/// ★1016 命運之書的測試選單（Play 模式）。選單：我你他／命運之書／…
///   檢查素材：Resources/FateBook/ruins 的 page、sketch、page_layout 在不在、尺寸對不對。
///   翻開廢墟頁（進場）／結束頁／全部上墨／清掉這一輪的墨：不用真的走到那裡。
///   第一次死亡（含 D 頁）：照 FirstWolfDeathStory 的測試走一次真的重生（會回到存檔點）。
/// </summary>
public static class FateBookMenu
{
    const string M = "我你他/命運之書/";

    [MenuItem(M + "檢查素材（Resources/FateBook/ruins）")]
    static void Check()
    {
        Texture2D page = Resources.Load<Texture2D>("FateBook/ruins/page");
        Texture2D sketch = Resources.Load<Texture2D>("FateBook/ruins/sketch");
        TextAsset layout = Resources.Load<TextAsset>("FateBook/ruins/page_layout");
        string msg = "page.png：" + (page != null ? page.width + "×" + page.height : "缺") +
                     "\nsketch.png：" + (sketch != null ? sketch.width + "×" + sketch.height : "缺（會用 page 代替）") +
                     "\npage_layout.json：" + (layout != null ? layout.text.Length + " 字" : "缺");
        if (page != null && page.width < 3000) msg += "\n\npage 不到 3840 寬：Project 視窗右鍵 page.png →「Reimport」（RespawnStoryImport 會設成原尺寸）";
        msg += "\n\n整包開關 FateBookState.Enabled＝" + FateBookState.Enabled;
        Debug.Log("[命運之書] " + msg.Replace("\n", "｜"));
        EditorUtility.DisplayDialog("命運之書・素材", msg, "好");
    }

    static FateBookRuins Ruins()
    {
        if (!Application.isPlaying) { Debug.Log("[命運之書] 要在 Play 模式下用"); return null; }
        FateBookRuins r = Object.FindAnyObjectByType<FateBookRuins>();
        if (r == null) Debug.Log("[命運之書] 這個場景沒有廢墟頁的觸發器（不是廢墟場景，或沒有素材）");
        return r;
    }

    [MenuItem(M + "測試：翻開廢墟頁（進場）")]
    static void Enter() { FateBookRuins r = Ruins(); if (r != null) r.TestEnter(); }

    [MenuItem(M + "測試：結束頁（推到 I、翻頁）")]
    static void End() { FateBookRuins r = Ruins(); if (r != null) r.TestEnd(); }

    [MenuItem(M + "測試：全部格子都上墨")]
    static void InkAll() { FateBookRuins r = Ruins(); if (r != null) r.TestInkAll(); }

    [MenuItem(M + "測試：第一次死亡（含 D 頁，走真的重生）")]
    static void FirstDeath()
    {
        if (!Application.isPlaying) { Debug.Log("[命運之書] 要在 Play 模式下用"); return; }
        FirstWolfDeathStory s = Object.FindAnyObjectByType<FirstWolfDeathStory>();
        if (s == null) { Debug.Log("[命運之書] 場景裡沒有 FirstWolfDeathStory（Player 身上，有狼的場景才會掛）"); return; }
        s.SendMessage("TestPlayNow", SendMessageOptions.DontRequireReceiver);
    }

    [MenuItem(M + "清掉這一輪的墨與刻痕")]
    static void Reset() { FateBookState.ResetRun(); Debug.Log("[命運之書] 已清掉"); }

    [MenuItem(M + "整包開關：切換 FateBookState.Enabled")]
    static void Toggle() { FateBookState.Enabled = !FateBookState.Enabled; Debug.Log("[命運之書] Enabled＝" + FateBookState.Enabled + "（只影響這次執行；要永久關就改 FateBookState.cs 的預設值）"); }
}
