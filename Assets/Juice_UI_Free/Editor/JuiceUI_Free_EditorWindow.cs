// ============================================================================
// JUICE UI FREE - Editor Window
// ============================================================================
#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace SpankyBoy.JuiceUI.Free
{
    [InitializeOnLoad] 
    public class JuiceUI_Free_EditorWindow : EditorWindow
    {
        private const string VERSION = "v2.0.0";
        private const string FREE_ASSET_STORE_URL = "https://assetstore.unity.com/packages/slug/353086";
        private const string PRO_ASSET_STORE_URL = "https://assetstore.unity.com/packages/slug/328841";
        private const string PUBLISHER_URL = "https://assetstore.unity.com/publishers/109386";

        private Vector2 scrollPosition;
        private GUIStyle headerStyle;
        private GUIStyle subHeaderStyle;
        private GUIStyle proHeaderStyle;
        private GUIStyle buttonStyle;
        private GUIStyle proButtonStyle;
        private GUIStyle linkButtonStyle;
        private GUIStyle boxStyle;
        private GUIStyle proBoxStyle;
        private bool stylesInitialized = false;
        private Texture2D logoTexture;

        static JuiceUI_Free_EditorWindow()
        {
            
            EditorApplication.delayCall += CheckFirstTime;
        }

        private static void CheckFirstTime()
        {
            string prefsKey = "JuiceUI_Free_Shown_" + VERSION;
            if (!EditorPrefs.GetBool(prefsKey, false))
            {
                ShowWindow();
                EditorPrefs.SetBool(prefsKey, true);
            }
        }
        // -----------------------

        [MenuItem("Tools/JUICE UI Free/About", false, 0)]
        public static void ShowWindow()
        {
            var window = GetWindow<JuiceUI_Free_EditorWindow>("JUICE UI Free");
            window.minSize = new Vector2(450, 650);
            window.Show();
        }

        private void OnEnable()
        {

            logoTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Juice_UI_Free/Art/Logo_Transparent.png");
        }

        private void InitializeStyles()
        {
            if (stylesInitialized) return;

            headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 24,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.3f, 0.8f, 0.3f) }
            };

            proHeaderStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 20,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 0.84f, 0f) }
            };

            subHeaderStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = Color.white }
            };

            buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(20, 20, 10, 10)
            };

            proButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(20, 20, 12, 12),
                normal = { textColor = new Color(1f, 0.84f, 0f) }
            };

            linkButtonStyle = new GUIStyle(EditorStyles.linkLabel)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleCenter
            };

            boxStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(15, 15, 15, 15)
            };

            proBoxStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(15, 15, 15, 15),
                normal = { background = MakeTexture(2, 2, new Color(1f, 0.84f, 0f, 0.1f)) }
            };

            stylesInitialized = true;
        }

        private Texture2D MakeTexture(int width, int height, Color color)
        {
            Color[] pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
            Texture2D texture = new Texture2D(width, height);
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private void OnGUI()
        {
            InitializeStyles();
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            GUILayout.Space(20);

            if (logoTexture != null)
            {
                float aspectRatio = (float)logoTexture.width / logoTexture.height;
                float width = EditorGUIUtility.currentViewWidth * 0.7f;
                float height = width / aspectRatio;

                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUILayout.Label(logoTexture, GUILayout.Width(width), GUILayout.Height(height));
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }
            else
            {

                EditorGUILayout.LabelField("JUICE UI", headerStyle, GUILayout.Height(40));
            }

            EditorGUILayout.LabelField($"Version {VERSION} - Free Edition", EditorStyles.centeredGreyMiniLabel);
            GUILayout.Space(10);

            // Description Box
            EditorGUILayout.BeginVertical(boxStyle);
            EditorGUILayout.LabelField("Free UI Animation Toolkit for DOTween", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("Get started with juicy UI animations!", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndVertical();

            GUILayout.Space(15);

            // UPGRADE TO PRO
            EditorGUILayout.BeginVertical(proBoxStyle);
            GUILayout.Space(5);
            EditorGUILayout.LabelField("✨ UPGRADE TO PRO ✨", proHeaderStyle, GUILayout.Height(30));
            GUILayout.Space(5);
            EditorGUILayout.HelpBox("Unlock the full power of JUICE UI!", MessageType.Info);
            GUILayout.Space(5);
            DrawProFeature("15+ Animation Types (vs 4 in Free)");
            DrawProFeature("10+ Hover Effects (vs 5 in Free)");
            DrawProFeature("Advanced Timing & Curves");
            GUILayout.Space(10);
            if (GUILayout.Button("🚀 UPGRADE TO PRO NOW", proButtonStyle, GUILayout.Height(40)))
                Application.OpenURL(PRO_ASSET_STORE_URL);
            EditorGUILayout.EndVertical();

            GUILayout.Space(15);

            DrawSection("📦 Free Version Includes", () =>
            {
                DrawBulletPoint("Button Animator - Hover & Click Effects");
                DrawBulletPoint("Panel Animator - Basic Transitions");
                DrawBulletPoint("Shake & Pulse Animators");
                GUILayout.Space(5);
                EditorGUILayout.HelpBox("Find components under: Component → JUICE UI Free", MessageType.Info);
            });

            GUILayout.Space(10);

            DrawSection("🔗 Quick Links", () =>
            {
                if (DrawLinkButton("📦 Free Version Page", FREE_ASSET_STORE_URL))
                    Application.OpenURL(FREE_ASSET_STORE_URL);

                if (DrawLinkButton("🎨 My Other Assets", PUBLISHER_URL))
                    Application.OpenURL(PUBLISHER_URL);
            });

            GUILayout.Space(10);

            // Support (Discord Removed)
            DrawSection("💡 Support & Feedback", () =>
            {
                EditorGUILayout.LabelField("Enjoying the free version?", EditorStyles.centeredGreyMiniLabel);
                if (GUILayout.Button("⭐ Leave a Review", buttonStyle))
                    Application.OpenURL(FREE_ASSET_STORE_URL);
            });

            GUILayout.Space(20);
            EditorGUILayout.LabelField("Made with ❤️ by SpankyBoy", EditorStyles.centeredGreyMiniLabel);
            GUILayout.Space(20);
            EditorGUILayout.EndScrollView();
        }

        private void DrawSection(string title, System.Action content)
        {
            EditorGUILayout.BeginVertical(boxStyle);
            EditorGUILayout.LabelField(title, subHeaderStyle);
            GUILayout.Space(5);
            content?.Invoke();
            EditorGUILayout.EndVertical();
        }

        private bool DrawLinkButton(string label, string url)
        {
            var content = new GUIContent(label);
            var rect = GUILayoutUtility.GetRect(content, buttonStyle);
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            return GUI.Button(rect, content, buttonStyle);
        }

        private void DrawBulletPoint(string text)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(10);
            EditorGUILayout.LabelField("•", GUILayout.Width(10));
            EditorGUILayout.LabelField(text, EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawProFeature(string text)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(10);
            EditorGUILayout.LabelField("✓", GUILayout.Width(15));
            EditorGUILayout.LabelField(text, EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndHorizontal();
        }
    }
}
#endif