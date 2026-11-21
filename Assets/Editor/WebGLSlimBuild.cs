using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// 網頁版（WebGL / Unity Play）瘦身打包工具。只動「WebGL 專用」的貼圖設定，exe（Standalone）設定不動。
///
///  1. ApplySlim：把「長邊超過 1024、而且是書頭怪貼圖或邊長不是 4 的倍數（壓不了）」的貼圖，WebGL 上限設 1024。
///     改到的 .meta 路徑記在 Library/WebGLSlim_changed.txt。
///  2. 打包完成（WebGL）後，自動把專案裡的 mp4 複製進輸出資料夾 StreamingAssets（空白換底線），給 WebGLVideoFix 讀。
///  3. 還原：打包完用 git checkout 把記錄裡的 .meta 還原（不 commit、不推），exe 版與隊友都不受影響。
/// </summary>
public static class WebGLSlimBuild
{
    private const string ChangedList = "Library/WebGLSlim_changed.txt";

    public static string ApplySlim()
    {
        List<string> changed = new List<string>();
        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets" });
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (string g in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti == null) continue;
                int w, h; ti.GetSourceTextureWidthAndHeight(out w, out h);
                if (Mathf.Max(w, h) <= 1024) continue;
                bool nonMul4 = (w % 4 != 0) || (h % 4 != 0);
                bool bookHead = path.Contains("BookHeadMonster");
                if (!nonMul4 && !bookHead) continue;
                TextureImporterPlatformSettings s = ti.GetPlatformTextureSettings("WebGL");
                if (s.overridden && s.maxTextureSize <= 1024) continue;
                s.overridden = true;
                s.maxTextureSize = 1024;
                ti.SetPlatformTextureSettings(s);
                EditorUtility.SetDirty(ti);
                ti.SaveAndReimport();
                changed.Add(path + ".meta");
            }
        }
        finally { AssetDatabase.StopAssetEditing(); }
        File.WriteAllLines(ChangedList, changed.ToArray());
        return "改了 " + changed.Count + " 張貼圖的 WebGL 設定，清單：" + ChangedList;
    }

    public class CopyVideosAfterBuild : IPostprocessBuildWithReport
    {
        public int callbackOrder { get { return 0; } }
        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.WebGL) return;
            string dest = Path.Combine(report.summary.outputPath, "StreamingAssets");
            Directory.CreateDirectory(dest);
            foreach (string g in AssetDatabase.FindAssets("t:VideoClip", new[] { "Assets" }))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                string name = Path.GetFileNameWithoutExtension(p).Replace(' ', '_') + ".mp4";
                File.Copy(p, Path.Combine(dest, name), true);
                Debug.Log("【WebGL打包】複製影片 " + p + " → " + name);
            }
        }
    }
}
