using System;
using AutoDeepDungeon.Data;
using AutoDeepDungeon.Helpers;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.Event;

namespace AutoDeepDungeon.Managers;

public sealed class RunLifecycle : IDisposable
{
    public Stage CurrentStage { get; private set; } = Stage.Idle;
    public DateTime StageEnteredAt { get; private set; } = DateTime.UtcNow;
    public int FloorsCleared { get; private set; }

    private uint lastTerritory;

    public RunLifecycle()
    {
        Svc.Framework.Update += Tick;
        Svc.ClientState.TerritoryChanged += OnTerritoryChanged;
    }

    public void Dispose()
    {
        Svc.ClientState.TerritoryChanged -= OnTerritoryChanged;
        Svc.Framework.Update -= Tick;
        DisablePlanning();
    }

    private void OnTerritoryChanged(ushort newTerritory)
    {
        // Territory changes when entering a DD, leaving it, or dropping a
        // floor. If we were planning/executing and this is a floor drop while
        // still in DD, fold it into the run stats and return to Planning so
        // the next floor kicks off cleanly.
        var wasTerritory = lastTerritory;
        lastTerritory = newTerritory;
        var inDd = DDStateHelper.IsInDeepDungeon();
        if (CurrentStage == Stage.Idle) return;

        if (inDd && wasTerritory != newTerritory && wasTerritory != 0)
        {
            FloorsCleared++;
            Svc.Log.Information($"[Lifecycle] Floor dropped → {FloorsCleared} cleared this session.");
            SetStage(Stage.Planning);
        }
    }

    public void Start()
    {
        if (!Plugin.Config.MasterEnabled)
        {
            Svc.Log.Warning("/adg start ignored: master toggle is off. Accept ToS and enable in the config window first.");
            return;
        }
        if (CurrentStage != Stage.Idle)
        {
            Svc.Log.Warning($"/adg start ignored: already at stage {CurrentStage}.");
            return;
        }
        SetStage(DDStateHelper.IsInDeepDungeon() ? Stage.Entering : Stage.Queueing);
    }

    public void Stop()
    {
        if (CurrentStage == Stage.Idle) return;
        DisablePlanning();
        LeaveCurrentContentIfNeeded();
        SetStage(Stage.Idle);
    }

    private void SetStage(Stage next)
    {
        if (CurrentStage == next) return;
        Svc.Log.Information($"Stage: {CurrentStage} -> {next}");
        CurrentStage = next;
        StageEnteredAt = DateTime.UtcNow;

        // Planning/Executing drive the autopath. Leaving either releases the
        // planner so a manual Stop / death / cutscene doesn't leave vnav
        // walking a stale plan.
        if (next == Stage.Planning) EnablePlanning();
        else if (next != Stage.Executing) DisablePlanning();
    }

    private static void EnablePlanning()
    {
        var passage = Plugin.Floor?.Current?.Passage;
        if (passage is null)
        {
            Svc.Log.Warning("[Lifecycle] EnablePlanning — no passage known yet; planner will pick up when one is cached.");
        }
        Plugin.Planner.AutoDrive = true;
        Plugin.Planner.Enable(PlanGoal.ToPassage(passage?.Position ?? System.Numerics.Vector3.Zero));
    }

    private static void DisablePlanning()
    {
        Plugin.Planner.AutoDrive = false;
        Plugin.Planner.Disable();
        Plugin.Exec.Stop();
    }

    private void Tick(IFramework framework)
    {
        var inDd = DDStateHelper.IsInDeepDungeon();

        switch (CurrentStage)
        {
            case Stage.Idle:
                // Nothing to do — waiting for user trigger.
                break;

            case Stage.Queueing:
                // Day 4 skeleton: actual duty-finder automation lands in a later milestone.
                // If the user manually enters a DD while queued, advance.
                if (inDd)
                    SetStage(Stage.Entering);
                break;

            case Stage.Entering:
                // Once in DD and the floor has resolved (Floor > 0), we're ready for planning.
                if (!inDd)
                {
                    // Backed out of the duty before it started; return to queueing.
                    SetStage(Stage.Queueing);
                    break;
                }
                if (DDStateHelper.CurrentFloor() > 0)
                    SetStage(Stage.Planning);
                break;

            case Stage.Planning:
                if (!inDd) { SetStage(Stage.Idle); break; }
                // Planning transitions to Executing the moment the planner
                // has a viable non-fatal plan — gives us a distinct stage
                // marker for 'driving toward passage' vs 'still resolving'.
                if (Plugin.Planner.Current.Waypoints.Count > 0 &&
                    !Plugin.Planner.Current.Score.HasTrap)
                {
                    SetStage(Stage.Executing);
                }
                break;

            case Stage.Executing:
                if (!inDd) { SetStage(Stage.Idle); break; }
                // Passage activates when the kill count is satisfied; walk to
                // it and wait for the floor transition. FloorClear's job is
                // just 'approach the passage' — territory change fires us
                // back into Planning for the next floor.
                if (Plugin.Floor.Current.Passage is { Active: true })
                {
                    SetStage(Stage.FloorClear);
                }
                break;

            case Stage.FloorClear:
                if (!inDd) { SetStage(Stage.Idle); break; }
                // Territory change handler fires separately and takes us back
                // to Planning for the next floor. If we somehow end up here
                // with an inactive passage (pomander triggered descent?),
                // fall back to Planning.
                if (Plugin.Floor.Current.Passage is not { Active: true })
                {
                    SetStage(Stage.Planning);
                }
                break;

            case Stage.Combat:
            case Stage.Panic:
            case Stage.Descending:
                if (!inDd) SetStage(Stage.Idle);
                // Combat/Panic/Descending transitions land in M3/M4.
                break;

            case Stage.Dead:
                // DeathHandler drives exit; M4 will flesh this out.
                if (!inDd)
                    SetStage(Stage.Idle);
                break;
        }
    }

    private static unsafe void LeaveCurrentContentIfNeeded()
    {
        try
        {
            if (DDStateHelper.IsInDeepDungeon() && EventFramework.CanLeaveCurrentContent())
            {
                EventFramework.LeaveCurrentContent(true);
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Warning($"LeaveCurrentContent failed: {ex.Message}");
        }
    }
}
