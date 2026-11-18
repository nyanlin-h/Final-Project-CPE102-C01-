using UnityEngine;

namespace KMUTT.ARNav
{
    /// <summary>One storey of the building (Floor_10, Floor_11). Children: Walls, Zones, Waypoints.</summary>
    public class FloorRoot : MonoBehaviour
    {
        public int level = 10;
        [Tooltip("Map-space height of this floor's walking surface (m).")]
        public float baseY;
        public float storeyHeight = 3f;
        public MeshRenderer wallRenderer;
        public Transform waypointsRoot;
        public Transform zonesRoot;

        public void SetVisible(bool visible)
        {
            if (wallRenderer != null) wallRenderer.enabled = visible;
        }
    }
}
