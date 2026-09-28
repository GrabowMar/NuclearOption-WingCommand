using System;
using System.Collections.Generic;
using HarmonyLib;
using NOAvionics;
using UnityEngine;

// Harmony calls prefixes by reflection.
#pragma warning disable IDE0051

namespace WingCommand
{
    /// <summary>Right-click orders on the maximized map (spec WMC program §5): an armed mode claims the map through
    /// <see cref="MapPicker"/> and places its order for the WMC scope; with no mode and wingmen selected a right-click is
    /// MOVE. Every order goes through <see cref="WingOrders.Run"/>.</summary>
    internal sealed class WmcMapInput
    {
        private static WmcMapInput active;
        private readonly MapGesture gesture = new MapGesture();
        private readonly List<uint> targets = new List<uint>(WingOrder.MaxUnits);
        private bool pressed, heldForMove, pressOnMap;
        private GlobalPosition pressAt;
        private int selected;

        public MapMode Mode { get; private set; }
        /// <summary>The PLAN tool armed (spec bezel v2 §6): its right-clicks go to <see cref="PlanClick"/>, not to an order.</summary>
        public PlanTool Tool { get; private set; }
        /// <summary>PLAN's editor takes a tool's click: the point, the unit under it, shift, and the radius a drag set (0: none).</summary>
        public Action<WmcContext, GlobalPosition, Unit, bool, float> PlanClick;
        /// <summary>The lane a tool adds to, in words (the cue and the status line).</summary>
        public string ToolLane = "A";

        /// <summary>A CAP or SWEEP right-drag under way: its centre and radius (the overlay's live ring).</summary>
        public bool Dragging { get; private set; }
        public float DragX { get; private set; }
        public float DragZ { get; private set; }
        public float DragRadius { get; private set; }

        private bool Armed => Mode != MapMode.Off || Tool != PlanTool.Off;
        private bool Area => MapOrders.IsArea(Mode) || Tool == PlanTool.Cap || Tool == PlanTool.Sweep;

        /// <summary>An accepted order was placed at a point (the overlay pings it).</summary>
        public event Action<GlobalPosition> Placed;

        public WmcMapInput() => active = this;

        public string Prompt(string scope) => Tool != PlanTool.Off ? PlanWords.ToolCue(Tool, ToolLane) : MapOrders.Prompt(Mode, scope);

        /// <summary>Arms a mode for the next right-clicks; says why when it cannot.</summary>
        public bool Arm(WmcContext c, MapMode mode)
        {
            if (mode == MapMode.Off)
            {
                Disarm();
                return true;
            }
            if (!DynamicMap.mapMaximized || SceneSingleton<DynamicMap>.i == null)
            {
                WingToast.Show("Open the map to place orders");
                return false;
            }
            if (c == null || !c.CanOrder)
            {
                WingToast.Show(c != null && c.Client ? "WMC: orders are host only for now" : "Wing Command is not ready");
                return false;
            }
            if (OtherOwner() || !MapPicker.TryArm(MapPicker.WingPoint, MapPicker.GestureRight, MapOrders.Prompt(mode, c.ScopeLabel)))
            {
                WingToast.Show("Another map tool is armed - cancel it first");
                return false;
            }
            Mode = mode;
            Tool = PlanTool.Off;
            heldForMove = false;
            gesture.Clear();
            pressed = false;
            targets.Clear();
            Publish();
            return true;
        }

        /// <summary>Arms a PLAN tool (an armed order mode goes); says why when it cannot.</summary>
        public bool ArmTool(WmcContext c, PlanTool tool)
        {
            if (tool == PlanTool.Off)
            {
                Disarm();
                return true;
            }
            if (!DynamicMap.mapMaximized || SceneSingleton<DynamicMap>.i == null)
            {
                WingToast.Show("Open the map to plan");
                return false;
            }
            if (c == null || !c.CanOrder)
            {
                WingToast.Show(c != null && c.Client ? "The host plans this mission" : "Wing Command is not ready");
                return false;
            }
            if (OtherOwner() || !MapPicker.TryArm(MapPicker.WingPoint, MapPicker.GestureRight, PlanWords.ToolCue(tool, ToolLane)))
            {
                WingToast.Show("Another map tool is armed - cancel it first");
                return false;
            }
            Mode = MapMode.Off;
            Tool = tool;
            heldForMove = false;
            gesture.Clear();
            pressed = false;
            Publish();
            return true;
        }

        public void Disarm()
        {
            Mode = MapMode.Off;
            Tool = PlanTool.Off;
            Dragging = false;
            heldForMove = false;
            MapPicker.Disarm(MapPicker.WingPoint);
            gesture.Clear();
            pressed = false;
            targets.Clear();
            Publish();
        }

        /// <summary>What a companion reads (WingMapMode) and the pause key: held while an order is armed (Esc disarms it instead of
        /// pausing; spec bezel v2 §6).</summary>
        private void Publish()
        {
            WingCommand.Interop.WingMapMode.GestureArmed = Armed || heldForMove;
            PauseKeyHold.Set(KeyHold.Map, Armed);
        }

        /// <summary>With nothing armed and wingmen selected on a COMMAND tab, a right-click is WMC's MOVE: WMC holds the map picker
        /// while that is so, so a companion's right-click menu does not open on the same click (spec bezel v2 §6).</summary>
        private void HoldForMove(bool want, string scope)
        {
            if (Armed) return;
            if (want && !heldForMove)
            {
                if (OtherOwner() || !MapPicker.TryArm(MapPicker.WingPoint, MapPicker.GestureRight, MapOrders.Prompt(MapMode.Move, scope ?? "WING"))) return;
                heldForMove = true;
                Publish();
            }
            else if (!want && heldForMove)
            {
                heldForMove = false;
                MapPicker.Disarm(MapPicker.WingPoint);
                Publish();
            }
        }

        /// <summary>Every frame while the panel is installed: follow the right button and place a click.</summary>
        public void Update(WmcContext c, bool visible)
        {
            // Spec bezel v2 §6: with nothing armed, a right-click MOVE for the selection is TACTICAL's and FORM's only (the tabs with
            // the COMMAND scope row); elsewhere the right-click stays the game's.
            bool command = WmcPanel.Instance != null && WmcPanel.Instance.CommandShowing;
            selected = c != null && command ? c.Selection.Count : 0;
            WingCommand.Interop.WingMapMode.TacticalCommandActive = command;
            if (!visible)
            {
                if (Armed) Disarm();
                HoldForMove(false, null);
                pressed = false;
                return;
            }
            // A PLAN tool belongs to PLAN › ELEMENTS: leaving it disarms the tool (an order mode stays armed across tabs).
            if (Tool != PlanTool.Off && WmcPanel.Instance != null && !WmcPanel.Instance.PlanShowing) Disarm();
            HoldForMove(selected > 0 && DynamicMap.mapMaximized && c != null && c.CanOrder, c?.ScopeLabel);
            if (Armed && Input.GetKeyDown(KeyCode.Escape) && !WmcNameField.Typing)
            {
                Disarm();
                WingToast.Show("Map tool cancelled");
                return;
            }
            if (!MapOrders.Consumes(Armed, selected > 0, OtherOwner()))
            {
                pressed = false;
                Dragging = false;
                return;
            }
            Vector3 mouse = Input.mousePosition;
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (Input.GetMouseButtonDown(1))
            {
                pressed = true;
                gesture.NotePointerDown(mouse.x, mouse.y);
                pressOnMap = WmcMapPointer.TryGet(map, out pressAt, out _, out _);
                return;
            }
            // CAP and SWEEP: the press is the centre, the drag the radius, drawn live.
            if (pressed && Area && pressOnMap && !gesture.ReleasedAsClick(mouse.x, mouse.y)
                && WmcMapPointer.TryGet(map, out GlobalPosition now, out _, out _))
            {
                Dragging = true;
                DragX = pressAt.x;
                DragZ = pressAt.z;
                DragRadius = MapOrders.DragRadius(pressAt.x, pressAt.z, now.x, now.z);
            }
            if (!pressed || !Input.GetMouseButtonUp(1)) return;
            pressed = false;
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (Dragging)
            {
                Dragging = false;
                Place(c, pressAt, null, shift, DragRadius);
                return;
            }
            if (!gesture.ReleasedAsClick(mouse.x, mouse.y)) return;
            if (!WmcMapPointer.TryGet(map, out GlobalPosition point, out Unit unit, out _)) return;
            Place(c, point, unit, shift);
        }

        /// <summary>Places what the current mode or PLAN tool (or a plain MOVE for a selection) does at <paramref name="point"/>;
        /// <paramref name="radius"/> is a CAP or SWEEP's (0: the tool's or the order's default).</summary>
        public void Place(WmcContext c, GlobalPosition point, Unit unit, bool shift, float radius = 0f)
        {
            if (Tool != PlanTool.Off)
            {
                PlanClick?.Invoke(c, point, unit, shift, radius);
                return;
            }
            MapPointer pointer = unit == null || unit.disabled ? MapPointer.Empty
                : DynamicMap.GetFactionMode(unit.NetworkHQ, false) == FactionMode.Enemy ? MapPointer.Enemy : MapPointer.Other;
            MapClick click = MapOrders.Resolve(Mode, selected > 0, pointer, shift);
            if (click == MapClick.None) return;
            if (click == MapClick.NeedEnemy)
            {
                WingToast.Show("Right-click an enemy on the map");
                return;
            }
            if (click == MapClick.AddPoint)
            {
                WingToast.Show(c.Draft.Add(point.x, point.z) ? "Point " + c.Draft.Count + " added" : "The route is full (16 points)");
                return;
            }
            WmcUi.Order(c, () =>
            {
                if (WingOrders.Run(Build(c, click, point, unit, radius)).Accepted) Placed?.Invoke(point);
            });
        }

        private WingOrder Build(WmcContext c, MapClick click, GlobalPosition point, Unit unit, float radius)
        {
            Waypoint at = Waypoint.At(point.x, point.z);
            at.Altitude = c.Draft.Altitude;
            at.Speed = c.Draft.Speed;
            switch (click)
            {
                case MapClick.Orbit: return WingOrder.Tasked(WingTask.Orbit(at), c.Scope);
                case MapClick.Hold:
                {
                    Vec3 from = From(c, out _);
                    return WingOrder.Tasked(WingTask.Hold(at, Vec3.HeadingDeg(new Vec3(point.x - from.X, 0f, point.z - from.Z))), c.Scope);
                }
                case MapClick.Cargo:
                    at.Action = ArrivalAction.Cargo;
                    return WingOrder.Tasked(WingTask.Move(at), c.Scope);
                case MapClick.Land:
                    at.Action = ArrivalAction.Land;
                    return WingOrder.Tasked(WingTask.Move(at), c.Scope);
                case MapClick.Cap: return WingOrder.Tasked(WingTask.Cap(at, radius > 0f ? radius : AreaGuard.CapRadius), c.Scope);
                case MapClick.Sweep: return WingOrder.Tasked(WingTask.Sweep(at, radius > 0f ? radius : AreaGuard.SweepRadius), c.Scope);
                case MapClick.Attack:
                case MapClick.AddTarget:
                {
                    if (click == MapClick.Attack) targets.Clear();
                    uint id = unit.persistentID.Id;
                    if (!targets.Contains(id) && targets.Count < WingOrder.MaxUnits) targets.Add(id);
                    return new WingOrder { Kind = OrderKind.Attack, Units = targets.ToArray(), Scope = c.Scope };
                }
                default: return WingOrder.Tasked(WingTask.Move(at), c.Scope);
            }
        }

        /// <summary>Where the scope is now and how fast it goes: its element's task lead when one flies, else the selected (or
        /// all) members' mean, else the player. Used for a HOLD's heading and the draft's first leg.</summary>
        public static Vec3 From(WmcContext c, out float speed)
        {
            speed = 0f;
            WingService w = c?.Wing;
            if (w == null) return Vec3.Zero;
            WingPlanner p = w.PlannerOf(c.ScopeElement);
            if (p != null && p.Active && p.Lead != null)
            {
                speed = p.Lead.Speed;
                return p.Lead.Position;
            }
            Vec3 sum = Vec3.Zero;
            float v = 0f;
            int n = 0;
            foreach (WingMember m in w.Members)
            {
                if ((object)m.Aircraft == null || !m.Alive) continue;
                if (c.Selection.Count > 0 && !c.Selection.Contains(m.Aircraft.persistentID.Id)) continue;
                sum += m.Last.Pos;
                v += m.Last.Vel.Length;
                n++;
            }
            if (n > 0)
            {
                speed = v / n;
                return sum * (1f / n);
            }
            Aircraft player = w.Player;
            if (player == null) return Vec3.Zero;
            speed = player.rb != null ? player.rb.velocity.magnitude : 0f;
            return player.GlobalPosition().ToVec3();
        }

        private static bool OtherOwner() => MapPicker.IsBusy && !MapPicker.IsOwner(MapPicker.WingPoint);

        /// <summary>Whether this right-press is WMC's (the game's map controls skip it).</summary>
        public static bool ConsumesNow()
        {
            WmcMapInput m = active;
            if (m == null || !DynamicMap.mapMaximized || WmcPanel.Instance == null || !WmcPanel.Instance.Visible) return false;
            if (!Input.GetMouseButton(1) && !Input.GetMouseButtonDown(1) && !Input.GetMouseButtonUp(1)) return false;
            return MapOrders.Consumes(m.Armed, m.selected > 0, OtherOwner());
        }
    }

    /// <summary>While WMC owns the right-click (spec WMC program §5), the game's map controls skip that press (the 0.9 rule).
    /// ponytail: the whole MapControls call is skipped for the frames of a WMC right-press, so zoom and pan pause for that
    /// press; a transpiler that removes only the waypoint branch if that ever matters.</summary>
    [HarmonyPatch(typeof(DynamicMap), "MapControls")]
    internal static class WmcMapControlsPatch
    {
        // A CENTER or FIT waiting for the map goes in first, so the game draws the view from the new offsets this frame
        // (critic §14.12).
        [HarmonyPrefix]
        private static bool Prefix(DynamicMap __instance)
        {
            bool run = !WmcMapInput.ConsumesNow();
            if (run) WmcMap.Apply(__instance);
            return run;
        }
    }
}
