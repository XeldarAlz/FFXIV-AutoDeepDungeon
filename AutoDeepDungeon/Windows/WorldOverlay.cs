using System;
using System.Collections.Generic;
using System.Numerics;
using AutoDeepDungeon.Data;
using AutoDeepDungeon.Managers;
using ECommons.DalamudServices;
using Pictomancy;

namespace AutoDeepDungeon.Windows;

/// <summary>
/// Pictomancy-backed world-space debug overlay. Renders aggro footprints, traps, hoards,
/// coffers, and passage active-state so the M1 exit criteria can be eyeballed in-game.
/// Gated on <see cref="Config.EnableDebugOverlay"/>.
///
/// Triangle-budget notes: Pictomancy caps a draw list at 1024 triangles per frame. Filled
/// cones and circles burn the budget fast with DD mob counts, so we render outlines (low
/// segment counts) and cull distant persistent-data markers.
/// </summary>
public sealed class WorldOverlay : IDisposable
{
    // Pictomancy has per-primitive DX overhead in addition to the 1024-tri cap. Both matter
    // for frame time — aggressive draw-call limits are how we stay smooth.
    private const int MaxAggroShapes           = 12;
    private const float AggroDrawRange         = 35f;   // yalms; further mobs aren't worth drawing
    private const float PersistentDrawRange    = 35f;   // ditto for saved trap/hoard hints
    private const int SegAggroCone             = 10;
    private const int SegAggroOmni             = 12;
    private const int SegMarkerLive            = 10;
    private const int SegMarkerPersistent      = 8;
    private const int SegPassage               = 14;
    private const float OutlineThickness       = 2f;
    private const float OutlineThickHighlight  = 3f;

    public WorldOverlay()
    {
        Svc.PluginInterface.UiBuilder.Draw += Draw;
    }

    public void Dispose()
    {
        Svc.PluginInterface.UiBuilder.Draw -= Draw;
    }

    // ImGui ABGR packing: (a << 24) | (b << 16) | (g << 8) | r.
    private static uint Rgba(byte r, byte g, byte b, byte a)
        => ((uint)a << 24) | ((uint)b << 16) | ((uint)g << 8) | r;

    private static readonly uint ColorSelf         = Rgba(64, 160, 255, 255);
    private static readonly uint ColorAggroSight   = Rgba(255, 64,  64,  220);
    private static readonly uint ColorAggroOmni    = Rgba(255, 160, 64,  220);
    private static readonly uint ColorMimic        = Rgba(255, 80,  0,   230);
    private static readonly uint ColorTrapLive     = Rgba(255, 40,  40,  230);
    private static readonly uint ColorTrapPersist  = Rgba(255, 40,  40,  110);
    private static readonly uint ColorHoardLive    = Rgba(255, 220, 40,  230);
    private static readonly uint ColorHoardPersist = Rgba(255, 220, 40,  110);
    private static readonly uint ColorCoffer       = Rgba(255, 255, 255, 220);
    private static readonly uint ColorPassageOn    = Rgba(64,  255, 64,  230);
    private static readonly uint ColorPassageOff   = Rgba(160, 160, 160, 180);

    private static readonly List<MobEntity> mobBuffer = new();

    private static void Draw()
    {
        if (!Plugin.Config.EnableDebugOverlay) return;
        var floor = Plugin.Floor?.Current;
        if (floor == null || !floor.InDeepDungeon) return;

        try
        {
            using var dl = PictoService.Draw();
            if (dl == null) return;

            dl.AddCircle(floor.SelfPosition, 0.5f, ColorSelf, 16, OutlineThickness);

            DrawAggro(dl, floor);
            DrawTraps(dl, floor);
            DrawHoards(dl, floor);
            DrawCoffers(dl, floor);
            DrawPassage(dl, floor);
        }
        catch (Exception ex)
        {
            Svc.Log.Warning($"[WorldOverlay] draw failed: {ex.Message}");
        }
    }

    private static void DrawAggro(PctDrawList dl, FloorState floor)
    {
        var rangeSq = AggroDrawRange * AggroDrawRange;

        mobBuffer.Clear();
        foreach (var m in floor.Mobs)
        {
            if (m.CurrentHp == 0) continue;
            if (Vector3.DistanceSquared(floor.SelfPosition, m.Position) > rangeSq) continue;
            mobBuffer.Add(m);
        }
        mobBuffer.Sort(DistanceComparer(floor.SelfPosition));

        var limit = Math.Min(mobBuffer.Count, MaxAggroShapes);
        for (var i = 0; i < limit; i++)
        {
            var m = mobBuffer[i];
            var geom = AggroMap.Compute(m);
            var color = m.IsMimicCandidate
                ? ColorMimic
                : (geom.Omnidirectional ? ColorAggroOmni : ColorAggroSight);
            var thickness = m.IsMimicCandidate ? OutlineThickHighlight : OutlineThickness;

            if (geom.Omnidirectional)
            {
                dl.AddCircle(geom.Origin, geom.Radius, color, SegAggroOmni, thickness);
            }
            else
            {
                var half = geom.ConeHalfAngle;
                var left  = geom.Facing + half;
                var right = geom.Facing - half;

                dl.AddArc(geom.Origin, geom.Radius, right, left, color, SegAggroCone, thickness);

                var pLeft  = geom.Origin + new Vector3(MathF.Sin(left)  * geom.Radius, 0f, MathF.Cos(left)  * geom.Radius);
                var pRight = geom.Origin + new Vector3(MathF.Sin(right) * geom.Radius, 0f, MathF.Cos(right) * geom.Radius);
                dl.AddLine(geom.Origin, pLeft,  0f, color, thickness);
                dl.AddLine(geom.Origin, pRight, 0f, color, thickness);
            }
        }
    }

    private static Comparison<MobEntity> DistanceComparer(Vector3 self) =>
        (a, b) => Vector3.DistanceSquared(self, a.Position)
                       .CompareTo(Vector3.DistanceSquared(self, b.Position));

    private static void DrawTraps(PctDrawList dl, FloorState floor)
    {
        foreach (var t in floor.Traps)
            dl.AddCircle(t.Position, 1.5f, ColorTrapLive, SegMarkerLive, OutlineThickHighlight);

        foreach (var p in floor.PersistentTraps)
        {
            if (Vector3.DistanceSquared(floor.SelfPosition, p) > PersistentDrawRange * PersistentDrawRange) continue;
            dl.AddCircle(p, 1.5f, ColorTrapPersist, SegMarkerPersistent, OutlineThickness);
        }
    }

    private static void DrawHoards(PctDrawList dl, FloorState floor)
    {
        foreach (var h in floor.Hoards)
            dl.AddCircle(h.Position, 1.0f, ColorHoardLive, SegMarkerLive, OutlineThickHighlight);

        foreach (var p in floor.PersistentHoards)
        {
            if (Vector3.DistanceSquared(floor.SelfPosition, p) > PersistentDrawRange * PersistentDrawRange) continue;
            dl.AddCircle(p, 1.0f, ColorHoardPersist, SegMarkerPersistent, OutlineThickness);
        }
    }

    private static void DrawCoffers(PctDrawList dl, FloorState floor)
    {
        foreach (var c in floor.Coffers)
            dl.AddCircle(c.Position, 0.8f, ColorCoffer, SegMarkerLive, OutlineThickness);
    }

    private static void DrawPassage(PctDrawList dl, FloorState floor)
    {
        if (floor.Passage is not { } p) return;
        var color = p.Active ? ColorPassageOn : ColorPassageOff;
        dl.AddCircle(p.Position, 2.0f, color, SegPassage, OutlineThickHighlight);
    }
}
