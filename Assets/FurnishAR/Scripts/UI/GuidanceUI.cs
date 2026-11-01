using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FurnishAR.Core;

namespace FurnishAR.UI
{
    /// <summary>
    /// Displays guidance messages to the user:
    /// - "Scan" phase: tells user to move camera to detect planes
    /// - "Place" phase: tells user they can now place furniture
    /// Fades out gracefully after a delay.
    /// </summary>
    public class GuidanceUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private ARPlacementManager placementManager;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private TMP_Text guidanceText;
        [SerializeField] private Image scanIcon;

        [Header("Messages")]
        [SerializeField] private string scanMessage = "Alanı taramak için kameranızı yavaşça hareket ettirin";
        [SerializeField] private string placeMessage = "Mobilya seçip zemine dokunarak yerleştirin";

        [Header("Timing")]
        [SerializeField] private float messageDuration = 4f;
        [SerializeField] private float fadeSpeed = 3f;

        private enum GuidanceState { Scanning, PlaceReady, Hidden }
        private GuidanceState state = GuidanceState.Scanning;
        private float hideTimer;

        private void Start()
        {
            // Start with scan message
            ShowMessage(scanMessage, true);

            if (placementManager != null)
                placementManager.OnPlaneDetected += OnPlaneDetected;
        }

        private void OnDestroy()
        {
            if (placementManager != null)
                placementManager.OnPlaneDetected -= OnPlaneDetected;
        }

        private void Update()
        {
            if (state == GuidanceState.Hidden)
            {
                if (canvasGroup.alpha > 0.01f)
                    canvasGroup.alpha = Mathf.Lerp(canvasGroup.alpha, 0f, Time.deltaTime * fadeSpeed);
                else
                    canvasGroup.alpha = 0f;
                return;
            }

            // Ensure visible
            if (canvasGroup.alpha < 1f)
                canvasGroup.alpha = Mathf.Lerp(canvasGroup.alpha, 1f, Time.deltaTime * fadeSpeed * 2f);

            // Auto-hide timer for placeReady message
            if (state == GuidanceState.PlaceReady)
            {
                hideTimer -= Time.deltaTime;
                if (hideTimer <= 0f)
                    state = GuidanceState.Hidden;
            }
        }

        private void OnPlaneDetected()
        {
            if (state == GuidanceState.Scanning)
            {
                ShowMessage(placeMessage, false);
                state = GuidanceState.PlaceReady;
                hideTimer = messageDuration;
            }
        }

        private void ShowMessage(string message, bool showIcon)
        {
            if (guidanceText != null)
                guidanceText.text = message;

            if (scanIcon != null)
                scanIcon.gameObject.SetActive(showIcon);
        }
    }
}

