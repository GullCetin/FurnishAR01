using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using TMPro;
using UnityEngine.XR.ARFoundation;

namespace FurnishAR.Editor
{
    /// <summary>
    /// Runtime-facing fixes staged through a single menu action:
    /// 1. Adds the ARBackgroundRendererFeature to the URP renderers (Mobile + PC).
    ///    Without it URP never draws the device camera feed - the app shows whatever
    ///    is left in the framebuffer (often a solid yellow/green) instead of the camera.
    /// 2. Catalog visibility polish: preview icon alpha 1.0, stronger card/panel
    ///    contrast, readable label color.
    /// Idempotent: safe to re-run from the FurnishAR menu.
    /// </summary>
    public static class FurnishARRuntimeFixes
    {
        private const string CardPrefabPath = "Assets/FurnishAR/Prefabs/UI/FurnitureCardPrefab.prefab";

        private static readonly string[] RendererPaths =
        {
            "Assets/Settings/Mobile_Renderer.asset",
            "Assets/Settings/PC_Renderer.asset",
        };

        [MenuItem("FurnishAR/Apply Runtime Fixes (AR Feed + Catalog)")]
        public static void Apply()
        {
            EnsureARBackgroundFeature();
            PolishCardPrefab();
            PolishScenePanels();

            AssetDatabase.SaveAssets();
            Debug.Log("[FurnishAR] ✅ Runtime fixes applied: AR background renderer feature + catalog visibility.");
        }

        // --------------------------------------------------------------
        // 1. AR camera background renderer feature
        // --------------------------------------------------------------
        private static void EnsureARBackgroundFeature()
        {
            foreach (var path in RendererPaths)
            {
                var data = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(path);
                if (data == null)
                {
                    Debug.LogWarning("[FurnishAR] Renderer data not found: " + path);
                    continue;
                }

                bool alreadyThere = false;
                foreach (var f in data.rendererFeatures)
                {
                    if (f is ARBackgroundRendererFeature) alreadyThere = true;
                }
                if (alreadyThere)
                {
                    Debug.Log("[FurnishAR] " + data.name + " already has ARBackgroundRendererFeature.");
                    continue;
                }

                var feature = ScriptableObject.CreateInstance<ARBackgroundRendererFeature>();
                feature.name = "ARBackgroundRendererFeature";
                AssetDatabase.AddObjectToAsset(feature, data);

                var so = new SerializedObject(data);
                var feats = so.FindProperty("m_RendererFeatures");
                int index = feats.arraySize;
                feats.arraySize++;
                feats.GetArrayElementAtIndex(index).objectReferenceValue = feature;

                // URP keeps a parallel map of internal instance ids; fill a random one
                var map = so.FindProperty("m_RendererFeatureMap");
                if (map != null && map.isArray)
                {
                    map.arraySize = feats.arraySize;
                    var rnd1 = Random.Range(1, int.MaxValue);
                    var rnd2 = Random.Range(1, int.MaxValue);
                    map.GetArrayElementAtIndex(index).longValue = ((long)rnd1 << 32) | (uint)rnd2;
                }

                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(data);
                Debug.Log("[FurnishAR] ✅ ARBackgroundRendererFeature added to " + data.name);
            }
        }

        // --------------------------------------------------------------
        // 2. Card prefab visibility polish
        // --------------------------------------------------------------
        private static void PolishCardPrefab()
        {
            var fill = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/FurnishAR/Textures/UI/UIRoundedFill.png");
            var ring = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/FurnishAR/Textures/UI/UIRoundedRing.png");
            if (fill == null) return;

            var root = PrefabUtility.LoadPrefabContents(CardPrefabPath);
            try
            {
                // 2a. Card body: pure white, fully opaque (was 0.92 alpha over a white panel)
                var cardImage = root.GetComponent<Image>();
                if (cardImage != null)
                {
                    cardImage.sprite = fill;
                    cardImage.type = Image.Type.Sliced;
                    cardImage.color = new Color(1f, 1f, 1f, 1f);
                }

                // 2b. Preview: full opacity (was 0.5 → looked empty/white-on-white)
                var preview = root.transform.Find("PreviewImage");
                if (preview != null)
                {
                    var previewImage = preview.GetComponent<Image>();
                    previewImage.color = new Color(1f, 1f, 1f, 1f);
                    previewImage.preserveAspect = true;
                    previewImage.raycastTarget = false;
                }

                // 2c. Selection ring: thin dark outline kept, color stays blue for selected state
                var border = root.transform.Find("SelectionBorder");
                if (border != null)
                {
                    var borderImage = border.GetComponent<Image>();
                    if (ring != null) borderImage.sprite = ring;
                    borderImage.type = Image.Type.Sliced;
                    borderImage.raycastTarget = false;
                }

                // 2d. Label: dark, readable
                var label = root.transform.Find("NameLabel");
                if (label != null)
                {
                    var txt = label.GetComponent<TextMeshProUGUI>();
                    if (txt != null)
                    {
                        txt.color = new Color(0.10f, 0.12f, 0.17f, 1f);
                        txt.fontSize = 13f;
                        txt.alignment = TextAlignmentOptions.Center;
                        txt.raycastTarget = false;
                    }
                }

                PrefabUtility.SaveAsPrefabAsset(root, CardPrefabPath);
                Debug.Log("[FurnishAR] Card prefab visibility polished (alpha + contrast).");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // --------------------------------------------------------------
        // 3. Live scene panels (slightly translucent so cards pop)
        // --------------------------------------------------------------
        private static void PolishScenePanels()
        {
            var canvas = GameObject.Find("UI Canvas");
            if (canvas == null) return;

            var catalogT = canvas.transform.Find("SafeAreaRoot/CatalogPanel") ?? canvas.transform.Find("CatalogPanel");
            if (catalogT != null)
            {
                var bg = catalogT.GetComponent<Image>();
                if (bg != null) bg.color = new Color(0.96f, 0.97f, 0.99f, 0.85f);
            }

            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
    }
}
