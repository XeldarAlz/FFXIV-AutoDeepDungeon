using System;
using System.Collections.Generic;
using System.Numerics;
using AutoDeepDungeon.Data;

namespace AutoDeepDungeon.Managers;

/// <summary>
/// Tunable weights for <see cref="PathCost.Score"/>. Pure data struct so the
/// scorer stays a pure function; Day 2.2 wires these to Config.
/// </summary>
public readonly record struct PathCostWeights(
    float AggroPenalty,
    float CofferReward,
    float TrapAvoidRadius,
    float CofferDetourYalms)
{
    /// <summary>Hardcoded defaults — used only when Config isn't available (e.g. unit tests).</summary>
    public static PathCostWeights Default { get; } = new(
        AggroPenalty: 50f,
        CofferReward: 25f,
        TrapAvoidRadius: 1.5f,
        CofferDetourYalms: 10f);

    /// <summary>Reads the current user-tuned weights out of <see cref="Plugin.Config"/>.</summary>
    public static PathCostWeights FromConfig()
    {
        var c = Plugin.Config;
        return new PathCostWeights(
            AggroPenalty: c.PlannerAggroPenalty,
            CofferReward: c.PlannerCofferReward,
            TrapAvoidRadius: c.PlannerTrapAvoidRadius,
            CofferDetourYalms: c.CofferDetourYalms);
    }
}

/// <summary>Breakdown of a path's cost. Higher Total = worse.</summary>
public readonly record struct PathScore(
    float Length,
    float ConePenalty,
    float CofferReward,
    bool HasTrap,
    int ConeCrossings,
    int CoffersOnPath)
{
    public float Total => HasTrap ? float.PositiveInfinity : Length + ConePenalty - CofferReward;
    public static PathScore Empty { get; } = default;
}

/// <summary>
/// Scores a vnavmesh waypoint path against the current FloorState. A segment
/// that grazes any live mob's aggro footprint pays one flat AggroPenalty per
/// unique mob. A segment passing within CofferDetourYalms of an unopened
/// coffer subtracts CofferReward per unique coffer. A segment passing within
/// TrapAvoidRadius of any live or persistent trap marks the path as fatal
/// (Total = +∞) so the planner rejects it unless Safety pomander is active —
/// Safety-aware scoring lands with the pomander system in M4.
/// </summary>
public static class PathCost
{
    // 2y samples per segment — dense enough to catch a 10y aggro circle crossed
    // diagonally, sparse enough to stay cheap over ~50-mob PotD floors.
    private const float SampleStepYalms = 2f;

    public static PathScore Score(IReadOnlyList<Vector3> waypoints, FloorState state)
        => Score(waypoints, state, PathCostWeights.Default);

    public static PathScore Score(IReadOnlyList<Vector3> waypoints, FloorState state, PathCostWeights w)
    {
        if (waypoints is null || waypoints.Count < 2) return PathScore.Empty;

        var length = 0f;
        var hitMobs = new HashSet<ulong>();
        var hitCoffers = new HashSet<ulong>();
        var hasTrap = false;
        var cofferDetourSq = w.CofferDetourYalms * w.CofferDetourYalms;

        for (var i = 0; i < waypoints.Count - 1; i++)
        {
            var a = waypoints[i];
            var b = waypoints[i + 1];
            length += Vector3.Distance(a, b);

            foreach (var pt in SampleSegment(a, b, SampleStepYalms))
            {
                foreach (var m in state.Mobs)
                {
                    if (m.CurrentHp == 0) continue;
                    if (hitMobs.Contains(m.ObjectId)) continue;
                    if (AggroMap.Compute(m).Contains(pt)) hitMobs.Add(m.ObjectId);
                }

                foreach (var c in state.Coffers)
                {
                    if (hitCoffers.Contains(c.ObjectId)) continue;
                    if (Vector3.DistanceSquared(pt, c.Position) <= cofferDetourSq)
                        hitCoffers.Add(c.ObjectId);
                }
            }

            // Traps: closest-point-on-segment rather than sample points — a 1.5y
            // trap radius between 2y samples could otherwise slip through.
            if (SegmentNearAnyPoint(a, b, state.Traps, w.TrapAvoidRadius) ||
                SegmentNearAnyVector(a, b, state.PersistentTraps, w.TrapAvoidRadius))
            {
                hasTrap = true;
            }
        }

        return new PathScore(
            Length: length,
            ConePenalty: hitMobs.Count * w.AggroPenalty,
            CofferReward: hitCoffers.Count * w.CofferReward,
            HasTrap: hasTrap,
            ConeCrossings: hitMobs.Count,
            CoffersOnPath: hitCoffers.Count);
    }

    private static IEnumerable<Vector3> SampleSegment(Vector3 a, Vector3 b, float stepYalms)
    {
        yield return a;
        var dist = Vector3.Distance(a, b);
        if (dist <= stepYalms) { yield return b; yield break; }
        var steps = (int)(dist / stepYalms);
        for (var i = 1; i <= steps; i++)
        {
            var t = i / (float)(steps + 1);
            yield return Vector3.Lerp(a, b, t);
        }
        yield return b;
    }

    private static bool SegmentNearAnyPoint(Vector3 a, Vector3 b, IReadOnlyList<EObjEntity> points, float radius)
    {
        var rSq = radius * radius;
        foreach (var p in points)
            if (DistanceToSegmentSqXz(a, b, p.Position) <= rSq) return true;
        return false;
    }

    private static bool SegmentNearAnyVector(Vector3 a, Vector3 b, IReadOnlyList<Vector3> points, float radius)
    {
        var rSq = radius * radius;
        foreach (var p in points)
            if (DistanceToSegmentSqXz(a, b, p) <= rSq) return true;
        return false;
    }

    /// <summary>
    /// Closest-point-on-segment distance in XZ plane only. Traps are floor
    /// tiles and the player is always at roughly the same Y as the trap —
    /// Y differences (stairs, slopes, or just navmesh elevation jitter)
    /// otherwise make 3D distance overshoot the trap radius and produce
    /// false negatives.
    /// </summary>
    private static float DistanceToSegmentSqXz(Vector3 a, Vector3 b, Vector3 p)
    {
        var ax = a.X; var az = a.Z;
        var bx = b.X; var bz = b.Z;
        var px = p.X; var pz = p.Z;
        var abx = bx - ax;
        var abz = bz - az;
        var abLenSq = abx * abx + abz * abz;
        if (abLenSq < 1e-4f)
        {
            var d1x = px - ax; var d1z = pz - az;
            return d1x * d1x + d1z * d1z;
        }
        var t = Math.Clamp(((px - ax) * abx + (pz - az) * abz) / abLenSq, 0f, 1f);
        var cx = ax + abx * t;
        var cz = az + abz * t;
        var dx = px - cx;
        var dz = pz - cz;
        return dx * dx + dz * dz;
    }
}
