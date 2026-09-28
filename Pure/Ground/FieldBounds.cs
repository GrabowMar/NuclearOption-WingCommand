using System.Collections.Generic;
using System.Globalization;

namespace WingCommand
{
    /// <summary>What belongs to a field: a hangar, service point or pad within <see cref="Margin"/> of the field's runways, taxiways
    /// and centre. In-game 2026-09-28 Opal Airport (airbase_island5) listed a hangar ~45 km away: a wingman bought there spawned by
    /// another island and taxied toward Opal's runway for the rest of the session. Such a point is dropped before the taxi graph and
    /// the launches see it.</summary>
    internal static class FieldBounds
    {
        public static float Margin = 3000f;

        /// <summary><paramref name="p"/> lies within <see cref="Margin"/> of the field's footprint (horizontal).</summary>
        public static bool Near(AirbaseSample field, Vec3 p)
        {
            float minX = field.Center.X, maxX = minX, minZ = field.Center.Z, maxZ = minZ;
            void Add(Vec3 v)
            {
                if (v.X < minX) minX = v.X;
                if (v.X > maxX) maxX = v.X;
                if (v.Z < minZ) minZ = v.Z;
                if (v.Z > maxZ) maxZ = v.Z;
            }
            foreach (RunwaySample r in field.Runways)
            {
                Add(r.Start);
                Add(r.End);
            }
            foreach (Vec3[] road in field.Roads)
                if (road != null)
                    foreach (Vec3 v in road) Add(v);
            float dx = p.X < minX ? minX - p.X : p.X > maxX ? p.X - maxX : 0f;
            float dz = p.Z < minZ ? minZ - p.Z : p.Z > maxZ ? p.Z - maxZ : 0f;
            return dx * dx + dz * dz <= Margin * Margin;
        }

        /// <summary>Drops the hangars, service points and pads that are not <see cref="Near"/> the field; says which, or null.</summary>
        public static string Prune(AirbaseSample field)
        {
            string dropped = null;
            var hangars = new List<HangarSample>(field.Hangars.Length);
            foreach (HangarSample h in field.Hangars)
            {
                if (Near(field, h.Spawn.Pos)) hangars.Add(h);
                else dropped = Say(dropped, "hangar " + h.Index.ToString(CultureInfo.InvariantCulture), field, h.Spawn.Pos);
            }
            if (hangars.Count != field.Hangars.Length) field.Hangars = hangars.ToArray();
            field.ServicePoints = Keep(field, field.ServicePoints, "service point", ref dropped);
            field.Pads = Keep(field, field.Pads, "pad", ref dropped);
            return dropped;
        }

        /// <summary>The position in <see cref="AirbaseSample.Hangars"/> of the airbase's hangar <paramref name="airbaseIndex"/>, or -1
        /// when it was dropped (or never read).</summary>
        public static int SampleOf(AirbaseSample field, int airbaseIndex)
        {
            for (int j = 0; j < field.Hangars.Length; j++)
                if (field.Hangars[j].Index == airbaseIndex) return j;
            return -1;
        }

        private static Pose[] Keep(AirbaseSample field, Pose[] poses, string what, ref string dropped)
        {
            var kept = new List<Pose>(poses.Length);
            for (int i = 0; i < poses.Length; i++)
            {
                if (Near(field, poses[i].Pos)) kept.Add(poses[i]);
                else dropped = Say(dropped, what + " " + i.ToString(CultureInfo.InvariantCulture), field, poses[i].Pos);
            }
            return kept.Count == poses.Length ? poses : kept.ToArray();
        }

        private static string Say(string dropped, string what, AirbaseSample field, Vec3 at)
        {
            float km = (at - field.Center).Horizontal.Length / 1000f;
            string line = what + " " + km.ToString("0.0", CultureInfo.InvariantCulture) + " km from the centre";
            return dropped == null ? line : dropped + ", " + line;
        }
    }
}
