using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace WingCommand
{
    /// <summary>Moves a stuck aircraft to a pose, once (spec M3 §2.1): every rigidbody of the aircraft keeps its offset
    /// from the root, velocities are zeroed, and every finite-difference velocity is reseeded (Aircraft, Pilot,
    /// GForceDamage, ImpactDetector, FuelTank: zero means "no previous value", native §E2) so the move is not an
    /// impact.</summary>
    internal static class SafeRelocate
    {
        private static readonly FieldInfo ImpactPrev = AccessTools.Field(typeof(ImpactDetector), "velocityPrev");
        private static readonly FieldInfo FuelPrev = AccessTools.Field(typeof(FuelTank), "velocityPrev");

        public static void Move(Aircraft a, Pose to)
        {
            Transform root = a.transform;
            Vector3 target = to.Pos.ToLocal();
            Vector3 fwd = to.Fwd.Horizontal.SqrLength > 1e-4f ? to.Fwd.Horizontal.Normalized.ToUnity() : root.forward;
            Quaternion turn = Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.Inverse(Quaternion.LookRotation(
                Vector3.ProjectOnPlane(root.forward, Vector3.up).normalized, Vector3.up));
            Vector3 origin = root.position;
            foreach (Rigidbody rb in a.GetComponentsInChildren<Rigidbody>())
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
        }
    }
}
