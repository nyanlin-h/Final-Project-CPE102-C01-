using System.Collections.Generic;
using UnityEngine;

namespace KMUTT.ARNav
{
    /// <summary>
    /// Root of the digital twin. Everything navigation-related (walls, zones, waypoints, spawned markers)
    /// lives under this transform, in "map space". MapAligner moves this root so map space matches the real building.
    /// </summary>
    [DisallowMultipleComponent]
    public class BuildingMap : MonoBehaviour
    {
        [Tooltip("Floors in ascending order. Filled in by KMUTT > 1. Build Building Map.")]
        public List<FloorRoot> floors = new List<FloorRoot>();

        [Header("Materials")]
        [Tooltip("Invisible depth-only material: hides virtual markers behind modelled walls (works on every device).")]
        public Material occluderMaterial;
        [Tooltip("Semi-transparent material for debugging alignment on device.")]
        public Material debugMaterial;

        [SerializeField] bool showDebugWalls;

        public bool ShowDebugWalls
        {
            get => showDebugWalls;
            set { showDebugWalls = value; ApplyWallMaterial(); }
        }

        void Awake() => ApplyWallMaterial();
        void OnValidate() => ApplyWallMaterial();

        void ApplyWallMaterial()
        {
            var mat = showDebugWalls ? debugMaterial : occluderMaterial;
            if (mat == null) return;
            foreach (var f in floors)
                if (f != null && f.wallRenderer != null) f.wallRenderer.sharedMaterial = mat;
        }

        public Vector3 WorldToMap(Vector3 world) => transform.InverseTransformPoint(world);
        public Vector3 MapToWorld(Vector3 map) => transform.TransformPoint(map);

        /// <summary>Floor whose storey band contains the given map-space height (e.g. the user's eye height).</summary>
        public FloorRoot FloorAtHeight(float mapY)
        {
            FloorRoot best = null;
            foreach (var f in floors)
            {
                if (f == null) continue;
                if (mapY >= f.baseY - 0.5f) best = f;   // floors are ascending: keep the highest one we are above
            }
            return best != null ? best : (floors.Count > 0 ? floors[0] : null);
        }

        public FloorRoot GetFloor(int level) => floors.Find(f => f != null && f.level == level);
    }
}
