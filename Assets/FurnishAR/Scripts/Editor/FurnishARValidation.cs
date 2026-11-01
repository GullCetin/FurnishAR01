using System.Text;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using FurnishAR.Core;
using FurnishAR.Data;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.ARFoundation;
using Unity.XR.CoreUtils;

namespace FurnishAR.Editor
{
    public static class FurnishARValidation
    {
        [MenuItem("FurnishAR/Validate Model Dimensions")]
        public static void ValidateModels()
        {
            var report = new StringBuilder("Furnish AR model audit — metres\n");
            var catalog = Resources.Load<FurnitureCatalog>("FurnitureCatalog");
            if (catalog == null) throw new System.Exception("Catalog missing");
            foreach (var item in catalog.items)
            {
                if (item == null || item.prefab == null || item.previewIcon == null) throw new System.Exception("Missing catalog model/preview");
                var root = new GameObject("Audit");
                var model = Object.Instantiate(item.prefab, root.transform);
                model.transform.localPosition = Vector3.zero;
                model.transform.localScale *= item.placementScale;
                var bounds = PlacedFurniture.MeasureLocalBounds(root.transform, model.GetComponentsInChildren<Renderer>());
                report.AppendLine(item.name + " | " + AssetDatabase.GetAssetPath(item.prefab) + " | " + bounds.size.ToString("F3") + " | scale=" + item.placementScale);
                Object.DestroyImmediate(root);
            }
            Directory.CreateDirectory("Builds");
            File.WriteAllText("Builds/ModelAudit.txt", report.ToString());
            Debug.Log(report.ToString());
        }
        [MenuItem("FurnishAR/Repair AR Scene")]
        public static void RepairScene()
        {
            var origin = Object.FindAnyObjectByType<XROrigin>();
            if (origin == null) throw new System.Exception("XR Origin missing");
            origin.transform.localScale = Vector3.one;
            origin.CameraYOffset = 0f;
            if (origin.GetComponent<ARAnchorManager>() == null) origin.gameObject.AddComponent<ARAnchorManager>();
            var camera = origin.Camera;
            var driver = camera.GetComponent<TrackedPoseDriver>();
            if (driver == null) driver = camera.gameObject.AddComponent<TrackedPoseDriver>();
            var p = new InputAction("Position", InputActionType.Value, "<HandheldARInputDevice>/devicePosition", expectedControlType: "Vector3");
            p.AddBinding("<XRHMD>/centerEyePosition");
            var r = new InputAction("Rotation", InputActionType.Value, "<HandheldARInputDevice>/deviceRotation", expectedControlType: "Quaternion");
            r.AddBinding("<XRHMD>/centerEyeRotation");
            var t = new InputAction("Tracking", InputActionType.Value, "<HandheldARInputDevice>/trackingState", expectedControlType: "Integer");
            t.AddBinding("<XRHMD>/trackingState");
            driver.positionInput = new InputActionProperty(p); driver.rotationInput = new InputActionProperty(r); driver.trackingStateInput = new InputActionProperty(t);
            driver.ignoreTrackingState = false;
            driver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
            driver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            driver.enabled = true;
            camera.nearClipPlane = .05f; camera.farClipPlane = 50f;
            camera.GetComponent<ARCameraManager>().requestedFacingDirection = UnityEngine.XR.ARFoundation.CameraFacingDirection.World;
            var session = Object.FindAnyObjectByType<ARSession>();
            if (Object.FindAnyObjectByType<ARInputManager>() == null) session.gameObject.AddComponent<ARInputManager>();
            var plane = origin.GetComponent<ARPlaneManager>();
            plane.requestedDetectionMode = UnityEngine.XR.ARSubsystems.PlaneDetectionMode.Horizontal;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.bundleVersion = "1.1.0";
            PlayerSettings.Android.bundleVersionCode = 7;
            EditorSceneManager.MarkSceneDirty(origin.gameObject.scene);
            EditorSceneManager.SaveScene(origin.gameObject.scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[FurnishAR] AR scene repaired.");
        }
    }
}

