#if UNITY_WEBGL && !UNITY_EDITOR
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Video;

/// <summary>
/// 網頁版（WebGL）不支援「內嵌」的影片（VideoClip），主選單背景會變黑。
/// 這支只在網頁版打包時存在：場景載入後，把場景裡來源是 VideoClip 的 VideoPlayer 改成讀網址（StreamingAssets/影片名.mp4，空白換成底線）。
/// 影片檔由 Assets/Editor/WebGLSlimBuild.cs 在網頁版打包後複製進輸出資料夾。exe 版與編輯器完全不含這段。
/// </summary>
public class WebGLVideoFix : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        GameObject go = new GameObject("[WebGL影片修正]");
        DontDestroyOnLoad(go);
        go.AddComponent<WebGLVideoFix>();
    }

    private void OnEnable() { SceneManager.sceneLoaded += OnLoaded; StartCoroutine(Fix(null)); }
    private void OnDisable() { SceneManager.sceneLoaded -= OnLoaded; }
    private void OnLoaded(Scene s, LoadSceneMode m) { StartCoroutine(Fix(s.name)); }

    private IEnumerator Fix(string scene)
    {
        // 場景剛載入時 VideoPlayer 可能已經開始播內嵌影片：等一幀，全部換成網址再播
        yield return null;
        foreach (VideoPlayer vp in FindObjectsByType<VideoPlayer>(FindObjectsInactive.Include))
        {
            if (vp == null || vp.source != VideoSource.VideoClip || vp.clip == null) continue;
            string file = vp.clip.name.Replace(' ', '_') + ".mp4";
            bool play = vp.isPlaying || vp.playOnAwake;
            vp.Stop();
            vp.source = VideoSource.Url;
            vp.url = Application.streamingAssetsPath + "/" + file;
            Debug.Log("【WebGL影片修正】" + vp.gameObject.name + " 改讀網址 " + vp.url);
            if (play) vp.Play();
        }
    }
}
#endif
