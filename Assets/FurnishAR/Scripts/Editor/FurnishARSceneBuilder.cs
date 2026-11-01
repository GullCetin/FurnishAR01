using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.XR.ARFoundation;
using UnityEngine.EventSystems;
using TMPro;
using FurnishAR.Core;
using FurnishAR.UI;
using FurnishAR.Data;

namespace FurnishAR.Editor
{
    /// <summary>
    /// One-click scene builder that creates the complete FurnishAR scene
    /// with proper AR setup, modern UI, and all wiring.
    /// </summary>
    public static class FurnishARSceneBuilder
    {
        // Apple-inspired color palette
        private static readonly Color PanelBg = new(0.98f, 0.98f, 0.98f, 0.85f);
        private static readonly Color CardBg = new(1f, 1f, 1f, 0.92f);
        private static readonly Color CardSelectedBg = new(0.93f, 0.96f, 1f, 0.95f);
        private static readonly Color SelectedBorder = new(0f, 0.478f, 1f, 1f); // iOS blue
        private static readonly Color TextPrimary = new(0.11f, 0.11f, 0.118f, 1f);
        private static readonly Color TextSecondary = new(0.55f, 0.55f, 0.58f, 1f);
        private static readonly Color GuidanceBg = new(0f, 0f, 0f, 0.55f);
        private static readonly Color DeleteRed = new(1f, 0.27f, 0.23f, 1f);
        private static readonly Color InteractionBg = new(0.15f, 0.15f, 0.15f, 0.8f);

        [MenuItem("FurnishAR/Build AR Scene")]
        public static void BuildScene()
        {
            // Create new scene
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // === 1. AR SESSION ===
            var arSession = new GameObject("AR Session");
            arSession.AddComponent<ARSession>();
            arSession.AddComponent<ARInputManager>();

            // === 2. XR ORIGIN (Camera) ===
            var xrOrigin = new GameObject("XR Origin");
            var xrOriginComp = xrOrigin.AddComponent<Unity.XR.CoreUtils.XROrigin>();

            var cameraOffset = new GameObject("Camera Offset");
            cameraOffset.transform.SetParent(xrOrigin.transform);

            var arCameraGO = new GameObject("AR Camera");
            arCameraGO.transform.SetParent(cameraOffset.transform);
            arCameraGO.tag = "MainCamera";

            var cam = arCameraGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 50f;

            arCameraGO.AddComponent<AudioListener>();
            var arCameraManager = arCameraGO.AddComponent<ARCameraManager>();
            arCameraGO.AddComponent<ARCameraBackground>();

            // Add UniversalAdditionalCameraData for URP
            var urpCamData = arCameraGO.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            urpCamData.renderShadows = true;

            xrOriginComp.CameraFloorOffsetObject = cameraOffset;
            xrOriginComp.Camera = cam;

            // === 3. AR MANAGERS ===
            var arRaycastManager = xrOrigin.AddComponent<ARRaycastManager>();
            var arPlaneManager = xrOrigin.AddComponent<ARPlaneManager>();
            arPlaneManager.requestedDetectionMode = UnityEngine.XR.ARSubsystems.PlaneDetectionMode.Horizontal;

            // === 4. PLACEMENT INDICATOR ===
            var placementIndicator = CreatePlacementIndicator();
            placementIndicator.transform.SetParent(null);

            // === 5. MANAGERS ===
            var managersGO = new GameObject("--- Managers ---");

            var placementMgr = managersGO.AddComponent<ARPlacementManager>();
            // Set serialized fields via SerializedObject
            var placementSO = new SerializedObject(placementMgr);
            placementSO.FindProperty("arRaycastManager").objectReferenceValue = arRaycastManager;
            placementSO.FindProperty("arPlaneManager").objectReferenceValue = arPlaneManager;
            placementSO.FindProperty("placementIndicator").objectReferenceValue = placementIndicator;
            placementSO.ApplyModifiedPropertiesWithoutUndo();

            var interactionMgr = managersGO.AddComponent<FurnitureInteractionManager>();
            var interactionSO = new SerializedObject(interactionMgr);
            interactionSO.FindProperty("arRaycastManager").objectReferenceValue = arRaycastManager;
            interactionSO.FindProperty("arCamera").objectReferenceValue = cam;
            interactionSO.ApplyModifiedPropertiesWithoutUndo();

            // === 6. CANVAS (Main UI) ===
            var canvas = CreateMainCanvas(arCameraGO);

            // === 7. EVENT SYSTEM ===
            if (Object.FindAnyObjectByType<EventSystem>() == null)
            {
                var eventSystemGO = new GameObject("EventSystem");
                eventSystemGO.AddComponent<EventSystem>();
                eventSystemGO.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            }

            // === 8. GUIDANCE UI ===
            CreateGuidanceUI(canvas.transform, placementMgr);

            // === 9. BOTTOM CATALOG ===
            var catalogUI = CreateCatalogUI(canvas.transform, placementMgr);

            // === 10. INTERACTION PANEL ===
            CreateInteractionPanel(canvas.transform, interactionMgr);

            // === 11. CARD PREFAB ===
            CreateCardPrefab();

            // Wire up the catalog UI card prefab
            var catalogComp = catalogUI.GetComponent<CatalogUI>();
            if (catalogComp != null)
            {
                var cardPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/FurnishAR/Prefabs/UI/FurnitureCardPrefab.prefab");
                if (cardPrefab != null)
                {
                    var catSO = new SerializedObject(catalogComp);
                    catSO.FindProperty("cardPrefab").objectReferenceValue = cardPrefab;
                    catSO.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            // === 12. LIGHTING ===
            var dirLight = new GameObject("Directional Light");
            var light = dirLight.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = Color.white;
            light.intensity = 1f;
            light.shadows = LightShadows.Soft;
            dirLight.transform.rotation = Quaternion.Euler(50, -30, 0);

            // Save scene
            string scenePath = "Assets/FurnishAR/Scenes/FurnishARScene.unity";
            string sceneDir = System.IO.Path.GetDirectoryName(scenePath).Replace("\\", "/");
            if (!AssetDatabase.IsValidFolder(sceneDir))
            {
                AssetDatabase.CreateFolder("Assets/FurnishAR", "Scenes");
            }
            EditorSceneManager.SaveScene(scene, scenePath);

            Debug.Log("[FurnishAR] ✅ AR Scene built successfully at " + scenePath);
        }

        private static GameObject CreatePlacementIndicator()
        {
            var indicator = new GameObject("PlacementIndicator");

            // Create a subtle ring/circle indicator
            var cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cylinder.name = "IndicatorVisual";
            cylinder.transform.SetParent(indicator.transform);
            cylinder.transform.localScale = new Vector3(0.15f, 0.001f, 0.15f);
            cylinder.transform.localPosition = Vector3.zero;

            // Remove collider from visual
            var collider = cylinder.GetComponent<Collider>();
            if (collider != null) Object.DestroyImmediate(collider);

            // Create a translucent material
            var rend = cylinder.GetComponent<Renderer>();
            if (rend != null)
            {
                var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                mat.SetFloat("_Surface", 1); // Transparent
                mat.SetFloat("_Blend", 0);   // Alpha
                mat.SetFloat("_AlphaClip", 0);
                mat.SetColor("_BaseColor", new Color(0f, 0.478f, 1f, 0.4f));
                mat.renderQueue = 3000;
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                rend.material = mat;

                // Save material
                string matPath = "Assets/FurnishAR/Materials/PlacementIndicator.mat";
                if (!AssetDatabase.LoadAssetAtPath<Material>(matPath))
                {
                    AssetDatabase.CreateAsset(mat, matPath);
                }
            }

            indicator.SetActive(false);
            return indicator;
        }

        private static Canvas CreateMainCanvas(GameObject cameraGO)
        {
            var canvasGO = new GameObject("UI Canvas");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;

            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 2340);
            scaler.matchWidthOrHeight = 0.5f;

            canvasGO.AddComponent<GraphicRaycaster>();

            return canvas;
        }

        private static void CreateGuidanceUI(Transform canvasParent, ARPlacementManager placementMgr)
        {
            // Container
            var guidanceGO = new GameObject("GuidanceUI");
            guidanceGO.transform.SetParent(canvasParent, false);

            var rect = guidanceGO.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.1f, 0.55f);
            rect.anchorMax = new Vector2(0.9f, 0.65f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            // Background pill
            var bgImage = guidanceGO.AddComponent<Image>();
            bgImage.color = GuidanceBg;
            bgImage.raycastTarget = false;

            // Round corners (use a default sprite trick)
            // We'll add the rounded look via a child mask later; for now the dark pill works great

            var canvasGroup = guidanceGO.AddComponent<CanvasGroup>();
            canvasGroup.blocksRaycasts = false;

            // Text
            var textGO = new GameObject("GuidanceText");
            textGO.transform.SetParent(guidanceGO.transform, false);
            var textRect = textGO.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(20, 0);
            textRect.offsetMax = new Vector2(-20, 0);

            var text = textGO.AddComponent<TextMeshProUGUI>();
            text.text = "Alanı taramak için kameranızı yavaşça hareket ettirin";
            text.fontSize = 26;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.raycastTarget = false;

            // Add GuidanceUI component
            var guidanceComp = guidanceGO.AddComponent<GuidanceUI>();
            var guidanceSO = new SerializedObject(guidanceComp);
            guidanceSO.FindProperty("placementManager").objectReferenceValue = placementMgr;
            guidanceSO.FindProperty("canvasGroup").objectReferenceValue = canvasGroup;
            guidanceSO.FindProperty("guidanceText").objectReferenceValue = text;
            guidanceSO.ApplyModifiedPropertiesWithoutUndo();
        }

        private static GameObject CreateCatalogUI(Transform canvasParent, ARPlacementManager placementMgr)
        {
            // === Bottom Panel Container ===
            var panelGO = new GameObject("CatalogPanel");
            panelGO.transform.SetParent(canvasParent, false);

            var panelRect = panelGO.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 0f);
            panelRect.anchorMax = new Vector2(1f, 0f);
            panelRect.pivot = new Vector2(0.5f, 0f);
            panelRect.sizeDelta = new Vector2(0f, 200f);
            panelRect.anchoredPosition = Vector2.zero;

            // Semi-transparent background
            var panelBg = panelGO.AddComponent<Image>();
            panelBg.color = PanelBg;
            panelBg.raycastTarget = true;

            // === Scroll View ===
            var scrollGO = new GameObject("ScrollView");
            scrollGO.transform.SetParent(panelGO.transform, false);

            var scrollRect = scrollGO.AddComponent<RectTransform>();
            scrollRect.anchorMin = Vector2.zero;
            scrollRect.anchorMax = Vector2.one;
            scrollRect.offsetMin = new Vector2(16, 12);
            scrollRect.offsetMax = new Vector2(-16, -12);

            var scrollView = scrollGO.AddComponent<ScrollRect>();
            scrollView.horizontal = true;
            scrollView.vertical = false;
            scrollView.movementType = ScrollRect.MovementType.Elastic;
            scrollView.elasticity = 0.1f;
            scrollView.decelerationRate = 0.135f;
            scrollView.scrollSensitivity = 1;

            scrollGO.AddComponent<Image>().color = Color.clear; // Required for scroll input
            scrollGO.AddComponent<Mask>().showMaskGraphic = false;

            // === Viewport ===
            var viewportGO = new GameObject("Viewport");
            viewportGO.transform.SetParent(scrollGO.transform, false);

            var viewportRect = viewportGO.AddComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;

            viewportGO.AddComponent<Image>().color = Color.clear;
            viewportGO.AddComponent<Mask>().showMaskGraphic = false;

            scrollView.viewport = viewportRect;

            // === Content (Horizontal Layout) ===
            var contentGO = new GameObject("Content");
            contentGO.transform.SetParent(viewportGO.transform, false);

            var contentRect = contentGO.AddComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0, 0);
            contentRect.anchorMax = new Vector2(0, 1);
            contentRect.pivot = new Vector2(0, 0.5f);
            contentRect.sizeDelta = new Vector2(0, 0);
            contentRect.anchoredPosition = Vector2.zero;

            var layout = contentGO.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 16;
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = false;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;

            var csf = contentGO.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

            scrollView.content = contentRect;

            // === CatalogUI Component ===
            var catalogComp = panelGO.AddComponent<CatalogUI>();
            var catSO = new SerializedObject(catalogComp);
            catSO.FindProperty("placementManager").objectReferenceValue = placementMgr;
            catSO.FindProperty("cardContainer").objectReferenceValue = contentGO.transform;

            // Try to load the catalog asset
            var catalog = AssetDatabase.LoadAssetAtPath<FurnitureCatalog>("Assets/FurnishAR/Resources/FurnitureCatalog.asset");
            if (catalog != null)
                catSO.FindProperty("catalog").objectReferenceValue = catalog;

            catSO.ApplyModifiedPropertiesWithoutUndo();

            return panelGO;
        }

        private static void CreateInteractionPanel(Transform canvasParent, FurnitureInteractionManager interactionMgr)
        {
            // Floating panel anchored center-bottom, above the catalog
            var panelGO = new GameObject("InteractionPanel");
            panelGO.transform.SetParent(canvasParent, false);

            var rect = panelGO.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(280, 56);
            rect.anchoredPosition = new Vector2(0, 220);

            var bgImage = panelGO.AddComponent<Image>();
            bgImage.color = InteractionBg;
            bgImage.raycastTarget = true;

            var canvasGroup = panelGO.AddComponent<CanvasGroup>();

            // Horizontal layout for buttons
            var hlg = panelGO.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 12;
            hlg.padding = new RectOffset(12, 12, 8, 8);
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = false;

            // Rotate Left button
            var rotLeftBtn = CreateIconButton(panelGO.transform, "RotateLeft", "↺", Color.white);
            // Rotate Right button
            var rotRightBtn = CreateIconButton(panelGO.transform, "RotateRight", "↻", Color.white);
            // Delete button
            var deleteBtn = CreateIconButton(panelGO.transform, "Delete", "✕", DeleteRed);

            // InteractionPanel component
            var interPanel = panelGO.AddComponent<InteractionPanel>();
            var ipSO = new SerializedObject(interPanel);
            ipSO.FindProperty("interactionManager").objectReferenceValue = interactionMgr;
            ipSO.FindProperty("canvasGroup").objectReferenceValue = canvasGroup;
            ipSO.FindProperty("rotateLeftButton").objectReferenceValue = rotLeftBtn.GetComponent<Button>();
            ipSO.FindProperty("rotateRightButton").objectReferenceValue = rotRightBtn.GetComponent<Button>();
            ipSO.FindProperty("deleteButton").objectReferenceValue = deleteBtn.GetComponent<Button>();
            ipSO.ApplyModifiedPropertiesWithoutUndo();

            panelGO.SetActive(false);
        }

        private static GameObject CreateIconButton(Transform parent, string name, string icon, Color textColor)
        {
            var btnGO = new GameObject(name);
            btnGO.transform.SetParent(parent, false);

            var btnRect = btnGO.AddComponent<RectTransform>();
            btnRect.sizeDelta = new Vector2(50, 40);

            var btnImg = btnGO.AddComponent<Image>();
            btnImg.color = new Color(1, 1, 1, 0.15f);

            var btn = btnGO.AddComponent<Button>();
            btn.targetGraphic = btnImg;

            // Icon text
            var textGO = new GameObject("Icon");
            textGO.transform.SetParent(btnGO.transform, false);
            var textRect = textGO.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            var text = textGO.AddComponent<TextMeshProUGUI>();
            text.text = icon;
            text.fontSize = 28;
            text.color = textColor;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;

            return btnGO;
        }

        private static void CreateCardPrefab()
        {
            string prefabPath = "Assets/FurnishAR/Prefabs/UI/FurnitureCardPrefab.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null)
            {
                Debug.Log("[FurnishAR] Card prefab already exists, skipping creation.");
                return;
            }

            // Root card
            var cardGO = new GameObject("FurnitureCardPrefab");

            var cardRect = cardGO.AddComponent<RectTransform>();
            cardRect.sizeDelta = new Vector2(130, 160);

            // Background with rounded corners
            var bgImage = cardGO.AddComponent<Image>();
            bgImage.color = CardBg;
            bgImage.raycastTarget = true;

            var layoutElement = cardGO.AddComponent<LayoutElement>();
            layoutElement.preferredWidth = 130;
            layoutElement.preferredHeight = 160;
            layoutElement.minWidth = 130;

            var button = cardGO.AddComponent<Button>();
            button.targetGraphic = bgImage;

            // Button colors
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.95f, 0.95f, 0.95f);
            colors.pressedColor = new Color(0.9f, 0.9f, 0.9f);
            button.colors = colors;

            // Selection border (hidden by default)
            var borderGO = new GameObject("SelectionBorder");
            borderGO.transform.SetParent(cardGO.transform, false);
            var borderRect = borderGO.AddComponent<RectTransform>();
            borderRect.anchorMin = Vector2.zero;
            borderRect.anchorMax = Vector2.one;
            borderRect.offsetMin = new Vector2(-3, -3);
            borderRect.offsetMax = new Vector2(3, 3);
            var borderImg = borderGO.AddComponent<Image>();
            borderImg.color = SelectedBorder;
            borderImg.raycastTarget = false;
            borderGO.SetActive(false);

            // Preview image area
            var previewGO = new GameObject("PreviewImage");
            previewGO.transform.SetParent(cardGO.transform, false);
            var previewRect = previewGO.AddComponent<RectTransform>();
            previewRect.anchorMin = new Vector2(0.1f, 0.25f);
            previewRect.anchorMax = new Vector2(0.9f, 0.95f);
            previewRect.offsetMin = Vector2.zero;
            previewRect.offsetMax = Vector2.zero;

            var previewImg = previewGO.AddComponent<Image>();
            previewImg.color = new Color(0.95f, 0.95f, 0.95f, 0.5f);
            previewImg.preserveAspect = true;
            previewImg.raycastTarget = false;

            // Name label
            var nameGO = new GameObject("NameLabel");
            nameGO.transform.SetParent(cardGO.transform, false);
            var nameRect = nameGO.AddComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0f, 0f);
            nameRect.anchorMax = new Vector2(1f, 0.25f);
            nameRect.offsetMin = new Vector2(8, 4);
            nameRect.offsetMax = new Vector2(-8, 0);

            var nameText = nameGO.AddComponent<TextMeshProUGUI>();
            nameText.text = "Furniture";
            nameText.fontSize = 16;
            nameText.color = TextPrimary;
            nameText.alignment = TextAlignmentOptions.Center;
            nameText.textWrappingMode = TextWrappingModes.NoWrap;
            nameText.overflowMode = TextOverflowModes.Ellipsis;
            nameText.raycastTarget = false;

            // FurnitureCard component
            var cardComp = cardGO.AddComponent<FurnitureCard>();
            var cardSO = new SerializedObject(cardComp);
            cardSO.FindProperty("previewImage").objectReferenceValue = previewImg;
            cardSO.FindProperty("cardBackground").objectReferenceValue = bgImage;
            cardSO.FindProperty("selectionBorder").objectReferenceValue = borderImg;
            cardSO.FindProperty("nameLabel").objectReferenceValue = nameText;
            cardSO.ApplyModifiedPropertiesWithoutUndo();

            // Save as prefab
            PrefabUtility.SaveAsPrefabAsset(cardGO, prefabPath);
            Object.DestroyImmediate(cardGO);

            Debug.Log("[FurnishAR] Card prefab created at " + prefabPath);
        }
    }
}

