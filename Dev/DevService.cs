using System.Globalization;
using UnityEngine;

namespace WingCommand
{
    /// <summary>Dev tools behind <c>Debug/DevTools</c>: the overlay, the telemetry dump hotkey and the bridge
    /// snapshot (2 Hz).</summary>
    internal sealed class DevService : IWingService
    {
        public string Name => "Dev";
        private float nextBridge;

        public void Activate() => TelemetryRecorder.Clear();

        public void Deactivate()
        {
            DebugOverlay.Hide();
            TelemetryRecorder.Clear();
        }

        public void FixedTick(float dt)
        {
        }

        public void Tick(float dt)
        {
            WingConfig s = Plugin.Settings;
            if (!s.DevTools.Value)
            {
                DebugOverlay.Hide();
                return;
            }
            WingService wing = WingService.Instance;
            if (s.Overlay.Value) DebugOverlay.Draw(wing);
            else DebugOverlay.Hide();
            if (s.KeyDumpTelemetry.Value.MainKey != KeyCode.None && s.KeyDumpTelemetry.Value.IsDown()) TelemetryRecorder.Dump("manual");
            if (Time.unscaledTime >= nextBridge)
            {
                nextBridge = Time.unscaledTime + 0.5f;
                UpdateBridge(wing);
            }
        }

        private static void UpdateBridge(WingService wing)
        {
            BridgeState b = BridgeState.Instance;
            if (b == null) return;
            PlayerAutopilot ap = PlayerAutopilot.Instance;
            int n = wing != null ? wing.Members.Count : 0;
            b.Summary = wing?.Selection == null ? "wing disabled"
                : $"{n} member(s), {wing.Selection.Current.Id} {wing.Selection.Spacing} {wing.Selection.SpacingMetres:0} m, " +
                  $"leader {(wing.Leader != null ? wing.Leader.definition.unitName : "none")}";
            b.Autopilot = ap != null ? WingHudText.Autopilot(ap.Session.Spec, ap.Session.LateralOverride, ap.Session.VerticalOverride) : "";
            b.AiMsPerFrame = wing != null ? wing.LastFrameAiMs : 0.0;
            for (int i = 0; i < b.Members.Length; i++)
            {
                if (i >= n)
                {
                    b.Members[i] = "";
                    continue;
                }
                WingMember m = wing.Members[i];
                float error = (wing.Wing.Frame.Slots[m.Brain.Slot].Ref.Pos - m.Last.Pos).Length;
                b.Members[i] = WingHudText.Member(m.Brain.Slot, WingHudText.Phase(m.Brain.Mind.Current, m.Brain.LastRejoin.FallingBehind),
                    error, m.Brain.Pipeline.Report.Describe()) +
                    " σ" + m.Brain.LastRejoin.Sigma.ToString("0.00", CultureInfo.InvariantCulture);
            }
        }
    }
}
