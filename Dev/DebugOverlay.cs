using UnityEngine;

namespace WingCommand
{
    /// <summary>World-space debug lines per wingman (spec §8):
    /// <list type="bullet">
    /// <item>green to its slot;</item>
    /// <item>yellow to the reference it is tracking (rendezvous, pre-slot or slot);</item>
    /// <item>cyan along its velocity command (1 s of travel);</item>
    /// <item>red along a collision bias.</item>
    /// </list>
    /// Pooled LineRenderers with the built-in <c>Hidden/Internal-Colored</c> shader; positions are converted
    /// to local every frame.</summary>
    internal static class DebugOverlay
    {
        private const int PerMember = 4;
        private static readonly LineRenderer[] lines = new LineRenderer[FormationCatalog.MaxSlots * PerMember];
        private static GameObject root;
        private static bool failed;

        public static void Draw(WingService wing)
        {
            if (!Ensure()) return;
            int used = 0;
            if (wing?.Wing != null)
            {
                for (int i = 0; i < wing.Members.Count; i++)
                {
                    WingMember m = wing.Members[i];
                    WingFrame frame = wing.FrameOf(m);
                    if (frame == null || m.Brain.Slot >= frame.Count) continue;
                    Vec3 p = m.Last.Pos;
                    int slot = m.Brain.Slot;
                    Line(used++, p, frame.Slots[slot].Ref.Pos, Color.green);
                    Line(used++, p, m.Brain.LastIntent.Ref.Pos, Color.yellow);
                    Line(used++, p, p + m.Brain.LastGuidance.VelCmd, Color.cyan);
                    Vec3 bias = frame.Bias[slot];
                    if (bias.SqrLength > 1e-4f) Line(used++, p, p + bias * 10f, Color.red);
                }
            }
            for (int i = used; i < lines.Length; i++) lines[i].enabled = false;
        }

        public static void Hide()
        {
            if (root == null) return;
            foreach (LineRenderer l in lines) l.enabled = false;
        }

        private static bool Ensure()
        {
            if (root != null) return true;
            if (failed) return false;
            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
            {
                failed = true;
                Plugin.Logger.LogWarning("[Overlay] line shader not found; the debug overlay is off");
                return false;
            }
            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            root = new GameObject("WingCommand_Overlay") { hideFlags = HideFlags.HideAndDontSave };
            Object.DontDestroyOnLoad(root);
            for (int i = 0; i < lines.Length; i++)
            {
                var go = new GameObject("Line" + i) { hideFlags = HideFlags.HideAndDontSave };
                go.transform.SetParent(root.transform, false);
                LineRenderer lr = go.AddComponent<LineRenderer>();
                lr.sharedMaterial = material;
                lr.positionCount = 2;
                lr.widthMultiplier = 1.5f;
                lr.useWorldSpace = true;
                lr.enabled = false;
                lines[i] = lr;
            }
            return true;
        }

        private static void Line(int i, Vec3 a, Vec3 b, Color c)
        {
            LineRenderer lr = lines[i];
            lr.enabled = true;
            lr.startColor = c;
            lr.endColor = c;
            lr.SetPosition(0, a.ToLocal());
            lr.SetPosition(1, b.ToLocal());
        }
    }
}
