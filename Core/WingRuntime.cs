using System;
using System.Collections.Generic;
using UnityEngine;

// Unity calls Update and FixedUpdate by reflection.
#pragma warning disable IDE0051

namespace WingCommand
{
    /// <summary>A subsystem driven by <see cref="WingRuntime"/>. Services activate when a mission becomes
    /// playable and deactivate on leaving it, so no mission state survives into menus.</summary>
    internal interface IWingService
    {
        string Name { get; }
        void Activate();
        void Deactivate();
        void Tick(float dt);
        void FixedTick(float dt);
    }

    /// <summary>Persistent host that owns mission lifecycle and ticks services in registration order. A
    /// service that throws is disabled for the rest of the mission instead of breaking the others.</summary>
    [DefaultExecutionOrder(10000)]
    internal sealed class WingRuntime : MonoBehaviour
    {
        private readonly List<IWingService> services = new List<IWingService>();
        private readonly HashSet<IWingService> faulted = new HashSet<IWingService>();
        private bool active;

        internal static WingRuntime Instance { get; private set; }

        internal static bool InPlayableState
        {
            get
            {
                GameState s = GameManager.gameState;
                return s == GameState.SinglePlayer || s == GameState.Multiplayer;
            }
        }

        internal void Register(IWingService service)
        {
            services.Add(service);
            if (active) Run(service, Phase.Activate, 0f);
        }

        private void Awake() => Instance = this;

        private void Update()
        {
            bool playable = InPlayableState;
            if (playable != active)
            {
                if (playable) BeginMission();
                else EndMission();
            }
            if (!active) return;

            WingFrameGate.NoteFrame(Time.unscaledDeltaTime);
            float dt = Time.deltaTime;
            for (int i = 0; i < services.Count; i++) Run(services[i], Phase.Tick, dt);
        }

        private void FixedUpdate()
        {
            if (!active) return;
            float dt = Time.fixedDeltaTime;
            for (int i = 0; i < services.Count; i++) Run(services[i], Phase.FixedTick, dt);
        }

        private void BeginMission()
        {
            active = true;
            faulted.Clear();
            // Snapshot fidelity on mission entry; setting changes apply to the next mission.
            WingFidelity.Begin(Plugin.Settings.Mode.Value);
            Plugin.Logger.LogInfo(new WingDiagnostic(WingDiagnosticEvent.MissionStarted,
                WingFidelity.Mode == WingMode.Performance ? 1 : 0));
            for (int i = 0; i < services.Count; i++) Run(services[i], Phase.Activate, 0f);
        }

        private void EndMission()
        {
            for (int i = services.Count - 1; i >= 0; i--) Run(services[i], Phase.Deactivate, 0f);
            active = false;
            Plugin.Logger.LogInfo(new WingDiagnostic(WingDiagnosticEvent.WingReset, 0));
        }

        private enum Phase { Activate, Deactivate, Tick, FixedTick }

        // Dispatch by phase rather than delegate so the per-frame path allocates nothing.
        private void Run(IWingService service, Phase phase, float dt)
        {
            if (faulted.Contains(service)) return;
            try
            {
                switch (phase)
                {
                    case Phase.Activate: service.Activate(); break;
                    case Phase.Deactivate: service.Deactivate(); break;
                    case Phase.Tick: service.Tick(dt); break;
                    case Phase.FixedTick: service.FixedTick(dt); break;
                }
            }
            catch (Exception e)
            {
                faulted.Add(service);
                Plugin.Logger.LogError($"[Runtime] service '{service.Name}' failed in {phase} and is disabled " +
                    $"until the next mission: {e}");
            }
        }
    }
}
