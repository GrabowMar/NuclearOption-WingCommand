using System.Collections.Generic;
using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Live copy of each hangar's serialized <c>availableAircraft</c> list.
    ///
    /// The game does not keep a JSON catalogue of which airframes fit which pads. Each
    /// hangar prefab carries that array, and an airbase is just the union of its hangars.
    /// This walks the friendly fields once they exist and answers spawn questions from
    /// those lists instead of guessing rotary-vs-jet or hangar size.
    /// </summary>
    internal static class WingHangarStock
    {
        private struct Pad
        {
            internal Airbase Airbase;
            internal Hangar Hangar;
            internal string PadName;
            internal string PadCode;
            internal AircraftDefinition[] Types;
        }

        private static readonly List<Pad> pads = new List<Pad>();
        private static bool logged;

        internal static void Refresh(FactionHQ hq)
        {
            pads.Clear();
            if (hq == null) return;

            foreach (Airbase airbase in hq.GetAirbases())
            {
                if (airbase == null || airbase.disabled || airbase.hangars == null) continue;
                IList<Hangar> hangars = airbase.hangars;
                for (int i = 0; i < hangars.Count; i++)
                {
                    Hangar hangar = hangars[i];
                    if (hangar == null || hangar.Disabled) continue;
                    AircraftDefinition[] types = hangar.GetAvailableAircraft();
                    if (types == null || types.Length == 0) continue;
                    pads.Add(new Pad
                    {
                        Airbase = airbase,
                        Hangar = hangar,
                        PadName = hangar.name,
                        PadCode = PadCodeOf(hangar),
                        Types = types,
                    });
                }
            }

            LogOnce();
        }

        internal static bool FieldLists(Airbase airbase, AircraftDefinition definition)
        {
            if (airbase == null || definition == null) return false;
            if (pads.Count == 0) return WalkField(airbase, definition);
            for (int i = 0; i < pads.Count; i++)
            {
                if (pads[i].Airbase != airbase) continue;
                if (Lists(pads[i].Types, definition)) return true;
            }
            return false;
        }

        internal static bool HangarLists(Hangar hangar, AircraftDefinition definition)
        {
            if (hangar == null || definition == null) return false;
            return Lists(hangar.GetAvailableAircraft(), definition);
        }

        /// <summary>
        /// The hangar's own definition instance, which native <c>CanSpawnAircraft</c>
        /// compares by reference. Shop catalogue entries are usually the same object;
        /// when they are not, spawning the hangar's copy is what the pad will accept.
        /// </summary>
        internal static AircraftDefinition NativeDefinition(Hangar hangar, AircraftDefinition wanted)
        {
            if (hangar == null || wanted == null) return wanted;
            AircraftDefinition[] types = hangar.GetAvailableAircraft();
            if (types == null) return wanted;
            for (int i = 0; i < types.Length; i++)
                if (Matches(types[i], wanted)) return types[i];
            return wanted;
        }

        internal static string FieldStockText(Airbase airbase)
        {
            if (airbase == null) return "";
            var summaries = new List<string>();
            for (int i = 0; i < pads.Count; i++)
            {
                if (pads[i].Airbase != airbase) continue;
                summaries.Add(HangarStockPolicy.FormatPadStock(
                    HangarStockPolicy.PadLabel(pads[i].PadCode), CodesOf(pads[i].Types)));
            }
            if (summaries.Count == 0 && airbase.hangars != null)
            {
                IList<Hangar> hangars = airbase.hangars;
                for (int i = 0; i < hangars.Count; i++)
                {
                    Hangar hangar = hangars[i];
                    if (hangar == null || hangar.Disabled) continue;
                    summaries.Add(HangarStockPolicy.FormatPadStock(
                        HangarStockPolicy.PadLabel(PadCodeOf(hangar)),
                        CodesOf(hangar.GetAvailableAircraft())));
                }
            }
            return HangarStockPolicy.FormatFieldStock(summaries);
        }

        internal static string AirframeLaunchText(AircraftDefinition definition, bool allowedOnly)
        {
            if (definition == null)
                return HangarStockPolicy.FormatLaunchSites(null);

            var sites = new List<string>();
            Airbase last = null;
            var padLabels = new List<string>();
            for (int i = 0; i < pads.Count; i++)
            {
                Pad pad = pads[i];
                if (!Lists(pad.Types, definition)) continue;
                if (allowedOnly && !WingLaunchFields.IsAllowed(pad.Airbase)) continue;
                if (last != null && pad.Airbase != last)
                {
                    sites.Add(Site(last, padLabels));
                    padLabels.Clear();
                }
                last = pad.Airbase;
                string label = HangarStockPolicy.PadLabel(pad.PadCode);
                if (!padLabels.Contains(label)) padLabels.Add(label);
            }
            if (last != null) sites.Add(Site(last, padLabels));
            return HangarStockPolicy.FormatLaunchSites(sites);
        }

        internal static void Reset()
        {
            pads.Clear();
            logged = false;
        }

        private static string Site(Airbase airbase, List<string> padLabels) =>
            WingLaunchFields.DisplayName(airbase) +
            (padLabels.Count == 0 ? "" : " (" + HangarStockPolicy.JoinLimited(padLabels, 4) + ")");

        private static bool WalkField(Airbase airbase, AircraftDefinition definition)
        {
            IList<Hangar> hangars = airbase.hangars;
            if (hangars == null) return false;
            for (int i = 0; i < hangars.Count; i++)
            {
                Hangar hangar = hangars[i];
                if (hangar == null || hangar.Disabled) continue;
                if (HangarLists(hangar, definition)) return true;
            }
            return false;
        }

        private static bool Lists(AircraftDefinition[] types, AircraftDefinition wanted)
        {
            if (types == null || wanted == null) return false;
            for (int i = 0; i < types.Length; i++)
                if (Matches(types[i], wanted)) return true;
            return false;
        }

        private static bool Matches(AircraftDefinition listed, AircraftDefinition wanted)
        {
            if (listed == null || wanted == null) return false;
            if (ReferenceEquals(listed, wanted)) return true;
            return HangarStockPolicy.SameAirframe(
                listed.jsonKey, listed.unitName, wanted.jsonKey, wanted.unitName);
        }

        private static string PadCodeOf(Hangar hangar)
        {
            Unit unit = hangar != null ? hangar.attachedUnit : null;
            UnitDefinition definition = unit != null ? unit.definition : null;
            return definition != null ? definition.code : "";
        }

        private static List<string> CodesOf(AircraftDefinition[] types)
        {
            var codes = new List<string>();
            if (types == null) return codes;
            for (int i = 0; i < types.Length; i++)
            {
                AircraftDefinition type = types[i];
                if (type == null) continue;
                string code = !string.IsNullOrEmpty(type.code) ? type.code : type.unitName;
                if (!string.IsNullOrEmpty(code) && !codes.Contains(code)) codes.Add(code);
            }
            return codes;
        }

        private static void LogOnce()
        {
            if (logged || pads.Count == 0) return;
            logged = true;
            Airbase last = null;
            var parts = new List<string>();
            for (int i = 0; i <= pads.Count; i++)
            {
                bool flush = i == pads.Count || (last != null && pads[i].Airbase != last);
                if (flush && last != null)
                {
                    Plugin.LogVerbose("[Shop] hangar stock " +
                        WingLaunchFields.DisplayName(last) + ": " +
                        HangarStockPolicy.JoinLimited(parts, 12));
                    parts.Clear();
                }
                if (i == pads.Count) break;
                Pad pad = pads[i];
                last = pad.Airbase;
                parts.Add(pad.PadName + " [" + HangarStockPolicy.PadLabel(pad.PadCode) + "] " +
                          HangarStockPolicy.JoinLimited(CodesOf(pad.Types), 10));
            }
        }
    }
}
