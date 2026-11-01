using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.ARFoundation;

namespace FurnishAR.Core
{
    /// <summary>Owns selection gestures before placement input is processed.</summary>
    [DefaultExecutionOrder(-10)]
    public class FurnitureInteractionManager : MonoBehaviour
    {
        [SerializeField] private ARRaycastManager arRaycastManager;
        [SerializeField] private Camera arCamera;
        [SerializeField] private float rotationSpeed = 1f;
        [SerializeField] private float dragSmoothSpeed = 14f;
        [SerializeField] private float dragThresholdDp = 10f;
        public event System.Action<PlacedFurniture> OnFurnitureSelected;
        public event System.Action OnFurnitureDeselected;
        public PlacedFurniture SelectedFurniture { get; private set; }
        public bool IsInteracting => dragging || rotating;
        public bool TapConsumedThisFrame => consumedFrame == Time.frameCount;
        private ARPlacementManager placement;
        private bool candidate, dragging, rotating, blocked, lastTwo;
        private Vector2 start, lastDirection;
        private Vector3 grabOffset;
        private int consumedFrame = -1;
        private void Start()
        {
            placement = FindAnyObjectByType<ARPlacementManager>();
            var cameraManager = FindAnyObjectByType<ARCameraManager>();
            if (arCamera == null) arCamera = cameraManager != null ? cameraManager.GetComponent<Camera>() : Camera.main;
        }
        private bool OverUI(Vector2 p) => placement != null ? placement.IsOverUI(p) : ARPlacementManager.PointerOverUI(p);
        private void Update()
        {
            if (PointerInput.TryGetPress(out var p))
            {
                blocked = OverUI(p); candidate = false;
                if (!blocked)
                {
                    var hit = Pick(p);
                    if (hit != null)
                    {
                        SelectFurniture(hit); start = p; candidate = true; consumedFrame = Time.frameCount;
                        grabOffset = Vector3.zero;
                        if (placement.TryGetPlanePose(p, out var floor)) grabOffset = hit.transform.position - floor.position;
                        grabOffset.y = 0f;
                    }
                    else if (SelectedFurniture != null) { DeselectFurniture(); consumedFrame = Time.frameCount; }
                }
            }
            if (PointerInput.ActiveFingerCount >= 2)
            {
                if (dragging) FinishMove();
                candidate = false;
                if (SelectedFurniture != null && !blocked && PointerInput.TryGetFingerPosition(0, out var a) && PointerInput.TryGetFingerPosition(1, out var b) && !OverUI(a) && !OverUI(b))
                {
                    var direction = b - a;
                    if (lastTwo) RotateSelected(-Vector2.SignedAngle(lastDirection, direction) * rotationSpeed);
                    lastDirection = direction; lastTwo = true; rotating = true;
                }
                else lastTwo = false;
                return;
            }
            lastTwo = false; rotating = false;
            if (!blocked && candidate && SelectedFurniture != null && PointerInput.TryGetHoldPosition(out var held))
            {
                if (!dragging && Vector2.Distance(start, held) > Mathf.Max(12f, dragThresholdDp * (Screen.dpi > 0 ? Screen.dpi / 160f : 1f)))
                { dragging = true; SelectedFurniture.BeginMove(); }
                if (dragging && !OverUI(held) && placement.TryGetPlanePose(held, out var floor))
                {
                    var target = floor.position + grabOffset;
                    SelectedFurniture.transform.position = Vector3.Lerp(SelectedFurniture.transform.position, target, 1f - Mathf.Exp(-dragSmoothSpeed * Time.deltaTime));
                    SelectedFurniture.GroundAt(floor.position.y);
                }
            }
            if (PointerInput.TryGetRelease(out _) || (dragging && !PointerInput.TryGetHoldPosition(out _)))
            { FinishMove(); candidate = false; blocked = false; }
#if UNITY_EDITOR
            if (Mouse.current != null && !OverUI(Mouse.current.position.ReadValue()))
                RotateSelected(Mouse.current.scroll.ReadValue().y * .1f);
#endif
        }
        private PlacedFurniture Pick(Vector2 p)
        {
            if (arCamera == null) return null;
            PlacedFurniture best = null; float distance = float.MaxValue;
            foreach (var hit in Physics.RaycastAll(arCamera.ScreenPointToRay(p), 40f))
            {
                var item = hit.collider.GetComponentInParent<PlacedFurniture>();
                if (item != null && hit.distance < distance) { best = item; distance = hit.distance; }
            }
            return best;
        }
        private async void FinishMove()
        {
            bool moved = dragging; dragging = false;
            if (moved && SelectedFurniture != null) await SelectedFurniture.AttachAnchorAsync();
        }
        public void SelectFurniture(PlacedFurniture item)
        {
            if (SelectedFurniture == item) return;
            DeselectFurniture(); SelectedFurniture = item; item.Select(); OnFurnitureSelected?.Invoke(item);
        }
        public void DeselectFurniture()
        {
            FinishMove(); candidate = false;
            if (SelectedFurniture == null) return;
            SelectedFurniture.Deselect(); SelectedFurniture = null; OnFurnitureDeselected?.Invoke();
        }
        public void RotateSelected(float angle)
        {
            if (SelectedFurniture != null && Mathf.Abs(angle) > .001f) SelectedFurniture.transform.Rotate(Vector3.up, angle, Space.World);
        }
        public void DeleteSelected()
        {
            if (SelectedFurniture == null) return;
            var item = SelectedFurniture; dragging = false; DeselectFurniture(); Destroy(item.gameObject);
        }
        private void OnApplicationFocus(bool focus) { if (!focus) { FinishMove(); candidate = false; blocked = true; } }
    }
}
