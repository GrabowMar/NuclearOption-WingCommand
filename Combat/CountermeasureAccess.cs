using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace WingCommand
{
    /// <summary>
    /// The one reflected boundary for native countermeasure stations. It is capability-
    /// checked at plugin startup and fails closed when a game update moves the private field.
    /// </summary>
    internal static class CountermeasureAccess
    {
        private static readonly Dictionary<Type, MethodInfo> firstCountermeasureMethods =
            new Dictionary<Type, MethodInfo>();

        private static FieldInfo stationsField;
        private static bool initialised;

        public static bool Available { get; private set; }

        public static void Initialise()
        {
            if (initialised) return;
            initialised = true;
            stationsField = typeof(CountermeasureManager).GetField(
                "countermeasureStations", BindingFlags.Instance | BindingFlags.NonPublic);
            Available = stationsField != null;

            if (!Available)
                Plugin.Logger.LogWarning(
                    "Countermeasure station access unavailable; panic ECM support is disabled.");
        }

        /// <summary>
        /// The station holding an <i>expendable</i> that answers this seeker — chaff for a
        /// radar missile, flares for an infrared one.
        ///
        /// This exists because <c>CountermeasureManager.ChooseCountermeasure</c> cannot be
        /// trusted with the question. It returns the first station whose threat types
        /// contain the seeker, walking a list the game keeps sorted by display name — and
        /// <c>RadarJammer.GetThreatTypes()</c> returns exactly the same
        /// <c>{ "ARH", "SARH" }</c> that <c>ChaffEjector.GetThreatTypes()</c> does. On an
        /// aircraft carrying both, which one it picks is decided by alphabetical order.
        ///
        /// When it picks the jammer, the defensive state holds the dispense trigger on a
        /// jammer station and no chaff is ever released at a radar missile, while
        /// <see cref="RadarJammerPulser"/> is separately driving the same station. That is
        /// the reason a wingman could beam a SARH shot correctly and still take it.
        ///
        /// Skipping every non-expendable is the whole fix: the jammer is driven on its own
        /// cadence by the pulser and has no business being the selected dispenser.
        /// </summary>
        public static bool TryFindExpendable(CountermeasureManager manager, string seekerType,
                                             out int index, out string reason)
        {
            index = -1;
            reason = null;
            if (!initialised) Initialise();
            if (!Available || manager == null || string.IsNullOrEmpty(seekerType))
            {
                reason = "native countermeasure station list is unavailable";
                return false;
            }

            try
            {
                if (!(stationsField.GetValue(manager) is IList stations))
                {
                    reason = "native countermeasure station list is unreadable";
                    return false;
                }

                for (int i = 0; i < stations.Count; i++)
                {
                    Countermeasure countermeasure = FirstCountermeasure(stations[i]);
                    if (countermeasure == null || countermeasure is RadarJammer) continue;

                    List<string> types = countermeasure.GetThreatTypes();
                    if (types == null || !types.Contains(seekerType)) continue;

                    index = i;
                    return true;
                }

                return false;
            }
            catch (Exception e)
            {
                reason = e.GetType().Name + " - " + e.Message;
                return false;
            }
        }

        public static bool TryFindRadarJammer(CountermeasureManager manager, out int index,
                                              out string reason)
        {
            index = -1;
            reason = null;
            if (!initialised) Initialise();
            if (!Available || manager == null)
            {
                reason = "native countermeasure station list is unavailable";
                return false;
            }

            try
            {
                IList stations = stationsField.GetValue(manager) as IList;
                if (stations == null)
                {
                    reason = "native countermeasure station list is unreadable";
                    return false;
                }

                for (int i = 0; i < stations.Count; i++)
                {
                    if (!(FirstCountermeasure(stations[i]) is RadarJammer)) continue;
                    index = i;
                    return true;
                }

                return false;
            }
            catch (Exception e)
            {
                reason = e.GetType().Name + " - " + e.Message;
                return false;
            }
        }

        /// <summary>
        /// The countermeasure a station holds. <c>GetFirstCountermeasure</c> is public on the
        /// station, but the station type itself is private, so the call is reflected and the
        /// resolved method cached per type.
        /// </summary>
        private static Countermeasure FirstCountermeasure(object station)
        {
            if (station == null) return null;

            Type type = station.GetType();
            if (!firstCountermeasureMethods.TryGetValue(type, out MethodInfo method))
            {
                method = type.GetMethod(
                    "GetFirstCountermeasure", BindingFlags.Instance | BindingFlags.Public);
                firstCountermeasureMethods[type] = method;
            }

            return method?.Invoke(station, null) as Countermeasure;
        }
    }
}
