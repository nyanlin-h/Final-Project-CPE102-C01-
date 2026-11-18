using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace KMUTT.ARNav
{
    [Serializable]
    public class CalibrationTarget
    {
        [Tooltip("Must match the image name in your XRReferenceImageLibrary.")]
        public string referenceImageName;
        [Tooltip("Empty GameObject under BuildingMap placed exactly where the printed poster hangs. " +
                 "Its +Y axis points OUT of the wall (towards the viewer), +Z points to the TOP of the poster.")]
        public Transform markerInMap;
        public int floorLevel = 10;
    }

    /// <summary>
    /// Registers the BuildingMap (digital twin) onto the real building.
    ///   1) Image calibration (production): scan a printed poster whose position is known in the map.
    ///   2) Manual "stand here" alignment (testing): stand on a known node facing a known direction.
    /// After alignment the map is parented under an ARAnchor so ARCore/ARKit keep correcting drift.
    /// Requires AR Foundation 6.x. On 5.x, swap trackablesChanged for trackedImagesChanged.
    /// </summary>
    public class MapAligner : MonoBehaviour
    {
        public BuildingMap map;
        public ARTrackedImageManager imageManager;
        [Tooltip("Optional: used to find the real floor height for manual alignment.")]
        public ARPlaneManager planeManager;
        public Camera arCamera;

        public List<CalibrationTarget> targets = new List<CalibrationTarget>();

        [Header("Image calibration quality")]
        [Tooltip("Keep the map upright (gravity from AR) and take only heading from the image. Strongly recommended.")]
        public bool yawOnly = true;
        [Tooltip("Number of stable tracked frames averaged before locking.")]
        public int samplesToLock = 20;
        [Tooltip("Ignore samples taken from farther than this (pose noise grows with distance).")]
        public float maxScanDistance = 2.5f;
        [Tooltip("Re-calibrating from an image is ignored if it would move the map less than this (m).")]
        public float minCorrection = 0.05f;

        [Header("Manual alignment")]
        public float fallbackEyeHeight = 1.4f;

        public bool IsAligned { get; private set; }
        public int LastAlignedFloor { get; private set; } = int.MinValue;
        public event Action<int> Aligned;              // floor level of the target used
        public event Action<float> CalibrationProgress; // 0..1 while collecting samples

        ARAnchor currentAnchor;
        CalibrationTarget collectingFor;
        readonly List<Vector3> posSamples = new List<Vector3>();
        Vector3 fwdSum;

        void Reset()
        {
            map = FindFirstObjectByType<BuildingMap>();
            imageManager = FindFirstObjectByType<ARTrackedImageManager>();
            planeManager = FindFirstObjectByType<ARPlaneManager>();
            arCamera = Camera.main;
        }

        void OnEnable()
        {
            if (imageManager != null) imageManager.trackablesChanged.AddListener(OnImagesChanged);
        }

        void OnDisable()
        {
            if (imageManager != null) imageManager.trackablesChanged.RemoveListener(OnImagesChanged);
        }

        void OnImagesChanged(ARTrackablesChangedEventArgs<ARTrackedImage> args)
        {
            foreach (var img in args.added) Consider(img);
            foreach (var img in args.updated) Consider(img);
        }

        void Consider(ARTrackedImage img)
        {
            if (img.trackingState != TrackingState.Tracking) return;   // Limited = extrapolated, not good enough
            var target = targets.Find(t => t.referenceImageName == img.referenceImage.name);
            if (target == null || target.markerInMap == null) return;
            if (arCamera != null && Vector3.Distance(arCamera.transform.position, img.transform.position) > maxScanDistance) return;

            if (collectingFor != target) { collectingFor = target; posSamples.Clear(); fwdSum = Vector3.zero; }

            ComputeMapPose(target.markerInMap, img.transform.position, img.transform.rotation, out var pos, out var rot);
            posSamples.Add(pos);
            fwdSum += rot * Vector3.forward;
            CalibrationProgress?.Invoke(Mathf.Clamp01(posSamples.Count / (float)samplesToLock));

            if (posSamples.Count < samplesToLock) return;

            // Average: mean position, mean heading (vector mean avoids 359°/1° wrap issues)
            Vector3 mean = Vector3.zero;
            foreach (var p in posSamples) mean += p;
            mean /= posSamples.Count;
            Vector3 fwd = fwdSum; fwd.y = yawOnly ? 0f : fwd.y;
            Quaternion meanRot = Quaternion.LookRotation(fwd.normalized, Vector3.up);

            posSamples.Clear(); fwdSum = Vector3.zero; collectingFor = null;

            if (IsAligned && Vector3.Distance(mean, map.transform.position) < minCorrection &&
                Quaternion.Angle(meanRot, map.transform.rotation) < 1f)
                return;

            ApplyPose(mean, meanRot, target.floorLevel);
        }

        /// <summary>Pose the map root must take so that markerInMap coincides with the observed image pose.</summary>
        void ComputeMapPose(Transform marker, Vector3 imgPos, Quaternion imgRot, out Vector3 mapPos, out Quaternion mapRot)
        {
            Transform root = map.transform;
            // Marker pose expressed in map space (independent of where the map currently is)
            Vector3 mLocalPos = root.InverseTransformPoint(marker.position);
            Quaternion mLocalRot = Quaternion.Inverse(root.rotation) * marker.rotation;

            mapRot = imgRot * Quaternion.Inverse(mLocalRot);
            if (yawOnly)
            {
                Vector3 f = mapRot * Vector3.forward; f.y = 0f;
                if (f.sqrMagnitude < 1e-6f) f = Vector3.forward;
                mapRot = Quaternion.LookRotation(f.normalized, Vector3.up);
            }
            mapPos = imgPos - mapRot * mLocalPos;
        }

        /// <summary>
        /// Testing without posters: stand on <paramref name="node"/> facing along the node's blue (+Z) axis, then call this.
        /// </summary>
        public void AlignStandingOnNode(NavNode node)
        {
            if (node == null || arCamera == null) return;
            Transform root = map.transform;
            Vector3 nodeLocal = root.InverseTransformPoint(node.transform.position);
            Vector3 nodeFwdLocal = root.InverseTransformDirection(node.transform.forward); nodeFwdLocal.y = 0;

            Vector3 camFwd = arCamera.transform.forward; camFwd.y = 0;
            if (camFwd.sqrMagnitude < 1e-4f) camFwd = arCamera.transform.up; // looking straight down
            camFwd.y = 0;

            Quaternion rot = Quaternion.FromToRotation(nodeFwdLocal.normalized, camFwd.normalized);
            // keep pure yaw
            Vector3 f = rot * Vector3.forward; f.y = 0;
            rot = Quaternion.LookRotation(f.normalized, Vector3.up);

            Vector3 feet = arCamera.transform.position;
            feet.y = FloorHeightBelowCamera();
            Vector3 pos = feet - rot * nodeLocal;
            ApplyPose(pos, rot, node.floorLevel);
        }

        float FloorHeightBelowCamera()
        {
            float camY = arCamera.transform.position.y;
            float best = float.NegativeInfinity;
            if (planeManager != null)
            {
                foreach (var plane in planeManager.trackables)
                {
                    if (plane.alignment != PlaneAlignment.HorizontalUp) continue;
                    float y = plane.transform.position.y;
                    if (y < camY - 0.6f && y > camY - 2.2f && y > best) best = y;
                }
            }
            return float.IsNegativeInfinity(best) ? camY - fallbackEyeHeight : best;
        }

        void ApplyPose(Vector3 pos, Quaternion rot, int floorLevel)
        {
            Transform root = map.transform;

            // Detach from the previous anchor before destroying it
            root.SetParent(null, true);
            if (currentAnchor != null) Destroy(currentAnchor.gameObject);

            var anchorGO = new GameObject($"BuildingMapAnchor (F{floorLevel})");
            anchorGO.transform.SetPositionAndRotation(pos, rot);
            // Adding ARAnchor as a component registers it with the ARAnchorManager (must exist on XR Origin).
            currentAnchor = anchorGO.AddComponent<ARAnchor>();

            root.SetParent(anchorGO.transform, false);
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;
            root.localScale = Vector3.one;

            Physics.SyncTransforms();   // wall colliders moved; keep raycasts/smoothing correct this frame
            IsAligned = true;
            LastAlignedFloor = floorLevel;
            Aligned?.Invoke(floorLevel);
            Debug.Log($"[MapAligner] Map aligned via floor {floorLevel} at {pos}, yaw {rot.eulerAngles.y:F1}°");
        }

        public void ResetAlignment()
        {
            IsAligned = false;
            map.transform.SetParent(null, true);
            if (currentAnchor != null) Destroy(currentAnchor.gameObject);
            currentAnchor = null;
        }
    }
}
