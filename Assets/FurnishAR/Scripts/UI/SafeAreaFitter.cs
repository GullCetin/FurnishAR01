using UnityEngine;

namespace FurnishAR.UI
{
    /// <summary>
    /// Anchors this RectTransform to the screen-safe area so UI survives
    /// notches, camera cutouts and home-indicator bars on modern Android phones.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    public class SafeAreaFitter : MonoBehaviour
    {
        private RectTransform rect;
        private Rect lastSafeArea = new(0f, 0f, -1f, -1f);
        private Vector2 lastResolution;

        private void Awake()
        {
            rect = (RectTransform)transform;
            Apply();
        }

        private void Update()
        {
            Apply();
        }

        private void Apply()
        {
            Rect safeArea = Screen.safeArea;
            Vector2 resolution = new(Screen.width, Screen.height);

            if (resolution == lastResolution && safeArea == lastSafeArea) return;

            lastSafeArea = safeArea;
            lastResolution = resolution;

            Vector2 anchorMin = new(safeArea.xMin / resolution.x, safeArea.yMin / resolution.y);
            Vector2 anchorMax = new(safeArea.xMax / resolution.x, safeArea.yMax / resolution.y);

            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
