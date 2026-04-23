using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using AutoDeepDungeon.Data;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;

namespace AutoDeepDungeon.Managers;

public enum PlanGoalKind { None, Passage, Point }

/// <summary>Where the planner is trying to get to. Passage resolves from FloorState each tick
/// (so the planner follows the passage if it moves between floor transitions);
/// Point is a fixed world-space target set by the caller.</summary>
public readonly record struct PlanGoal(PlanGoalKind Kind, Vector3 Point)
{
    public static PlanGoal None { get; } = new(PlanGoalKind.None, Vector3.Zero);
    public static PlanGoal ToPassage(Vector3 p) => new(PlanGoalKind.Passage, p);
    public static PlanGoal ToPoint(Vector3 p) => new(PlanGoalKind.Point, p);
}

/// <summary>One successful pathfind + score, snapshotted at BuiltAt.</summary>
public sealed record Plan(
    PlanGoal Goal,
    Vector3 From,
    IReadOnlyList<Vector3> Waypoints,
    PathScore Score,
    DateTime BuiltAt)
{
    public static Plan Empty { get; } = new(
        PlanGoal.None,
        Vector3.Zero,
        Array.Empty<Vector3>(),
        PathScore.Empty,
        DateTime.MinValue);
}

/// <summary>
/// Issues vnavmesh Pathfind queries and scores them with <see cref="PathCost"/>.
/// Single-shot via <see cref="PlanOnce"/>, or enable the 200ms auto-replan tick
/// with <see cref="Enable"/>.
///
/// Each evaluation round fires the direct self→goal path first; if that path
/// crosses mob aggro cones or traps, it also tries lateral detour candidates
/// (perpendicular offsets snapped back onto the navmesh) and keeps the lowest-
/// scoring one. Day 4.2 adds a hysteresis gate so tiny score improvements
/// don't thrash the active plan.
/// </summary>
public sealed class PathPlanner : IDisposable
{
    // 200ms per the plan's continuous-replanner spec. Anything tighter spams
    // vnavmesh with redundant queries; looser and the planner stops reacting
    // to mob patrols inside a combat encounter.
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(200);

    // Lateral offsets (yalms) around the direct path's midpoint, perpendicular to
    // the self→goal line. Four samples is enough coverage to route around a
    // single aggro cone without blowing the Pathfind budget per tick.
    private static readonly float[] DetourOffsetsYalms = { -16f, -8f, 8f, 16f };

    public Plan Current { get; private set; } = Plan.Empty;
    public bool Enabled { get; private set; }
    public bool QueryInFlight { get; private set; }
    public string? LastError { get; private set; }
    public int ReplanCount { get; private set; }
    public int RoundCount { get; private set; }
    public int LastCandidatesConsidered { get; private set; }
    public int LastCandidatesViable { get; private set; }
    public PlanGoal ActiveGoal => activeGoal;

    private PlanGoal activeGoal = PlanGoal.None;
    private DateTime nextTickUtc = DateTime.MinValue;
    private bool disposed;

    public PathPlanner()
    {
        Svc.Framework.Update += Tick;
    }

    /// <summary>Kick a one-shot plan toward <paramref name="goal"/>. Result lands in
    /// <see cref="Current"/> once vnavmesh resolves.</summary>
    public void PlanOnce(PlanGoal goal) => KickPlan(goal);

    /// <summary>Start the 200ms auto-replan loop toward <paramref name="goal"/>.</summary>
    public void Enable(PlanGoal goal)
    {
        activeGoal = goal;
        Enabled = true;
        nextTickUtc = DateTime.MinValue; // force immediate plan on next Framework.Update
    }

    public void Disable()
    {
        Enabled = false;
        activeGoal = PlanGoal.None;
    }

    public void Dispose()
    {
        disposed = true;
        Svc.Framework.Update -= Tick;
    }

    private void Tick(IFramework framework)
    {
        if (!Enabled) return;
        if (activeGoal.Kind == PlanGoalKind.None) return;
        if (QueryInFlight) return;
        if (DateTime.UtcNow < nextTickUtc) return;
        nextTickUtc = DateTime.UtcNow + TickInterval;

        // Passage goals re-resolve from FloorState so the planner naturally
        // follows the passage through floor transitions without the caller
        // having to re-Enable. Missing passage (between floors / in a cutscene)
        // just skips this tick.
        var effective = activeGoal;
        if (effective.Kind == PlanGoalKind.Passage)
        {
            var p = Plugin.Floor.Current.Passage;
            if (p is null) return;
            effective = PlanGoal.ToPassage(p.Position);
        }

        KickPlan(effective);
    }

    private void KickPlan(PlanGoal goal)
    {
        if (goal.Kind == PlanGoalKind.None) return;
        if (QueryInFlight) return;

        if (!Plugin.Vnav.IsReady) { LastError = "vnavmesh not ready"; return; }

        var self = Svc.ClientState.LocalPlayer?.Position;
        if (self is null) { LastError = "no local player"; return; }

        var from = self.Value;
        QueryInFlight = true;
        LastError = null;

        // Fire-and-forget the async evaluation. QueryInFlight keeps the tick
        // from stacking rounds; `disposed` guards Current/LastError writes
        // arriving after Dispose().
        _ = EvaluateAndStore(from, goal);
    }

    private async Task EvaluateAndStore(Vector3 from, PlanGoal goal)
    {
        try
        {
            var best = await EvaluateCandidates(from, goal);
            if (disposed) return;
            RoundCount++;
            if (best is null)
            {
                LastError ??= "no viable path";
                return;
            }

            var currentPlan = Current;
            if (!ShouldSwap(currentPlan, goal, best.Value.Score)) return;

            Current = new Plan(goal, from, best.Value.Waypoints, best.Value.Score, DateTime.UtcNow);
            ReplanCount++;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Svc.Log.Warning($"[PathPlanner] EvaluateAndStore failed: {ex}");
        }
        finally
        {
            QueryInFlight = false;
        }
    }

    /// <summary>
    /// Hysteresis gate — only swap to a new candidate when it's meaningfully
    /// better, so a patrolling mob flicking in and out of the path doesn't
    /// replace the active plan every tick. Forced swaps: first plan, goal
    /// changed, or escaping a fatal (trap) current plan.
    /// </summary>
    private static bool ShouldSwap(Plan current, PlanGoal goal, PathScore best)
    {
        if (current.Waypoints.Count == 0) return true;
        if (current.Goal != goal) return true;

        // Fatality transitions bypass the ratio: finite is always preferred
        // over fatal, and we never willingly move from finite into fatal.
        if (current.Score.HasTrap && !best.HasTrap) return true;
        if (!current.Score.HasTrap && best.HasTrap) return false;
        if (current.Score.HasTrap && best.HasTrap) return false;

        var ratio = Plugin.Config.PlannerHysteresisRatio;
        return best.Total < current.Score.Total * ratio;
    }

    private async Task<(List<Vector3> Waypoints, PathScore Score)?> EvaluateCandidates(Vector3 from, PlanGoal goal)
    {
        var pathfind = Plugin.Vnav.Raw.Pathfind;
        if (pathfind is null) { LastError = "Pathfind IPC missing"; return null; }

        var weights = PathCostWeights.FromConfig();
        var floorSnapshot = Plugin.Floor.Current;
        LastCandidatesConsidered = 1;
        LastCandidatesViable = 0;

        var directWps = await pathfind.Invoke(from, goal.Point, false).ConfigureAwait(false);
        if (directWps is null || directWps.Count == 0)
        {
            LastError = "vnavmesh returned empty direct path";
            return null;
        }
        LastCandidatesViable = 1;

        var directScore = PathCost.Score(directWps, floorSnapshot, weights);
        var best = (Waypoints: directWps, Score: directScore);

        // Only spend extra Pathfind calls when the direct path has something
        // worth routing around. Clean floors get O(1) queries per tick.
        var worthDetouring = directScore.HasTrap || directScore.ConeCrossings > 0;
        if (!worthDetouring) return best;

        var dir = goal.Point - from;
        var lenSqXz = dir.X * dir.X + dir.Z * dir.Z;
        if (lenSqXz < 1f) return best; // too short to sidestep meaningfully

        var lenXz = MathF.Sqrt(lenSqXz);
        // Perpendicular in the XZ plane. DD mobs are effectively 2D so we
        // ignore Y when choosing detour points.
        var perp = new Vector3(dir.Z / lenXz, 0f, -dir.X / lenXz);
        var midpoint = (from + goal.Point) * 0.5f;

        var nearestPoint = Plugin.Vnav.Raw.NearestPoint;
        foreach (var offset in DetourOffsetsYalms)
        {
            LastCandidatesConsidered++;
            var rawDetour = midpoint + perp * offset;
            var snapped = nearestPoint?.Invoke(rawDetour, 5f, 2f);
            if (snapped is null) continue;

            var leg1 = await pathfind.Invoke(from, snapped.Value, false).ConfigureAwait(false);
            if (leg1 is null || leg1.Count == 0) continue;
            var leg2 = await pathfind.Invoke(snapped.Value, goal.Point, false).ConfigureAwait(false);
            if (leg2 is null || leg2.Count == 0) continue;

            // Concatenate legs, dropping the duplicate detour waypoint.
            var combined = new List<Vector3>(leg1.Count + leg2.Count - 1);
            combined.AddRange(leg1);
            for (var i = 1; i < leg2.Count; i++) combined.Add(leg2[i]);

            var score = PathCost.Score(combined, floorSnapshot, weights);
            LastCandidatesViable++;
            if (score.Total < best.Score.Total)
            {
                best = (combined, score);
            }
        }

        return best;
    }
}
