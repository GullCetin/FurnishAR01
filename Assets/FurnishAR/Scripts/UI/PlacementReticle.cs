using UnityEngine;
using FurnishAR.Core;

namespace FurnishAR.UI
{
    /// <summary>
    /// Minimal aiming reticle: a soft ring that sticks to the horizontal plane under
    /// the screen centre. It is the only always-on 3D UI element, so the user can see
    /// that a plane is actually detected before tapping, and it doubles as a fallback
    /// target when a tap lands on a not-yet-polygonised part of the floor.
    ///
    /// Built entirely in code (quad + generated ring texture + URP Unlit material) so
    /// it cannot break because of missing prefabs or shaders.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlacementReticle : MonoBehaviour
    {
        private const float Diameter = 0.30f;
        private const float SurfaceOffset = 0.004f;

        private ARPlacementManager placement;
        private GameObject visual;
        private Renderer visualRenderer;
        private Material material;
        private Texture2D ringTexture;
        private bool visible;

        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private void Awake()
        {
            BuildVisual();
        }

        public void Initialize(ARPlacementManager manager)
        {
            placement = manager;
            if (visual != null) visual.SetActive(false);
        }

        private void BuildVisual()
        {
            // Prefer the project asset (guarantees the URP Unlit shader ships in the
            // IL2CPP build); fall back to Shader.Find for safety.
            Material template = Resources.Load<Material>("Effects/ReticleMaterial");

            if (template != null)
            {
                material = new Material(template) { name = "ReticleMaterial_Runtime", hideFlags = HideFlags.DontSave };
            }
            else
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) shader = Shader.Find("Unlit/Color");

                material = new Material(shader) { name = "ReticleMaterial", hideFlags = HideFlags.DontSave };
                if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f); // transparent
                if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);     // alpha
                if (material.HasProperty("_SrcBlend"))
                    material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                if (material.HasProperty("_DstBlend"))
                    material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
                if (material.HasProperty("_Cull")) material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }

            ringTexture = BuildRingTexture(128);
            material.SetTexture(BaseMapId, ringTexture);
            material.SetColor(BaseColorId, Color.white);

            visual = GameObject.CreatePrimitive(PrimitiveType.Quad);
            visual.name = "ReticleQuad";
            visual.transform.SetParent(transform, false);
            visual.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            visual.transform.localScale = new Vector3(Diameter, Diameter, 1f);

            var collider = visual.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            visualRenderer = visual.GetComponent<Renderer>();
            visualRenderer.sharedMaterial = material;
            visualRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            visualRenderer.receiveShadows = false;

            visual.SetActive(false);
        }

        private void Update()
        {
            if (placement == null) return;

            Vector2 center = placement.AimPoint;

            if (!placement.TryGetPlanePose(center, out Pose pose))
            {
                if (visible)
                {
                    visual.SetActive(false);
                    visible = false;
                }
                return;
            }

            if (!visible)
            {
                visual.SetActive(true);
                visible = true;
            }

            visual.transform.position = pose.position + Vector3.up * SurfaceOffset;

            // Gentle breathing so the ring reads as "live" without being distracting.
            float pulse = 1f + Mathf.Sin(Time.unscaledTime * 2.2f) * 0.04f;
            visual.transform.localScale = new Vector3(Diameter * pulse, Diameter * pulse, 1f);
        }

        private void OnDestroy()
        {
            if (material != null) Destroy(material);
            if (ringTexture != null) Destroy(ringTexture);
        }

        /// <summary>White anti-aliased annulus with a small centre dot, on transparency.</summary>
        private static Texture2D BuildRingTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "ReticleRing",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };

            float half = size * 0.5f;
            var pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - half) / half;
                    float dy = (y + 0.5f - half) / half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);

                    // Outer ring: crisp band between 0.62 and 0.80.
                    float ring = SmoothBand(r, 0.62f, 0.80f, 0.03f);
                    // Centre dot.
                    float dot = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.10f, 0.14f, r));

                    float alpha = Mathf.Max(ring, dot * 0.95f);
                    if (alpha <= 0.001f) { pixels[y * size + x] = new Color32(255, 255, 255, 0); continue; }

                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha) * 235f));
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        private static float SmoothBand(float value, float min, float max, float feather)
        {
            float inner = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(min - feather, min + feather, value));
            float outer = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(max - feather, max + feather, value));
            return Mathf.Clamp01(inner * outer);
        }
    }
}


