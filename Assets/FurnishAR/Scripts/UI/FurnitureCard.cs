using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FurnishAR.Data;

namespace FurnishAR.UI
{
    /// <summary>
    /// Represents a single furniture card in the bottom catalog scroll view.
    /// Shows a preview image and display name. Handles selection visual state.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class FurnitureCard : MonoBehaviour
    {
        [Header("Card UI Elements")]
        [SerializeField] private Image previewImage;
        [SerializeField] private Image cardBackground;
        [SerializeField] private Image selectionBorder;
        [SerializeField] private TMP_Text nameLabel;

        [Header("Colors")]
        [SerializeField] private Color normalBgColor = new(1f, 1f, 1f, 0.9f);
        [SerializeField] private Color selectedBgColor = new(0.93f, 0.96f, 1f, 0.95f);
        [SerializeField] private Color selectedBorderColor = new(0.2f, 0.5f, 1f, 1f);

        private FurnitureItem item;
        private Button button;
        private bool isSelected;
        private System.Action<FurnitureCard> onSelected;

        public FurnitureItem Item => item;

        private void Awake()
        {
            button = GetComponent<Button>();
        }

        public void Setup(FurnitureItem furnitureItem, System.Action<FurnitureCard> selectionCallback)
        {
            item = furnitureItem;
            onSelected = selectionCallback;

            // Set display name
            if (nameLabel != null)
                nameLabel.text = item.displayName;

            // Set preview image
            if (previewImage != null && item.previewIcon != null)
            {
                previewImage.sprite = item.previewIcon;
                previewImage.preserveAspect = true;
            }

            // Hook up button
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(OnCardTapped);

            // Initial unselected state
            SetSelected(false);
        }

        /// <summary>
        /// Assigns UI element references programmatically (used by the procedural
        /// fallback card builder in CatalogUI when the prefab path fails on device).
        /// </summary>
        public void WireReferences(Image preview, Image background, Image border, TMP_Text label)
        {
            previewImage = preview;
            cardBackground = background;
            selectionBorder = border;
            nameLabel = label;
            button = GetComponent<Button>();
        }

        private void OnCardTapped()
        {
            onSelected?.Invoke(this);
        }

        public void SetSelected(bool selected)
        {
            isSelected = selected;

            if (cardBackground != null)
                cardBackground.color = selected ? selectedBgColor : normalBgColor;

            if (selectionBorder != null)
            {
                selectionBorder.gameObject.SetActive(selected);
                selectionBorder.color = selectedBorderColor;
            }

            // Subtle scale animation
            transform.localScale = selected ? Vector3.one * 1.05f : Vector3.one;
        }
    }
}

