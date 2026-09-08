using System;

namespace WingCommand
{
    /// <summary>Validated host-vehicle profile pushed by companion plugins. Describes surface-command
    /// capabilities without running extension callbacks inside UI or flight loops; the default leaves
    /// aircraft behaviour unchanged.</summary>
    public static class WingHost
    {
        /// <summary>Runtime API version for incompatible profile changes. A property avoids baking the
        /// value into consuming assemblies like a const would.</summary>
        public static int ApiVersion => 1;

        private static WingHostProfile current;
        private static int revision;

        /// <summary>Validated active host profile, defaulting to an ordinary aircraft.</summary>
        public static WingHostProfile Current => current;

        /// <summary>Revision incremented on Set/Clear so cached radial labels and slices
        /// refresh.</summary>
        public static int Revision => revision;

        /// <summary>Set host metadata after synchronous validation; invalid profiles fail at registration
        /// rather than during flight.</summary>
        public static void Set(in WingHostProfile profile)
        {
            if (profile.Owner == null)
            {
                throw new ArgumentException(
                    "A host profile must name the aircraft it describes.", nameof(profile));
            }

            profile.Validate();
            current = profile;
            revision++;
        }

        /// <summary>Restore the default aircraft profile.</summary>
        public static void Clear()
        {
            if (current.Owner == null) return;
            current = default;
            revision++;
        }

        /// <summary>Clear a profile when its owner is no longer leader. The central leader-change path
        /// covers death, ejection, mission changes, and takeover.</summary>
        internal static void NoteLeader(object leader)
        {
            if (current.Owner == null) return;
            if (ReferenceEquals(current.Owner, leader)) return;
            Clear();
        }

        /// <summary>Internal test reset; extensions should call Clear.</summary>
        internal static void Reset()
        {
            current = default;
            revision = 0;
        }
    }

    /// <summary>Immutable host description with inert defaults for unspecified features.</summary>
    public readonly struct WingHostProfile
    {
        // Supported order count; reject longer tables as incompatible instead of truncating them.
        private const int OrderCount = 13;

        /// <summary>Profile owner compared only by reference. Object keeps the contract
        /// engine-independent.</summary>
        public object Owner { get; }

        /// <summary>Whether the host is a surface vehicle.</summary>
        public bool IsSurfaceVehicle { get; }

        /// <summary>Short diagnostic vehicle-class tag.</summary>
        public string VehicleClass { get; }

        /// <summary>Force leader-tracking deck hold for overhead escort while preserving explicit
        /// orders.</summary>
        public bool Overwatch { get; }

        /// <summary>Permit mixed rotary/fixed-wing members only with Overwatch, where no aircraft must
        /// match another's slot speed.</summary>
        public bool AllowMixedAirframes { get; }

        /// <summary>Permit members without autopilots under Overwatch; registered surface behaviours must
        /// provide their control.</summary>
        public bool AllowSurfaceWingmen { get; }

        /// <summary>Orbit height above host in metres; zero retains defaults.</summary>
        public float OverwatchAltitude { get; }

        /// <summary>WingOrder bitmask of commands hidden from all interfaces.</summary>
        public uint HiddenOrders { get; }

        /// <summary>Explanation for rejecting a hidden command.</summary>
        public string HiddenReason { get; }

        /// <summary>Custom deck-hold notification text.</summary>
        public string OverwatchToast { get; }

        /// <summary>Custom deck-hold HUD code, replacing HOLD.</summary>
        public string DeckHoldShortCode { get; }

        /// <summary>Custom deck-hold roster text, replacing HOLDING.</summary>
        public string DeckHoldLabel { get; }

        private readonly string[] labels;
        private readonly string[] shortLabels;

        public WingHostProfile(
            object owner,
            bool isSurfaceVehicle = false,
            string vehicleClass = null,
            bool overwatch = false,
            bool allowMixedAirframes = false,
            bool allowSurfaceWingmen = false,
            float overwatchAltitude = 0f,
            uint hiddenOrders = 0u,
            string hiddenReason = null,
            string overwatchToast = null,
            string deckHoldShortCode = null,
            string deckHoldLabel = null,
            string[] labels = null,
            string[] shortLabels = null)
        {
            Owner = owner;
            IsSurfaceVehicle = isSurfaceVehicle;
            VehicleClass = vehicleClass;
            Overwatch = overwatch;
            AllowMixedAirframes = allowMixedAirframes;
            AllowSurfaceWingmen = allowSurfaceWingmen;
            OverwatchAltitude = overwatchAltitude;
            HiddenOrders = hiddenOrders;
            HiddenReason = hiddenReason;
            OverwatchToast = overwatchToast;
            DeckHoldShortCode = deckHoldShortCode;
            DeckHoldLabel = deckHoldLabel;
            this.labels = labels;
            this.shortLabels = shortLabels;
        }

        /// <summary>Whether a profile currently has an owner.</summary>
        public bool Active => Owner != null;

        /// <summary>Custom order label, or null for the default.</summary>
        public string LabelFor(WingOrder order) => Lookup(labels, order);

        /// <summary>Custom compact order code, or null for the default.</summary>
        public string ShortLabelFor(WingOrder order) => Lookup(shortLabels, order);

        /// <summary>Whether every command interface hides this order.</summary>
        public bool IsHidden(WingOrder order)
        {
            int i = (int)order;
            if (i < 0 || i >= 32) return false;
            return (HiddenOrders & (1u << i)) != 0u;
        }

        /// <summary>Build the hidden-order bitmask.</summary>
        public static uint Mask(params WingOrder[] orders)
        {
            uint mask = 0u;
            if (orders == null) return mask;

            for (int i = 0; i < orders.Length; i++)
            {
                int bit = (int)orders[i];
                if (bit >= 0 && bit < 32) mask |= 1u << bit;
            }
            return mask;
        }

        private static string Lookup(string[] table, WingOrder order)
        {
            if (table == null) return null;

            int i = (int)order;
            if (i < 0 || i >= table.Length) return null;

            string s = table[i];
            return string.IsNullOrEmpty(s) ? null : s;
        }

        /// <summary>Validate profile safety and compatibility. Short label tables override only supplied
        /// orders; longer tables imply an incompatible enum and are rejected.</summary>
        internal void Validate()
        {
            if (labels != null && labels.Length > OrderCount)
            {
                throw new ArgumentException(
                    "Label table is longer than WingOrder - built against a different Wing Command?");
            }

            if (shortLabels != null && shortLabels.Length > OrderCount)
            {
                throw new ArgumentException(
                    "Short label table is longer than WingOrder - built against a different Wing Command?");
            }

            if (float.IsNaN(OverwatchAltitude) || OverwatchAltitude < 0f)
            {
                throw new ArgumentException(
                    "Overwatch altitude must be zero (stock) or a positive height in metres.");
            }

            if (AllowMixedAirframes && !Overwatch)
            {
                throw new ArgumentException(
                    "Mixed airframes are only safe under overwatch - a helicopter cannot hold a formation slot on a jet.");
            }

            if (AllowSurfaceWingmen && !Overwatch)
            {
                throw new ArgumentException(
                    "Surface wingmen are only safe under overwatch - the aircraft in the wing must stop flying slots first.");
            }
        }
    }
}
