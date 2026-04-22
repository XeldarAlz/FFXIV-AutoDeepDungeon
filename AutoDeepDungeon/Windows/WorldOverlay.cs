using System;
using System.Collections.Generic;
using System.Numerics;
using AutoDeepDungeon.Data;
using AutoDeepDungeon.Managers;
using Dalamud.Bindings.ImGui;
using ECommons.DalamudServices;

namespace AutoDeepDungeon.Windows;

/// <summary>
/// World-space debug overlay drawn via ImGui's background draw list. Same technique as
/// RadarPlugin: per-vertex <see cref="IGameGui.WorldToScreen"/> projection, one
/// <c>PathStroke</c> per shape (not <c>AddLine</c> per segment).
///
/// Classification is taken from <see cref="FloorScanner"/>'s 10Hz snapshot, but positions
/// and rotations are pulled LIVE from <see cref="Svc.Objects"/> and <see cref="Svc.ClientState"/>
/// each frame. That keeps moving mobs + the player's own cone anchor smooth at full
/// game frame rate without running the full scan every frame.
/// </summary>
public sealed class WorldOverlay : IDisposable
{
    private const int MaxAggroShapes       = 15;
    private const float AggroDrawRange     = 35f;
    private const float PersistentDrawRange= 35f;

    private const int SegCone              = 24;
    private const int SegCircle            = 40;
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
    private static readonly uint ColorTrap         = Rgba(255, 40,  40,  230);
    private static readonly uint ColorHoard        = Rgba(255, 220, 40,  230);

    // Live scan and PalacePal-persistent data both have coordinates for the same traps;
    // a ~3y tolerance collapses them to a single marker (PalacePal-style rendering).
    private const float DedupDistSq = 9f;
    private static readonly uint ColorCoffer       = Rgba(255, 255, 255, 220);
    private static readonly uint ColorPassageOn    = Rgba(64,  255, 64,  235);
    private static readonly uint ColorPassageOff   = Rgba(160, 160, 160, 180);

    private readonly record struct LiveMob(Vector3 Position, float Rotation, bool IsMimic);
    private static readonly List<LiveMob> liveMobs = new();

    private static void Draw()
    {
        if (!Plugin.Config.EnableDebugOverlay) return;
        var floor = Plugin.Floor?.Current;
        if (floor == null || !floor.InDeepDungeon) return;

        // Live self position — FloorScanner's snapshot is 10Hz and would make the
        // player-anchored visuals stutter.
        var self = Svc.ClientState.LocalPlayer?.Position ?? floor.SelfPosition;

        try
        {
            var dl = ImGui.GetBackgroundDrawList();
            DrawCircleWorld(dl, self, 0.5f, ColorSelf, ThicknessOutline, 16);
            DrawAggro(dl, floor, self);
            DrawTraps(dl, floor, self);
            DrawHoards(dl, floor, self);
            DrawCoffers(dl, floor);
            DrawPassage(dl, floor);
        }
        catch (Exception ex)
        {
            Svc.Log.Warning($"[WorldOverlay] draw failed: {ex.Message}");
        }
    }

    private static void DrawAggro(ImDrawListPtr dl, FloorState floor, Vector3 self)
    {
        var rangeSq = AggroDrawRange * AggroDrawRange;

        liveMobs.Clear();
        foreach (var m in floor.Mobs)
        {
            if (m.CurrentHp == 0) continue;

            // Live position/rotation lookup by GameObjectId — keeps the overlay at
            // frame rate even while FloorScanner runs at 10Hz.
            Vector3 pos; float rot;
            var live = Svc.Objects.SearchById(m.ObjectId);
            if (live != null && !live.IsDead)
            {
                pos = live.Position;
                rot = live.Rotation;
            }
            else
            {
                pos = m.Position;
                rot = m.Rotation;
            }

            if (Vector3.DistanceSquared(self, pos) > rangeSq) continue;
            liveMobs.Add(new LiveMob(pos, rot, m.IsMimicCandidate));
        }
        // Nearest-first so the MaxAggroShapes cap keeps the closest mobs.
        liveMobs.Sort(DistanceComparer(self));

        var limit = Math.Min(liveMobs.Count, MaxAggroShapes);
        for (var i = 0; i < limit; i++)
        {
            var lm = liveMobs[i];
            var coneColor = lm.IsMimic ? ColorMimic : ColorAggroSight;
            var ringColor = lm.IsMimic ? ColorMimicRing : ColorAggroRing;
            var thickness = lm.IsMimic ? ThicknessHighlight : ThicknessOutline;

            DrawCircleWorld(dl, lm.Position, AggroMap.DefaultRadius, ringColor, ThicknessOutline, SegCircle);
            DrawSectorWorld(dl, lm.Position, AggroMap.DefaultRadius,
                            lm.Rotation, AggroMap.SightConeHalfAngle,
                            coneColor, thickness, SegCone);
        }
    }

    private static void DrawTraps(ImDrawListPtr dl, FloorState floor, Vector3 self)
    {
        var rangeSq = PersistentDrawRange * PersistentDrawRange;

        foreach (var t in floor.Traps)
            DrawCircleWorld(dl, t.Position, 1.5f, ColorTrap, ThicknessOutline, SegMarker);

        foreach (var p in floor.PersistentTraps)
        {
            if (Vector3.DistanceSquared(self, p) > rangeSq) continue;
            if (IsCoveredByLive(p, floor.Traps)) continue;
            DrawCircleWorld(dl, p, 1.5f, ColorTrap, ThicknessOutline, SegMarker);
        }
    }

    private static void DrawHoards(ImDrawListPtr dl, FloorState floor, Vector3 self)
    {
        var rangeSq = PersistentDrawRange * PersistentDrawRange;

        foreach (var h in floor.Hoards)
            DrawCircleWorld(dl, h.Position, 1.0f, ColorHoard, ThicknessOutline, SegMarker);

        foreach (var p in floor.PersistentHoards)
        {
            if (Vector3.DistanceSquared(self, p) > rangeSq) continue;
            if (IsCoveredByLive(p, floor.Hoards)) continue;
            DrawCircleWorld(dl, p, 1.0f, ColorHoard, ThicknessOutline, SegMarker);
        }
    }

    private static bool IsCoveredByLive(Vector3 point, IReadOnlyList<EObjEntity> live)
    {
        foreach (var e in live)
            if (Vector3.DistanceSquared(e.Position, point) < DedupDistSq) return true;
        return false;
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
    /// Stroke a horizontal circle as a single ImGui polyline. Behind-camera points break
    /// the path — we stroke what we have and restart, which clips cleanly at the screen
    /// edge without drawing garbage lines.
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
    /// Stroke a horizontal circular sector: origin → arc samples spanning
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
                dl.PathStroke(color, ImDrawFlags.None, thickness);
                arcHasPoints = false;
            }
        }
        if (arcHasPoints)
            dl.PathStroke(color, ImDrawFlags.Closed, thickness);
        else
            dl.PathClear();
    }

    private static Comparison<LiveMob> DistanceComparer(Vector3 self) =>
        (a, b) => Vector3.DistanceSquared(self, a.Position)
                       .CompareTo(Vector3.DistanceSquared(self, b.Position));
}
