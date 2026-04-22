using System;
using System.Numerics;
using AutoDeepDungeon.Data;
using AutoDeepDungeon.Managers;
using Dalamud.Bindings.ImGui;
using ECommons.DalamudServices;
using Pictomancy;

namespace AutoDeepDungeon.Windows;

/// <summary>
/// Pictomancy-backed world-space debug overlay. Renders a running commentary of what the
/// FloorScanner sees so the M1 exit criteria can be eyeballed in-game: mob aggro
/// footprints, live and persistent traps, hoards, coffers, and the passage's active state.
/// Gated on <see cref="Config.EnableDebugOverlay"/>; drawing is a no-op otherwise.
/// </summary>
public sealed class WorldOverlay : IDisposable
{
    public WorldOverlay()
    {
        Svc.PluginInterface.UiBuilder.Draw += Draw;
    }

    public void Dispose()
    {
        Svc.PluginInterface.UiBuilder.Draw -= Draw;
    }

    // Colors packed as ImGui ABGR (alpha, blue, green, red low-to-high).
    private static uint Rgba(byte r, byte g, byte b, byte a)
        => ((uint)a << 24) | ((uint)b << 16) | ((uint)g << 8) | r;

    private static readonly uint ColorSelf        = Rgba(64, 160, 255, 255);
    private static readonly uint ColorAggroSight  = Rgba(255, 64,  64,  96);
    private static readonly uint ColorAggroOmni   = Rgba(255, 160, 64,  96);
    private static readonly uint ColorTrapLive    = Rgba(255, 0,   0,   200);
    private static readonly uint ColorTrapPersist = Rgba(255, 0,   0,   64);
    private static readonly uint ColorHoardLive   = Rgba(255, 200, 0,   230);
    private static readonly uint ColorHoardPersist= Rgba(255, 200, 0,   80);
    private static readonly uint ColorCoffer      = Rgba(255, 255, 255, 220);
    private static readonly uint ColorMimic       = Rgba(255, 80,  0,   230);
    private static readonly uint ColorPassageOn   = Rgba(64,  255, 64,  230);
    private static readonly uint ColorPassageOff  = Rgba(160, 160, 160, 180);

    private static void Draw()
    {
        if (!Plugin.Config.EnableDebugOverlay) return;
        var floor = Plugin.Floor?.Current;
        if (floor == null || !floor.InDeepDungeon) return;

        try
        {
            using var dl = PictoService.Draw();
            if (dl == null) return;

            // Self ring — visual anchor for orientation checks.
            dl.AddCircle(floor.SelfPosition, 0.5f, ColorSelf, 32, 2f);

            DrawAggro(dl, floor);
            DrawTraps(dl, floor);
            DrawHoards(dl, floor);
            DrawCoffers(dl, floor);
            DrawPassage(dl, floor);
        }
        catch (Exception ex)
        {
            // Don't let an overlay glitch take the game's draw loop down. Log once per
            // session by keying on message to avoid spam.
            Svc.Log.Warning($"[WorldOverlay] draw failed: {ex.Message}");
        }
    }

    private static void DrawAggro(PctDrawList dl, FloorState floor)
    {
        foreach (var m in floor.Mobs)
        {
            if (m.CurrentHp == 0) continue;
            var geom = AggroMap.Compute(m);
            var color = m.IsMimicCandidate
                ? ColorMimic
                : (geom.Omnidirectional ? ColorAggroOmni : ColorAggroSight);

            if (geom.Omnidirectional)
            {
                dl.AddCircleFilled(geom.Origin, geom.Radius, color, null, 48);
            }
            else
            {
                dl.AddConeFilled(
                    origin: geom.Origin,
                    radius: geom.Radius,
                    rotation: geom.Facing,
                    angle: geom.ConeHalfAngle * 2f,
                    color: color,
                    outerColor: null,
                    numSegments: 24);
            }
        }
    }

    private static void DrawTraps(PctDrawList dl, FloorState floor)
    {
        foreach (var t in floor.Traps)
            dl.AddCircleFilled(t.Position, 1.5f, ColorTrapLive, null, 24);

        foreach (var p in floor.PersistentTraps)
            dl.AddCircle(p, 1.5f, ColorTrapPersist, 24, 2f);
    }

    private static void DrawHoards(PctDrawList dl, FloorState floor)
    {
        foreach (var h in floor.Hoards)
            dl.AddCircleFilled(h.Position, 1.0f, ColorHoardLive, null, 24);

        foreach (var p in floor.PersistentHoards)
            dl.AddCircle(p, 1.0f, ColorHoardPersist, 24, 2f);
    }

    private static void DrawCoffers(PctDrawList dl, FloorState floor)
    {
        foreach (var c in floor.Coffers)
            dl.AddCircle(c.Position, 0.8f, ColorCoffer, 24, 2f);
    }

    private static void DrawPassage(PctDrawList dl, FloorState floor)
    {
        if (floor.Passage is not { } p) return;
        var color = p.Active ? ColorPassageOn : ColorPassageOff;
        dl.AddCircle(p.Position, 2.0f, color, 32, 3f);
        dl.AddCircle(p.Position, 1.0f, color, 32, 3f);
    }
}
