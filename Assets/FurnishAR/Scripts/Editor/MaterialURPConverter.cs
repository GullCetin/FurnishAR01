using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FurnishAR.Editor
{
    /// <summary>
    /// Converts every Built-in "Standard" material in the project to
    /// "Universal Render Pipeline/Lit".
    ///
    /// WHY THIS EXISTS:
    /// The NextGen Furniture Pack ships with Built-in Standard materials. In a URP
    /// project those render as solid magenta (Unity's missing-shader fallback), which
    /// made every furniture preview icon and every placed model magenta/invisible.
    /// This upgrader copies albedo, normal, occlusion and emission data across so the
    /// furniture looks exactly like the pack screenshots.
    ///
    /// Idempotent: materials already on URP Lit are skipped.
    /// </summary>
    public static class MaterialURPConverter
    {
        private const string UrpLitName = "Universal Render Pipeline/Lit";
        private const string ProgressTitle = "FurnishAR • URP material upgrade";

        [MenuItem("FurnishAR/Convert Materials to URP", priority = 20)]
        public static void ConvertAllStandardMaterials()
        {
            Shader urpLit = Shader.Find(UrpLitName);
            if (urpLit == null)
            {
                Debug.LogError("[FurnishAR] '" + UrpLitName + "' shader not found. Is the URP package installed?");
                return;
            }

            string[] guids = AssetDatabase.FindAssets("t:Material", new[] { "Assets" });
            var toConvert = new List<Material>();

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null || mat.shader == null) continue;

                string shaderName = mat.shader.name;
                bool isStandard = shaderName == "Standard" || shaderName == "Standard (Specular setup)";
                if (isStandard) toConvert.Add(mat);
            }

            if (toConvert.Count == 0)
            {
                Debug.Log("[FurnishAR] All materials already use URP shaders. Nothing to convert.");
                return;
            }

            int converted = 0;
            try
            {
                for (int i = 0; i < toConvert.Count; i++)
                {
                    Material mat = toConvert[i];
                    if (mat == null) continue;

                    EditorUtility.DisplayProgressBar(ProgressTitle, mat.name,
                        (float)i / Mathf.Max(1, toConvert.Count));

                    if (ConvertToLit(mat, urpLit)) converted++;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[FurnishAR] ✅ Converted {converted}/{toConvert.Count} Standard materials to URP Lit.");
        }

        /// <summary>Converts one material in place, preserving its asset GUID/references.</summary>
        private static bool ConvertToLit(Material mat, Shader urpLit)
        {
            // Capture everything BEFORE the shader switch, because Built-in property
            // names stop resolving as soon as the shader changes.
            Texture baseMap = GetTexture(mat, "_MainTex");
            Vector2 baseScale = GetTextureScale(mat, "_MainTex");
            Vector2 baseOffset = GetTextureOffset(mat, "_MainTex");
            Color baseColor = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;

            Texture normalMap = GetTexture(mat, "_BumpMap");
            Vector2 normalScale = GetTextureScale(mat, "_BumpMap");
            float bumpScale = mat.HasProperty("_BumpScale") ? mat.GetFloat("_BumpScale") : 1f;

            Texture occlusionMap = GetTexture(mat, "_OcclusionMap");
            float occlusionStrength = mat.HasProperty("_OcclusionStrength") ? mat.GetFloat("_OcclusionStrength") : 1f;

            Texture emissionMap = GetTexture(mat, "_EmissionMap");
            Color emissionColor = mat.HasProperty("_EmissionColor") ? mat.GetColor("_EmissionColor") : Color.black;

            float metallic = mat.HasProperty("_Metallic") ? mat.GetFloat("_Metallic") : 0f;
            float glossiness = mat.HasProperty("_Glossiness") ? mat.GetFloat("_Glossiness") : 0.5f;

            mat.shader = urpLit;

            // ---- Surface / blending (opaque) ----
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 0f);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
            if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", 0f);
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 1f);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Back);
            mat.renderQueue = -1;
            mat.SetOverrideTag("RenderType", "Opaque");

            // ---- Albedo ----
            if (mat.HasProperty("_BaseMap"))
            {
                mat.SetTexture("_BaseMap", baseMap);
                mat.SetTextureScale("_BaseMap", baseScale);
                mat.SetTextureOffset("_BaseMap", baseOffset);
            }
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", baseColor);

            // ---- Normal ----
            if (mat.HasProperty("_BumpMap"))
            {
                mat.SetTexture("_BumpMap", normalMap);
                mat.SetTextureScale("_BumpMap", normalScale);
            }
            if (mat.HasProperty("_BumpScale")) mat.SetFloat("_BumpScale", bumpScale);

            // ---- Occlusion ----
            if (mat.HasProperty("_OcclusionMap")) mat.SetTexture("_OcclusionMap", occlusionMap);
            if (mat.HasProperty("_OcclusionStrength")) mat.SetFloat("_OcclusionStrength", occlusionStrength);

            // ---- Emission ----
            if (mat.HasProperty("_EmissionMap")) mat.SetTexture("_EmissionMap", emissionMap);
            if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", emissionColor);

            // ---- PBR ----
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", glossiness);
            if (mat.HasProperty("_SpecularHighlights")) mat.SetFloat("_SpecularHighlights", 1f);
            if (mat.HasProperty("_EnvironmentReflections")) mat.SetFloat("_EnvironmentReflections", 1f);

            // ---- Shader keywords ----
            SetKeyword(mat, "_NORMALMAP", normalMap != null);
            SetKeyword(mat, "_OCCLUSIONMAP", occlusionMap != null);
            SetKeyword(mat, "_EMISSION", emissionMap != null && emissionColor.maxColorComponent > 0.01f);
            SetKeyword(mat, "_METALLICSPECGLOSSMAP", false);
            SetKeyword(mat, "_SPECGLOSSMAP", false);
            SetKeyword(mat, "_ALPHATEST_ON", false);
            SetKeyword(mat, "_ALPHAPREMULTIPLY_ON", false);

            // Furniture is instanced-friendly and never animated: enable GPU instancing.
            mat.enableInstancing = true;

            EditorUtility.SetDirty(mat);
            return true;
        }

        private static void SetKeyword(Material mat, string keyword, bool on)
        {
            if (on) mat.EnableKeyword(keyword);
            else mat.DisableKeyword(keyword);
        }

        private static Texture GetTexture(Material mat, string prop)
        {
            return mat.HasProperty(prop) ? mat.GetTexture(prop) : null;
        }

        private static Vector2 GetTextureScale(Material mat, string prop)
        {
            return mat.HasProperty(prop) ? mat.GetTextureScale(prop) : Vector2.one;
        }

        private static Vector2 GetTextureOffset(Material mat, string prop)
        {
            return mat.HasProperty(prop) ? mat.GetTextureOffset(prop) : Vector2.zero;
        }
    }
}
