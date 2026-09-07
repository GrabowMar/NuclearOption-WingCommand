using System;

namespace WingCommand
{
    /// <summary>
    /// Where on a runway a requisitioned aircraft is put down, as scalars.
    ///
    /// The vector arithmetic itself lives in <c>WingAirfield</c>, because a runway's heading
    /// and threshold are Unity transforms. What is here is the part that is decidable
    /// without an engine and therefore worth testing: which end of the strip to use, how far
    /// along it the aircraft's centre goes, and whether a strip is fit to launch from at all.
    ///
    /// The along-track offset is the number the whole approach turns on. The stock taxi
    /// state's destination is the threshold itself, so an aircraft placed too far past it is
    /// steered <i>backwards</i> down the runway by <c>AutoAim</c> and eventually leaves the
    /// pavement; one placed too close leaves its tail hanging off the end. Both failures are
    /// invisible in a build and obvious in a log, which is exactly the shape of thing to
    /// pin down in a test.
    /// </summary>
    internal static class LaunchGeometry
    {
        /// <summary>Clearance kept between the tail and the paved end of the strip.</summary>
        public const float ThresholdMargin = 8f;

        /// <summary>
        /// Never place an aircraft further along the strip than this.
        ///
        /// The stock taxi state hands off to takeoff inside twelve metres of the threshold,
        /// and that distance is measured in three dimensions from a point the aircraft is
        /// also lifted above by its own <c>spawnOffset.y</c> — so the along-track budget is
        /// not the full twelve. Ten leaves room for a tall airframe and still puts the
        /// handoff on the second physics tick after the spawn, before the aircraft has
        /// steered anywhere.
        /// </summary>
        public const float MaximumThresholdOffset = 10f;

        /// <summary>Shortest strip worth launching from, before the airframe's own needs.</summary>
        public const float MinimumRunwayLength = 200f;

        /// <summary>
        /// How far past the threshold, along the takeoff direction, the aircraft's centre is
        /// placed.
        ///
        /// Half the airframe's largest footprint plus a margin would put the tail clear of
        /// the paved end, and that is the number this starts from — but it is capped, because
        /// past <see cref="MaximumThresholdOffset"/> the aircraft is behind its own taxi
        /// destination. A long airframe therefore overhangs the threshold slightly rather
        /// than being steered back at it; the overhang costs nothing, because
        /// <c>Runway.AircraftOnRunway</c> measures the fuselage origin against the centreline
        /// and clamps to the threshold for anything behind it.
        /// </summary>
        public static float ThresholdOffset(float length, float width)
        {
            float half = Math.Max(Math.Max(length, width), 0f) * 0.5f;
            return Math.Min(MaximumThresholdOffset, half + ThresholdMargin);
        }

        /// <summary>
        /// Whether a strip can launch this airframe.
        ///
        /// <c>Airbase.GetTakeoffRunway</c> deliberately ignores occupancy and length beyond
        /// the caller's own minimum, so the caller has to supply the judgement. A sloped or
        /// short strip is refused here rather than discovered at rotation speed.
        /// </summary>
        public static bool IsUsable(bool takeoff, float runwayLength, float takeoffRun,
                                    bool level)
        {
            if (!takeoff || !level) return false;
            if (runwayLength < MinimumRunwayLength) return false;
            return runwayLength >= Math.Max(0f, takeoffRun) + MaximumThresholdOffset;
        }

        /// <summary>
        /// Ground roll to allow for, from the airframe's takeoff speed.
        ///
        /// A rough <c>v²/2a</c> at a nominal acceleration, deliberately generous. It exists
        /// to reject a strip that is obviously too short for a heavy aircraft, not to predict
        /// a rotation point, and the stock takeoff state does not consult it at all.
        /// </summary>
        public static float TakeoffRun(float takeoffSpeed)
        {
            float v = Math.Max(0f, takeoffSpeed);
            return v * v / (2f * NominalTakeoffAcceleration);
        }

        private const float NominalTakeoffAcceleration = 4f;

        /// <summary>
        /// Which end of a reversible strip to launch from: the nearer one, as the stock
        /// takeoff and taxi states both choose it.
        ///
        /// Kept separate from the engine call so that the tie — a field whose two ends are
        /// equidistant — resolves the same way every time rather than on float noise.
        /// </summary>
        public static bool PreferReverse(float distanceToStart, float distanceToEnd,
                                         bool reversable)
        {
            if (!reversable) return false;
            return distanceToEnd < distanceToStart;
        }

        /// <summary>
        /// Whether a spawned aircraft is where it was meant to be, checked a physics step
        /// after the spawn rather than on the line that requested it.
        ///
        /// <c>Spawner.SpawnAircraft</c> instantiates a prefab that has not yet run its own
        /// initialisation, so reading its pose immediately proves nothing. A delivery that
        /// fails this has landed somewhere other than the strip and must not be handed to
        /// the takeoff state.
        /// </summary>
        public static bool OnRunway(bool aircraftOnRunway, float headingDot) =>
            aircraftOnRunway && headingDot >= HeadingTolerance;

        /// <summary>
        /// Nose alignment the stock takeoff state itself requires before it will firewall
        /// the throttle, rather than aim across the field for the runway heading.
        /// </summary>
        public const float HeadingTolerance = 0.95f;
    }
}
