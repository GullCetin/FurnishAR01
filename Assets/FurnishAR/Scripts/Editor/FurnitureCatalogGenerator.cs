using UnityEngine;
using UnityEditor;
using FurnishAR.Data;
using System.IO;

namespace FurnishAR.Editor
{
    /// <summary>
    /// Editor utility to auto-generate FurnitureItem ScriptableObjects
    /// from the NextGen Furniture Pack prefabs, plus create the master catalog.
    /// </summary>
    public static class FurnitureCatalogGenerator
    {
        private const string PrefabFolder = "Assets/NextGen Furniture Pack/Prefabs";
        private const string OutputFolder = "Assets/FurnishAR/Resources/FurnitureItems";
        private const string CatalogPath = "Assets/FurnishAR/Resources/FurnitureCatalog.asset";

        [MenuItem("FurnishAR/Generate Furniture Catalog")]
        public static void GenerateCatalog()
        {
            // Ensure output folder exists
            if (!AssetDatabase.IsValidFolder(OutputFolder))
            {
                string parent = Path.GetDirectoryName(OutputFolder).Replace("\\", "/");
                if (!AssetDatabase.IsValidFolder(parent))
                {
                    string grandparent = Path.GetDirectoryName(parent).Replace("\\", "/");
                    AssetDatabase.CreateFolder(grandparent, Path.GetFileName(parent));
                }
                AssetDatabase.CreateFolder(parent, Path.GetFileName(OutputFolder));
            }

            // Find all prefabs
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolder });
            FurnitureItem[] items = new FurnitureItem[prefabGuids.Length];
            int count = 0;

            foreach (string guid in prefabGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;

                string prefabName = prefab.name;
                string assetPath = $"{OutputFolder}/{prefabName}.asset";

                // Check if already exists
                FurnitureItem existing = AssetDatabase.LoadAssetAtPath<FurnitureItem>(assetPath);
                if (existing != null)
                {
                    items[count++] = existing;
                    continue;
                }

                // Create new item
                FurnitureItem item = ScriptableObject.CreateInstance<FurnitureItem>();
                item.prefab = prefab;
                item.displayName = FormatDisplayName(prefabName);
                item.category = DetermineCategory(prefabName);
                item.placementScale = 1f;
                item.rotationOffset = 0f;

                // Try to get asset preview as icon
                Texture2D preview = AssetPreview.GetAssetPreview(prefab);
                if (preview != null)
                {
                    // We'll generate preview textures via the PreviewGenerator
                    item.previewIcon = null; // Will be set up later or use runtime preview
                }

                AssetDatabase.CreateAsset(item, assetPath);
                items[count++] = item;

                Debug.Log($"[FurnishAR] Created FurnitureItem: {item.displayName} -> {assetPath}");
            }

            // Trim array
            System.Array.Resize(ref items, count);

            // Create or update the catalog
            FurnitureCatalog catalog = AssetDatabase.LoadAssetAtPath<FurnitureCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<FurnitureCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            catalog.items = items;
            EditorUtility.SetDirty(catalog);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[FurnishAR] Catalog generated with {count} items at {CatalogPath}");
        }

        private static string FormatDisplayName(string prefabName)
        {
            // "chair_1" -> "Chair 1", "sofa_2" -> "Sofa 2", "commode_1" -> "Commode 1"
            string[] parts = prefabName.Split('_');
            if (parts.Length >= 2)
            {
                string category = char.ToUpper(parts[0][0]) + parts[0][1..];
                category = parts[0].ToLowerInvariant() switch { "chair" => "Sandalye", "sofa" => "Kanepe", "table" => "Masa", "commode" => "Konsol", _ => category };
                return $"{category} {parts[1]}";
            }
            return prefabName;
        }

        private static FurnitureCategory DetermineCategory(string prefabName)
        {
            string lower = prefabName.ToLower();
            if (lower.StartsWith("chair")) return FurnitureCategory.Chair;
            if (lower.StartsWith("sofa")) return FurnitureCategory.Sofa;
            if (lower.StartsWith("table")) return FurnitureCategory.Table;
            if (lower.StartsWith("commode")) return FurnitureCategory.Commode;
            return FurnitureCategory.Chair;
        }
    }
}


