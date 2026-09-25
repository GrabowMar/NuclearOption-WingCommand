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
        private bool pressed;
        private int selected;

        public MapMode Mode { get; private set; }

        /// <summary>An accepted order was placed at a point (the overlay pings it).</summary>
        public event Action<GlobalPosition> Placed;

        public WmcMapInput() => active = this;

        public string Prompt(string scope) => MapOrders.Prompt(Mode, scope);

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
            gesture.Clear();
            pressed = false;
            targets.Clear();
            return true;
        }

        public void Disarm()
        {
            Mode = MapMode.Off;
            MapPicker.Disarm(MapPicker.WingPoint);
            gesture.Clear();
            pressed = false;
            targets.Clear();
        }

        /// <summary>Every frame while the panel is installed: follow the right button and place a click.</summary>
        public void Update(WmcContext c, bool visible)
        {
            // Spec WMC rebuild: with nothing armed, a right-click MOVE for the selection is TACTICAL's only (the 0.9 rule); on
            // SUPPLY, LOADOUT or WING the right-click stays the game's. The room places its own orders (it is open over the map).
            bool tactical = WmcPanel.Instance != null && WmcPanel.Instance.TacticalShowing;
            bool room = WmcRoom.Instance != null && WmcRoom.Instance.IsOpen;
            selected = c != null && (tactical || room) ? c.Selection.Count : 0;
            if (!visible)
            {
                if (Mode != MapMode.Off) Disarm();
                pressed = false;
                return;
            }
            // Review P5 I2: under the room (and on the frame it closed) a right-click is the room's, never the map's.
            if (WmcRoom.Instance != null && WmcRoom.Instance.JustOpen)
            {
                pressed = false;
                return;
            }
            if (Mode != MapMode.Off && Input.GetKeyDown(KeyCode.Escape))
            {
                Disarm();
                WingToast.Show("Map order cancelled");
                return;
            }
            if (!MapOrders.Consumes(Mode, selected > 0, OtherOwner()))
            {
                pressed = false;
                return;
            }
            Vector3 mouse = Input.mousePosition;
            if (Input.GetMouseButtonDown(1))
            {
                pressed = true;
                gesture.NotePointerDown(mouse.x, mouse.y);
                return;
            }
            if (!pressed || !Input.GetMouseButtonUp(1)) return;
            pressed = false;
            if (!gesture.ReleasedAsClick(mouse.x, mouse.y)) return;
            if (!WmcMapPointer.TryGet(SceneSingleton<DynamicMap>.i, out GlobalPosition point, out Unit unit, out _)) return;
            Place(c, point, unit, Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
        }

        /// <summary>Places what the current mode (or a plain MOVE for a selection) does at <paramref name="point"/>.</summary>
        public void Place(WmcContext c, GlobalPosition point, Unit unit, bool shift)
        {
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
                if (WingOrders.Run(Build(c, click, point, unit)).Accepted) Placed?.Invoke(point);
            });
        }

        private WingOrder Build(WmcContext c, MapClick click, GlobalPosition point, Unit unit)
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
            return MapOrders.Consumes(m.Mode, m.selected > 0, OtherOwner());
        }
    }

    /// <summary>While WMC owns the right-click (spec WMC program §5), the game's map controls skip that press (the 0.9 rule).
    /// ponytail: the whole MapControls call is skipped for the frames of a WMC right-press, so zoom and pan pause for that
    /// press; a transpiler that removes only the waypoint branch if that ever matters.</summary>
    [HarmonyPatch(typeof(DynamicMap), "MapControls")]
    internal static class WmcMapControlsPatch
    {
        // The room covers the map: its wheel and drags are the room's, not the game map's underneath.
        [HarmonyPrefix]
        private static bool Prefix() => !WmcMapInput.ConsumesNow() && !(WmcRoom.Instance != null && WmcRoom.Instance.IsOpen);
    }
}
