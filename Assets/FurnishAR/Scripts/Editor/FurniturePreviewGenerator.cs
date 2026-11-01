using UnityEngine;
using UnityEditor;
using System.IO;
using FurnishAR.Data;

namespace FurnishAR.Editor
{
    /// <summary>
    /// Generates high-quality isometric 2D preview sprites for all 15 furniture models.
    /// Captures them in an off-screen staging area with studio lighting and transparent background.
    /// </summary>
    public static class FurniturePreviewGenerator
    {
        private const string OutputFolder = "Assets/FurnishAR/Textures/Previews";
        private const string PrefabFolder = "Assets/NextGen Furniture Pack/Prefabs";
        private const string ItemsFolder = "Assets/FurnishAR/Resources/FurnitureItems";

        [MenuItem("FurnishAR/Generate Preview Icons")]
        public static void GenerateAllPreviews()
        {
            if (!AssetDatabase.IsValidFolder(OutputFolder))
            {
                string parent = Path.GetDirectoryName(OutputFolder).Replace("\\", "/");
                AssetDatabase.CreateFolder(parent, Path.GetFileName(OutputFolder));
            }

            // Staging position far from origin to avoid interference
            Vector3 stageOrigin = new Vector3(5000f, 5000f, 5000f);

            // Create staging camera
            var camGO = new GameObject("PreviewCam_Temp");
            camGO.transform.position = stageOrigin + new Vector3(1.5f, 1.3f, -1.8f);
            var cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0, 0, 0, 0); // Transparent
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 100f;
            cam.fieldOfView = 28f;

            // Create studio lights
            var keyLightGO = new GameObject("KeyLight_Temp");
            keyLightGO.transform.position = stageOrigin;
            var keyLight = keyLightGO.AddComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.color = new Color(1f, 0.98f, 0.95f);
            keyLight.intensity = 1.3f;
            keyLightGO.transform.rotation = Quaternion.Euler(45, -35, 0);

            var fillLightGO = new GameObject("FillLight_Temp");
            fillLightGO.transform.position = stageOrigin;
            var fillLight = fillLightGO.AddComponent<Light>();
            fillLight.type = LightType.Directional;
            fillLight.color = new Color(0.85f, 0.9f, 1f);
            fillLight.intensity = 0.7f;
            fillLightGO.transform.rotation = Quaternion.Euler(20, 140, 0);

            // Render texture with alpha
            var rt = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = 4;
            cam.targetTexture = rt;

            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolder });

            try
            {
                foreach (string guid in prefabGuids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null) continue;

                    string prefabName = prefab.name;

                    // Instantiate at staging origin
                    GameObject instance = Object.Instantiate(prefab, stageOrigin, Quaternion.identity);

                    // Calculate bounding box to frame model perfectly
                    Bounds bounds = CalculateBounds(instance);
                    Vector3 center = bounds.center;
                    float maxDim = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
                    if (maxDim < 0.1f) maxDim = 1f;

                    // Position camera based on model size
                    float distance = maxDim * 2.5f;
                    Vector3 camOffset = new Vector3(distance * 0.75f, distance * 0.65f, -distance * 0.9f);
                    camGO.transform.position = center + camOffset;
                    camGO.transform.LookAt(center);

                    // Render. URP compiles shader variants lazily on first use, so the
                    // very first pass can come back empty — retry until visible.
                    Texture2D tex = null;
                    for (int attempt = 0; attempt < 4; attempt++)
                    {
                        RenderTexture.active = rt;
                        cam.Render();
                        RenderTexture.active = null;

                        tex = ReadRenderTexture(rt, 256);
                        if (HasVisiblePixels(tex)) break;

                        Object.DestroyImmediate(tex);
                        tex = null;
                    }

                    if (tex == null)
                    {
                        Debug.LogWarning($"[FurnishAR] Preview for {prefabName} stayed empty after 4 renders.");
                        tex = new Texture2D(256, 256, TextureFormat.RGBA32, false);
                    }

                    // Save PNG
                    byte[] bytes = tex.EncodeToPNG();
                    string pngPath = $"{OutputFolder}/{prefabName}.png";
                    File.WriteAllBytes(pngPath, bytes);
                    Object.DestroyImmediate(tex);
                    Object.DestroyImmediate(instance);

                    Debug.Log($"[FurnishAR] Rendered preview for {prefabName} -> {pngPath}");
                }
            }
            finally
            {
                cam.targetTexture = null;
                RenderTexture.active = null;
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(camGO);
                Object.DestroyImmediate(keyLightGO);
                Object.DestroyImmediate(fillLightGO);
            }

            AssetDatabase.Refresh();

            // Set sprite import settings and assign to items
            foreach (string guid in prefabGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string prefabName = Path.GetFileNameWithoutExtension(path);
                string pngPath = $"{OutputFolder}/{prefabName}.png";

                TextureImporter importer = AssetImporter.GetAtPath(pngPath) as TextureImporter;
                if (importer != null)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.alphaIsTransparency = true;
                    importer.SaveAndReimport();
                }

                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(pngPath);
                string itemPath = $"{ItemsFolder}/{prefabName}.asset";
                FurnitureItem item = AssetDatabase.LoadAssetAtPath<FurnitureItem>(itemPath);
                if (item != null && sprite != null)
                {
                    item.previewIcon = sprite;
                    EditorUtility.SetDirty(item);
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[FurnishAR] ✅ All 15 furniture previews generated and linked successfully!");
        }

        private static Texture2D ReadRenderTexture(RenderTexture rt, int size)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            tex.Apply();

            RenderTexture.active = previous;
            return tex;
        }

        /// <summary>True when the render contains at least a few non-transparent pixels.</summary>
        private static bool HasVisiblePixels(Texture2D tex)
        {
            Color32[] pixels = tex.GetPixels32();
            int hits = 0;
            for (int i = 0; i < pixels.Length; i += 5)
            {
                if (pixels[i].a > 12 && ++hits > 20) return true;
            }
            return false;
        }

        private static Bounds CalculateBounds(GameObject go)
        {
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return new Bounds(go.transform.position, Vector3.one);

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }
            return bounds;
        }
    }
}

