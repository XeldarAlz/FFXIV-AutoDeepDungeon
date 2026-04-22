using System;
using System.Collections.Generic;
using System.Numerics;
using AutoDeepDungeon.Data;
using AutoDeepDungeon.Managers;
using Dalamud.Bindings.ImGui;
using ECommons.DalamudServices;

namespace AutoDeepDungeon.Windows;

/// <summary>
/// World-space debug overlay drawn via ImGui's background draw list. We project each
/// world-space vertex to screen with <see cref="IGameGui.WorldToScreen"/> and stroke the
/// resulting polylines — the same technique RadarPlugin uses. Much cheaper than
/// Pictomancy's DX-backed world draws and has no per-frame triangle budget.
/// </summary>
public sealed class WorldOverlay : IDisposable
{
    private const int MaxAggroShapes       = 15;
    private const float AggroDrawRange     = 35f;
    private const float PersistentDrawRange= 35f;

    private const int SegCone              = 24;   // each shape now strokes in a single
    private const int SegCircle            = 40;   // draw call, so higher counts are cheap.
    private const int SegMarker            = 20;

    private const float ThicknessOutline   = 1.8f;
    private const float ThicknessHighlight = 2.6f;

    public WorldOverlay()
    {
        Svc.PluginInterface.UiBuilder.Draw += Draw;
    }

    public void Dispose()
    {
        Svc.PluginInterface.UiBuilder.Draw -= Draw;
    }

    private static uint Rgba(byte r, byte g, byte b, byte a)
        => ((uint)a << 24) | ((uint)b << 16) | ((uint)g << 8) | r;

    private static readonly uint ColorSelf         = Rgba(64, 160, 255, 255);
    private static readonly uint ColorAggroSight   = Rgba(255, 64,  64,  230);
    private static readonly uint ColorAggroRing    = Rgba(255, 64,  64,  80);
    private static readonly uint ColorMimic        = Rgba(255, 80,  0,   235);
    private static readonly uint ColorMimicRing    = Rgba(255, 80,  0,   90);
    private static readonly uint ColorTrapLive     = Rgba(255, 40,  40,  230);
    private static readonly uint ColorTrapPersist  = Rgba(255, 40,  40,  110);
    private static readonly uint ColorHoardLive    = Rgba(255, 220, 40,  230);
    private static readonly uint ColorHoardPersist = Rgba(255, 220, 40,  110);
    private static readonly uint ColorCoffer       = Rgba(255, 255, 255, 220);
    private static readonly uint ColorPassageOn    = Rgba(64,  255, 64,  235);
    private static readonly uint ColorPassageOff   = Rgba(160, 160, 160, 180);

    private static readonly List<MobEntity> mobBuffer = new();

    private static void Draw()
    {
        if (!Plugin.Config.EnableDebugOverlay) return;
        var floor = Plugin.Floor?.Current;
        if (floor == null || !floor.InDeepDungeon) return;

        try
        {
            var dl = ImGui.GetBackgroundDrawList();
            DrawCircleWorld(dl, floor.SelfPosition, 0.5f, ColorSelf, ThicknessOutline, 16);
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

    private static void DrawAggro(ImDrawListPtr dl, FloorState floor)
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
            var isMimic = m.IsMimicCandidate;
            var coneColor = isMimic ? ColorMimic : ColorAggroSight;
            var ringColor = isMimic ? ColorMimicRing : ColorAggroRing;
            var thickness = isMimic ? ThicknessHighlight : ThicknessOutline;

            // Faded detection-radius ring.
            DrawCircleWorld(dl, geom.Origin, geom.Radius, ringColor, ThicknessOutline, SegCircle);

            if (geom.Omnidirectional)
            {
                // Solid full ring already drawn above; bright outline on top for emphasis.
                DrawCircleWorld(dl, geom.Origin, geom.Radius, coneColor, thickness, SegCircle);
            }
            else
            {
                DrawSectorWorld(dl, geom.Origin, geom.Radius, geom.Facing, geom.ConeHalfAngle,
                                coneColor, thickness, SegCone);
            }
        }
    }

    private static void DrawTraps(ImDrawListPtr dl, FloorState floor)
    {
        var persistSq = PersistentDrawRange * PersistentDrawRange;

        foreach (var t in floor.Traps)
            DrawCircleWorld(dl, t.Position, 1.5f, ColorTrapLive, ThicknessHighlight, SegMarker);

        foreach (var p in floor.PersistentTraps)
        {
            if (Vector3.DistanceSquared(floor.SelfPosition, p) > persistSq) continue;
            DrawCircleWorld(dl, p, 1.5f, ColorTrapPersist, ThicknessOutline, SegMarker);
        }
    }

    private static void DrawHoards(ImDrawListPtr dl, FloorState floor)
    {
        var persistSq = PersistentDrawRange * PersistentDrawRange;

        foreach (var h in floor.Hoards)
            DrawCircleWorld(dl, h.Position, 1.0f, ColorHoardLive, ThicknessHighlight, SegMarker);

        foreach (var p in floor.PersistentHoards)
        {
            if (Vector3.DistanceSquared(floor.SelfPosition, p) > persistSq) continue;
            DrawCircleWorld(dl, p, 1.0f, ColorHoardPersist, ThicknessOutline, SegMarker);
        }
    }

    private static void DrawCoffers(ImDrawListPtr dl, FloorState floor)
    {
        foreach (var c in floor.Coffers)
            DrawCircleWorld(dl, c.Position, 0.8f, ColorCoffer, ThicknessOutline, SegMarker);
    }

    private static void DrawPassage(ImDrawListPtr dl, FloorState floor)
    {
        if (floor.Passage is not { } p) return;
        var color = p.Active ? ColorPassageOn : ColorPassageOff;
        DrawCircleWorld(dl, p.Position, 2.0f, color, ThicknessHighlight, 20);
    }

    /// <summary>
    /// Stroke a horizontal circle at <paramref name="center"/> as a single ImGui polyline.
    /// Behind-camera points break the path — we stroke what we have and restart, which
    /// gives a natural clip at the screen edge without drawing garbage lines.
    /// </summary>
    private static void DrawCircleWorld(ImDrawListPtr dl, Vector3 center, float radius,
                                        uint color, float thickness, int segments)
    {
        var pathHasPoints = false;
        for (var i = 0; i <= segments; i++)
        {
            var angle = i * MathF.Tau / segments;
            var world = new Vector3(
                center.X + MathF.Sin(angle) * radius,
                center.Y,
                center.Z + MathF.Cos(angle) * radius);

            if (Svc.GameGui.WorldToScreen(world, out var scr))
            {
                dl.PathLineTo(scr);
                pathHasPoints = true;
            }
            else if (pathHasPoints)
            {
                dl.PathStroke(color, ImDrawFlags.None, thickness);
                pathHasPoints = false;
            }
        }
        if (pathHasPoints)
            dl.PathStroke(color, ImDrawFlags.Closed, thickness);
    }

    /// <summary>
    /// Stroke a horizontal circular sector: origin → arc spanning
    /// [facing − half, facing + half] → back to origin, as a single closed polyline.
    /// </summary>
    private static void DrawSectorWorld(ImDrawListPtr dl, Vector3 origin, float radius,
                                        float facing, float halfAngle,
                                        uint color, float thickness, int arcSegments)
    {
        if (!Svc.GameGui.WorldToScreen(origin, out var originScr)) return;

        dl.PathLineTo(originScr);
        var arcHasPoints = false;
        for (var i = 0; i <= arcSegments; i++)
        {
            var t = i / (float)arcSegments;
            var angle = (facing - halfAngle) + t * (2f * halfAngle);
            var world = new Vector3(
                origin.X + MathF.Sin(angle) * radius,
                origin.Y,
                origin.Z + MathF.Cos(angle) * radius);

            if (Svc.GameGui.WorldToScreen(world, out var scr))
            {
                dl.PathLineTo(scr);
                arcHasPoints = true;
            }
            else if (arcHasPoints)
            {
                // Arc crossed off-screen mid-way — flush what we have and drop the
                // origin from the restart so we don't re-draw the radial edge.
                dl.PathStroke(color, ImDrawFlags.None, thickness);
                arcHasPoints = false;
            }
        }
        if (arcHasPoints)
            dl.PathStroke(color, ImDrawFlags.Closed, thickness);
        else
            dl.PathClear();
    }

    private static Comparison<MobEntity> DistanceComparer(Vector3 self) =>
        (a, b) => Vector3.DistanceSquared(self, a.Position)
                       .CompareTo(Vector3.DistanceSquared(self, b.Position));
}
