using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace FurnishAR.Core
{
    /// <summary>Places metre-scale furniture only at tracked, upward-facing floor hits.</summary>
    [DefaultExecutionOrder(0)]
    public class ARPlacementManager : MonoBehaviour
    {
        [SerializeField] private ARRaycastManager arRaycastManager;
        [SerializeField] private ARPlaneManager arPlaneManager;
        [SerializeField] private GameObject placementIndicator;
        [SerializeField] private float indicatorLerpSpeed = 15f;
        public event System.Action<GameObject> OnFurniturePlaced;
        public event System.Action OnPlaneDetected;
        public event System.Action<string, bool> OnPlacementFeedback;
        private readonly List<ARRaycastHit> hits = new();
        private GameObject selectedPrefab;
        private float placementScale = 1f, rotationOffset;
        private Camera arCamera;
        private FurnitureInteractionManager interaction;
        private ARAnchorManager anchors;
        private bool pressPending;
        private Vector2 pressPosition;
        private InputAction positionAction, rotationAction, trackingAction;
        public bool HasPlaneDetected { get; private set; }
        public PlacedFurniture LastPlacedFurniture { get; private set; }
        public string LastPlacementInfo { get; private set; } = "—";
        public float BottomUiPixels { get; set; }
        public bool CameraTrackingActive { get; private set; }
        public Vector2 AimPoint => new(Screen.width * .5f, Mathf.Lerp(BottomUiPixels, Screen.height, .45f));

        private void Awake()
        {
            arRaycastManager ??= FindAnyObjectByType<ARRaycastManager>();
            arPlaneManager ??= FindAnyObjectByType<ARPlaneManager>();
            interaction = FindAnyObjectByType<FurnitureInteractionManager>();
            var manager = FindAnyObjectByType<ARCameraManager>();
            arCamera = manager != null ? manager.GetComponent<Camera>() : Camera.main;
            var origin = FindAnyObjectByType<XROrigin>();
            if (origin != null)
            {
                anchors = origin.GetComponent<ARAnchorManager>();
                if (anchors == null) anchors = origin.gameObject.AddComponent<ARAnchorManager>();
            }
            if (placementIndicator != null) placementIndicator.SetActive(false);
        }

        private void Start()
        {
#if UNITY_EDITOR
            if (GetComponent<EditorSimulator>() == null) gameObject.AddComponent<EditorSimulator>();
            if (arCamera != null && arCamera.TryGetComponent<TrackedPoseDriver>(out var editorDriver)) editorDriver.enabled = false;
#else
            ConfigureTracking();
#endif
        }

        private void ConfigureTracking()
        {
            if (arCamera == null) return;
            var driver = arCamera.GetComponent<TrackedPoseDriver>();
            if (driver == null) driver = arCamera.gameObject.AddComponent<TrackedPoseDriver>();
            driver.enabled = false;
            positionAction = new InputAction("AR Position", InputActionType.Value, "<HandheldARInputDevice>/devicePosition", expectedControlType: "Vector3");
            positionAction.AddBinding("<XRHMD>/centerEyePosition");
            rotationAction = new InputAction("AR Rotation", InputActionType.Value, "<HandheldARInputDevice>/deviceRotation", expectedControlType: "Quaternion");
            rotationAction.AddBinding("<XRHMD>/centerEyeRotation");
            trackingAction = new InputAction("AR Tracking", InputActionType.Value, "<HandheldARInputDevice>/trackingState", expectedControlType: "Integer");
            trackingAction.AddBinding("<XRHMD>/trackingState");
            driver.positionInput = new InputActionProperty(positionAction);
            driver.rotationInput = new InputActionProperty(rotationAction);
            driver.trackingStateInput = new InputActionProperty(trackingAction);
            driver.ignoreTrackingState = false;
            driver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            driver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
            driver.enabled = true;
            if (FindAnyObjectByType<ARInputManager>() == null)
            {
                var session = FindAnyObjectByType<ARSession>();
                if (session != null) session.gameObject.AddComponent<ARInputManager>();
            }
        }

        private void OnApplicationFocus(bool focus) { if (!focus) pressPending = false; }

        private void OnDestroy()
        {
            if (arCamera != null && arCamera.TryGetComponent<TrackedPoseDriver>(out var driver)) driver.enabled = false;
            positionAction?.Dispose(); rotationAction?.Dispose(); trackingAction?.Dispose();
        }

        private void Update()
        {
            CameraTrackingActive = ARSession.state == ARSessionState.SessionTracking &&
                positionAction != null && positionAction.controls.Count > 0 && rotationAction.controls.Count > 0;
            bool found = false;
            if (arPlaneManager != null)
                foreach (var plane in arPlaneManager.trackables)
                    if (plane.alignment == PlaneAlignment.HorizontalUp && plane.trackingState == TrackingState.Tracking) { found = true; break; }
#if UNITY_EDITOR
            found |= EditorSimulator.SimActive;
#endif
            if (found && !HasPlaneDetected) OnPlaneDetected?.Invoke();
            HasPlaneDetected = found;
            if (PointerInput.TryGetPress(out var p))
            {
                pressPosition = p;
                pressPending = selectedPrefab != null && !IsOverUI(p) && !HitFurniture(p) &&
                    (interaction == null || (!interaction.TapConsumedThisFrame && interaction.SelectedFurniture == null));
            }
            if (PointerInput.ActiveFingerCount > 1) pressPending = false;
            if (PointerInput.TryGetHoldPosition(out var held) && Vector2.Distance(held, pressPosition) > Mathf.Max(14f, Screen.height * .015f)) pressPending = false;
            if (PointerInput.TryGetRelease(out var released))
            {
                bool place = pressPending && !IsOverUI(released);
                pressPending = false;
                if (!place) return;
                if (TryGetPlanePose(released, out var pose)) PlaceAtPose(pose);
                else Feedback("Bu noktada zemin bulunamadı. Kameranızı yavaşça hareket ettirin.", false);
            }
        }

        public void SetSelectedPrefab(GameObject prefab, float scale, float yaw)
        {
            selectedPrefab = prefab; placementScale = scale > 0f ? scale : 1f; rotationOffset = yaw;
            pressPending = false;
            interaction?.DeselectFurniture();
        }

        private async void PlaceAtPose(Pose pose)
        {
            if (selectedPrefab == null) return;
            var root = new GameObject(selectedPrefab.name + "_Placed");
            root.transform.SetPositionAndRotation(pose.position, Quaternion.Euler(0f, (arCamera != null ? arCamera.transform.eulerAngles.y : 0f) + 180f + rotationOffset, 0f));
            var model = Instantiate(selectedPrefab, root.transform, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localScale *= placementScale; // Preserve the pack's import/prefab scale.
            foreach (var t in model.GetComponentsInChildren<Transform>()) t.gameObject.isStatic = false;
            var piece = root.AddComponent<PlacedFurniture>();
            piece.Initialize();
            piece.GroundAt(pose.position.y);
            LastPlacedFurniture = piece;
            LastPlacementInfo = $"{piece.LocalBoundsSize.x:F2} × {piece.LocalBoundsSize.y:F2} × {piece.LocalBoundsSize.z:F2} m";
            OnFurniturePlaced?.Invoke(root);
            Feedback("Yerleştirildi. Düzenlemek için mobilyaya dokunun.", true);
            await piece.AttachAnchorAsync(anchors);
        }

        public bool TryGetPlanePose(Vector2 screenPosition, out Pose pose)
        {
            pose = default;
#if UNITY_EDITOR
            if (EditorSimulator.SimActive) return EditorSimulator.TryScreenToFloorPose(arCamera, screenPosition, out pose);
#endif
            if (ARSession.state != ARSessionState.SessionTracking || arRaycastManager == null || arPlaneManager == null) return false;
            if (!arRaycastManager.Raycast(screenPosition, hits, TrackableType.PlaneWithinPolygon)) return false;
            foreach (var hit in hits)
            {
                var plane = arPlaneManager.GetPlane(hit.trackableId);
                if (plane == null || plane.alignment != PlaneAlignment.HorizontalUp || plane.trackingState != TrackingState.Tracking) continue;
                if (!IsPoseUsable(arCamera, hit.pose)) continue;
                pose = new Pose(hit.pose.position, Quaternion.identity);
                return true;
            }
            return false;
        }
        public static bool IsPoseUsable(Camera cam, Pose pose)
        {
            if (cam == null) return false;
            var delta = pose.position - cam.transform.position;
            return delta.sqrMagnitude >= .0625f && delta.sqrMagnitude <= 64f && Vector3.Dot(delta, cam.transform.forward) > 0f;
        }
        public bool IsOverUI(Vector2 p) => p.y <= BottomUiPixels || PointerOverUI(p);
        public static bool PointerOverUI(Vector2 p)
        {
            if (EventSystem.current == null) return false;
            var results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = p }, results);
            return results.Exists(r => r.module is UnityEngine.UI.GraphicRaycaster);
        }
        private bool HitFurniture(Vector2 p)
        {
            if (arCamera == null) return false;
            foreach (var hit in Physics.RaycastAll(arCamera.ScreenPointToRay(p), 40f))
                if (hit.collider.GetComponentInParent<PlacedFurniture>() != null) return true;
            return false;
        }
        private void Feedback(string text, bool success) => OnPlacementFeedback?.Invoke(text, success);
#if UNITY_EDITOR
        public void SimulatePlaneDetected() { HasPlaneDetected = true; OnPlaneDetected?.Invoke(); }
#endif
    }
}

