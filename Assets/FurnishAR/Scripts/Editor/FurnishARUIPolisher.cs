using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace FurnishAR.Editor
{
    /// <summary>
    /// Applies the "finished product" look to the UI:
    /// - Generates rounded-corner fill, outline ring and soft-shadow 9-slice sprites
    /// - Assigns them to the card prefab, catalog panel, guidance pill, interaction
    ///   panel and icon buttons
    /// - Adds soft shadow halos behind cards and panels
    /// Idempotent: safe to re-run anytime from the FurnishAR menu.
    /// </summary>
    public static class FurnishARUIPolisher
    {
        private const string SpriteFolder = "Assets/FurnishAR/Textures/UI";
        private const string CardPrefabPath = "Assets/FurnishAR/Prefabs/UI/FurnitureCardPrefab.prefab";

        private const string FillSpritePath = SpriteFolder + "/UIRoundedFill.png";
        private const string RingSpritePath = SpriteFolder + "/UIRoundedRing.png";
        private const string ShadowSpritePath = SpriteFolder + "/UISoftShadow.png";

        private const int SliceBorder = 36;

        [MenuItem("FurnishAR/Polish UI")]
        public static void Apply()
        {
            GenerateSprites();
            PolishCardPrefab();
            PolishSceneUI();

            AssetDatabase.SaveAssets();
            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[FurnishAR] ✨ UI polish complete.");
        }

        // ------------------------------------------------------------------
        // Sprite generation
        // ------------------------------------------------------------------
        private static void GenerateSprites()
        {
            if (!AssetDatabase.IsValidFolder(SpriteFolder))
                AssetDatabase.CreateFolder("Assets/FurnishAR/Textures", "UI");

            // 1. Solid rounded rect fill
            if (AssetDatabase.LoadAssetAtPath<Sprite>(FillSpritePath) == null)
                WriteSprite(FillSpritePath, (x, y, w, h) => RoundedAlpha(x, y, w, h, 0f, 0.02f), SliceBorder);

            // 2. Rounded outline ring (selection border)
            if (AssetDatabase.LoadAssetAtPath<Sprite>(RingSpritePath) == null)
                WriteSprite(RingSpritePath, (x, y, w, h) => RingAlpha(x, y, w, h), SliceBorder);

            // 3. Soft shadow (feathered interior)
            if (AssetDatabase.LoadAssetAtPath<Sprite>(ShadowSpritePath) == null)
                WriteSprite(ShadowSpritePath, (x, y, w, h) => ShadowAlpha(x, y, w, h), SliceBorder);
        }

        private delegate float AlphaSampler(int x, int y, int w, int h);

        private static void WriteSprite(string path, AlphaSampler sampler, int border)
        {
            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(sampler(x, y, size, size)));
                    pixels[(size - 1 - y) * size + x] = new Color(1f, 1f, 1f, a);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);

            // 9-slice import settings
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.spritePixelsPerUnit = 100;
                importer.spriteBorder = new Vector4(border, border, border, border);
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }
        }

        // Signed distance to a rounded rect (in pixel units), normalized to [0,1]
        private static float RoundedAlpha(int px, int py, int w, int h, float inflate, float feather)
        {
            const float radius = 48f;
            float x = px + 0.5f;
            float y = py + 0.5f;

            float qx = Mathf.Abs(x - w * 0.5f) - (w * 0.5f - radius);
            float qy = Mathf.Abs(y - h * 0.5f) - (h * 0.5f - radius);

            float ox = Mathf.Max(qx, 0f);
            float oy = Mathf.Max(qy, 0f);
            float distOutside = Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f);

            float dist = (radius - distOutside) + inflate * radius;
            float edge = dist / Mathf.Max(1f, feather * radius);
            return Mathf.Clamp01(edge);
        }

        private static float RingAlpha(int px, int py, int w, int h)
        {
            const float stroke = 5f;
            float outer = RoundedAlphaRaw(px, py, w, h, 0f);
            float inner = RoundedAlphaRaw(px, py, w, h, -stroke / 48f);
            float ring = Mathf.Min(outer, 1f - inner);
            return Mathf.Clamp01(ring);
        }

        private static float RoundedAlphaRaw(int px, int py, int w, int h, float inflateInRadii)
        {
            return Mathf.Clamp01(RoundedAlpha(px, py, w, h, inflateInRadii, 1f));
        }

        private static float ShadowAlpha(int px, int py, int w, int h)
        {
            float inner = RoundedAlpha(px, py, w, h, -0.12f, 0.05f);
            float spread = RoundedAlpha(px, py, w, h, 0.06f, 0.09f);
            return Mathf.Clamp01(spread * 0.85f + inner * 0.15f) * 0.9f;
        }

        // ------------------------------------------------------------------
        // Card prefab
        // ------------------------------------------------------------------
        private static void PolishCardPrefab()
        {
            var fill = AssetDatabase.LoadAssetAtPath<Sprite>(FillSpritePath);
            var ring = AssetDatabase.LoadAssetAtPath<Sprite>(RingSpritePath);
            var shadow = AssetDatabase.LoadAssetAtPath<Sprite>(ShadowSpritePath);
            if (fill == null || ring == null || shadow == null)
            {
                Debug.LogError("[FurnishAR] UI sprites not generated; cannot polish card prefab.");
                return;
            }

            var root = PrefabUtility.LoadPrefabContents(CardPrefabPath);
            try
            {
                var bg = root.GetComponent<Image>();
                if (bg != null)
                {
                    bg.sprite = fill;
                    bg.type = Image.Type.Sliced;
                }

                // Soft shadow behind the card
                var cardTransform = root.transform;
                if (cardTransform.Find("CardShadow") == null)
                {
                    var shadowGO = new GameObject("CardShadow");
                    var shadowRect = shadowGO.AddComponent<RectTransform>();
                    shadowRect.SetParent(cardTransform, false);
                    shadowRect.SetSiblingIndex(0);
                    shadowRect.anchorMin = Vector2.zero;
                    shadowRect.anchorMax = Vector2.one;
                    shadowRect.offsetMin = new Vector2(-16f, -16f);
                    shadowRect.offsetMax = new Vector2(16f, 16f);

                    var shadowImage = shadowGO.AddComponent<Image>();
                    shadowImage.sprite = shadow;
                    shadowImage.type = Image.Type.Sliced;
                    shadowImage.color = new Color(0f, 0f, 0f, 0.10f);
                    shadowImage.raycastTarget = false;
                }

                // Selection border becomes a rounded outline ring slightly outside the card
                var borderTransform = cardTransform.Find("SelectionBorder");
                if (borderTransform != null)
                {
                    var borderRect = (RectTransform)borderTransform;
                    borderRect.offsetMin = new Vector2(-7f, -7f);
                    borderRect.offsetMax = new Vector2(7f, 7f);

                    var borderImage = borderTransform.GetComponent<Image>();
                    if (borderImage != null)
                    {
                        borderImage.sprite = ring;
                        borderImage.type = Image.Type.Sliced;
                        borderImage.raycastTarget = false;
                    }
                }

                PrefabUtility.SaveAsPrefabAsset(root, CardPrefabPath);
                Debug.Log("[FurnishAR] Card prefab polished.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ------------------------------------------------------------------
        // Live scene UI
        // ------------------------------------------------------------------
        private static void PolishSceneUI()
        {
            var fill = AssetDatabase.LoadAssetAtPath<Sprite>(FillSpritePath);
            var shadow = AssetDatabase.LoadAssetAtPath<Sprite>(ShadowSpritePath);

            var canvas = GameObject.Find("UI Canvas");
            if (canvas == null)
            {
                Debug.LogError("[FurnishAR] UI Canvas not found in active scene.");
                return;
            }

            // Catalog panel
            var catalogPanel = FindPanel(canvas.transform, "CatalogPanel")?.transform;
            if (catalogPanel != null)
            {
                var bg = catalogPanel.GetComponent<Image>();
                if (bg != null)
                {
                    bg.sprite = fill;
                    bg.type = Image.Type.Sliced;
                }
                AddShadow(catalogPanel, "PanelShadow", shadow, 24f, 0.08f);
            }

            // Guidance pill
            var guidance = FindPanel(canvas.transform, "GuidanceUI");
            if (guidance != null)
            {
                var bg = guidance.GetComponent<Image>();
                if (bg != null)
                {
                    bg.sprite = fill;
                    bg.type = Image.Type.Sliced;
                }
            }

            // Interaction panel + its icon buttons
            var interaction = FindPanel(canvas.transform, "InteractionPanel");
            if (interaction != null)
            {
                var bg = interaction.GetComponent<Image>();
                if (bg != null)
                {
                    bg.sprite = fill;
                    bg.type = Image.Type.Sliced;
                }

                foreach (var buttonName in new[] { "RotateLeft", "RotateRight", "Delete" })
                {
                    var buttonTransform = interaction.transform.Find(buttonName);
                    if (buttonTransform == null) continue;
                    var btnImage = buttonTransform.GetComponent<Image>();
                    if (btnImage != null)
                    {
                        btnImage.sprite = fill;
                        btnImage.type = Image.Type.Sliced;
                    }
                }

                AddShadow(interaction.transform, "PanelShadow", shadow, 14f, 0.30f);
            }

            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static GameObject FindPanel(Transform canvasTransform, string name)
        {
            var direct = canvasTransform.Find(name);
            if (direct != null) return direct.gameObject;
            var underSafeArea = canvasTransform.Find($"SafeAreaRoot/{name}");
            if (underSafeArea != null) return underSafeArea.gameObject;
            return null;
        }

        private static void AddShadow(Transform parent, string shadowName, Sprite shadow, float spread, float alpha)
        {
            if (parent == null || shadow == null) return;
            if (parent.Find(shadowName) != null) return;

            var shadowGO = new GameObject(shadowName);
            var shadowRect = shadowGO.AddComponent<RectTransform>();
            shadowRect.SetParent(parent, false);
            shadowRect.SetSiblingIndex(0);
            shadowRect.anchorMin = Vector2.zero;
            shadowRect.anchorMax = Vector2.one;
            shadowRect.offsetMin = new Vector2(-spread, -spread);
            shadowRect.offsetMax = new Vector2(spread, spread);

            var shadowImage = shadowGO.AddComponent<Image>();
            shadowImage.sprite = shadow;
            shadowImage.type = Image.Type.Sliced;
            shadowImage.color = new Color(0f, 0f, 0f, alpha);
            shadowImage.raycastTarget = false;
        }
    }
}
