using UnityEngine;
using UnityEditor;

namespace FurnishAR.Editor
{
    /// <summary>
    /// Master runner to execute the full FurnishAR setup in the exact correct order.
    /// Available under menu: FurnishAR > 🚀 Run Complete Project Setup
    /// </summary>
    public static class FurnishARMasterSetup
    {
        private const string FlagPath = "Assets/FurnishAR/.setup_done";

        [InitializeOnLoadMethod]
        private static void AutoRunOnEditorLoad()
        {
            if (System.IO.File.Exists(FlagPath)) return;

            EditorApplication.delayCall += () =>
            {
                if (System.IO.File.Exists(FlagPath)) return;
                Debug.Log("[FurnishAR] 🚀 Running automatic initial project setup...");
                RunFullSetup(showDialog: false);
                System.IO.File.WriteAllText(FlagPath, System.DateTime.UtcNow.ToString());
                AssetDatabase.Refresh();
            };
        }

        [MenuItem("FurnishAR/🚀 Run Complete Project Setup", false, 0)]
        public static void RunFullSetupMenu()
        {
            RunFullSetup(showDialog: true);
        }

        public static void RunFullSetup(bool showDialog)
        {
            Debug.Log("[FurnishAR] === Starting Furnish AR Setup ===");

            // Step 1: Catalog data
            Debug.Log("[FurnishAR] Step 1/4: Generating Furniture Catalog & Data Assets...");
            FurnitureCatalogGenerator.GenerateCatalog();

            // Step 2: Preview sprites
            Debug.Log("[FurnishAR] Step 2/4: Generating 2D Isometric Preview Sprites...");
            FurniturePreviewGenerator.GenerateAllPreviews();

            // Step 3: Build AR Scene & UI hierarchy
            Debug.Log("[FurnishAR] Step 3/4: Building AR Scene & UI Hierarchy...");
            FurnishARSceneBuilder.BuildScene();

            // Step 4: Configure Android build & player settings
            Debug.Log("[FurnishAR] Step 4/4: Configuring Android & Build Settings...");
            AndroidSettingsConfigurator.ConfigureSettings();

            Debug.Log("[FurnishAR] === ✅ Full Setup Completed Successfully! ===");
            if (showDialog)
            {
                EditorUtility.DisplayDialog("Furnish AR", 
                    "Furnish AR projesi başarıyla kuruldu!\n\n" +
                    "• 15 Mobilya Modeli bağlandı (Koltuk, Sandalye, Masa, Komodin)\n" +
                    "• 2D Önizleme görselleri üretildi\n" +
                    "• Apple tarzı minimalist UI ve kart kataloğu oluşturuldu\n" +
                    "• AR Zemin Algılama ve Dokunmatik Yerleşim kuruldu\n" +
                    "• Seçme, Taşıma, Döndürme ve Silme kontrolleri bağlandı\n" +
                    "• Android ARCore ayarları yapılandırıldı\n\n" +
                    "Sahneyi açmak için: Assets/FurnishAR/Scenes/FurnishARScene.unity", 
                    "Harika!");
            }
        }
    }
}
