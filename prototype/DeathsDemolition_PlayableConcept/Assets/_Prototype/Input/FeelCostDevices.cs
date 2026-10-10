#if DEVELOPMENT_BUILD || UNITY_EDITOR
using UnityEngine.InputSystem;

namespace DD.Prototype.Inputs
{
    /// <summary>
    /// WP-095 R3 · the gamepad the feel-cost replay records (Diagnostics/FeelCostAgent: device id, layout, interface, native) — it lives
    /// in Input/, the only folder that reads the Input System (ADR-024-2 / WP-059 parity — InputParityContractTests.InputSystem_YalnizInputKlasorunde).
    /// No pad: id 0, layout and interface "NONE", not native. Reads cached device fields only (no allocation in sampled frames).
    /// </summary>
    public static class FeelCostDevices
    {
        public static int PadId => Gamepad.current != null ? Gamepad.current.deviceId : 0;

        public static void Read(out int deviceId, out string layout, out string interfaceName, out bool native)
        {
            Gamepad pad = Gamepad.current;
            deviceId = pad != null ? pad.deviceId : 0;
            layout = pad != null ? pad.layout : "NONE";
            interfaceName = pad != null ? pad.description.interfaceName : "NONE";
            native = pad != null && pad.native;
        }
    }
}
#endif
