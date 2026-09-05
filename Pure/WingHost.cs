using System;

namespace WingCommand
{
    /// <summary>
    /// What the player is commanding *from*, when it is not an aircraft.
    ///
    /// Two community mods - KAR (ground vehicles) and BOTE (warships) - put the player in
    /// something the game still models as an Aircraft but which has no autopilot, no gear,
    /// and a top speed a jet cannot fly at. Every assumption this mod makes about the leader
    /// then reads wrong: formation slots land in the sea, the deck-hold entry test never
    /// fires because there is no gear to extend, and the shop offers helicopters only
    /// because a null autopilot classifies as rotary.
    ///
    /// This is the seam a companion plugin uses to say so. It is deliberately a *pushed
    /// value*, not a callback: the call sites are the label table, the order gate and the
    /// deck test, all of which run per roster row per repaint or every tick. A delegate or
    /// an interface would put third-party code inside all three and then need the try/catch,
    /// the fault set and the per-tick cache that <see cref="WingAi"/> needs for exactly that
    /// reason. A struct cannot throw, and the caching is the registration itself.
    ///
    /// The default profile is inert in every field, so nothing here changes behaviour until
    /// something calls <see cref="Set"/>.
    /// </summary>
    public static class WingHost
    {
        /// <summary>
        /// Bumped when the shape of <see cref="WingHostProfile"/> changes incompatibly.
        ///
        /// A property rather than a const, and that is the entire point of it: a const is
        /// baked into the calling assembly at compile time, so a plugin checking one would
        /// be comparing its own build-time copy against itself and could never detect a
        /// mismatch. This is read from the Wing Command that is actually loaded.
        /// </summary>
        public static int ApiVersion => 1;

        private static WingHostProfile current;
        private static int revision;

        /// <summary>The active profile. Never invalid; the default describes an ordinary aircraft.</summary>
        public static WingHostProfile Current => current;

        /// <summary>
        /// Increments on every <see cref="Set"/> and <see cref="Clear"/>.
        ///
        /// The radial wheel and the overlay slice table are built once and cached, so a
        /// relabel is invisible to both without something to compare against. This is that
        /// something.
        /// </summary>
        public static int Revision => revision;

        /// <summary>
        /// Describe the vehicle the player is commanding from.
        ///
        /// Validation throws here, at the caller, rather than at the tick that would have
        /// read the bad value - which is the whole reason the seam pushes rather than calls.
        /// </summary>
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

        /// <summary>Go back to describing an ordinary aircraft.</summary>
        public static void Clear()
        {
            if (current.Owner == null) return;
            current = default;
            revision++;
        }

        /// <summary>
        /// Drop a profile whose vehicle is no longer the leader.
        ///
        /// Called from the one place the leader is assigned, which makes this the whole
        /// liveness story: a mission change, an ejection, a death and a takeover into a
        /// wingman's seat all pass through there, and none of them needs its own hook. A
        /// registrant that never unregisters therefore cannot leave a stale profile applied
        /// to an aircraft that is not the one it described.
        /// </summary>
        internal static void NoteLeader(object leader)
        {
            if (current.Owner == null) return;
            if (ReferenceEquals(current.Owner, leader)) return;
            Clear();
        }

        /// <summary>Test seam. Not for plugins, which unregister through <see cref="Clear"/>.</summary>
        internal static void Reset()
        {
            current = default;
            revision = 0;
        }
    }

    /// <summary>
    /// An immutable description of a non-aircraft host vehicle.
    ///
    /// Every field is inert at its default, so a partially filled profile degrades to stock
    /// behaviour for everything it does not mention.
    /// </summary>
    public readonly struct WingHostProfile
    {
        // The number of WingOrder members. A table longer than this was built against a
        // different Wing Command, which Validate refuses rather than silently truncating.
        private const int OrderCount = 12;

        /// <summary>
        /// The aircraft this profile describes, compared by reference only.
        ///
        /// Typed object so this file stays engine-free and testable: the only operation
        /// performed on it is a reference comparison.
        /// </summary>
        public object Owner { get; }

        /// <summary>True when the host is a surface vehicle rather than an aircraft.</summary>
        public bool IsSurfaceVehicle { get; }

        /// <summary>A short tag for logs and toasts - "kar", "bote", "surface".</summary>
        public string VehicleClass { get; }

        /// <summary>
        /// Hold the wing overhead unconditionally.
        ///
        /// The existing deck hold is already the behaviour a surface leader wants - a
        /// leader-tracking orbit that leaves explicit orders alone - so this forces its
        /// entry test true rather than introducing a second kind of holding.
        /// </summary>
        public bool Overwatch { get; }

        /// <summary>
        /// Let rotary and fixed-wing share the wing.
        ///
        /// Safe only alongside <see cref="Overwatch"/>: the refusal exists because a
        /// helicopter cannot hold a slot on a jet, and in overwatch nobody holds a slot.
        /// </summary>
        public bool AllowMixedAirframes { get; }

        /// <summary>
        /// Let units with no autopilot join the wing as members.
        ///
        /// Off by default and gated the same way as <see cref="AllowMixedAirframes"/>,
        /// because with it on the roster can contain something none of the built-in flight
        /// states can fly - safe only once the wing has stopped trying to hold slots.
        /// </summary>
        public bool AllowSurfaceWingmen { get; }

        /// <summary>Metres above the host to orbit at. Zero keeps the stock altitudes.</summary>
        public float OverwatchAltitude { get; }

        /// <summary>Bitmask over <see cref="WingOrder"/>: set bits are offered nowhere.</summary>
        public uint HiddenOrders { get; }

        /// <summary>Why a hidden order is hidden, for the toast that refuses it.</summary>
        public string HiddenReason { get; }

        /// <summary>Replaces "Leader on the deck - wing holding overhead".</summary>
        public string OverwatchToast { get; }

        /// <summary>Replaces the deck-hold HUD code, stock "HOLD".</summary>
        public string DeckHoldShortCode { get; }

        /// <summary>Replaces the deck-hold roster label, stock "HOLDING".</summary>
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

        /// <summary>True while a profile is applied at all.</summary>
        public bool Active => Owner != null;

        /// <summary>The override name for an order, or null to use the stock one.</summary>
        public string LabelFor(WingOrder order) => Lookup(labels, order);

        /// <summary>The override short code for an order, or null to use the stock one.</summary>
        public string ShortLabelFor(WingOrder order) => Lookup(shortLabels, order);

        /// <summary>Whether this order is withheld from every surface.</summary>
        public bool IsHidden(WingOrder order)
        {
            int i = (int)order;
            if (i < 0 || i >= 32) return false;
            return (HiddenOrders & (1u << i)) != 0u;
        }

        /// <summary>Build a mask for <see cref="HiddenOrders"/>.</summary>
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

        /// <summary>
        /// Reject a profile that would misbehave, rather than let it through to a tick.
        ///
        /// A label table shorter than the enum is not an error - it simply overrides the
        /// orders it covers - but one longer than the enum means the caller was built
        /// against a different WingOrder, and ignoring the tail would hide that.
        /// </summary>
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
