using UnityEngine;
using UnityEngine.UI;
using FurnishAR.Core;

namespace FurnishAR.UI
{
    /// <summary>
    /// Shows rotate and delete controls when a placed furniture is selected.
    /// Hides automatically when nothing is selected.
    /// Minimal, non-intrusive design — small floating panel.
    /// </summary>
    public class InteractionPanel : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private FurnitureInteractionManager interactionManager;
        [SerializeField] private CanvasGroup canvasGroup;

        [Header("Buttons")]
        [SerializeField] private Button rotateLeftButton;
        [SerializeField] private Button rotateRightButton;
        [SerializeField] private Button deleteButton;
        [SerializeField] private Button deselectButton;

        [Header("Settings")]
        [SerializeField] private float rotateAngle = 45f;
        [SerializeField] private float fadeSpeed = 8f;

        private bool shouldShow;

        private void Start()
        {
            // Start hidden
            if (canvasGroup != null)
                canvasGroup.alpha = 0f;
            gameObject.SetActive(false);

            // Hook up buttons
            if (rotateLeftButton != null)
                rotateLeftButton.onClick.AddListener(() => interactionManager?.RotateSelected(-rotateAngle));

            if (rotateRightButton != null)
                rotateRightButton.onClick.AddListener(() => interactionManager?.RotateSelected(rotateAngle));

            if (deleteButton != null)
                deleteButton.onClick.AddListener(() => interactionManager?.DeleteSelected());

            if (deselectButton != null)
                deselectButton.onClick.AddListener(() => interactionManager?.DeselectFurniture());

            // Listen for selection events
            if (interactionManager != null)
            {
                interactionManager.OnFurnitureSelected += OnFurnitureSelected;
                interactionManager.OnFurnitureDeselected += OnFurnitureDeselected;
            }
        }

        private void OnDestroy()
        {
            if (interactionManager != null)
            {
                interactionManager.OnFurnitureSelected -= OnFurnitureSelected;
                interactionManager.OnFurnitureDeselected -= OnFurnitureDeselected;
            }
        }

        private void Update()
        {
            if (canvasGroup == null) return;

            float targetAlpha = shouldShow ? 1f : 0f;
            canvasGroup.alpha = Mathf.Lerp(canvasGroup.alpha, targetAlpha, Time.deltaTime * fadeSpeed);

            if (!shouldShow && canvasGroup.alpha < 0.01f)
                gameObject.SetActive(false);
        }

        private void OnFurnitureSelected(PlacedFurniture furniture)
        {
            shouldShow = true;
            gameObject.SetActive(true);
        }

        private void OnFurnitureDeselected()
        {
            shouldShow = false;
        }
    }
}

