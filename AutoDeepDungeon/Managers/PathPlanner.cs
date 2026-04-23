using System;
using System.Collections.Generic;
using System.Numerics;
using AutoDeepDungeon.Data;
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
/// Day 3.1 is single-shot only — call <see cref="PlanOnce"/> to trigger a plan;
/// Day 3.2 adds a 200ms auto-tick; Day 4 adds candidate detours + hysteresis.
/// </summary>
public sealed class PathPlanner : IDisposable
{
    public Plan Current { get; private set; } = Plan.Empty;
    public bool QueryInFlight { get; private set; }
    public string? LastError { get; private set; }
    public int ReplanCount { get; private set; }

    private bool disposed;

    /// <summary>Kick a one-shot plan toward <paramref name="goal"/>. Result lands in
    /// <see cref="Current"/> once vnavmesh resolves.</summary>
    public void PlanOnce(PlanGoal goal) => KickPlan(goal);

    public void Dispose()
    {
        disposed = true;
    }

    private void KickPlan(PlanGoal goal)
    {
        if (goal.Kind == PlanGoalKind.None) return;
        if (QueryInFlight) return;

        if (!Plugin.Vnav.IsReady) { LastError = "vnavmesh not ready"; return; }

        var self = Svc.ClientState.LocalPlayer?.Position;
        if (self is null) { LastError = "no local player"; return; }

        var pathfind = Plugin.Vnav.Raw.Pathfind;
        if (pathfind is null) { LastError = "Pathfind IPC missing"; return; }

        var from = self.Value;
        QueryInFlight = true;
        LastError = null;

        // vnavmesh's Pathfind resolves on a worker thread — Current/LastError are
        // single-writer/single-reader so no lock is needed, but guard against
        // late completions that arrive after Dispose().
        _ = pathfind.Invoke(from, goal.Point, false).ContinueWith(t =>
        {
            try
            {
                if (disposed) return;
                if (t.IsFaulted)
                {
                    LastError = t.Exception?.GetBaseException().Message ?? "pathfind faulted";
                    return;
                }
                var wps = t.Result;
                if (wps is null || wps.Count == 0)
                {
                    LastError = "vnavmesh returned empty path";
                    return;
                }
                var score = PathCost.Score(wps, Plugin.Floor.Current, PathCostWeights.FromConfig());
                Current = new Plan(goal, from, wps, score, DateTime.UtcNow);
                ReplanCount++;
            }
            finally
            {
                QueryInFlight = false;
            }
        });
    }
}
