using System.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace KMUTT.ARNav
{
    /// <summary>
    /// Two occlusion layers, chosen per device at runtime:
    ///   A) Model occlusion (always): BuildingMap walls use the KMUTT/AR Depth Mask shader. Works on every device,
    ///      reaches any distance, but only knows about walls that are in the model.
    ///   B) Sensor occlusion (when supported): AROcclusionManager environment depth.
    ///      Android: ARCore Depth API devices. iOS: LiDAR devices (environment depth) or A12+ (people only).
    ///      Hides markers behind people, pillars, furniture that are NOT in the model.
    /// Put this on the same GameObject as AROcclusionManager (the AR Camera).
    /// </summary>
    [RequireComponent(typeof(AROcclusionManager))]
    public class AROcclusionConfigurator : MonoBehaviour
    {
        public BuildingMap map;
        [Tooltip("Fastest is usually enough for arrows and saves battery; Best gives sharper edges.")]
        public EnvironmentDepthMode depthMode = EnvironmentDepthMode.Fastest;
        public bool temporalSmoothing = true;
        [Tooltip("Turn model occlusion off (e.g. to compare in a demo).")]
        public bool useModelOcclusion = true;

        public string Status { get; private set; } = "Waiting for AR session…";

        AROcclusionManager occlusion;

        void Awake()
        {
            occlusion = GetComponent<AROcclusionManager>();
            // Request first, check support once the subsystem is running.
            occlusion.requestedEnvironmentDepthMode = depthMode;
            occlusion.environmentDepthTemporalSmoothingRequested = temporalSmoothing;
            occlusion.requestedOcclusionPreferenceMode = OcclusionPreferenceMode.PreferEnvironmentOcclusion;
        }

        IEnumerator Start()
        {
            while (ARSession.state < ARSessionState.SessionTracking) yield return null;
            yield return null; // one more frame so the descriptor is populated

            var d = occlusion.descriptor;
            bool envDepth = d != null && d.environmentDepthImageSupported == Supported.Supported;
            bool humanSeg = d != null && d.humanSegmentationStencilImageSupported == Supported.Supported;

            if (envDepth)
            {
                Status = "Environment depth occlusion + model occlusion";
            }
            else if (humanSeg)
            {
                occlusion.requestedEnvironmentDepthMode = EnvironmentDepthMode.Disabled;
                occlusion.requestedHumanStencilMode = HumanSegmentationStencilMode.Fastest;
                occlusion.requestedHumanDepthMode = HumanSegmentationDepthMode.Fastest;
                occlusion.requestedOcclusionPreferenceMode = OcclusionPreferenceMode.PreferHumanOcclusion;
                Status = "People occlusion + model occlusion";
            }
            else
            {
                occlusion.enabled = false;   // nothing to gain, save GPU
                Status = "Model occlusion only (device has no depth sensor support)";
            }

            if (map != null) foreach (var f in map.floors) if (f) f.SetVisible(useModelOcclusion);
            Debug.Log($"[Occlusion] {Status}");
        }
    }
}
