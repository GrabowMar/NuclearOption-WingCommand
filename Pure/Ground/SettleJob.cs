using System;

namespace WingCommand
{
    /// <summary>What a settled helicopter is there for (spec M4 §7.3).</summary>
    internal enum SettleTask : byte { Hold, Cargo, Rescue }

    [Flags]
    internal enum SettleAction : byte { None = 0, FireCargo = 1, TakeOff = 2 }

    /// <summary>Spec M4 §7.3: a settled helicopter's job once down. Hold waits for Take Off; Cargo fires its cargo once
    /// <see cref="CargoDelay"/> after touchdown and lifts off <see cref="CargoWait"/> later; Rescue lifts off when the
    /// survivor is gone or after <see cref="RescueWait"/>. Only time down counts, summed across bounces: a bounce neither
    /// fires twice nor restarts the wait.</summary>
    internal sealed class SettleJob
    {
        public static float CargoDelay = 2f, CargoWait = 8f, RescueWait = 90f;

        public readonly SettleTask Task;
        private bool done;

        public SettleJob(SettleTask task) => Task = task;

        public bool Fired { get; private set; }
        /// <summary>Seconds down, summed across bounces.</summary>
        public float DownTotal { get; private set; }

        public SettleAction Step(SettlePhase phase, float dt, bool rescued)
        {
            if (done || Task == SettleTask.Hold || phase != SettlePhase.Down) return SettleAction.None;
            DownTotal += dt;
            SettleAction act = SettleAction.None;
            if (Task == SettleTask.Cargo)
            {
                if (!Fired && DownTotal >= CargoDelay - 1e-4f)
                {
                    Fired = true;
                    act |= SettleAction.FireCargo;
                }
                if (Fired && DownTotal >= CargoDelay + CargoWait - 1e-4f) act |= SettleAction.TakeOff;
            }
            else if (rescued || DownTotal >= RescueWait - 1e-4f) act |= SettleAction.TakeOff;
            if ((act & SettleAction.TakeOff) != 0) done = true;
            return act;
        }
    }
}
