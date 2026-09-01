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
                    object station = stations[i];
                    if (station == null) continue;

                    Type type = station.GetType();
                    if (!firstCountermeasureMethods.TryGetValue(type, out MethodInfo method))
                    {
                        method = type.GetMethod(
                            "GetFirstCountermeasure", BindingFlags.Instance | BindingFlags.Public);
                        firstCountermeasureMethods[type] = method;
                    }

                    Countermeasure countermeasure = method?.Invoke(station, null) as Countermeasure;
                    if (!(countermeasure is RadarJammer)) continue;
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
    }
}
