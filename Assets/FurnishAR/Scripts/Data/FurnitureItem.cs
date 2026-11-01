using UnityEngine;

namespace FurnishAR.Data
{
    /// <summary>
    /// ScriptableObject that holds data for a single furniture item.
    /// Each item maps to a prefab from the NextGen Furniture Pack.
    /// </summary>
    [CreateAssetMenu(fileName = "NewFurnitureItem", menuName = "FurnishAR/Furniture Item")]
    public class FurnitureItem : ScriptableObject
    {
        [Header("Display")]
        [Tooltip("Display name shown on the card")]
        public string displayName;

        [Tooltip("Category for filtering (Chair, Sofa, Table, Commode)")]
        public FurnitureCategory category;

        [Header("Prefab")]
        [Tooltip("The 3D model prefab from NextGen Furniture Pack")]
        public GameObject prefab;

        [Header("Preview")]
        [Tooltip("Preview icon for the catalog card (auto-generated if null)")]
        public Sprite previewIcon;

        [Header("Placement")]
        [Tooltip("Scale multiplier for AR placement (1 = original size)")]
        public float placementScale = 1f;

        [Tooltip("Y-axis rotation offset in degrees to correct model facing direction")]
        public float rotationOffset = 0f;
    }

    public enum FurnitureCategory
    {
        Chair,
        Sofa,
        Table,
        Commode
    }
}

