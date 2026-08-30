using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class SetupUnderwaterNurseryItemsTool
{
    [MenuItem("Tools/自動配置水下育兒物品音效與機制 (Setup Nursery Items)")]
    public static void SetupNurseryItemsInScene()
    {
        AudioClip musicBoxClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Music/水下/水下_育兒物品_音樂盒.wav");
        AudioClip babyBottleClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Music/水下/水下_育兒物品_奶瓶.wav");
        AudioClip babyBellClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Music/水下/水下_育兒物品_搖鈴.wav");
        AudioClip contactClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Music/水下/水下_物件接觸_01.wav");

        Transform[] allTransforms = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None);
        int setupCount = 0;

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Setup Underwater Nursery Items");
        int undoGroup = Undo.GetCurrentGroup();

        foreach (var t in allTransforms)
        {
            if (t == null) continue;
            string name = t.name.ToLower();

            AudioClip matchedClip = null;
            if (name.Contains("music_box") || name.Contains("musicbox") || name.Contains("音樂盒"))
            {
                matchedClip = musicBoxClip;
            }
            else if (name.Contains("baby_bottle") || name.Contains("babybottle") || name.Contains("bottle") || name.Contains("奶瓶"))
            {
                matchedClip = babyBottleClip;
            }
            else if (name.Contains("baby_bell") || name.Contains("babybell") || name.Contains("bell") || name.Contains("搖鈴"))
            {
                matchedClip = babyBellClip;
            }

            if (matchedClip != null)
            {
                Undo.RegisterCompleteObjectUndo(t.gameObject, "Setup Nursery Item");

                SphereCollider oldSc = t.GetComponent<SphereCollider>();
                if (oldSc != null) Undo.DestroyObjectImmediate(oldSc);

                UnderwaterNurseryItem item = t.GetComponent<UnderwaterNurseryItem>();
                if (item == null)
                {
                    item = Undo.AddComponent<UnderwaterNurseryItem>(t.gameObject);
                }

                item.proximityRange = 6.0f;
                item.contactRadius = 1.5f;
                item.proximityClip = matchedClip;
                item.contactClip = contactClip;
                item.proximityVolume = 2.5f; // 大音量增益
                item.contactVolume = 3.0f;   // 超清晰拾取反饋
                item.animateOnCollect = true;
                item.fadeDuration = 0.45f;

                BoxCollider box = t.GetComponent<BoxCollider>();
                if (box == null) box = Undo.AddComponent<BoxCollider>(t.gameObject);
                box.isTrigger = true;

                float scaleX = Mathf.Abs(t.lossyScale.x) > 0.001f ? Mathf.Abs(t.lossyScale.x) : 1f;
                float scaleY = Mathf.Abs(t.lossyScale.y) > 0.001f ? Mathf.Abs(t.lossyScale.y) : 1f;
                float scaleZ = Mathf.Abs(t.lossyScale.z) > 0.001f ? Mathf.Abs(t.lossyScale.z) : 1f;
                box.size = new Vector3((item.contactRadius * 2f) / scaleX, (item.contactRadius * 2f) / scaleY, 20f / scaleZ);

                AudioSource audio = t.GetComponent<AudioSource>();
                if (audio == null) audio = Undo.AddComponent<AudioSource>(t.gameObject);
                audio.playOnAwake = false;
                audio.spatialBlend = 0.0f;
                audio.volume = 1.0f;

                EditorUtility.SetDirty(t.gameObject);
                setupCount++;
                Debug.Log($"✅ 成功配置育兒物品 [{t.name}]，綁定專屬靠近音效：{matchedClip.name} (大音量增益 2.5x) 與 接觸音效：{contactClip?.name} (3.0x)");
            }
        }

        Undo.CollapseUndoOperations(undoGroup);

        if (setupCount > 0)
        {
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorUtility.DisplayDialog("成功", $"已成功為場景中的 {setupCount} 個水下育兒物品自動配置大音量增益音效、感應範圍與拾取機制！", "確定");
        }
        else
        {
            EditorUtility.DisplayDialog("提示", "未在場景中找到 music_box、baby_bottle 或 baby_bell 物件！請確認物件名稱或已開啟對應水下場景。", "確定");
        }
    }
}
