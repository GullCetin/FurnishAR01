using UnityEngine;

namespace FurnishAR.Core
{
    /// <summary>
    /// Keeps a soft contact shadow quad flat (facing straight up) and snapped
    /// just above the detected plane under the furniture it belongs to.
    /// Created automatically by PlacedFurniture; lives on the shadow quad itself.
    /// </summary>
    public class FurnitureContactShadow : MonoBehaviour
    {
        [SerializeField] private float groundLift = 0.004f;

        private void LateUpdate()
        {
            Transform root = transform.parent;
            if (root == null) return;

            var furniture = root.GetComponentInParent<PlacedFurniture>();
            float groundedY = furniture != null
                ? furniture.transform.position.y - furniture.BottomOffset
                : root.position.y;

            Vector3 p = root.position;
            transform.position = new Vector3(p.x, groundedY + groundLift, p.z);

            // Flat on the ground plane, yaw-following the furniture only.
            transform.rotation = Quaternion.Euler(-90f, root.eulerAngles.y, 0f);
        }
    }
}
