using System;
using System.Collections.Generic;
using System.Numerics;
using ECommons.DalamudServices;

namespace AutoDeepDungeon.Managers;

/// <summary>
/// Thin wrapper over vnavmesh's Path.* / SimpleMove.* IPC. Owns the currently
/// executing movement command so the rest of the plugin doesn't talk to vnavmesh
/// directly. Later commits add stuck detection on top.
/// </summary>
public sealed class Executor : IDisposable
{
    public bool IsRunning
    {
        get
        {
            if (!Plugin.Vnav.IsReady) return false;
            try { return Plugin.Vnav.Raw.IsRunning?.Invoke() ?? false; }
            catch (Exception ex) { Svc.Log.Warning($"[Executor] IsRunning probe failed: {ex.Message}"); return false; }
        }
    }

    public int WaypointCount
    {
        get
        {
            if (!Plugin.Vnav.IsReady) return 0;
            try { return Plugin.Vnav.Raw.NumWaypoints?.Invoke() ?? 0; }
            catch (Exception ex) { Svc.Log.Warning($"[Executor] NumWaypoints probe failed: {ex.Message}"); return 0; }
        }
    }

    /// <summary>Ask vnavmesh to pathfind from the player to <paramref name="target"/> and walk it.</summary>
    public bool Start(Vector3 target, bool fly = false)
    {
        if (!Plugin.Vnav.IsReady)
        {
            Svc.Log.Warning("[Executor] Start skipped — vnavmesh not ready.");
            return false;
        }
        try
        {
            return Plugin.Vnav.Raw.PathfindAndMoveTo?.Invoke(target, fly) ?? false;
        }
        catch (Exception ex)
        {
            Svc.Log.Warning($"[Executor] Start({target}) failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>Walk a pre-computed waypoint list (e.g. PathPlanner output in M2 day 3+).</summary>
    public bool StartWaypoints(List<Vector3> waypoints, bool fly = false)
    {
        if (!Plugin.Vnav.IsReady)
        {
            Svc.Log.Warning("[Executor] StartWaypoints skipped — vnavmesh not ready.");
            return false;
        }
        if (waypoints is null || waypoints.Count == 0) return false;
        try
        {
            Plugin.Vnav.Raw.MoveTo?.Invoke(waypoints, fly);
            return true;
        }
        catch (Exception ex)
        {
            Svc.Log.Warning($"[Executor] StartWaypoints({waypoints.Count} wp) failed: {ex.Message}");
            return false;
        }
    }

    public void Stop()
    {
        if (!Plugin.Vnav.IsReady) return;
        try { Plugin.Vnav.Raw.Stop?.Invoke(); }
        catch (Exception ex) { Svc.Log.Warning($"[Executor] Stop failed: {ex.Message}"); }
    }

    public void Dispose()
    {
        Stop();
    }
}
