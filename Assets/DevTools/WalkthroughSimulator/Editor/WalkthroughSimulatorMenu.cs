using UnityEditor;

namespace KMUTT.DevTools
{
    static class WalkthroughSimulatorMenu
    {
        const string MenuPath = "KMUTT/Dev/Walkthrough Simulator (Play mode)";

        [MenuItem(MenuPath, priority = 200)]
        static void Toggle()
        {
            bool on = !EditorPrefs.GetBool(WalkthroughSimulator.PrefKey, true);
            EditorPrefs.SetBool(WalkthroughSimulator.PrefKey, on);
            UnityEngine.Debug.Log($"[WalkthroughSimulator] {(on ? "Enabled" : "Disabled")} — takes effect next time you press Play.");
        }

        [MenuItem(MenuPath, true)]
        static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, EditorPrefs.GetBool(WalkthroughSimulator.PrefKey, true));
            return true;
        }
    }
}
