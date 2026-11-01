using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace FurnishAR.Core
{
    /// <summary>A floor-rooted furniture instance; model geometry and AR anchor have separate ownership.</summary>
    public class PlacedFurniture : MonoBehaviour
    {
        private Renderer[] modelRenderers;
        private MaterialPropertyBlock[][] originalBlocks;
        private ARAnchor anchor;
        private ARAnchorManager anchorManager;
        private int anchorRevision;
        private bool selected;
        public float BottomOffset => 0f;
        public bool IsSelected => selected;
        public int SpawnFrame { get; private set; }
        public Vector3 AnchorPosition => transform.position;
        public bool HasAnchor => anchor != null;
        public Vector3 LocalBoundsCenter { get; private set; }
        public Vector3 LocalBoundsSize { get; private set; }

        public void Initialize()
        {
            SpawnFrame = Time.frameCount;
            modelRenderers = GetComponentsInChildren<Renderer>();
            // Root stays at the surface. Shift the actual model once, excluding decorative shadows.
            Bounds bounds = MeasureLocalBounds(transform, modelRenderers);
            if (transform.childCount > 0)
                transform.GetChild(0).localPosition -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            bounds = MeasureLocalBounds(transform, modelRenderers);
            LocalBoundsCenter = bounds.center;
            LocalBoundsSize = bounds.size;
            var box = gameObject.AddComponent<BoxCollider>();
            box.center = bounds.center;
            box.size = bounds.size;
            originalBlocks = new MaterialPropertyBlock[modelRenderers.Length][];
            for (int i = 0; i < modelRenderers.Length; i++)
            {
                originalBlocks[i] = new MaterialPropertyBlock[modelRenderers[i].sharedMaterials.Length];
                for (int j = 0; j < originalBlocks[i].Length; j++)
                {
                    originalBlocks[i][j] = new MaterialPropertyBlock();
                    modelRenderers[i].GetPropertyBlock(originalBlocks[i][j], j);
                }
            }
            var shadowPrefab = Resources.Load<GameObject>("Effects/ContactShadow");
            if (shadowPrefab != null)
            {
                var shadow = Instantiate(shadowPrefab, transform, false);
                shadow.transform.localScale = new Vector3(bounds.size.x * 1.05f, bounds.size.z * 1.05f, 1f);
                foreach (var collider in shadow.GetComponentsInChildren<Collider>()) Destroy(collider);
            }
        }

        public static Bounds MeasureLocalBounds(Transform root, Renderer[] renderers)
        {
            var result = new Bounds();
            bool first = true;
            foreach (var renderer in renderers)
            {
                var b = renderer.localBounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var point = root.InverseTransformPoint(renderer.transform.TransformPoint(corner));
                    if (first) { result = new Bounds(point, Vector3.zero); first = false; }
                    else result.Encapsulate(point);
                }
            }
            return result;
        }

        public void GroundAt(float y) { var p = transform.position; p.y = y; transform.position = p; }
        public void RefreshBottomOffset() { }
        // Kept for existing editor utilities. A transform snapshot is not an AR anchor.
        public void SetAnchor() { }

        public void BeginMove()
        {
            anchorRevision++;
            transform.SetParent(null, true);
            ReleaseAnchor();
        }
        public async Awaitable AttachAnchorAsync(ARAnchorManager manager = null)
        {
            if (manager != null) anchorManager = manager;
            if (anchorManager == null || anchorManager.subsystem == null || !anchorManager.subsystem.running) return;
            int revision = ++anchorRevision;
            try
            {
                var result = await anchorManager.TryAddAnchorAsync(new Pose(transform.position, Quaternion.identity));
                if (!result.status.IsSuccess()) { Debug.LogWarning("[FurnishAR] AR anchor could not be created; the object remains at its world pose."); return; }
                if (this == null || revision != anchorRevision)
                {
                    if (anchorManager != null && anchorManager.isActiveAndEnabled) anchorManager.TryRemoveAnchor(result.value);
                    return;
                }
                ReleaseAnchor();
                anchor = result.value;
                transform.SetParent(anchor.transform, true);
            }
            catch (System.Exception e) { Debug.LogWarning("[FurnishAR] Anchor: " + e.Message); }
        }
        private void ReleaseAnchor()
        {
            if (anchor == null) return;
            if (anchorManager != null && anchorManager.isActiveAndEnabled) anchorManager.TryRemoveAnchor(anchor);
            else Destroy(anchor.gameObject);
            anchor = null;
        }
        private void OnDestroy() { anchorRevision++; ReleaseAnchor(); }
        public void Select()
        {
            if (selected) return;
            selected = true;
            for (int i = 0; i < modelRenderers.Length; i++)
            {
                var materials = modelRenderers[i].sharedMaterials;
                for (int j = 0; j < materials.Length; j++)
                {
                    var block = new MaterialPropertyBlock();
                    modelRenderers[i].GetPropertyBlock(block, j);
                    if (materials[j] != null && materials[j].HasProperty("_BaseColor"))
                        block.SetColor("_BaseColor", Color.Lerp(materials[j].GetColor("_BaseColor"), new Color(.55f,.72f,.8f), .15f));
                    modelRenderers[i].SetPropertyBlock(block, j);
                }
            }
        }
        public void Deselect()
        {
            if (!selected) return;
            selected = false;
            for (int i = 0; i < modelRenderers.Length; i++)
                for (int j = 0; j < originalBlocks[i].Length; j++) modelRenderers[i].SetPropertyBlock(originalBlocks[i][j], j);
        }
    }
}
