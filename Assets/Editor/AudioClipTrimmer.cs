using UnityEngine;
using UnityEditor;
using UnityEngine.Timeline;
using System.IO;
using System.Collections.Generic;

public class AudioClipTrimmer : EditorWindow
{
    private TimelineAsset targetTimeline;
    private string outputFileName = "Timeline_MixedAudio";

    // 單曲手動裁切備用
    private AudioClip singleClip;
    private float singleStartTime = 0f;
    private float singleEndTime = 1f;

    [MenuItem("Tools/🎵 音效裁切與 Timeline 軌道混音匯出工具")]
    public static void ShowWindow()
    {
        GetWindow<AudioClipTrimmer>("Timeline 音效匯出工具");
    }

    private void OnEnable()
    {
        FindActiveTimeline();
    }

    private void FindActiveTimeline()
    {
        // 1. 先找選取的物件
        if (Selection.activeObject is TimelineAsset selectedTimeline)
        {
            targetTimeline = selectedTimeline;
            return;
        }

        // 2. 搜尋場景或專案中的 TimelineAsset
        string[] guids = AssetDatabase.FindAssets("t:TimelineAsset");
        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.Contains("AudioCutter"))
            {
                targetTimeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(path);
                return;
            }
        }

        if (guids.Length > 0)
        {
            targetTimeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }
    }

    private void OnGUI()
    {
        GUILayout.Label("🎵 Unity Timeline 完整音軌混音與匯出工具", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("支援多片段拼接、剪接裁切、交叉淡入淡出 (Crossfade) 與音量混音，直接匯出成完整 .wav 檔！", MessageType.Info);
        EditorGUILayout.Space();

        // ==========================================
        // 模式 1：Timeline 完整軌道烘焙匯出 (支援多段拼接與交叉淡入淡出)
        // ==========================================
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        GUILayout.Label("🎬 【方案 1：Timeline 完整混音匯出】(多片段/交叉淡化/剪接)", EditorStyles.boldLabel);
        EditorGUILayout.Space(2);

        targetTimeline = (TimelineAsset)EditorGUILayout.ObjectField("目標 Timeline", targetTimeline, typeof(TimelineAsset), false);

        if (targetTimeline != null)
        {
            // 掃描 Timeline 上的音訊片段資訊
            List<TimelineClip> audioClips = GetAudioClips(targetTimeline);
            double totalDuration = targetTimeline.duration;

            EditorGUILayout.LabelField("音訊片段總數", $"{audioClips.Count} 個片段");
            EditorGUILayout.LabelField("Timeline 總時長", $"{totalDuration:F2} 秒");

            if (audioClips.Count > 0)
            {
                EditorGUILayout.Space(2);
                GUILayout.Label("📝 軌道片段清單：", EditorStyles.miniBoldLabel);
                foreach (var clip in audioClips)
                {
                    if (clip.asset is AudioPlayableAsset apa && apa.clip != null)
                    {
                        string blendInfo = "";
                        if (clip.blendInDuration > 0 || clip.blendOutDuration > 0)
                        {
                            blendInfo = $" [淡入: {clip.blendInDuration:F2}s, 淡出: {clip.blendOutDuration:F2}s]";
                        }
                        EditorGUILayout.LabelField($"• {apa.clip.name}", $"位置: {clip.start:F2}s ~ {(clip.start + clip.duration):F2}s (長度 {clip.duration:F2}s){blendInfo}");
                    }
                }

                EditorGUILayout.Space(4);
                outputFileName = EditorGUILayout.TextField("匯出檔名", outputFileName);

                EditorGUILayout.Space(4);
                GUI.backgroundColor = new Color(0.25f, 0.85f, 0.35f);
                if (GUILayout.Button("🚀 立即烘焙匯出完整 Timeline 音效 (.wav) 到 Assets", GUILayout.Height(42)))
                {
                    BakeAndExportTimeline(targetTimeline, audioClips, totalDuration);
                }
                GUI.backgroundColor = Color.white;
            }
            else
            {
                EditorGUILayout.HelpBox("該 Timeline 的 Audio Track 上目前沒有音訊片段，請在 Timeline 視窗中放入音效！", MessageType.Warning);
            }
        }
        else
        {
            EditorGUILayout.HelpBox("請將包含音效片段的 Timeline 資源拖入上方，或點擊下方按鈕搜尋！", MessageType.Warning);
            if (GUILayout.Button("🔍 自動尋找專案中的 Timeline"))
            {
                FindActiveTimeline();
            }
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(10);

        // ==========================================
        // 模式 2：單一音效手動秒數裁切
        // ==========================================
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        GUILayout.Label("✂️ 【方案 2：單一音效快速裁切】", EditorStyles.boldLabel);
        singleClip = (AudioClip)EditorGUILayout.ObjectField("原始音效", singleClip, typeof(AudioClip), false);
        if (singleClip != null)
        {
            singleStartTime = EditorGUILayout.FloatField("起點時間 (秒)", Mathf.Clamp(singleStartTime, 0f, singleClip.length));
            singleEndTime = EditorGUILayout.FloatField("終點時間 (秒)", Mathf.Clamp(singleEndTime, singleStartTime + 0.05f, singleClip.length));
            EditorGUILayout.LabelField("裁切後長度", $"{(singleEndTime - singleStartTime):F2} 秒");

            if (GUILayout.Button("✂️ 匯出單曲裁切 (.wav)"))
            {
                TrimSingleClip(singleClip, singleStartTime, singleEndTime);
            }
        }
        EditorGUILayout.EndVertical();
    }

    private List<TimelineClip> GetAudioClips(TimelineAsset timeline)
    {
        List<TimelineClip> clips = new List<TimelineClip>();
        if (timeline == null) return clips;

        foreach (var track in timeline.GetOutputTracks())
        {
            if (track is AudioTrack audioTrack)
            {
                foreach (var clip in audioTrack.GetClips())
                {
                    if (clip.asset is AudioPlayableAsset apa && apa.clip != null)
                    {
                        clips.Add(clip);
                    }
                }
            }
        }
        return clips;
    }

    private void BakeAndExportTimeline(TimelineAsset timeline, List<TimelineClip> clips, double totalDuration)
    {
        if (clips.Count == 0) return;

        // 取樣率與聲道以第一個 AudioClip 為基準
        AudioClip firstClip = null;
        foreach (var c in clips)
        {
            if (c.asset is AudioPlayableAsset apa && apa.clip != null)
            {
                firstClip = apa.clip;
                break;
            }
        }
        if (firstClip == null) return;

        int frequency = firstClip.frequency;
        int channels = firstClip.channels;

        // 計算最大結束時間
        double maxEndTime = totalDuration;
        foreach (var c in clips)
        {
            if (c.start + c.duration > maxEndTime) maxEndTime = c.start + c.duration;
        }

        int totalSampleFrames = Mathf.CeilToInt((float)maxEndTime * frequency);
        int totalSamples = totalSampleFrames * channels;
        float[] masterBuffer = new float[totalSamples];

        // 逐一片段混音並處理交叉淡入淡出 (Crossfade)
        foreach (var clip in clips)
        {
            if (!(clip.asset is AudioPlayableAsset apa) || apa.clip == null) continue;

            AudioClip src = apa.clip;
            float[] srcData = new float[src.samples * src.channels];
            src.GetData(srcData, 0);

            int clipStartSample = Mathf.FloorToInt((float)clip.start * frequency);
            int clipInOffsetSample = Mathf.FloorToInt((float)clip.clipIn * src.frequency);
            int clipDurationSamples = Mathf.FloorToInt((float)clip.duration * frequency);

            float blendInSec = (float)clip.blendInDuration;
            float blendOutSec = (float)clip.blendOutDuration;
            float clipDurSec = (float)clip.duration;

            for (int i = 0; i < clipDurationSamples; i++)
            {
                int targetMasterIdx = (clipStartSample + i) * channels;
                if (targetMasterIdx + (channels - 1) >= totalSamples) break;

                int sourceSampleIdx = (clipInOffsetSample + i) * src.channels;
                if (sourceSampleIdx + (channels - 1) >= srcData.Length) break;

                // 計算淡入淡出權重 (Weight)
                float currentSec = (float)i / frequency;
                float weight = 1f;

                // 淡入 (Blend In)
                if (blendInSec > 0.001f && currentSec < blendInSec)
                {
                    weight *= Mathf.Clamp01(currentSec / blendInSec);
                }

                // 淡出 (Blend Out)
                float remainingSec = clipDurSec - currentSec;
                if (blendOutSec > 0.001f && remainingSec < blendOutSec)
                {
                    weight *= Mathf.Clamp01(remainingSec / blendOutSec);
                }

                // 混音至主聲軌 (混音疊加)
                for (int ch = 0; ch < channels; ch++)
                {
                    float sampleVal = (ch < src.channels) ? srcData[sourceSampleIdx + ch] : srcData[sourceSampleIdx];
                    masterBuffer[targetMasterIdx + ch] += sampleVal * weight;
                }
            }
        }

        // 限制振幅防止爆音破音 (Clamp to -1.0 ~ 1.0)
        for (int i = 0; i < masterBuffer.Length; i++)
        {
            masterBuffer[i] = Mathf.Clamp(masterBuffer[i], -1f, 1f);
        }

        // 儲存檔案
        string timelinePath = AssetDatabase.GetAssetPath(timeline);
        string folder = string.IsNullOrEmpty(timelinePath) ? "Assets" : Path.GetDirectoryName(timelinePath);
        string savePath = Path.Combine(folder, outputFileName + ".wav");

        SaveWavFile(savePath, masterBuffer, frequency, channels);
        AssetDatabase.Refresh();

        AudioClip newAsset = AssetDatabase.LoadAssetAtPath<AudioClip>(savePath);
        if (newAsset != null)
        {
            Selection.activeObject = newAsset;
            EditorGUIUtility.PingObject(newAsset);
            EditorUtility.DisplayDialog("混音匯出成功！🎉", $"已成功將 Timeline 上的所有剪輯與交叉淡化音軌烘焙為：\n{savePath}\n\n已為您在 Project 視窗高亮選取！", "太棒了");
        }
    }

    private void TrimSingleClip(AudioClip src, float startSec, float endSec)
    {
        int frequency = src.frequency;
        int channels = src.channels;
        int startSample = Mathf.FloorToInt(startSec * frequency) * channels;
        int endSample = Mathf.FloorToInt(endSec * frequency) * channels;
        int sampleCount = endSample - startSample;

        float[] allData = new float[src.samples * channels];
        src.GetData(allData, 0);

        float[] trimmedData = new float[sampleCount];
        System.Array.Copy(allData, startSample, trimmedData, 0, sampleCount);

        string originalPath = AssetDatabase.GetAssetPath(src);
        string folder = string.IsNullOrEmpty(originalPath) ? "Assets" : Path.GetDirectoryName(originalPath);
        string savePath = Path.Combine(folder, src.name + "_Trimmed.wav");

        SaveWavFile(savePath, trimmedData, frequency, channels);
        AssetDatabase.Refresh();
    }

    public static void SaveWavFile(string filePath, float[] samples, int frequency, int channels)
    {
        using (FileStream fs = CreateEmpty(filePath))
        {
            byte[] bytesData = ConvertTo16BitByteArray(samples);
            WriteHeader(fs, samples.Length * 2, frequency, channels);
            fs.Write(bytesData, 0, bytesData.Length);
        }
    }

    private static FileStream CreateEmpty(string filepath)
    {
        var fileStream = new FileStream(filepath, FileMode.Create);
        byte emptyByte = new byte();
        for (int i = 0; i < 44; i++)
        {
            fileStream.WriteByte(emptyByte);
        }
        return fileStream;
    }

    private static byte[] ConvertTo16BitByteArray(float[] samples)
    {
        byte[] bytesData = new byte[samples.Length * 2];
        int rescaleFactor = 32767;

        for (int i = 0; i < samples.Length; i++)
        {
            short val = (short)(Mathf.Clamp(samples[i], -1f, 1f) * rescaleFactor);
            byte[] byteArr = System.BitConverter.GetBytes(val);
            bytesData[i * 2] = byteArr[0];
            bytesData[i * 2 + 1] = byteArr[1];
        }
        return bytesData;
    }

    private static void WriteHeader(FileStream stream, int byteCount, int frequency, int channels)
    {
        stream.Seek(0, SeekOrigin.Begin);
        byte[] riff = System.Text.Encoding.UTF8.GetBytes("RIFF");
        stream.Write(riff, 0, 4);

        byte[] chunkSize = System.BitConverter.GetBytes(byteCount + 36);
        stream.Write(chunkSize, 0, 4);

        byte[] wave = System.Text.Encoding.UTF8.GetBytes("WAVE");
        stream.Write(wave, 0, 4);

        byte[] fmt = System.Text.Encoding.UTF8.GetBytes("fmt ");
        stream.Write(fmt, 0, 4);

        byte[] subChunk1 = System.BitConverter.GetBytes(16);
        stream.Write(subChunk1, 0, 4);

        ushort audioFormat = 1; // PCM
        byte[] format = System.BitConverter.GetBytes(audioFormat);
        stream.Write(format, 0, 2);

        byte[] numChannels = System.BitConverter.GetBytes((ushort)channels);
        stream.Write(numChannels, 0, 2);

        byte[] sampleRate = System.BitConverter.GetBytes(frequency);
        stream.Write(sampleRate, 0, 4);

        byte[] byteRate = System.BitConverter.GetBytes(frequency * channels * 2);
        stream.Write(byteRate, 0, 4);

        ushort blockAlign = (ushort)(channels * 2);
        stream.Write(System.BitConverter.GetBytes(blockAlign), 0, 2);

        ushort bps = 16;
        stream.Write(System.BitConverter.GetBytes(bps), 0, 2);

        byte[] dataString = System.Text.Encoding.UTF8.GetBytes("data");
        stream.Write(dataString, 0, 4);

        byte[] subChunk2 = System.BitConverter.GetBytes(byteCount);
        stream.Write(subChunk2, 0, 4);
    }
}
