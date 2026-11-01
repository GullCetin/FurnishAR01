#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Unity.XR.CoreUtils;
using UnityEditor;
using Pose = UnityEngine.Pose;

namespace FurnishAR.Core
{
    /// <summary>
    /// EDITOR-ONLY AR simulator: lets the whole FurnishAR loop (camera picture, floor
    /// scanning, catalog selection, placement, move/rotate/delete) run in Play mode
    /// without building an APK.
    ///
    /// What it does when Play starts:
    /// - Streams the PC webcam to a background RawImage standing in for the AR feed
    /// - Creates a flat simulator floor at y=0 ("__SimFloor", layer SimFloor)
    /// - Bridges raycasts: placement + dragging use the sim floor instead of ARCore
    /// - WASD walks, RMB-drag looks (like walking around your room with the phone)
    /// - Fires the plane-detected event so the AR UI state machine becomes "Place ready"
    ///
    /// The whole file is wrapped in #if UNITY_EDITOR, so builds remain untouched.
    /// The component is added at runtime by ARPlacementManager (never serialized in
    /// the scene file), so there are no missing-script warnings in device builds.
    /// </summary>
    [DisallowMultipleComponent]
    public class EditorSimulator : MonoBehaviour
    {
        public const string SimFloorName = "__SimFloor";
        public const string SimLayerName = "SimFloor";
        private const string WebcamCanvasName = "__SimWebcam";

        /// <summary>True while the scene is running in the editor simulator.</summary>
        public static bool SimActive { get; private set; }

        private static int _simLayer = -1;

        [Header("Sim Settings")]
        [SerializeField] private float moveSpeed = 1.4f;
        [SerializeField] private float lookSpeed = 2.5f;

        private Transform xrRoot;
        private Camera simCam;
        private float pitch;

        // -------------------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------------------
        private void OnEnable()
        {
            SimActive = true;

            var origin = FindAnyObjectByType<XROrigin>();
            if (origin != null)
            {
                xrRoot = origin.transform;
                simCam = origin.Camera;
            }
            if (simCam == null) simCam = Camera.main;

            CreateFloor();
            // A ScreenSpaceOverlay webcam covers all 3D geometry, regardless of sortingOrder.
            // Use the simulated floor and camera background for honest editor testing.
            if (simCam != null) { simCam.clearFlags = CameraClearFlags.SolidColor; simCam.backgroundColor = new Color(.82f, .85f, .88f); }

            // Handheld phone emulation: stand 1.25 m tall, look slightly down at the floor
            // so the sim floor is visible immediately after Play starts.
            if (xrRoot != null && simCam != null)
            {
                if (xrRoot.position.y < 0.4f) xrRoot.position = new Vector3(xrRoot.position.x, 1.25f, xrRoot.position.z);
                pitch = 32f; // positive X pitch looks DOWN for a +Z-facing camera
                simCam.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            }

            // Floor is "scanned": move guidance into the place-ready phase.
            // Delayed so UI scripts that subscribe to the event in Start() don't miss it.
            Invoke(nameof(NotifyPlaneDetected), 0.75f);

            Debug.Log("[FurnishAR SIM] ✅ Editor simulator active — WASD = walk, hold RMB = look, " +
                      "click a catalog card then click the floor to place furniture.");
        }

        private void NotifyPlaneDetected()
        {
            var placement = FindAnyObjectByType<ARPlacementManager>();
            if (placement != null) placement.SimulatePlaneDetected();
        }

        private void OnDisable()
        {
            SimActive = false;
        }

        // -------------------------------------------------------------------
        // Camera walking
        // -------------------------------------------------------------------
        private void Update()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard == null || mouse == null || simCam == null || !Application.isPlaying) return;

            if (mouse.rightButton.isPressed)
            {
                Vector2 delta = mouse.delta.ReadValue();
                if (xrRoot != null) xrRoot.Rotate(Vector3.up, delta.x * lookSpeed * Time.deltaTime, Space.World);
                pitch = Mathf.Clamp(pitch - delta.y * lookSpeed * Time.deltaTime, -80f, 80f);
                simCam.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            }

            if (xrRoot == null) return;

            Vector3 step = Vector3.zero;
            if (keyboard.wKey.isPressed) step += Vector3.forward;
            if (keyboard.sKey.isPressed) step -= Vector3.forward;
            if (keyboard.aKey.isPressed) step -= Vector3.right;
            if (keyboard.dKey.isPressed) step += Vector3.right;

            float up = 0f;
            if (keyboard.eKey.isPressed) up += 1f;
            if (keyboard.qKey.isPressed) up -= 1f;
            if (up != 0f) step += Vector3.up * up;

            if (step.sqrMagnitude > 0.0001f)
            {
                float yawDeg = simCam.transform.eulerAngles.y;
                Quaternion yaw = Quaternion.Euler(0f, yawDeg, 0f);
                Vector3 world = Quaternion.Euler(0f, xrRoot.eulerAngles.y, 0f) * (yaw * step);
                xrRoot.position += world.normalized * (moveSpeed * Time.deltaTime);
            }
        }

        // -------------------------------------------------------------------
        // Sim world
        // -------------------------------------------------------------------
        private void CreateFloor()
        {
            if (GameObject.Find(SimFloorName) != null) return;

            int layer = SimLayerIndex();
            var floor = GameObject.CreatePrimitive(PrimitiveType.Quad);
            floor.name = SimFloorName;
            floor.layer = layer >= 0 ? layer : floor.layer;

            Object.DestroyImmediate(floor.GetComponent<Collider>());
            var collider = floor.AddComponent<BoxCollider>();
            // Thin box whose top face sits exactly at the floor level (y=0), so rays from
            // a camera above the floor hit it instead of starting inside the volume.
            // NOTE: the quad is rotated 90° around X, so local +Z maps to world -Y.
            collider.size = new Vector3(1f, 1f, 0.5f);
            collider.center = new Vector3(0f, 0f, 0.25f);

            var renderer = floor.GetComponent<MeshRenderer>();
            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            mat.SetColor("_BaseColor", new Color(0.72f, 0.75f, 0.80f, 1f));
            mat.SetOverrideTag("RenderType", "Opaque");
            mat.SetInt("_Surface", 0);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
            renderer.sharedMaterial = mat;

            floor.transform.position = Vector3.zero;
            floor.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // quad faces +Y (up)
            floor.transform.localScale = new Vector3(24f, 24f, 1f);
        }

        private void CreateWebcamBackground()
        {
            if (GameObject.Find(WebcamCanvasName) != null) return;

            var canvasGO = new GameObject(WebcamCanvasName);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = -1000; // behind the app UI
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>().enabled = false; // never intercept clicks

            var raw = canvasGO.AddComponent<RawImage>();
            raw.rectTransform.anchorMin = Vector2.zero;
            raw.rectTransform.anchorMax = Vector2.one;
            raw.rectTransform.offsetMin = Vector2.zero;
            raw.rectTransform.offsetMax = Vector2.zero;
            raw.color = new Color(0.82f, 0.82f, 0.82f, 1f);
            raw.raycastTarget = false;

            var devices = WebCamTexture.devices;
            if (devices != null && devices.Length > 0)
            {
                var device = devices[0];
                foreach (var d in devices)
                    if (d.isFrontFacing) { device = d; break; } // "next to me" feels closer to a phone cam

                var webcam = new WebCamTexture(device.name, 1280, 720, 30);
                raw.texture = webcam;
                webcam.Play();
                Debug.Log("[FurnishAR SIM] Webcam background streaming from: " + device.name);
            }
            else
            {
                Debug.LogWarning("[FurnishAR SIM] No webcam found — background stays neutral gray.");
            }
        }

        // -------------------------------------------------------------------
        // Raycast bridge (used by ARPlacementManager / FurnitureInteractionManager)
        // -------------------------------------------------------------------
        /// <summary>Screen point → sim-floor pose (position + camera bearing).</summary>
        public static bool TryScreenToFloorPose(Camera cam, Vector2 screenPos, out Pose pose)
        {
            pose = default;
            if (!SimActive) return false;
            if (cam == null) cam = Camera.main;
            if (cam == null) return false;

            Ray ray = cam.ScreenPointToRay(screenPos);
            int mask = SimLayerIndex() >= 0 ? 1 << SimLayerIndex() : ~0;

            if (Physics.Raycast(ray, out RaycastHit hit, 200f, mask))
            {
                Vector3 forward = cam.transform.forward;
                forward.y = 0f;
                forward = forward.sqrMagnitude < 0.0001f ? Vector3.forward : forward.normalized;

                pose = new Pose(hit.point, Quaternion.LookRotation(forward));
                return true;
            }
            return false;
        }

        // -------------------------------------------------------------------
        // Layer management (runtime editor-safe)
        // -------------------------------------------------------------------
        public static int SimLayerIndex()
        {
            if (_simLayer >= 0) return _simLayer;

            int layer = LayerMask.NameToLayer(SimLayerName);
            if (layer >= 0)
            {
                _simLayer = layer;
                return _simLayer;
            }

            // Create the layer in ProjectSettings/TagManager.asset
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0) return -1;
            var tagManager = new SerializedObject(assets[0]);
            var layers = tagManager.FindProperty("layers");

            for (int i = 8; i < layers.arraySize; i++)
            {
                var slot = layers.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(slot.stringValue))
                {
                    slot.stringValue = SimLayerName;
                    break;
                }
            }
            tagManager.ApplyModifiedProperties();

            _simLayer = LayerMask.NameToLayer(SimLayerName);
            return _simLayer;
        }
    }
}
#endif

