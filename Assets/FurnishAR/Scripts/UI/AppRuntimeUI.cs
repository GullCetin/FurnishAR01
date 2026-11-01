using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using TMPro;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using FurnishAR.Core;
using FurnishAR.Data;

namespace FurnishAR.UI
{
    /// <summary>
    /// Self-bootstrapping runtime UI for Furnish AR.
    ///
    /// WHY THIS EXISTS:
    /// Scene-serialized UI failed silently on at least one Android release build while
    /// working perfectly in the Editor. This component builds the entire catalog UI
    /// (panel, scroll view, cards, guidance, status) from pure code at app start, so it
    /// cannot be broken by stale scene data, missing prefab references, or layout drift.
    ///
    /// It also exposes a small live AR status pill (session state, tracked plane count,
    /// placed furniture count) so problems are visible on the device itself instead of
    /// requiring USB logcat access.
    /// </summary>
    [DisallowMultipleComponent]
    public class AppRuntimeUI : MonoBehaviour
    {
        /// <summary>Shown in the status pill; bump to prove which build is running.</summary>
        public const string BuildId = "V7";

        // ------------------------------------------------------------------
        // Palette
        // ------------------------------------------------------------------
        private static readonly Color Ink = new(0.09f, 0.11f, 0.16f, 1f);
        private static readonly Color MutedInk = new(0.40f, 0.44f, 0.52f, 1f);
        private static readonly Color Accent = new(0.12f, 0.47f, 1f, 1f);
        private static readonly Color PanelBg = new(0.97f, 0.98f, 1f, 0.97f);
        private static readonly Color PillBg = new(0.09f, 0.11f, 0.16f, 0.72f);
        private static readonly Color CardBg = new(1f, 1f, 1f, 1f);
        private static readonly Color CardSelectedBg = new(0.90f, 0.95f, 1f, 1f);
        private static readonly Color Danger = new(0.83f, 0.16f, 0.13f, 1f);

        // ------------------------------------------------------------------
        // Runtime references
        // ------------------------------------------------------------------
        private Canvas canvas;
        private RectTransform safeRoot;
        private RectTransform content;
        private GameObject catalogPanel;
        private TMP_Text statusText;
        private TMP_Text guidanceText;
        private Image guidanceBg;
        private ScrollRect scroll;
        private TMP_Text catalogTitle;
        private bool catalogExpanded = true;

        private FurnitureCatalog catalog;
        private ARPlacementManager placement;
        private FurnitureInteractionManager interaction;
        private ARSession session;
        private ARPlaneManager planeManager;

        private readonly List<FurnitureCard> cards = new();
        private readonly Dictionary<FurnitureCard, RectTransform> underlines = new();
        private FurnitureCard selected;

        private GameObject interactionPanel;
        private RectTransform interactionPanelRect;
        private PlacementReticle reticle;

        private GameObject diagnosticsPanel;
        private TMP_Text diagnosticsText;
        private GameObject debugCube;
        private Camera cachedCamera;
        private int statusTapCount;
        private float statusTapWindowStart;
        private float nextDiagnosticsAt;

        private string lastError;
        private float lastErrorAt = -100f;
        private float lastPlacedAt = -100f;
        private string feedbackMessage;
        private bool feedbackSuccess;
        private float feedbackAt = -100f;
        private float nextStatusAt;
        private float nextCountAt;
        private int placedCount;
        private string lastGuidanceText = "";

        // Camera-movement sampling for the diagnostics panel. Turning the phone must change
        // the camera transform; a static transform means device tracking is broken.
        private Vector3 lastDiagnosticsCameraPosition;
        private Quaternion lastDiagnosticsCameraRotation;
        private float lastDiagnosticsCameraSampleAt = -1f;

        // Reused so the per-frame UI-band measurement does not allocate.
        private readonly Vector3[] uiCorners = new Vector3[4];

        // ------------------------------------------------------------------
        // Boot (no scene wiring required)
        // ------------------------------------------------------------------
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (FindAnyObjectByType<AppRuntimeUI>() != null) return;
            var go = new GameObject("AppRuntimeUI");
            DontDestroyOnLoad(go);
            go.AddComponent<AppRuntimeUI>();
        }

        private void Awake()
        {
            Application.logMessageReceived += OnLogMessage;
            DisableLegacyPanels();
            EnsureEventSystem();
            BuildCanvas();
            FindArReferences();
            BuildCatalog();
            BuildInteractionPanel();
            HookPlacementEvents();
            ApplySafeArea(force: true);
        }

        private void OnDestroy()
        {
            Application.logMessageReceived -= OnLogMessage;
            if (debugCube != null) Destroy(debugCube);
        }

        // ------------------------------------------------------------------
        // Legacy scene UI cleanup
        // ------------------------------------------------------------------
        private void DisableLegacyPanels()
        {
            // The scene's serialized catalog/guidance are replaced by this runtime UI.
            // Set inactive (not destroyed) to avoid duplicate content or double input.
            GameObject legacyCatalog = GameObject.Find("CatalogPanel");
            if (legacyCatalog != null) legacyCatalog.SetActive(false);

            GameObject legacyGuidance = GameObject.Find("GuidanceUI");
            if (legacyGuidance != null) legacyGuidance.SetActive(false);
        }

        private void EnsureEventSystem()
        {
            EventSystem es = EventSystem.current;
            if (es == null)
            {
                var go = new GameObject("EventSystem");
                es = go.AddComponent<EventSystem>();
            }

            bool hasModule = false;
            foreach (BaseInputModule m in es.gameObject.GetComponents<BaseInputModule>())
            {
                if (m is StandaloneInputModule) Destroy(m);
                else if (m is InputSystemUIInputModule) hasModule = true;
            }
            if (!hasModule) es.gameObject.AddComponent<InputSystemUIInputModule>();
        }

        // ------------------------------------------------------------------
        // AR references / plane detection
        // ------------------------------------------------------------------
        private void FindArReferences()
        {
            placement = FindAnyObjectByType<ARPlacementManager>();
            session = FindAnyObjectByType<ARSession>();
            planeManager = FindAnyObjectByType<ARPlaneManager>();
            interaction = FindAnyObjectByType<FurnitureInteractionManager>();

            if (planeManager != null)
            {
                planeManager.enabled = true;
                planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal;
                Debug.Log("[FurnishAR:UI] plane manager ready, detection=" + planeManager.requestedDetectionMode);
            }
            else
            {
                Debug.LogError("[FurnishAR:UI] ARPlaneManager not found in scene!");
            }

            // Aiming aid: a soft ring that hugs whatever plane is under the reticle.
            if (placement != null)
            {
                var reticleGO = new GameObject("PlacementReticle");
                reticle = reticleGO.AddComponent<PlacementReticle>();
                reticle.Initialize(placement);
            }

            StartCoroutine(RetryPlaneDetection());
        }

        private IEnumerator RetryPlaneDetection()
        {
            // Some devices need the detection request re-issued after ARCore finishes
            // initializing; harmless when planes already work.
            yield return new WaitForSeconds(2.5f);
            if (planeManager != null && planeManager.trackables.count == 0)
            {
                planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal;
                Debug.Log("[FurnishAR:UI] re-requested horizontal plane detection");
            }
        }

        private void HookPlacementEvents()
        {
            if (placement == null) return;
            placement.OnFurniturePlaced -= OnFurniturePlaced;
            placement.OnFurniturePlaced += OnFurniturePlaced;
            placement.OnPlacementFeedback -= OnPlacementFeedback;
            placement.OnPlacementFeedback += OnPlacementFeedback;

            if (interaction != null)
            {
                interaction.OnFurnitureSelected -= OnFurnitureSelected;
                interaction.OnFurnitureSelected += OnFurnitureSelected;
                interaction.OnFurnitureDeselected -= OnFurnitureDeselected;
                interaction.OnFurnitureDeselected += OnFurnitureDeselected;
            }
        }

        private void OnFurniturePlaced(GameObject placed)
        {
            lastPlacedAt = Time.unscaledTime;
            placedCount++;
            Debug.Log("[FurnishAR:UI] placed '" + placed.name + "' at " + placed.transform.position);
        }

        private void OnPlacementFeedback(string message, bool success)
        {
            feedbackMessage = message;
            feedbackSuccess = success;
            feedbackAt = Time.unscaledTime;
            RefreshStatus();
        }

        private void OnFurnitureSelected(PlacedFurniture furniture)
        {
            SetCatalogExpanded(false);
            if (interactionPanelRect != null)
                interactionPanelRect.gameObject.SetActive(true);
        }

        private void OnFurnitureDeselected()
        {
            if (interactionPanelRect != null)
                interactionPanelRect.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------
        // UI construction
        // ------------------------------------------------------------------
        private void BuildCanvas()
        {
            var canvasGO = new GameObject("FurnishAR_RuntimeCanvas");
            canvasGO.transform.SetParent(transform, false);

            canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            // 360x780 = Android dp baseline: 1 UI unit ~= 1dp on typical phones,
            // so text and cards end up at native-comfortable sizes.
            scaler.referenceResolution = new Vector2(360f, 780f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0f;

            var raycaster = canvasGO.AddComponent<GraphicRaycaster>();
            raycaster.blockingObjects = GraphicRaycaster.BlockingObjects.None;

            var safeGO = new GameObject("SafeRoot", typeof(RectTransform));
            safeGO.transform.SetParent(canvasGO.transform, false);
            safeRoot = (RectTransform)safeGO.transform;
            safeRoot.anchorMin = Vector2.zero;
            safeRoot.anchorMax = Vector2.one;
            safeRoot.offsetMin = Vector2.zero;
            safeRoot.offsetMax = Vector2.zero;
            safeGO.AddComponent<SafeAreaFitter>();

            BuildStatusPill();
            BuildGuidance();
            BuildDiagnosticsPanel();
            BuildCatalogPanel();
        }

        private void BuildStatusPill()
        {
            var pillGO = new GameObject("StatusPill", typeof(RectTransform));
            pillGO.transform.SetParent(safeRoot, false);
            var rect = (RectTransform)pillGO.transform;
            rect.anchorMin = new Vector2(0.03f, 1f);
            rect.anchorMax = new Vector2(0.97f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -8f);
            rect.sizeDelta = new Vector2(0f, 38f);

            var bg = pillGO.AddComponent<Image>();
            bg.sprite = RuntimeSprites.Rounded;
            bg.type = Image.Type.Sliced;
            bg.color = PillBg;
            // Raycastable so the hidden triple-tap diagnostics gesture can receive taps,
            // and so taps here can never fall through to AR placement.
            bg.raycastTarget = true;

            // Hidden gesture: 3 quick taps open the on-device diagnostics panel.
            var button = pillGO.AddComponent<Button>();
            button.targetGraphic = bg;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(OnStatusPillTapped);

            statusText = CreateText(pillGO.transform, "StatusText", "", 13f, Color.white, TextAlignmentOptions.Center);
            Stretch(statusText.rectTransform, 12f, 3f);

            statusText.textWrappingMode = TextWrappingModes.NoWrap;
            statusText.overflowMode = TextOverflowModes.Ellipsis;
        }

        private void OnStatusPillTapped()
        {
            float now = Time.unscaledTime;
            if (now - statusTapWindowStart > 0.9f) statusTapCount = 0;
            statusTapWindowStart = now;
            statusTapCount++;

            if (statusTapCount >= 3)
            {
                statusTapCount = 0;
                ToggleDiagnostics();
            }
        }

        private void BuildGuidance()
        {
            var gGO = new GameObject("Guidance", typeof(RectTransform));
            gGO.transform.SetParent(safeRoot, false);
            var rect = (RectTransform)gGO.transform;
            rect.anchorMin = new Vector2(0.04f, 1f);
            rect.anchorMax = new Vector2(0.96f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -52f);
            rect.sizeDelta = new Vector2(0f, 52f);

            guidanceBg = gGO.AddComponent<Image>();
            guidanceBg.sprite = RuntimeSprites.Rounded;
            guidanceBg.type = Image.Type.Sliced;
            guidanceBg.color = new Color(0.98f, 0.99f, 1f, 0.93f);
            guidanceBg.raycastTarget = true;

            guidanceText = CreateText(gGO.transform, "GuidanceText", "", 14f, Ink, TextAlignmentOptions.Center);
            Stretch(guidanceText.rectTransform, 14f, 6f);
        }

        // ------------------------------------------------------------------
        // On-device diagnostics (hidden: 3 quick taps on the status pill)
        // ------------------------------------------------------------------
        private void BuildDiagnosticsPanel()
        {
            diagnosticsPanel = new GameObject("RuntimeDiagnostics", typeof(RectTransform));
            diagnosticsPanel.transform.SetParent(safeRoot, false);

            var rect = (RectTransform)diagnosticsPanel.transform;
            rect.anchorMin = new Vector2(0.04f, 1f);
            rect.anchorMax = new Vector2(0.96f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -110f);
            rect.sizeDelta = new Vector2(0f, 236f);

            var bg = diagnosticsPanel.AddComponent<Image>();
            bg.sprite = RuntimeSprites.Rounded;
            bg.type = Image.Type.Sliced;
            bg.color = new Color(0.07f, 0.08f, 0.11f, 0.94f);
            bg.raycastTarget = true; // swallow taps so the panel is never a placement surface

            diagnosticsText = CreateText(diagnosticsPanel.transform, "DiagnosticsText", "", 10.5f,
                new Color(0.82f, 0.90f, 1f, 1f), TextAlignmentOptions.TopLeft);
            Stretch(diagnosticsText.rectTransform, 12f, 10f);
            diagnosticsText.textWrappingMode = TextWrappingModes.Normal;
            diagnosticsText.overflowMode = TextOverflowModes.Overflow;

            diagnosticsPanel.SetActive(false);
        }

        private void ToggleDiagnostics()
        {
            if (diagnosticsPanel == null) return;

            bool show = !diagnosticsPanel.activeSelf;
            diagnosticsPanel.SetActive(show);
            SetDebugCube(show);

            if (show)
            {
                RefreshDiagnostics();
                nextDiagnosticsAt = Time.unscaledTime + 0.3f;
                Debug.Log("[FurnishAR:Diag] diagnostics panel opened");
            }
            else
            {
                Debug.Log("[FurnishAR:Diag] diagnostics panel closed");
            }
        }

        private void RefreshDiagnostics()
        {
            if (diagnosticsText == null) return;

            var sb = new StringBuilder();
            ARSessionState state = ARSession.state;
            NotTrackingReason reason = ARSession.notTrackingReason;
            int planes = planeManager != null ? planeManager.trackables.count : -1;

            sb.AppendLine("TEŞHİS " + BuildId + "  •  " + (Application.isEditor ? "Editor" : "Cihaz"));
            sb.AppendLine("AR: " + state + "  •  İzleme: " + reason);
            sb.AppendLine("Zemin: " + (planes < 0 ? "yok" : planes.ToString()) +
                          "  •  Mobilya: " + placedCount);
            sb.AppendLine("Yerleştirme: " + (placement != null ? "aktif" : "YOK") +
                          "  •  Seçili: " + (selected != null ? selected.Item.displayName : "—"));

            Camera cam = ResolveCamera();
            if (cam != null)
            {
                Vector3 p = cam.transform.position;
                Vector3 f = cam.transform.forward;
                sb.AppendLine($"Kamera: ({p.x:F2}, {p.y:F2}, {p.z:F2})  yön: ({f.x:F2}, {f.y:F2}, {f.z:F2})");

                var driver = cam.GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
                sb.AppendLine("Takip: " + (driver == null
                    ? "TrackedPoseDriver YOK — kamera sabit!"
                    : (driver.enabled ? "TrackedPoseDriver etkin" : "TrackedPoseDriver KAPALI")));

                float now = Time.unscaledTime;
                if (lastDiagnosticsCameraSampleAt > 0f)
                {
                    float elapsed = Mathf.Max(0.01f, now - lastDiagnosticsCameraSampleAt);
                    float moved = Vector3.Distance(p, lastDiagnosticsCameraPosition);
                    float turned = Quaternion.Angle(cam.transform.rotation, lastDiagnosticsCameraRotation);
                    sb.AppendLine($"Hareket ({elapsed:F1}s): {moved:F2} m • {turned:F1}° " +
                                  (moved > 0.01f || turned > 1f ? "izleme çalışıyor" : "değişim yok"));
                }

                lastDiagnosticsCameraPosition = p;
                lastDiagnosticsCameraRotation = cam.transform.rotation;
                lastDiagnosticsCameraSampleAt = now;
            }
            else
            {
                sb.AppendLine("Kamera: BULUNAMADI");
            }

            if (placement != null)
                sb.AppendLine("Alt UI bandı: " + placement.BottomUiPixels.ToString("F0") + " px");

            if (placement != null)
                sb.AppendLine("Son yerleşim: " + placement.LastPlacementInfo);

            PlacedFurniture last = placement != null ? placement.LastPlacedFurniture : null;
            if (last != null)
            {
                Renderer[] renderers = last.GetComponentsInChildren<Renderer>();
                int enabled = 0;
                int visible = 0;
                foreach (Renderer r in renderers)
                {
                    if (r == null || !r.enabled) continue;
                    enabled++;
                    if (r.isVisible) visible++;
                }

                Vector3 p = last.transform.position;
                sb.AppendLine($"Son obje: {last.name} ({p.x:F2}, {p.y:F2}, {p.z:F2})");
                sb.AppendLine($"Renderer: {enabled}/{renderers.Length} etkin, {visible} görünür");

                if (cam != null)
                {
                    Vector3 sp = cam.WorldToScreenPoint(ComputeBoundsCenter(last.gameObject));
                    bool onScreen = sp.z > 0f &&
                                    sp.x >= 0f && sp.x <= Screen.width &&
                                    sp.y >= 0f && sp.y <= Screen.height;
                    sb.AppendLine($"Ekran: ({sp.x:F0}, {sp.y:F0}) / ({Screen.width}×{Screen.height}) " +
                                  (onScreen ? "içinde" : "DIŞINDA"));
                }
            }
            else
            {
                sb.AppendLine("Son obje: —");
            }

            sb.AppendLine("Yeşil küp kameranın 1.2 m önünde.");
            sb.AppendLine("Küp görünmüyorsa 3D çizim sorunu;");
            sb.AppendLine("görünüyorsa yerleşim/poz sorunu.");
            sb.AppendLine("Kapat: durum çubuğuna 3 kez dokun.");

            diagnosticsText.text = sb.ToString();
        }

        private static Vector3 ComputeBoundsCenter(GameObject obj)
        {
            Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return obj.transform.position;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds.center;
        }

        /// <summary>
        /// Bright green probe cube parented to the AR camera. It answers one question
        /// no amount of log reading can: is the device drawing ANY 3D content at all?
        /// </summary>
        private void SetDebugCube(bool show)
        {
            Camera cam = ResolveCamera();
            if (cam == null) return;

            if (debugCube == null)
            {
                debugCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                debugCube.name = "FurnishAR_DebugCube";

                // Never let the probe block furniture interaction raycasts.
                Collider cubeCollider = debugCube.GetComponent<Collider>();
                if (cubeCollider != null)
                {
                    cubeCollider.enabled = false;
                    Destroy(cubeCollider);
                }

                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                {
                    Material reticle = Resources.Load<Material>("Effects/ReticleMaterial");
                    if (reticle != null) shader = reticle.shader;
                }

                if (shader != null)
                {
                    var material = new Material(shader) { name = "FurnishAR_DebugCubeMat" };
                    Color green = new(0.15f, 1f, 0.35f, 1f);
                    if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", green);
                    if (material.HasProperty("_Color")) material.SetColor("_Color", green);
                    if (material.HasProperty("_EmissionColor"))
                    {
                        material.EnableKeyword("_EMISSION");
                        material.SetColor("_EmissionColor", green * 1.8f);
                    }
                    material.hideFlags = HideFlags.DontSave;
                    debugCube.GetComponent<MeshRenderer>().sharedMaterial = material;
                }

                debugCube.transform.SetParent(cam.transform, false);
                debugCube.transform.localPosition = new Vector3(0f, 0f, 1.2f);
                debugCube.transform.localRotation = Quaternion.identity;
                debugCube.transform.localScale = Vector3.one * 0.12f;
            }

            // Re-parent in case the camera was recreated after a session restart.
            debugCube.transform.SetParent(cam.transform, false);
            debugCube.SetActive(show);
        }

        /// <summary>Resolves the AR camera without assuming the MainCamera tag survived.</summary>
        private Camera ResolveCamera()
        {
            if (cachedCamera != null) return cachedCamera;

            var cameraManager = FindAnyObjectByType<ARCameraManager>();
            if (cameraManager != null) cachedCamera = cameraManager.GetComponent<Camera>();

            if (cachedCamera == null) cachedCamera = Camera.main;

            if (cachedCamera == null)
            {
                Camera[] cameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
                if (cameras.Length > 0) cachedCamera = cameras[0];
            }

            return cachedCamera;
        }

        private void BuildCatalogPanel()
        {
            catalogPanel = new GameObject("RuntimeCatalogPanel", typeof(RectTransform));
            catalogPanel.transform.SetParent(safeRoot, false);
            var rect = (RectTransform)catalogPanel.transform;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(0f, 188f);

            var bg = catalogPanel.AddComponent<Image>();
            bg.sprite = RuntimeSprites.Rounded;
            bg.type = Image.Type.Sliced;
            bg.color = PanelBg;
            bg.raycastTarget = true; // swallow taps so they never fall through to placement

            // Title row
            TMP_Text title = CreateText(catalogPanel.transform, "CatalogTitle", "Mobilya koleksiyonu", 16f, Ink, TextAlignmentOptions.Left);
            var titleRect = title.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.offsetMin = new Vector2(16f, -30f);
            titleRect.offsetMax = new Vector2(-16f, -6f);
            title.fontStyle = FontStyles.Bold;
            catalogTitle = title;
            var titleButton = title.gameObject.AddComponent<Button>();
            title.raycastTarget = true;
            titleButton.onClick.AddListener(() => SetCatalogExpanded(!catalogExpanded));

            // Scroll view
            var scrollGO = new GameObject("CatalogScroll", typeof(RectTransform));
            scrollGO.transform.SetParent(catalogPanel.transform, false);
            var scrollRectT = (RectTransform)scrollGO.transform;
            scrollRectT.anchorMin = new Vector2(0f, 0f);
            scrollRectT.anchorMax = new Vector2(1f, 1f);
            scrollRectT.offsetMin = new Vector2(0f, 4f);
            scrollRectT.offsetMax = new Vector2(0f, -32f);

            scroll = scrollGO.AddComponent<ScrollRect>();
            scroll.horizontal = true;
            scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.elasticity = 0.12f;
            scroll.inertia = true;
            scroll.decelerationRate = 0.135f;
            scroll.scrollSensitivity = 28f;

            var viewportGO = new GameObject("Viewport", typeof(RectTransform));
            viewportGO.transform.SetParent(scrollGO.transform, false);
            var viewport = (RectTransform)viewportGO.transform;
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = Vector2.zero;
            viewport.offsetMax = Vector2.zero;
            viewportGO.AddComponent<RectMask2D>();

            var contentGO = new GameObject("CatalogContent", typeof(RectTransform));
            contentGO.transform.SetParent(viewportGO.transform, false);
            content = (RectTransform)contentGO.transform;
            content.anchorMin = new Vector2(0f, 0f);
            content.anchorMax = new Vector2(0f, 1f);
            content.pivot = new Vector2(0f, 0.5f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, 0f);

            var layout = contentGO.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(14, 14, 6, 6);
            layout.spacing = 10f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var fitter = contentGO.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

            scroll.content = content;
            scroll.viewport = viewport;
        }

        // ------------------------------------------------------------------
        // Placed-furniture control panel (rotate / delete / done)
        // ------------------------------------------------------------------
        private void BuildInteractionPanel()
        {
            var panelGO = new GameObject("RuntimeInteractionPanel", typeof(RectTransform));
            panelGO.transform.SetParent(safeRoot, false);
            interactionPanel = panelGO;

            interactionPanelRect = (RectTransform)panelGO.transform;
            interactionPanelRect.anchorMin = new Vector2(0.5f, 0f);
            interactionPanelRect.anchorMax = new Vector2(0.5f, 0f);
            interactionPanelRect.pivot = new Vector2(0.5f, 0f);
            interactionPanelRect.anchoredPosition = new Vector2(0f, 188f);
            interactionPanelRect.sizeDelta = new Vector2(310f, 54f);

            var bg = panelGO.AddComponent<Image>();
            bg.sprite = RuntimeSprites.Rounded;
            bg.type = Image.Type.Sliced;
            bg.color = PanelBg;
            bg.raycastTarget = true;

            var layout = panelGO.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(5, 5, 5, 5);
            layout.spacing = 5f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            AddActionButton(panelGO.transform, "Sola", () => Rotate(-30f), Ink);
            AddActionButton(panelGO.transform, "Sağa", () => Rotate(30f), Ink);
            AddActionButton(panelGO.transform, "Sil", Delete, Danger);
            AddActionButton(panelGO.transform, "Bitti", Done, MutedInk);

            panelGO.SetActive(false);
        }

        private void AddActionButton(Transform parent, string label, System.Action onClick, Color textColor)
        {
            var go = new GameObject("Action_" + label, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = 68f;
            le.preferredHeight = 44f;
            le.minWidth = 68f;
            le.minHeight = 44f;

            var image = go.AddComponent<Image>();
            image.sprite = RuntimeSprites.Rounded;
            image.type = Image.Type.Sliced;
            image.color = new Color(0.93f, 0.95f, 0.99f, 1f);

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.90f, 0.93f, 0.98f, 1f);
            colors.pressedColor = new Color(0.82f, 0.87f, 0.95f, 1f);
            colors.selectedColor = Color.white;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            button.onClick.AddListener(() => onClick());

            TMP_Text text = CreateText(go.transform, "Label", label, 12f, textColor, TextAlignmentOptions.Center);
            Stretch(text.rectTransform, 2f, 2f);
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
        }

        private void Rotate(float degrees)
        {
            if (interaction == null || interaction.SelectedFurniture == null) return;
            interaction.RotateSelected(degrees);
        }

        private void Delete()
        {
            if (interaction == null || interaction.SelectedFurniture == null) return;
            interaction.DeleteSelected();
            placedCount = FindObjectsByType<PlacedFurniture>(FindObjectsSortMode.None).Length;
            OnPlacementFeedback("Mobilya silindi", true);
        }

        private void Done()
        {
            if (interaction == null) return;
            interaction.DeselectFurniture();
        }

        // ------------------------------------------------------------------
        // Catalog cards (pure runtime, no prefab dependency)
        // ------------------------------------------------------------------
        private void BuildCatalog()
        {
            catalog = Resources.Load<FurnitureCatalog>("FurnitureCatalog");

            if (catalog == null)
            {
                Debug.LogError("[FurnishAR:UI] FurnitureCatalog asset not found in Resources!");
                lastError = "Katalog bulunamadi";
                lastErrorAt = Time.unscaledTime;
                return;
            }

            if (catalog.items == null || catalog.items.Length == 0)
            {
                Debug.LogError("[FurnishAR:UI] Catalog has no items!");
                lastError = "Katalog bos";
                lastErrorAt = Time.unscaledTime;
                return;
            }

            int built = 0;
            foreach (FurnitureItem item in catalog.items)
            {
                if (item == null || item.prefab == null) continue;
                FurnitureCard card = CreateCard(item);
                if (card != null)
                {
                    cards.Add(card);
                    built++;
                }
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            Debug.Log($"[FurnishAR:UI] runtime catalog built {built}/{catalog.items.Length} cards " +
                      $"content={content.rect.width:F0}x{content.rect.height:F0} screen={Screen.width}x{Screen.height}");

            if (built == 0)
            {
                lastError = "Kart olusturulamadi";
                lastErrorAt = Time.unscaledTime;
            }
        }

        private FurnitureCard CreateCard(FurnitureItem item)
        {
            var cardGO = new GameObject("Card_" + item.displayName, typeof(RectTransform));
            cardGO.transform.SetParent(content, false);

            var rect = (RectTransform)cardGO.transform;
            var le = cardGO.AddComponent<LayoutElement>();
            le.preferredWidth = 100f;
            le.preferredHeight = 126f;
            le.minWidth = 100f;
            le.minHeight = 126f;
            le.flexibleWidth = 0f;

            // Soft shadow (first child -> renders behind the background)
            var shadowGO = new GameObject("Shadow", typeof(RectTransform));
            shadowGO.transform.SetParent(cardGO.transform, false);
            var shadowRect = (RectTransform)shadowGO.transform;
            Stretch(shadowRect, 0f, 0f);
            shadowRect.offsetMin = new Vector2(-1.5f, -3f);
            shadowRect.offsetMax = new Vector2(1.5f, 1f);
            var shadow = shadowGO.AddComponent<Image>();
            shadow.sprite = RuntimeSprites.Rounded;
            shadow.type = Image.Type.Sliced;
            shadow.color = new Color(0f, 0f, 0f, 0.10f);
            shadow.raycastTarget = false;

            // Card background (raycast target, receives taps)
            var bgGO = new GameObject("Background", typeof(RectTransform));
            bgGO.transform.SetParent(cardGO.transform, false);
            Stretch((RectTransform)bgGO.transform, 0f, 0f);
            var bg = bgGO.AddComponent<Image>();
            bg.sprite = RuntimeSprites.Rounded;
            bg.type = Image.Type.Sliced;
            bg.color = CardBg;
            bg.raycastTarget = true;

            // Preview
            var previewGO = new GameObject("PreviewImage", typeof(RectTransform));
            previewGO.transform.SetParent(cardGO.transform, false);
            var previewRect = (RectTransform)previewGO.transform;
            previewRect.anchorMin = new Vector2(0.10f, 0.30f);
            previewRect.anchorMax = new Vector2(0.90f, 0.95f);
            previewRect.offsetMin = Vector2.zero;
            previewRect.offsetMax = Vector2.zero;
            var preview = previewGO.AddComponent<Image>();
            preview.preserveAspect = true;
            preview.raycastTarget = false;
            if (item.previewIcon != null)
            {
                preview.sprite = item.previewIcon;
                preview.color = Color.white;
            }
            else
            {
                preview.color = new Color(0.86f, 0.88f, 0.92f, 1f);
            }

            // Label
            TMP_Text label = CreateText(cardGO.transform, "NameLabel", item.displayName, 13f, Ink, TextAlignmentOptions.Center);
            var labelRect = label.rectTransform;
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(1f, 0.26f);
            labelRect.offsetMin = new Vector2(3f, 1f);
            labelRect.offsetMax = new Vector2(-3f, 0f);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.raycastTarget = false;

            // Selection underline
            var underlineGO = new GameObject("SelectionUnderline", typeof(RectTransform));
            underlineGO.transform.SetParent(cardGO.transform, false);
            var underlineRect = (RectTransform)underlineGO.transform;
            underlineRect.anchorMin = new Vector2(0.5f, 0f);
            underlineRect.anchorMax = new Vector2(0.5f, 0f);
            underlineRect.pivot = new Vector2(0.5f, 0f);
            underlineRect.anchoredPosition = new Vector2(0f, 6f);
            underlineRect.sizeDelta = new Vector2(44f, 3f);
            var underline = underlineGO.AddComponent<Image>();
            underline.color = Accent;
            underline.raycastTarget = false;
            underlineGO.SetActive(false);

            var button = cardGO.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = bg;

            var card = cardGO.AddComponent<FurnitureCard>();
            card.WireReferences(preview, bg, null, label);
            card.Setup(item, OnCardSelected);

            underlines[card] = underlineRect;
            return card;
        }

        private void SetCatalogExpanded(bool expanded)
        {
            catalogExpanded = expanded;
            if (catalogPanel == null) return;
            ((RectTransform)catalogPanel.transform).sizeDelta = new Vector2(0f, expanded ? 188f : 48f);
            if (scroll != null) scroll.gameObject.SetActive(expanded);
            if (catalogTitle != null) catalogTitle.text = expanded ? "Mobilya koleksiyonu  −" : (selected != null ? selected.Item.displayName + "  ·  Değiştir +" : "Mobilya seç +");
            if (interactionPanelRect != null) interactionPanelRect.anchoredPosition = new Vector2(0f, expanded ? 200f : 60f);
            Canvas.ForceUpdateCanvases();
            if (placement != null) placement.BottomUiPixels = ComputeBottomUiPixels();
        }

        private void OnCardSelected(FurnitureCard card)
        {
            if (selected != null && selected != card)
            {
                selected.SetSelected(false);
                SetUnderline(selected, false);
            }

            selected = card;
            selected.SetSelected(true);
            SetUnderline(card, true);

            if (placement == null) placement = FindAnyObjectByType<ARPlacementManager>();
            if (placement == null)
            {
                lastError = "AR yerlestirme yoneticisi yok";
                lastErrorAt = Time.unscaledTime;
                return;
            }

            placement.SetSelectedPrefab(card.Item.prefab, card.Item.placementScale, card.Item.rotationOffset);
            SetCatalogExpanded(false);
            Debug.Log("[FurnishAR:UI] selected '" + card.Item.displayName + "' — tap the floor to place");
            nextStatusAt = 0f; // refresh guidance immediately
        }

        private void SetUnderline(FurnitureCard card, bool on)
        {
            if (underlines.TryGetValue(card, out RectTransform rt) && rt != null)
                rt.gameObject.SetActive(on);
        }

        // ------------------------------------------------------------------
        // Live status / guidance
        // ------------------------------------------------------------------
        private void Update()
        {
            ApplySafeArea(force: false);

            // Tell the placement system how much of the bottom of the screen the UI covers,
            // so furniture is never spawned or dragged behind the catalog/control bars.
            if (placement != null)
                placement.BottomUiPixels = ComputeBottomUiPixels();

            float now = Time.unscaledTime;
            if (now >= nextStatusAt)
            {
                nextStatusAt = now + 0.25f;
                RefreshStatus();
            }

            if (now >= nextCountAt)
            {
                nextCountAt = now + 1f;
                placedCount = FindObjectsByType<PlacedFurniture>(FindObjectsSortMode.None).Length;
            }

            if (diagnosticsPanel != null && diagnosticsPanel.activeSelf && now >= nextDiagnosticsAt)
            {
                nextDiagnosticsAt = now + 0.3f;
                RefreshDiagnostics();
            }
        }

        private void RefreshStatus()
        {
            if (statusText == null) return;

            int planes = 0;
            ARSessionState state = ARSession.state;
            NotTrackingReason reason = ARSession.notTrackingReason;

            if (planeManager != null) planes = planeManager.trackables.count;

#if UNITY_EDITOR
            // Editor simulator: present a healthy tracking state so the UI behaves
            // exactly like a device with a detected floor.
            if (EditorSimulator.SimActive)
            {
                state = ARSessionState.SessionTracking;
                reason = NotTrackingReason.None;
                planes = Mathf.Max(planes, 1);
            }
#endif

            string stateIcon = "●";
            string stateWord;
            switch (state)
            {
                case ARSessionState.SessionTracking:
                    stateWord = planeManager != null && planes > 0 ? "AR hazır" : "AR tarıyor";
                    break;
                case ARSessionState.SessionInitializing:
                    stateWord = "AR başlatılıyor";
                    stateIcon = "◐";
                    break;
                case ARSessionState.Ready:
                    stateWord = "AR hazır (oturum bekleniyor)";
                    stateIcon = "◐";
                    break;
                case ARSessionState.CheckingAvailability:
                    stateWord = "AR kontrol ediliyor";
                    stateIcon = "◐";
                    break;
                case ARSessionState.NeedsInstall:
                    stateWord = "ARCore kurulmalı";
                    stateIcon = "■";
                    break;
                case ARSessionState.Installing:
                    stateWord = "ARCore kuruluyor";
                    stateIcon = "◐";
                    break;
                case ARSessionState.Unsupported:
                    stateWord = "AR desteklenmiyor";
                    stateIcon = "■";
                    break;
                default:
                    stateWord = state.ToString();
                    break;
            }

            statusText.text = $"Furnish AR   ·   {stateWord}   ·   {placedCount} mobilya";

            // Guidance line
            string guidance;
            Color bg = new(0.98f, 0.99f, 1f, 0.93f);
            Color fg = Ink;

            bool recentError = lastError != null && Time.unscaledTime - lastErrorAt < 8f;
            bool recentPlace = Time.unscaledTime - lastPlacedAt < 1.6f;
            bool recentFeedback = feedbackMessage != null && Time.unscaledTime - feedbackAt < 3.5f;

            if (recentFeedback)
            {
                guidance = feedbackMessage;
                if (feedbackSuccess)
                {
                    bg = new Color(0.92f, 1f, 0.94f, 0.97f);
                    fg = new Color(0.10f, 0.55f, 0.27f, 1f);
                }
                else
                {
                    bg = new Color(1f, 0.93f, 0.90f, 0.97f);
                    fg = Danger;
                }
            }
            else if (recentError)
            {
                guidance = "Hata: " + lastError;
                bg = new Color(1f, 0.94f, 0.93f, 0.97f);
                fg = Danger;
            }
            else if (recentPlace)
            {
                guidance = "Yerleştirildi";
                bg = new Color(0.92f, 1f, 0.94f, 0.97f);
                fg = new Color(0.10f, 0.55f, 0.27f, 1f);
            }
            else if (session == null)
            {
                guidance = "AR oturumu bulunamadı (sahne eksik)";
                bg = new Color(1f, 0.94f, 0.93f, 0.97f);
                fg = Danger;
            }
            else if (state != ARSessionState.SessionTracking)
            {
                if (state == ARSessionState.Unsupported)
                    guidance = "AR cihazda desteklenmiyor — ARCore uyumluluğunu kontrol edin";
                else if (state == ARSessionState.NeedsInstall)
                    guidance = "Google Play Services for AR (ARCore) kurulmalı";
                else if (state == ARSessionState.Installing)
                    guidance = "ARCore kuruluyor… lütfen bekleyin";
                else
                    guidance = "AR başlatılıyor… telefonu zemine doğru tutun";
            }
            else if (reason != NotTrackingReason.None)
            {
                guidance = "İzleme sınırlı: " + ReasonText(reason);
                bg = new Color(1f, 0.97f, 0.90f, 0.97f);
            }
            else if (planeManager == null)
            {
                guidance = "Zemin algılama kapalı (ARPlaneManager yok)";
                bg = new Color(1f, 0.94f, 0.93f, 0.97f);
                fg = Danger;
            }
            else if (planes == 0)
            {
                guidance = "Zemin taranıyor… telefonu yavaşça sağa-sola hareket ettirin";
                bg = new Color(1f, 0.97f, 0.90f, 0.97f);
            }
            else if (interaction != null && interaction.SelectedFurniture != null)
            {
                guidance = "Mobilya seçili — taşımak için sürükleyin, döndürmek/silmek için paneli kullanın";
            }
            else if (selected == null)
            {
                guidance = $"Zemin hazır ({planes}) — aşağıdan bir mobilya seçin";
                bg = new Color(0.92f, 1f, 0.94f, 0.97f);
            }
            else
            {
                guidance = selected.Item.displayName + " seçili — yerleştirmek için zemine dokunun";
            }

            if (guidance != lastGuidanceText && guidanceText != null)
            {
                guidanceText.text = guidance;
                lastGuidanceText = guidance;
            }
            if (guidanceBg != null) guidanceBg.color = bg;
            if (guidanceText != null) guidanceText.color = fg;
        }

        /// <summary>
        /// Top edge, in screen pixels, of whatever UI currently covers the bottom of the AR
        /// view. ARPlacementManager uses it to keep pieces out from behind the bars.
        /// </summary>
        private float ComputeBottomUiPixels()
        {
            float top = 0f;

            if (catalogPanel != null && catalogPanel.activeInHierarchy)
                top = Mathf.Max(top, MaxScreenY((RectTransform)catalogPanel.transform));

            if (interactionPanelRect != null && interactionPanelRect.gameObject.activeInHierarchy)
                top = Mathf.Max(top, MaxScreenY(interactionPanelRect));

            return top;
        }

        private float MaxScreenY(RectTransform rect)
        {
            // ScreenSpaceOverlay: world corners already map to screen pixels.
            rect.GetWorldCorners(uiCorners);

            float maxY = 0f;
            for (int i = 0; i < uiCorners.Length; i++)
            {
                Vector3 screenPoint = RectTransformUtility.WorldToScreenPoint(null, uiCorners[i]);
                if (screenPoint.y > maxY) maxY = screenPoint.y;
            }

            return maxY;
        }

        private static string ReasonText(NotTrackingReason reason)
        {
            switch (reason)
            {
                case NotTrackingReason.Initializing: return "kamera ısınıyor";
                case NotTrackingReason.Relocalizing: return "konum yeniden bulunuyor";
                case NotTrackingReason.InsufficientLight: return "ışık yetersiz — daha aydınlık bir yer";
                case NotTrackingReason.ExcessiveMotion: return "çok hızlı hareket — yavaşlayın";
                case NotTrackingReason.InsufficientFeatures: return "zemin dokusu zayıf — desenli yüzey deneyin";
                case NotTrackingReason.CameraUnavailable: return "kamera kullanılamıyor";
                default: return reason.ToString();
            }
        }

        private void OnLogMessage(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error) return;

            // Ignore known, harmless editor/AI-assistant noise.
            if (condition.Contains("NoSubscription")) return;
            if (condition.Contains("StopSubsystems")) return;
            if (condition.Contains("XRSessionSubsystem")) return;
            if (condition.Contains("XRInputSubsystem")) return;
            if (condition.Contains("XRCameraSubsystem")) return;
            if (condition.Contains("XRRaycastSubsystem")) return;
            if (condition.Contains("XRPlaneSubsystem")) return;
            if (condition.Contains("Tracked Pose Driver")) return;

            string trimmed = condition.Length > 120 ? condition.Substring(0, 120) + "…" : condition;
            lastError = trimmed;
            lastErrorAt = Time.unscaledTime;
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------
        private void ApplySafeArea(bool force)
        {
            if (safeRoot == null) return;

            Rect safe = Screen.safeArea;
            var resolution = new Vector2(Screen.width, Screen.height);
            if (resolution.x <= 0 || resolution.y <= 0) return;

            Vector2 min = new(safe.xMin / resolution.x, safe.yMin / resolution.y);
            Vector2 max = new(safe.xMax / resolution.x, safe.yMax / resolution.y);

            if (!force && safeRoot.anchorMin == min && safeRoot.anchorMax == max) return;

            safeRoot.anchorMin = min;
            safeRoot.anchorMax = max;
            safeRoot.offsetMin = Vector2.zero;
            safeRoot.offsetMax = Vector2.zero;
        }

        private static void Stretch(RectTransform rect, float horizontalPadding, float verticalPadding)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(horizontalPadding, verticalPadding);
            rect.offsetMax = new Vector2(-horizontalPadding, -verticalPadding);
        }

        private static TMP_Text CreateText(Transform parent, string name, string text,
            float size, Color color, TextAlignmentOptions alignment)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var t = go.AddComponent<TextMeshProUGUI>();
            if (TMP_Settings.defaultFontAsset != null)
                t.font = TMP_Settings.defaultFontAsset;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = alignment;
            t.raycastTarget = false;
            t.richText = false;

            return t;
        }
    }

    /// <summary>
    /// Tiny runtime sprite factory: 9-sliced rounded rectangle generated in code so
    /// the UI needs zero sprite assets from the build (nothing to strip, nothing missing).
    /// </summary>
    internal static class RuntimeSprites
    {
        private static Sprite rounded;

        public static Sprite Rounded
        {
            get
            {
                if (rounded != null) return rounded;

                const int size = 96;
                const float radius = 30f;
                const int border = 32;

                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    name = "RuntimeRounded",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.DontSave
                };

                var pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float px = x + 0.5f;
                        float py = y + 0.5f;

                        // Distance from the nearest corner arc center
                        float cx = Mathf.Clamp(px, radius, size - radius);
                        float cy = Mathf.Clamp(py, radius, size - radius);
                        float dist = Vector2.Distance(new Vector2(px, py), new Vector2(cx, cy));

                        float alpha = Mathf.Clamp01(radius - dist + 0.5f);
                        pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                    }
                }

                tex.SetPixels32(pixels);
                tex.Apply();

                rounded = Sprite.Create(
                    tex,
                    new Rect(0, 0, size, size),
                    new Vector2(0.5f, 0.5f),
                    100f,
                    0,
                    SpriteMeshType.FullRect,
                    new Vector4(border, border, border, border));
                rounded.name = "RuntimeRoundedSprite";
                rounded.hideFlags = HideFlags.DontSave;
                return rounded;
            }
        }
    }
}

