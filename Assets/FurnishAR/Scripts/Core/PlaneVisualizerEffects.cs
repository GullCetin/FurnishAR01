using UnityEngine;

namespace FurnishAR.Core
{
    /// <summary>
    /// Soft fade-in and gentle breathing pulse for AR plane visuals,
    /// so detected floors appear smoothly instead of popping into the scene.
    /// </summary>
    [RequireComponent(typeof(MeshRenderer))]
    public class PlaneVisualizerEffects : MonoBehaviour
    {
        [Header("Fade")]
        [SerializeField] private float fadeInDuration = 0.6f;

        [Header("Breathing")]
        [SerializeField] private float pulseAmplitude = 0.15f;
        [SerializeField] private float pulseSpeed = 1.1f;

        private MeshRenderer meshRenderer;
        private MaterialPropertyBlock block;
        private Color baseColor;
        private float age;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private void Awake()
        {
            meshRenderer = GetComponent<MeshRenderer>();
            block = new MaterialPropertyBlock();
            age = 0f;

            Material shared = meshRenderer.sharedMaterial;
            baseColor = shared != null && shared.HasProperty(BaseColorId) ? shared.GetColor(BaseColorId) : Color.white;
        }

        private void Update()
        {
            age += Time.deltaTime;

            float fadeIn = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(age / Mathf.Max(0.01f, fadeInDuration)));
            float pulse = 1f + Mathf.Sin(age * pulseSpeed) * pulseAmplitude;
            float alpha = Mathf.Clamp01(baseColor.a * fadeIn * pulse);

            block.SetColor(BaseColorId, new Color(baseColor.r, baseColor.g, baseColor.b, alpha));
            meshRenderer.SetPropertyBlock(block);
        }
    }
}
