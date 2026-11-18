using UnityEngine;

namespace KMUTT.ARNav
{
    /// <summary>
    /// A room / corridor volume from the source model (it was a solid block in the OBJ).
    /// Kept as a trigger volume so you can name rooms and answer "which room is the user in?".
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class MapZone : MonoBehaviour
    {
        public string displayName;
        public int floorLevel;

        public bool ContainsMapPoint(Vector3 mapPoint, Transform mapRoot)
        {
            var box = GetComponent<BoxCollider>();
            var world = mapRoot.TransformPoint(mapPoint);
            return box.bounds.Contains(world);
        }

        void OnDrawGizmos()
        {
            var box = GetComponent<BoxCollider>();
            Gizmos.color = new Color(0.6f, 0.2f, 0.9f, 0.15f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(box.center, box.size);
        }
    }
}
