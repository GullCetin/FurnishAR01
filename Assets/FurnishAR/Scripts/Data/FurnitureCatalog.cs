using UnityEngine;

namespace FurnishAR.Data
{
    /// <summary>
    /// ScriptableObject that holds the complete furniture catalog.
    /// Referenced by the UI to populate the bottom scroll panel.
    /// </summary>
    [CreateAssetMenu(fileName = "FurnitureCatalog", menuName = "FurnishAR/Furniture Catalog")]
    public class FurnitureCatalog : ScriptableObject
    {
        [Tooltip("All available furniture items in the catalog")]
        public FurnitureItem[] items;
    }
}

