using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace WingCommand
{
    /// <summary>Moves a stuck aircraft to a pose on the ground (raised by its spawn offset), once (spec M3 §2.1): every rigidbody of the aircraft keeps its offset
    /// from the root, velocities are zeroed, and every finite-difference velocity is reseeded (Aircraft, Pilot,
    /// GForceDamage, ImpactDetector, FuelTank: zero means "no previous value", native §E2) so the move is not an
    /// impact.</summary>
    internal static class SafeRelocate
    {
        public static float SurfaceProbeHeight = 20f;

        private static readonly FieldInfo ImpactPrev = AccessTools.Field(typeof(ImpactDetector), "velocityPrev");
        private static readonly FieldInfo FuelPrev = AccessTools.Field(typeof(FuelTank), "velocityPrev");
        // The bodies and objects of the aircraft being moved (reused; the move runs rarely).
        private static readonly List<Rigidbody> bodies = new List<Rigidbody>();
        private static readonly List<GameObject> objects = new List<GameObject>();

        /// <remarks>It is put down as the game's spawner puts an aircraft down: its spawn offset above the ground, turned to the
        /// pose and at its rest attitude (definition.restRotation). Night-2 sim: an attitude kept from the move (a helicopter caught
        /// climbing nose-down) or a level one (not its resting pitch) starts the gear's springs compressed.</remarks>
        public static void Move(Aircraft a, Pose to)
        {
            Transform root = a.transform;
            // The pose is on the ground; the aircraft's root stands its spawn offset above it (as a hangar spawn does). The
            // graph's height can be off the pavement's: of the surfaces under the point, the one nearest the graph's height wins —
            // never the first from above, which in a hangar is its roof (night-1 m3b-refit: a jet put on a roof fell and was lost).
            Vector3 target = to.Pos.ToLocal();
            RaycastHit[] hits = Physics.RaycastAll(target + Vector3.up * SurfaceProbeHeight, Vector3.down, 2f * SurfaceProbeHeight);
            int n = 0;
            float[] ys = new float[hits.Length];
            foreach (RaycastHit hit in hits)
                if (hit.collider != null && !hit.collider.transform.IsChildOf(a.transform)) ys[n++] = hit.point.y;
            float graphY = target.y;
            target.y = SurfacePick.Closest(target.y, ys, n, SurfaceProbeHeight);
            float surfaceY = target.y;
            target += Vector3.up * (a.definition != null ? a.definition.spawnOffset.y : 0f);
            Vector3 fwd = to.Fwd.Horizontal.SqrLength > 1e-4f ? to.Fwd.Horizontal.Normalized.ToUnity() : root.forward;
            Quaternion rest = Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.Euler(a.definition != null ? a.definition.restRotation : Vector3.zero);
            Quaternion turn = rest * Quaternion.Inverse(root.rotation);
            Vector3 origin = root.position;
            // Review (ground): on the host an aircraft flies complex physics — every part with mass is unparented with its own
            // Rigidbody, held to its neighbours by FixedJoints (Aircraft.SetComplexPhysics -> AeroPart.CreateRB SetParent(null),
            // CreateJoints). A move of the root's hierarchy alone left the wings and tail behind, and the joints flung the fuselage
            // back to them (night-2 sim: 40-490 m/s, lost). Every part's body and object moves with it.
            bodies.Clear();
            objects.Clear();
            objects.Add(a.gameObject);
            foreach (Rigidbody rb in a.GetComponentsInChildren<Rigidbody>()) bodies.Add(rb);
            if (a.partLookup != null)
                foreach (UnitPart part in a.partLookup)
                {
                    // A part that broke off stays where it lies (review minor: debris was carried along).
                    if (part == null || part.IsDetached()) continue;
                    if (part.rb != null && !bodies.Contains(part.rb)) bodies.Add(part.rb);
                    if (!part.transform.IsChildOf(root)) objects.Add(part.gameObject);
                }
            Plugin.LogVerbose($"[Ground] relocating {a.definition?.unitName}: {bodies.Count} bodies, {objects.Count - 1} parts apart from it, " +
                                  $"from {origin} to {target} (graph y {graphY:0.00}, surface y {surfaceY:0.00} of {n} hits, offset " +
                                  $"{(a.definition != null ? a.definition.spawnOffset.y : 0f):0.00})");
            foreach (Rigidbody rb in bodies)
            {
                Vector3 p = target + turn * (rb.position - origin);
                Quaternion r = turn * rb.rotation;
                rb.position = p;
                rb.rotation = r;
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                // An unparented part's transform goes with its body now (the joints are solved from both).
                if (!rb.transform.IsChildOf(root)) rb.transform.SetPositionAndRotation(p, r);
            }
            root.SetPositionAndRotation(target, turn * root.rotation);
            a.velocityPrev = Vector3.zero;
            if (a.pilots != null)
                foreach (Pilot pilot in a.pilots)
                    if (pilot != null) pilot.velocityPrev = Vector3.zero;
            foreach (GameObject o in objects)
            {
                foreach (GForceDamage g in o.GetComponentsInChildren<GForceDamage>(true)) g.velocityPrev = Vector3.zero;
                if (ImpactPrev != null)
                    foreach (ImpactDetector d in o.GetComponentsInChildren<ImpactDetector>(true)) ImpactPrev.SetValue(d, Vector3.zero);
                if (FuelPrev != null)
                    foreach (FuelTank f in o.GetComponentsInChildren<FuelTank>(true)) FuelPrev.SetValue(f, Vector3.zero);
            }
            Plugin.Logger.LogInfo($"[Ground] moved {a.definition.unitName} to ({to.Pos.X:0}, {to.Pos.Z:0}) heading {Vec3.HeadingDeg(to.Fwd):0} (stuck, or towed round at its stand)");
            WingRuntime.Instance?.StartCoroutine(Watch(a, target));
        }

        /// <summary>How the aircraft moves in the physics frames after a relocation (dev evidence, a few log lines).</summary>
        private static IEnumerator Watch(Aircraft a, Vector3 target)
        {
            int frame = 0;
            foreach (int at in new[] { 1, 2, 5, 15, 60 })
            {
                while (frame < at)
                {
                    yield return new WaitForFixedUpdate();
                    frame++;
                }
                if (a == null) yield break;
                Rigidbody rb = a.rb;
                Plugin.LogVerbose($"[Ground] after relocation +{frame} frames: {(a.transform.position - target).magnitude:0.0} m from where it was put, " +
                                      $"v {(rb != null ? rb.velocity.magnitude : 0f):0.0} m/s, w {(rb != null ? rb.angularVelocity.magnitude : 0f):0.00} rad/s, " +
                                      $"pitch {-a.transform.eulerAngles.x:0.0}, radar {a.radarAlt:0.0}, disabled {a.disabled}");
                if (a.disabled) yield break;
            }
        }
    }
}
