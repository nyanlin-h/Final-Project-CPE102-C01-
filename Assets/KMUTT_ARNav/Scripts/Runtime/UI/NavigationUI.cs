using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KMUTT.ARNav
{
    /// <summary>
    /// Minimal screen-space UI: destination dropdown, Go/Stop, step-free toggle, instruction banner,
    /// distance readout, and debug buttons for alignment testing.
    /// </summary>
    public class NavigationUI : MonoBehaviour
    {
        public NavigationController controller;
        public MapAligner aligner;
        public AROcclusionConfigurator occlusion;

        [Header("Widgets")]
        public TMP_Dropdown destinationDropdown;
        public Button goButton;
        public Button stopButton;
        public Toggle stepFreeToggle;
        public TMP_Text instructionText;
        public TMP_Text distanceText;
        public TMP_Text statusText;

        [Header("Debug (optional)")]
        public Toggle showWallsToggle;
        public Button alignHereButton;
        [Tooltip("Node you physically stand on (facing its +Z) when pressing 'Align Here' during testing.")]
        public NavNode debugStartNode;

        List<NavNode> destinations;

        void Start()
        {
            destinations = controller.GetDestinations();
            destinationDropdown.ClearOptions();
            var labels = new List<string>();
            foreach (var d in destinations) labels.Add($"F{d.floorLevel} · {d.Label}");
            destinationDropdown.AddOptions(labels);

            goButton.onClick.AddListener(() =>
            {
                if (destinations.Count == 0) return;
                controller.options.avoidStairs = stepFreeToggle != null && stepFreeToggle.isOn;
                controller.StartNavigation(destinations[destinationDropdown.value]);
            });
            if (stopButton) stopButton.onClick.AddListener(controller.Cancel);

            controller.onInstruction.AddListener(s => { if (instructionText) instructionText.text = s; });
            controller.onDistanceRemaining.AddListener(m => { if (distanceText) distanceText.text = $"{m:0} m"; });
            controller.onStateChanged.AddListener(s => { if (distanceText && s != NavState.Navigating && s != NavState.FloorTransition) distanceText.text = ""; });

            if (aligner != null)
            {
                aligner.CalibrationProgress += p => { if (statusText) statusText.text = $"Calibrating… {p:P0}"; };
                aligner.Aligned += f => { if (statusText) statusText.text = $"Aligned on floor {f}"; };
            }
            if (showWallsToggle) showWallsToggle.onValueChanged.AddListener(v => controller.map.ShowDebugWalls = v);
            if (alignHereButton) alignHereButton.onClick.AddListener(() => aligner.AlignStandingOnNode(debugStartNode));

            if (instructionText) instructionText.text = "Scan a KMUTT AR poster, then choose a destination";
        }

        void Update()
        {
            if (statusText && occlusion && aligner && aligner.IsAligned && Time.frameCount % 120 == 0)
                statusText.text = $"Floor {controller.CurrentFloor} · {occlusion.Status}";
        }
    }
}
