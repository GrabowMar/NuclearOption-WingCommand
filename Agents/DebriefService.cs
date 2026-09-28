using System;
using System.Collections.Generic;
using System.IO;

namespace WingCommand
{
    /// <summary>The DEBRIEF's sortie (spec WMC rebuild §PLAN LOG and DEBRIEF): the wing's events folded as they come, kills credited by
    /// callsign, and on leaving the mission the sortie kept with the last ten in <c>debrief.user.json</c> (written through a temporary
    /// file). Host only; a sortie with nothing flown is not kept.</summary>
    internal sealed class DebriefService : IWingService
    {
        public static DebriefService Instance { get; private set; }

        public string Name => "Debrief";
        public SortieLog Sortie { get; } = new SortieLog();
        private EventCursor cursor;
        private DebriefStore store;

        private static string FilePath => Path.Combine(WingConfig.DataRoot, "debrief.user.json");

        public DebriefService()
        {
            Instance = this;
            WingPilotRoster.Killed += OnKill;
        }

        /// <summary>The kept debriefs, newest first (read once).</summary>
        public DebriefStore Store
        {
            get
            {
                if (store != null) return store;
                var errors = new List<string>();
                try
                {
                    store = DebriefStore.FromJson(File.Exists(FilePath) ? File.ReadAllText(FilePath) : null, errors);
                }
                catch (Exception e)
                {
                    errors.Add(e.Message);
                    store = new DebriefStore();
                }
                foreach (string e in errors) Plugin.Logger.LogWarning("[Debrief] debrief.user.json: " + e);
                return store;
            }
        }

        public void Activate()
        {
            WingService w = WingService.Instance;
            Sortie.Begin(w?.MissionTime ?? 0f);
            Sortie.Theatre = WmcPlanFiles.Theatre;
            cursor.Seen = w?.Events.Total ?? 0;
        }

        public void Deactivate()
        {
            WingService w = WingService.Instance;
            Sortie.Finish(w?.MissionTime ?? Sortie.End);
            if (WingNet.ClientOnly || (Sortie.Launched == 0 && Sortie.Airborne == 0 && Sortie.Lost == 0 && Sortie.Kills == 0)) return;
            Store.Add(Sortie.Lines(), Sortie.Theatre);
            try
            {
                Directory.CreateDirectory(WingConfig.DataRoot);
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, Store.ToJson());
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(tmp, FilePath);
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("[Debrief] could not save debrief.user.json: " + e.Message);
            }
        }

        public void FixedTick(float dt) { }

        public void Tick(float dt)
        {
            WingService w = WingService.Instance;
            if (w == null) return;
            while (cursor.Next(w.Events, out WingEvent e)) Sortie.Add(e);
            Sortie.Finish(w.MissionTime);
        }

        private void OnKill(Aircraft shooter, uint victimId, string victimType) =>
            Sortie.Kill(WingPilotRoster.Of(shooter)?.Callsign, victimType);
    }
}
