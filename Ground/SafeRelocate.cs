using System.Collections;
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
            // Night-2 sim: relocated aircraft (VL-49, SFB-81, FS-20) were often flung (40-100 m/s, nose 56 deg down) and lost the
            // moment they were moved. Evidence first: every body moved, every joint and whether it holds a body left behind.
            Rigidbody[] bodies = a.GetComponentsInChildren<Rigidbody>();
            int joints = 0, outside = 0;
            foreach (Joint j in a.GetComponentsInChildren<Joint>(true))
            {
                joints++;
                if (j.connectedBody != null && !j.connectedBody.transform.IsChildOf(root)) outside++;
            }
            Plugin.Logger.LogInfo($"[Ground] relocating {a.definition?.unitName}: {bodies.Length} bodies, {joints} joints ({outside} to bodies outside it), " +
                                  $"from {origin} to {target} (graph y {graphY:0.00}, surface y {surfaceY:0.00} of {n} hits, offset " +
                                  $"{(a.definition != null ? a.definition.spawnOffset.y : 0f):0.00}), pitch {-root.eulerAngles.x:0.0} to rest " +
                                  $"{-rest.eulerAngles.x:0.0}");
            foreach (Rigidbody rb in bodies)
            {
                rb.position = target + turn * (rb.position - origin);
                rb.rotation = turn * rb.rotation;
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            root.SetPositionAndRotation(target, turn * root.rotation);
            a.velocityPrev = Vector3.zero;
            if (a.pilots != null)
                foreach (Pilot p in a.pilots)
                    if (p != null) p.velocityPrev = Vector3.zero;
            foreach (GForceDamage g in a.GetComponentsInChildren<GForceDamage>(true)) g.velocityPrev = Vector3.zero;
            if (ImpactPrev != null)
                foreach (ImpactDetector d in a.GetComponentsInChildren<ImpactDetector>(true)) ImpactPrev.SetValue(d, Vector3.zero);
            if (FuelPrev != null)
                foreach (FuelTank f in a.GetComponentsInChildren<FuelTank>(true)) FuelPrev.SetValue(f, Vector3.zero);
            Plugin.Logger.LogWarning($"[Ground] relocated {a.definition.unitName} to ({to.Pos.X:0}, {to.Pos.Z:0}) after it was stuck");
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
                Plugin.Logger.LogInfo($"[Ground] after relocation +{frame} frames: {(a.transform.position - target).magnitude:0.0} m from where it was put, " +
                                      $"v {(rb != null ? rb.velocity.magnitude : 0f):0.0} m/s, w {(rb != null ? rb.angularVelocity.magnitude : 0f):0.00} rad/s, " +
                                      $"pitch {-a.transform.eulerAngles.x:0.0}, radar {a.radarAlt:0.0}, disabled {a.disabled}");
                if (a.disabled) yield break;
            }
        }
    }
}
