using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using FurnishAR.Core;
using FurnishAR.UI;

namespace FurnishAR.Editor
{
    /// <summary>
    /// Applies the remaining scene-level details:
    /// 1. Soft plane visualizer prefab + subtle breathing fade, assigned to ARPlaneManager
    /// 2. Soft contact-shadow prefab for placed furniture (Resources/Effects)
    /// 3. SafeArea root wrapper (notch / home-indicator safe UI)
    /// Idempotent: safe to re-run anytime from the FurnishAR menu.
    /// </summary>
    public static class FurnishARDetailSetup
    {
        private const string PlanePrefabPath = "Assets/FurnishAR/Prefabs/AR/PlaneVisual.prefab";
        private const string PlaneMaterialPath = "Assets/FurnishAR/Materials/PlaneVisual.mat";
        private const string ShadowTexturePath = "Assets/FurnishAR/Textures/Effects/ContactShadow.png";
        private const string ShadowMaterialPath = "Assets/FurnishAR/Materials/ContactShadow.mat";
        private const string ShadowPrefabPath = "Assets/FurnishAR/Resources/Effects/ContactShadow.prefab";

        [MenuItem("FurnishAR/Apply AR & Scene Details")]
        public static void Apply()
        {
            CreateContactShadowPrefab();
            CreatePlanePrefabAndAssign();
            InstallSafeAreaRoot();

            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            AssetDatabase.SaveAssets();
            Debug.Log("[FurnishAR] ✅ AR & scene details applied (plane visuals, contact shadow, safe area).");
        }

        // ------------------------------------------------------------------
        // Contact shadow
        // ------------------------------------------------------------------
        private static void CreateContactShadowPrefab()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(ShadowPrefabPath) != null)
            {
                Debug.Log("[FurnishAR] ContactShadow prefab already exists, skipping.");
                return;
            }

            EnsureFolder("Assets/FurnishAR/Resources", "Effects");
            EnsureFolder("Assets/FurnishAR/Textures", "Effects");

            // 1. Soft radial shadow texture
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(ShadowTexturePath) == null)
            {
                int size = 256;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
                var pixels = new Color[size * size];
                float center = (size - 1) * 0.5f;
                float radius = center;
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dx = x - center;
                        float dy = y - center;
                        float t = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy) / radius);
                        float alpha = Mathf.Pow(1f - t, 1.5f);
                        pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                    }
                }
                tex.SetPixels(pixels);
                tex.Apply();
                File.WriteAllBytes(ShadowTexturePath, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(ShadowTexturePath);
            }

            var shadowTex = AssetDatabase.LoadAssetAtPath<Texture2D>(ShadowTexturePath);
            var texImporter = AssetImporter.GetAtPath(ShadowTexturePath) as TextureImporter;
            if (texImporter != null)
            {
                texImporter.alphaIsTransparency = true;
                texImporter.mipmapEnabled = true;
                texImporter.wrapMode = TextureWrapMode.Clamp;
                texImporter.SaveAndReimport();
            }

            // 2. Material (URP/Unlit, transparent, no shadow casting)
            Material shadowMat = AssetDatabase.LoadAssetAtPath<Material>(ShadowMaterialPath);
            if (shadowMat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                shadowMat = new Material(shader);
                shadowMat.SetFloat("_Surface", 1f); // transparent
                shadowMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                shadowMat.SetOverrideTag("RenderType", "Transparent");
                shadowMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                shadowMat.SetColor("_BaseColor", new Color(0f, 0f, 0f, 0.30f));
                if (shadowTex != null) shadowMat.SetTexture("_BaseMap", shadowTex);
                AssetDatabase.CreateAsset(shadowMat, ShadowMaterialPath);
            }

            // 3. Prefab: flat quad with the shadow material + runtime; behavior script
            var root = GameObject.CreatePrimitive(PrimitiveType.Quad);
            var collider = root.GetComponent<Collider>();
            if (collider != null) Object.DestroyImmediate(collider);

            root.name = "ContactShadow";
            root.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

            var meshRenderer = root.GetComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = shadowMat;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            root.AddComponent<FurnitureContactShadow>();

            PrefabUtility.SaveAsPrefabAsset(root, ShadowPrefabPath);
            Object.DestroyImmediate(root);
            Debug.Log("[FurnishAR] ContactShadow prefab created at " + ShadowPrefabPath);
        }

        // ------------------------------------------------------------------
        // Plane visualizer
        // ------------------------------------------------------------------
        private static void CreatePlanePrefabAndAssign()
        {
            GameObject planePrefab = PrefabUtilityHelper(PlanePrefabPath);
            var xrOrigin = GameObject.Find("XR Origin");
            if (xrOrigin == null)
            {
                Debug.LogError("[FurnishAR] XR Origin not found in active scene.");
                return;
            }

            var planeManager = xrOrigin.GetComponent<ARPlaneManager>();
            if (planeManager == null)
            {
                Debug.LogError("[FurnishAR] ARPlaneManager not found on XR Origin.");
                return;
            }

            if (planePrefab == null)
            {
                planePrefab = BuildPlanePrefab();
            }

            if (planePrefab != null)
            {
                planeManager.planePrefab = planePrefab;
                EditorUtility.SetDirty(planeManager);
            }
        }

        private static GameObject PrefabUtilityHelper(string path)
        {
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        private static GameObject BuildPlanePrefab()
        {
            var root = new GameObject("PlaneVisual");

            root.AddComponent<MeshFilter>();
            var meshRenderer = root.AddComponent<MeshRenderer>();

            Material planeMat = AssetDatabase.LoadAssetAtPath<Material>(PlaneMaterialPath);
            if (planeMat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                planeMat = new Material(shader);
                planeMat.SetFloat("_Surface", 1f); // transparent
                planeMat.SetFloat("_Blend", 0f);
                planeMat.SetFloat("_AlphaClip", 0f);
                planeMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                planeMat.SetOverrideTag("RenderType", "Transparent");
                planeMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                planeMat.SetColor("_BaseColor", new Color(0.30f, 0.68f, 1f, 0.16f));
                AssetDatabase.CreateAsset(planeMat, PlaneMaterialPath);
            }

            meshRenderer.sharedMaterial = planeMat;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            root.AddComponent<ARPlane>();
            root.AddComponent<ARPlaneMeshVisualizer>();
            root.AddComponent<PlaneVisualizerEffects>();

            PrefabUtility.SaveAsPrefabAsset(root, PlanePrefabPath);
            Object.DestroyImmediate(root);

            var created = AssetDatabase.LoadAssetAtPath<GameObject>(PlanePrefabPath);
            Debug.Log("[FurnishAR] PlaneVisual prefab created at " + PlanePrefabPath);
            return created;
        }

        // ------------------------------------------------------------------
        // Safe area
        // ------------------------------------------------------------------
        private static void InstallSafeAreaRoot()
        {
            var canvasTransform = GameObject.Find("UI Canvas")?.transform;
            if (canvasTransform == null)
            {
                Debug.LogError("[FurnishAR] UI Canvas not found in active scene.");
                return;
            }

            if (canvasTransform.Find("SafeAreaRoot") != null)
            {
                Debug.Log("[FurnishAR] SafeAreaRoot already installed, skipping.");
                return;
            }

            var safeAreaGO = new GameObject("SafeAreaRoot");
            var safeRect = safeAreaGO.AddComponent<RectTransform>();
            safeRect.SetParent(canvasTransform, false);
            safeRect.anchorMin = Vector2.zero;
            safeRect.anchorMax = Vector2.one;
            safeRect.offsetMin = Vector2.zero;
            safeRect.offsetMax = Vector2.zero;
            safeAreaGO.AddComponent<SafeAreaFitter>();

            string[] panels = { "GuidanceUI", "CatalogPanel", "InteractionPanel" };
            foreach (var name in panels)
            {
                var child = canvasTransform.Find(name);
                if (child != null)
                    child.SetParent(safeRect, false);
                else
                    Debug.LogWarning("[FurnishAR] Panel not found for safe-area re-parenting: " + name);
            }
        }

        private static void EnsureFolder(string parentPath, string folderName)
        {
            if (!AssetDatabase.IsValidFolder($"{parentPath}/{folderName}"))
                AssetDatabase.CreateFolder(parentPath, folderName);
        }
    }
}
