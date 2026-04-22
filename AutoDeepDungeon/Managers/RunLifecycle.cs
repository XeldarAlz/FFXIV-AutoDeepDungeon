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

    public RunLifecycle()
    {
        Svc.Framework.Update += Tick;
    }

    public void Dispose()
    {
        Svc.Framework.Update -= Tick;
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
        LeaveCurrentContentIfNeeded();
        SetStage(Stage.Idle);
    }

    private void SetStage(Stage next)
    {
        if (CurrentStage == next) return;
        Svc.Log.Information($"Stage: {CurrentStage} -> {next}");
        CurrentStage = next;
        StageEnteredAt = DateTime.UtcNow;
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
            case Stage.Executing:
            case Stage.Combat:
            case Stage.Panic:
            case Stage.FloorClear:
            case Stage.Descending:
                if (!inDd)
                {
                    // Left the DD unexpectedly (death-exit, disconnect, or /adg stop raced us).
                    SetStage(Stage.Idle);
                }
                // Gameplay-loop transitions land in M1/M2.
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
