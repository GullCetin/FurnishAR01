using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using TMPro;
using FurnishAR.Data;
using FurnishAR.Core;

namespace FurnishAR.UI
{
    /// <summary>
    /// Manages the bottom horizontal scrolling furniture catalog.
    /// Creates cards dynamically from the FurnitureCatalog ScriptableObject.
    ///
    /// Device-grade robustness (this once failed silently on Android — never again):
    /// - Self-heals broken scene references (catalog/prefab/container) at runtime
    /// - Ensures an EventSystem with the correct UI input module exists
    /// - Any failure shows a red diagnostic chip directly on the device screen
    /// - Success shows a brief green "catalog ready" confirmation
    /// - Falls back to a procedurally-built card if the prefab path fails
    /// </summary>
    public class CatalogUI : MonoBehaviour
    {
        private const string LogTag = "[FurnishAR:Catalog]";

        [Header("References")]
        [SerializeField] private FurnitureCatalog catalog;
        [SerializeField] private ARPlacementManager placementManager;
        [SerializeField] private Transform cardContainer;
        [SerializeField] private GameObject cardPrefab;

        [Header("Layout")]
        [SerializeField] private float cardSpacing = 16f;

        [Header("Device Diagnostics")]
        [Tooltip("Show a confirmation chip on device when the catalog populates")]
        [SerializeField] private bool showReadyChip = true;

        private readonly List<FurnitureCard> cards = new();
        private FurnitureCard selectedCard;
        private TextMeshProUGUI diagText;

        public IReadOnlyList<FurnitureCard> Cards => cards;

        private void Start()
        {
            // A single device-only exception used to kill card creation with zero
            // on-screen feedback. Every failure path must now be visible in UI.
            try
            {
                EnsureReferences();
                EnsureEventSystem();
                PopulateCatalog();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"{LogTag} FATAL during setup: {e}");
                ShowChip("HATA: " + e.GetType().Name, new Color(0.85f, 0.12f, 0.10f, 1f), 0f);

                // Even after a fatal error, attempt the minimal fallback build so the
                // user keeps a usable catalog instead of a bare white bar.
                try { PopulateCatalog(); } catch { /* chip already shows the cause */ }
            }
        }

        // --------------------------------------------------------------
        // Population
        // --------------------------------------------------------------
        private void PopulateCatalog()
        {
            foreach (Transform previous in cardContainer != null ? cardContainer : transform)
                Destroy(previous.gameObject);
            cards.Clear();

            if (cardContainer == null || cardPrefab == null)
            {
                Debug.LogError($"{LogTag} Missing wiring: container=" +
                               (cardContainer != null ? "OK" : "NULL") +
                               " prefab=" + (cardPrefab != null ? "OK" : "NULL"));
                ShowChip("Referans hatasi (loglari kontrol edin)", new Color(0.85f, 0.12f, 0.10f, 1f), 0f);
                return;
            }

            if (catalog == null || catalog.items == null || catalog.items.Length == 0)
            {
                Debug.LogError($"{LogTag} Catalog is null/empty (asset=" +
                               (catalog != null ? catalog.name : "NULL") + ")");
                ShowChip("Katalog bulunamadi", new Color(0.85f, 0.12f, 0.10f, 1f), 0f);
                return;
            }

            int built = 0;
            for (int i = 0; i < catalog.items.Length; i++)
            {
                FurnitureItem item = catalog.items[i];
                if (item == null)
                {
                    Debug.LogWarning($"{LogTag} item[{i}] is null, skipped");
                    continue;
                }
                if (item.prefab == null)
                {
                    Debug.LogWarning($"{LogTag} item[{i}] '{item.displayName}' has no prefab, skipped");
                    continue;
                }

                GameObject cardGO = null;
                try
                {
                    cardGO = Instantiate(cardPrefab, cardContainer);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"{LogTag} Instantiate failed for '{item.displayName}': {e}");
                }

                if (cardGO == null)
                {
                    // Prefab path failed on this device — build a card procedurally
                    // so the catalog is always usable.
                    cardGO = BuildProceduralCard(item);
                }

                if (cardGO == null)
                {
                    Debug.LogError($"{LogTag} Could not create card for item[{i}] '{item.displayName}'");
                    continue;
                }

                cardGO.name = "Card_" + item.displayName;

                FurnitureCard card = cardGO.GetComponent<FurnitureCard>();
                if (card == null) card = cardGO.AddComponent<FurnitureCard>();
                card.Setup(item, OnCardSelected);
                cards.Add(card);
                built++;
            }

            if (cardContainer.TryGetComponent<HorizontalLayoutGroup>(out var layout))
                layout.spacing = cardSpacing;

            // Force layout computation now (protects against a first-frame CanvasScaler
            // race that can leave cards collapsed on slower devices).
            if (cardContainer is RectTransform contentRect)
                LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);

            Vector2 contentSize = cardContainer is RectTransform rt ? rt.rect.size : Vector2.zero;
            float canvasScale = transform is RectTransform panelRect ? panelRect.lossyScale.x : 1f;
            Debug.Log($"{LogTag} populated. built={built}/{catalog.items.Length} " +
                      $"content={contentSize.x:F0}x{contentSize.y:F0} canvasScale={canvasScale:F2} " +
                      $"screen={Screen.width}x{Screen.height} dpi={(int)Screen.dpi}");

            if (built == 0)
            {
                ShowChip("Kartlar olusturulamadi (loglari kontrol edin)", new Color(0.85f, 0.12f, 0.10f, 1f), 0f);
            }
            else
            {
                if (showReadyChip)
                    ShowChip($"Katalog hazir ({built} parça)", new Color(0.10f, 0.65f, 0.30f, 1f), 3.5f);
                else
                    HideChip();
            }
        }

        /// <summary>Last-resort card builder: sprite + label + button, wired like the prefab.</summary>
        private GameObject BuildProceduralCard(FurnitureItem item)
        {
            try
            {
                var go = new GameObject("Card_" + item.displayName, typeof(RectTransform));
                var rect = (RectTransform)go.transform;
                rect.sizeDelta = new Vector2(130f, 160f);

                var image = go.AddComponent<Image>();
                image.color = new Color(1f, 1f, 1f, 1f);
                image.raycastTarget = true;

                // Preview
                var previewGO = new GameObject("PreviewImage", typeof(RectTransform));
                var pvRect = (RectTransform)previewGO.transform;
                pvRect.SetParent(go.transform, false);
                pvRect.anchorMin = new Vector2(0.1f, 0.25f);
                pvRect.anchorMax = new Vector2(0.9f, 0.95f);
                pvRect.offsetMin = Vector2.zero;
                pvRect.offsetMax = Vector2.zero;
                var pv = previewGO.AddComponent<Image>();
                pv.color = new Color(1f, 1f, 1f, 1f);
                pv.preserveAspect = true;
                pv.raycastTarget = false;
                if (item.previewIcon != null) pv.sprite = item.previewIcon;

                // Label
                var labelGO = new GameObject("NameLabel", typeof(RectTransform));
                var lbRect = (RectTransform)labelGO.transform;
                lbRect.SetParent(go.transform, false);
                lbRect.anchorMin = new Vector2(0f, 0f);
                lbRect.anchorMax = new Vector2(1f, 0.25f);
                lbRect.offsetMin = Vector2.zero;
                lbRect.offsetMax = Vector2.zero;
                var label = labelGO.AddComponent<TextMeshProUGUI>();
                label.text = item.displayName;
                label.font = TmpFont();
                label.color = new Color(0.1f, 0.12f, 0.17f, 1f);
                label.fontSize = 13f;
                label.alignment = TextAlignmentOptions.Center;
                label.raycastTarget = false;

                go.AddComponent<Button>();

                var card = go.AddComponent<FurnitureCard>();
                card.WireReferences(pv, image, null, label);
                return go;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"{LogTag} BuildProceduralCard failed: {e}");
                return null;
            }
        }

        // --------------------------------------------------------------
        // Selection
        // --------------------------------------------------------------
        private void OnCardSelected(FurnitureCard card)
        {
            if (selectedCard != null && selectedCard != card)
                selectedCard.SetSelected(false);

            selectedCard = card;
            selectedCard.SetSelected(true);

            if (placementManager == null)
            {
                Debug.LogError($"{LogTag} placementManager is NULL — re-healing references");
                EnsureReferences();
            }

            if (placementManager != null && card.Item != null && card.Item.prefab != null)
            {
                placementManager.SetSelectedPrefab(
                    card.Item.prefab,
                    card.Item.placementScale,
                    card.Item.rotationOffset);
                Debug.Log($"{LogTag} selected '{card.Item.displayName}' (place mode armed)");
            }
            else
            {
                Debug.LogError($"{LogTag} selection incomplete: manager=" + (placementManager != null) +
                               " item=" + (card.Item != null) +
                               " prefab=" + (card.Item != null && card.Item.prefab != null));
                ShowChip("Yerlestirme hazir degil", new Color(0.85f, 0.12f, 0.10f, 1f), 2.5f);
            }
        }

        // --------------------------------------------------------------
        // Robustness helpers
        // --------------------------------------------------------------
        private void EnsureReferences()
        {
            if (catalog == null)
                catalog = Resources.Load<FurnitureCatalog>("FurnitureCatalog");

            if (placementManager == null)
                placementManager = FindAnyObjectByType<ARPlacementManager>();

            if (cardContainer == null)
            {
                GameObject found = GameObject.Find("Content");
                if (found != null) cardContainer = found.transform;
            }

            if (cardPrefab == null)
                cardPrefab = Resources.Load<GameObject>("Prefabs/UI/FurnitureCardPrefab");

            Debug.Log($"{LogTag} refs: catalog=" + (catalog != null ? catalog.name : "NULL") +
                      " manager=" + (placementManager != null ? placementManager.name : "NULL") +
                      " container=" + (cardContainer != null ? cardContainer.name : "NULL") +
                      " prefab=" + (cardPrefab != null ? cardPrefab.name : "NULL"));
        }

        /// <summary>
        /// Guarantees a functional UI event pipeline: an EventSystem with
        /// InputSystemUIInputModule (this project uses the Input System package).
        /// A missing/broken module makes every UI tap dead on device.
        /// </summary>
        private void EnsureEventSystem()
        {
            EventSystem es = EventSystem.current;
            if (es == null)
            {
                var esGO = new GameObject("EventSystem");
                es = esGO.AddComponent<EventSystem>();
                Debug.LogWarning($"{LogTag} EventSystem was missing — created one");
            }

            bool hasInputModule = false;
            foreach (BaseInputModule module in es.gameObject.GetComponents<BaseInputModule>())
            {
                // Exactly one supported input module should remain.
                if (module is StandaloneInputModule)
                    Destroy(module);
                else if (module is InputSystemUIInputModule)
                    hasInputModule = true;
            }

            if (!hasInputModule)
            {
                es.gameObject.AddComponent<InputSystemUIInputModule>();
                hasInputModule = true;
                Debug.Log($"{LogTag} InputSystemUIInputModule attached to EventSystem");
            }

            Debug.Log($"{LogTag} eventSystem=" + es.gameObject.name +
                      " module=" + (hasInputModule ? "InputSystemUIInputModule" : "NONE"));
        }

        // --------------------------------------------------------------
        // On-device status/diagnostic chip
        // --------------------------------------------------------------
        private void ShowChip(string message, Color color, float autoHideAfterSeconds)
        {
            if (diagText == null)
            {
                var go = new GameObject("CatalogDiagChip", typeof(RectTransform));
                var chipRect = (RectTransform)go.transform;
                chipRect.SetParent(transform, false);
                chipRect.anchorMin = new Vector2(0f, 1f);
                chipRect.anchorMax = new Vector2(1f, 1f);
                chipRect.pivot = new Vector2(0.5f, 1f);
                chipRect.anchoredPosition = new Vector2(0f, -6f);
                chipRect.sizeDelta = new Vector2(-24f, 30f);

                diagText = go.AddComponent<TextMeshProUGUI>();
                diagText.font = TmpFont();
                diagText.fontSize = 15f;
                diagText.alignment = TextAlignmentOptions.Center;
                diagText.raycastTarget = false;
            }

            diagText.gameObject.SetActive(true);
            diagText.text = message;
            diagText.color = color;

            if (autoHideAfterSeconds > 0f)
                Invoke(nameof(HideChip), autoHideAfterSeconds);
        }

        private void HideChip()
        {
            if (diagText != null) diagText.gameObject.SetActive(false);
        }

        private static TMP_FontAsset TmpFont()
        {
            TMP_FontAsset asset = TMP_Settings.defaultFontAsset;
            if (asset != null) return asset;
            TMP_FontAsset[] all = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            return all != null && all.Length > 0 ? all[0] : null;
        }
    }
}
