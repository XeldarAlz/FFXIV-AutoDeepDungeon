using System;
using System.Collections.Generic;
using System.Numerics;
using AutoDeepDungeon.Data;
using AutoDeepDungeon.Managers;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using ECommons.SplatoonAPI;

namespace AutoDeepDungeon.Windows;

/// <summary>
/// Pushes <see cref="FloorState"/> classifications to Splatoon as a set of named dynamic
/// elements. Splatoon owns the rendering: per-frame world-space projection, near-plane
/// clipping, screen-edge clipping, and actor-following for cones all Just Work.
///
/// We refresh on the FloorScanner cadence (~10Hz) since cones are attached to live actors
/// via <c>refActorObjectID</c> — Splatoon follows the mob at frame rate without our help.
/// Fixed markers (traps, hoards, coffers, passage) don't move so the 100ms cadence is fine.
/// </summary>
public sealed class SplatoonOverlay : IDisposable
{
    private const string Namespace         = "adg_overlay";
    private const int   MaxAggroShapes     = 20;
    private const float AggroDrawRange     = 35f;
    // No distance cap on persistent trap/hoard markers — PalacePal shows the whole floor
    // lit up and the planner wants the full trap set visible for route planning anyway.
    // Splatoon handles hundreds of fixed-coordinate elements without frame-rate impact.
    private const float DedupDistSq        = 25f;

    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(100);
    private DateTime nextRefresh = DateTime.MinValue;
    private readonly List<Vector3> dedupBuffer = new();

    // Splatoon uses the same ABGR packing as ImGui.
    private static uint Rgba(byte r, byte g, byte b, byte a)
        => ((uint)a << 24) | ((uint)b << 16) | ((uint)g << 8) | r;

    private static readonly uint ColorAggroSight  = Rgba(255, 64,  64,  230);
    private static readonly uint ColorMimic       = Rgba(255, 80,  0,   235);
    private static readonly uint ColorTrap        = Rgba(255, 40,  40,  230);
    private static readonly uint ColorHoard       = Rgba(255, 220, 40,  230);
    private static readonly uint ColorCoffer      = Rgba(255, 255, 255, 220);
    private static readonly uint ColorPassageOn   = Rgba(64,  255, 64,  235);
    private static readonly uint ColorPassageOff  = Rgba(160, 160, 160, 180);
    private static readonly uint ColorPlanPath    = Rgba(0,   200, 255, 230);
    private static readonly uint ColorPlanPathFat = Rgba(255, 100, 255, 230);
    private static readonly uint ColorPlanNode    = Rgba(0,   200, 255, 255);

    public SplatoonOverlay()
    {
        Splatoon.SetOnConnect(OnConnect);
        Svc.Framework.Update += Tick;
        Svc.ClientState.TerritoryChanged += OnTerritoryChanged;
    }

    public void Dispose()
    {
        Svc.ClientState.TerritoryChanged -= OnTerritoryChanged;
        Svc.Framework.Update -= Tick;
        Clear();
    }

    public bool IsConnected => Splatoon.IsConnected();

    private static void OnConnect()
    {
        // Splatoon reconnected — all previously-added dynamic elements are gone. Next
        // Tick will repopulate them.
    }

    private static void OnTerritoryChanged(ushort _) => Clear();

    private static void Clear()
    {
        try { Splatoon.RemoveDynamicElements(Namespace); } catch { /* best effort */ }
    }

    private void Tick(IFramework framework)
    {
        if (!Plugin.Config.EnableDebugOverlay)
        {
            Clear();
            return;
        }
        if (!Splatoon.IsConnected()) return;

        var now = DateTime.UtcNow;
        if (now < nextRefresh) return;
        nextRefresh = now + Interval;

        var floor = Plugin.Floor?.Current;
        if (floor == null || !floor.InDeepDungeon)
        {
            Clear();
            return;
        }

        Refresh(floor);
    }

    private void Refresh(FloorState floor)
    {
        Clear();
        var elements = new List<Element>();

        AddAggroCones(elements, floor);
        AddCirclesWithDedup(elements, floor, floor.Traps,   floor.PersistentTraps,   1.5f, ColorTrap);
        AddCirclesWithDedup(elements, floor, floor.Hoards,  floor.PersistentHoards,  1.0f, ColorHoard);

        foreach (var c in floor.Coffers)
            elements.Add(MakeFixedCircle(c.Position, 0.8f, ColorCoffer));

        if (floor.Passage is { } p)
            elements.Add(MakeFixedCircle(p.Position, 2.0f, p.Active ? ColorPassageOn : ColorPassageOff));

        AddPlannerPath(elements);

        if (elements.Count > 0)
            Splatoon.AddDynamicElements(Namespace, elements.ToArray(), 0L);
    }

    /// <summary>
    /// Draws the planner's current waypoint list as a polyline so the user can
    /// see exactly what route the plugin is about to walk. Cyan when the path
    /// is non-fatal (what the executor will actually drive), magenta when the
    /// score says HasTrap (which means auto-drive is halted — the segments
    /// show which trap the route would cross if we ignored the halt).
    /// </summary>
    private static void AddPlannerPath(List<Element> elements)
    {
        var plan = Plugin.Planner?.Current;
        if (plan == null || plan.Waypoints.Count < 2) return;

        var color = plan.Score.HasTrap ? ColorPlanPathFat : ColorPlanPath;

        for (var i = 0; i < plan.Waypoints.Count - 1; i++)
        {
            var a = plan.Waypoints[i];
            var b = plan.Waypoints[i + 1];
            var line = new Element(ElementType.LineBetweenTwoFixedCoordinates)
            {
                color = color,
                thicc = 3f,
                Enabled = true,
            };
            line.SetRefCoord(a);
            line.SetOffCoord(b);
            elements.Add(line);
        }

        // Node markers at each waypoint so it's clear where vnav's turn points
        // are — useful for spotting when a waypoint lands too close to a trap.
        foreach (var w in plan.Waypoints)
            elements.Add(MakeFixedCircle(w, 0.4f, ColorPlanNode));
    }

    private static void AddAggroCones(List<Element> elements, FloorState floor)
    {
        var self = Svc.ClientState.LocalPlayer?.Position ?? floor.SelfPosition;
        var rangeSq = AggroDrawRange * AggroDrawRange;

        var sorted = new List<MobEntity>();
        foreach (var m in floor.Mobs)
        {
            if (m.CurrentHp == 0) continue;
            if (Vector3.DistanceSquared(self, m.Position) > rangeSq) continue;
            sorted.Add(m);
        }
        sorted.Sort((a, b) => Vector3.DistanceSquared(self, a.Position)
                                    .CompareTo(Vector3.DistanceSquared(self, b.Position)));

        var limit = Math.Min(sorted.Count, MaxAggroShapes);
        for (var i = 0; i < limit; i++)
        {
            var m = sorted[i];
            var cone = new Element(ElementType.ConeRelativeToObjectPosition)
            {
                radius = AggroMap.DefaultRadius,
                coneAngleMin = -45,
                coneAngleMax = 45,
                color = m.IsMimicCandidate ? ColorMimic : ColorAggroSight,
                thicc = 2f,
                refActorObjectID = (uint)m.ObjectId,
                refActorComparisonType = RefActorComparisonType.ObjectID,
                includeRotation = true,
                Filled = false,
                Enabled = true,
            };
            elements.Add(cone);
        }
    }

    private void AddCirclesWithDedup(List<Element> elements, FloorState floor,
                                     IReadOnlyList<EObjEntity> live, IReadOnlyList<Vector3> persistent,
                                     float radius, uint color)
    {
        dedupBuffer.Clear();
        foreach (var e in live)
        {
            elements.Add(MakeFixedCircle(e.Position, radius, color));
            dedupBuffer.Add(e.Position);
        }
        foreach (var p in persistent)
        {
            var covered = false;
            foreach (var prev in dedupBuffer)
            {
                if (Vector3.DistanceSquared(prev, p) < DedupDistSq) { covered = true; break; }
            }
            if (covered) continue;
            elements.Add(MakeFixedCircle(p, radius, color));
            dedupBuffer.Add(p);
        }
    }

    private static Element MakeFixedCircle(Vector3 pos, float radius, uint color)
    {
        var e = new Element(ElementType.CircleAtFixedCoordinates)
        {
            radius = radius,
            color = color,
            thicc = 2f,
            Filled = false,
            Enabled = true,
        };
        e.SetRefCoord(pos);
        return e;
    }
}
