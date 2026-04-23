using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;

namespace AutoDeepDungeon.Managers;

/// <summary>
/// Thin wrapper over vnavmesh's Path.* / SimpleMove.* IPC. Owns the currently
/// executing movement command so the rest of the plugin doesn't talk to vnavmesh
/// directly. Also monitors per-tick progress along the active path and raises
/// <see cref="StuckDetected"/> when the player hasn't moved far enough within
/// <see cref="StuckWindow"/> — the planner uses this to trigger a replan.
/// </summary>
public sealed class Executor : IDisposable
{
    // 0.5y is lenient enough to ignore micro-jitter from the server-snap camera
    // but catches true wall-hugs and AFK-like states. 3s gives vnavmesh time to
    // round a sharp corner before we declare the path broken.
    private const float MinProgressYalms = 0.5f;
    private static readonly TimeSpan StuckWindow = TimeSpan.FromSeconds(3);

    public event Action? StuckDetected;

    public DateTime LastStuckAt { get; private set; } = DateTime.MinValue;
    public int StuckEventCount { get; private set; }

    private bool watching;
    private DateTime lastProgressUtc;
    private Vector3 lastSampledPos;

    public Executor()
    {
        Svc.Framework.Update += Tick;
    }

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
            var started = Plugin.Vnav.Raw.PathfindAndMoveTo?.Invoke(target, fly) ?? false;
            if (started) BeginWatch();
            return started;
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
            BeginWatch();
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
        watching = false;
        if (!Plugin.Vnav.IsReady) return;
        try { Plugin.Vnav.Raw.Stop?.Invoke(); }
        catch (Exception ex) { Svc.Log.Warning($"[Executor] Stop failed: {ex.Message}"); }
    }

    public void Dispose()
    {
        Svc.Framework.Update -= Tick;
        Stop();
    }

    private void BeginWatch()
    {
        watching = true;
        lastProgressUtc = DateTime.UtcNow;
        lastSampledPos = Svc.ClientState.LocalPlayer?.Position ?? Vector3.Zero;
    }

    private void Tick(IFramework framework)
    {
        if (!watching) return;
        if (!IsRunning) { watching = false; return; }

        var self = Svc.ClientState.LocalPlayer?.Position;
        if (self is null) return;

        if (Vector3.Distance(lastSampledPos, self.Value) >= MinProgressYalms)
        {
            lastSampledPos = self.Value;
            lastProgressUtc = DateTime.UtcNow;
            return;
        }

        if (DateTime.UtcNow - lastProgressUtc < StuckWindow) return;

        watching = false;
        LastStuckAt = DateTime.UtcNow;
        StuckEventCount++;
        Svc.Log.Warning(
            $"[Executor] Stuck: <{MinProgressYalms}y moved in {StuckWindow.TotalSeconds:F0}s while path running (event #{StuckEventCount}).");
        StuckDetected?.Invoke();
    }
}
