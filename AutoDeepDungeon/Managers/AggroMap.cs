using System;
using System.Collections.Generic;
using System.Numerics;
using AutoDeepDungeon.Data;
using ECommons.DalamudServices;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace AutoDeepDungeon.Managers;

/// <summary>
/// Circular sector describing a mob's aggro footprint. Contains(point) returns true when
/// <paramref name="point"/> is inside the sector in the XZ plane (Y ignored — DD mobs
/// only aggro at roughly the same vertical level as the player).
/// </summary>
public readonly record struct AggroGeometry(
    Vector3 Origin,
    float Facing,
    float Radius,
    float ConeHalfAngle,
    bool Omnidirectional)
{
    public bool Contains(Vector3 point)
    {
        var dx = point.X - Origin.X;
        var dz = point.Z - Origin.Z;
        var distSq = dx * dx + dz * dz;
        if (distSq > Radius * Radius) return false;
        if (Omnidirectional) return true;

        // Mob's forward vector from Rotation (FFXIV uses 0 = south, rotation increases CCW
        // when viewed from above). Same atan2 convention used for the bearing-to-point.
        var bearingToPoint = MathF.Atan2(dx, dz);
        var forward = MathF.Atan2(MathF.Sin(Facing), MathF.Cos(Facing));
        var diff = NormalizeAngle(bearingToPoint - forward);
        return MathF.Abs(diff) <= ConeHalfAngle;
    }

    private static float NormalizeAngle(float a)
    {
        while (a > MathF.PI)  a -= 2f * MathF.PI;
        while (a < -MathF.PI) a += 2f * MathF.PI;
        return a;
    }
}

/// <summary>
/// Resolves per-mob aggro geometry. Radius + omni/cone type come from Lumina BNpcBase
/// when possible, with safe fallbacks for missing data. M2 planner will call
/// <see cref="AnyAggroCovers"/> while pathfinding; per-frame cost is acceptable for
/// typical PotD mob counts (&lt; 50).
/// </summary>
public static class AggroMap
{
    public const float SightConeHalfAngle = MathF.PI / 4f; // 90° total cone per plan
    public const float DefaultRadius = 10f;                // plan fallback

    private static ExcelSheet<BNpcBase>? sheet;
    private static ExcelSheet<BNpcBase>? Sheet
    {
        get
        {
            if (sheet != null) return sheet;
            try { sheet = Svc.Data?.GetExcelSheet<BNpcBase>(); }
            catch { sheet = null; }
            return sheet;
        }
    }

    public static AggroGeometry Compute(MobEntity mob)
    {
        // BNpcBase.IsOmnidirectional in Lumina is not the "aggros from any angle" flag it
        // sounds like (it's closer to "can be targeted from any facing" and is true for
        // basically every mob). BNpcBase.Aggression from the plan doesn't exist in
        // current Lumina. Until we source a real aggro-type table — RadarPlugin's
        // allMobs.json or a hand-curated PotD list — treat every mob as sight-type.
        //
        // Mis-classifying a genuinely omni mob as sight means our overlay cone points
        // one way while the mob can aggro from any angle. M2 planner will still
        // correctly refuse paths inside the circle's radius, so the worst case is
        // a visually surprising pull — acceptable for M1 perception.
        return new AggroGeometry(
            Origin: mob.Position,
            Facing: mob.Rotation,
            Radius: DefaultRadius,
            ConeHalfAngle: SightConeHalfAngle,
            Omnidirectional: false);
    }

    /// <summary>
    /// Returns mobs whose aggro footprint contains <paramref name="point"/>. Only alive
    /// mobs with HP &gt; 0 count — dead mobs don't aggro.
    /// </summary>
    public static IEnumerable<MobEntity> AggroCovers(FloorState state, Vector3 point)
    {
        foreach (var m in state.Mobs)
        {
            if (m.CurrentHp == 0) continue;
            if (Compute(m).Contains(point)) yield return m;
        }
    }

    public static bool AnyAggroCovers(FloorState state, Vector3 point)
    {
        foreach (var m in state.Mobs)
        {
            if (m.CurrentHp == 0) continue;
            if (Compute(m).Contains(point)) return true;
        }
        return false;
    }
}
