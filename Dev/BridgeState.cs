using UnityEngine;

// Unity calls Awake by reflection.
#pragma warning disable IDE0051

namespace WingCommand
{
    /// <summary>Live snapshot for nomodkit <c>bridge_find</c>/<c>bridge_inspect</c> (spec §8). Lives on the
    /// runtime object; <see cref="DevService"/> refreshes it at 2 Hz while dev tools are on.</summary>
    internal sealed class BridgeState : MonoBehaviour
    {
        public static BridgeState Instance { get; private set; }

        public string Summary = "";
        public string Autopilot = "";
        public string[] Members = new string[FormationCatalog.MaxSlots];
        public double AiMsPerFrame;

        private void Awake() => Instance = this;
    }
}
