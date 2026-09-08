using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace WingCommand
{
    /// <summary>Caches native countermeasure reflection at startup; disables access if the private field
    /// changes.</summary>
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

        /// <summary>Finds chaff or flares for the seeker, excluding jammers. Native selection sorts by
        /// display name and may choose ECM instead of chaff because both advertise ARH/SARH support.
        /// RadarJammerPulser drives ECM separately.</summary>
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

        /// <summary>Invokes the public GetFirstCountermeasure method on its private station type; caches
        /// the method per type.</summary>
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
