using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using FurnishAR.Core;
using FurnishAR.Data;
using FurnishAR.UI;

namespace FurnishAR.Editor
{
    public class FurnishARSmokeTests : MonoBehaviour
    {
        [MenuItem("FurnishAR/Run Play Mode Checks")]
        public static void Run()
        {
            if (!Application.isPlaying) throw new System.Exception("Enter Play mode first.");
            Object.FindAnyObjectByType<ARPlacementManager>().StartCoroutine(Check());
        }
        private static void Require(bool condition, string message) { if (!condition) throw new System.Exception(message); }
        private static IEnumerator Check()
        {
            var report = new StringBuilder();
            var catalog = Resources.Load<FurnitureCatalog>("FurnitureCatalog");
            var placement = Object.FindAnyObjectByType<ARPlacementManager>();
            var interaction = Object.FindAnyObjectByType<FurnitureInteractionManager>();
            var camera = Camera.main;
            var place = typeof(ARPlacementManager).GetMethod("PlaceAtPose", BindingFlags.Instance | BindingFlags.NonPublic);
            Require(catalog.items.Length == 15, "Catalog must contain all 15 source models");
            foreach (var item in catalog.items)
            {
                placement.SetSelectedPrefab(item.prefab, item.placementScale, item.rotationOffset);
                place.Invoke(placement, new object[] {new Pose(new Vector3(0,0,3), Quaternion.identity)});
                yield return null;
                var piece = placement.LastPlacedFurniture;
                Require(piece != null, "Placement failed: " + item.name);
                var model = piece.transform.GetChild(0);
                var bounds = PlacedFurniture.MeasureLocalBounds(piece.transform, model.GetComponentsInChildren<Renderer>());
                Require(Mathf.Abs(bounds.min.y) < .001f, "Grounding failed: " + item.name);
                Require(bounds.size.y > .5f && bounds.size.y < 1.5f, "Unexpected model height: " + item.name);
                var pos = piece.transform.position;
                var cameraPos = camera.transform.position;
                var cameraRot = camera.transform.rotation;
                camera.transform.position += Vector3.right;
                camera.transform.Rotate(0,45,0);
                yield return null;
                Require(Vector3.Distance(pos, piece.transform.position) < .0001f, "Furniture followed camera");
                camera.transform.SetPositionAndRotation(cameraPos, cameraRot);
                interaction.SelectFurniture(piece);
                var beforeRotation = piece.transform.rotation;
                interaction.RotateSelected(30);
                Require(Quaternion.Angle(beforeRotation, piece.transform.rotation) > 29f, "Rotation failed");
                piece.BeginMove(); piece.transform.position = new Vector3(1,.3f,2); piece.GroundAt(.3f);
                yield return null;
                bounds = PlacedFurniture.MeasureLocalBounds(piece.transform, model.GetComponentsInChildren<Renderer>());
                Require(Mathf.Abs(bounds.min.y) < .001f, "Grounding changed after rotation/move");
                interaction.DeselectFurniture();
                Require(!piece.IsSelected, "Deselect failed");
                report.AppendLine("PASS " + item.name + ": placement, metre scale, floor alignment, camera independence, select, rotate, move, deselect");
                interaction.SelectFurniture(piece); interaction.DeleteSelected();
                yield return null;
                Require(piece == null, "Delete failed");
            }
            foreach (var item in new[] {catalog.items[0], catalog.items[9]})
            {
                placement.SetSelectedPrefab(item.prefab, item.placementScale, item.rotationOffset);
                place.Invoke(placement, new object[] {new Pose(new Vector3(item == catalog.items[0] ? -.8f : .8f,0,3), Quaternion.identity)});
                yield return null;
            }
            Require(Object.FindObjectsByType<PlacedFurniture>().Length == 2, "Changing catalog selection removed existing furniture");
            Require(placement.IsOverUI(new Vector2(Screen.width / 2f, 10f)), "Bottom UI tap not blocked");
            report.AppendLine("PASS multiple furniture instances, catalog switching, bottom UI guard");
            Directory.CreateDirectory("Builds");
            File.WriteAllText("Builds/PlayModeChecks.txt", report.ToString());
            Debug.Log("[FurnishAR TEST] ALL CHECKS PASSED\n" + report);
        }
    }
}

