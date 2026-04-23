using System;
using AutoDeepDungeon.Configuration;
using AutoDeepDungeon.Helpers;
using AutoDeepDungeon.IPC;
using AutoDeepDungeon.Managers;
using Dalamud.Plugin;
using ECommons;
using ECommons.Configuration;
using ECommons.DalamudServices;
using ECommons.SimpleGui;
using AdgConfigWindow = AutoDeepDungeon.Windows.ConfigWindow;
using AdgDebugWindow = AutoDeepDungeon.Windows.DebugWindow;
using AdgSafetyModal = AutoDeepDungeon.Windows.SafetyModal;
using AdgSplatoonOverlay = AutoDeepDungeon.Windows.SplatoonOverlay;

namespace AutoDeepDungeon;

public sealed class Plugin : IDalamudPlugin
{
    internal static Plugin P = null!;
    internal static Config Config = null!;

    internal static VnavIPC Vnav = null!;
    internal static RotationSolverIPC RotationSolver = null!;
    internal static WrathComboIPC WrathCombo = null!;
    internal static BossModIPC BossMod = null!;
    internal static PalacePalReader PalacePal = null!;

    internal static RunLifecycle Lifecycle = null!;
    internal static SaveFileManager SaveFiles = null!;
    internal static DeathHandler Deaths = null!;
    internal static KillSwitch KillSwitch = null!;
    internal static FloorScanner Floor = null!;
    internal static PassageStateTracker PassageState = null!;
    internal static Executor Exec = null!;
    internal static PathPlanner Planner = null!;

    internal static AdgSplatoonOverlay Overlay = null!;

    private static AdgSafetyModal safetyModal = null!;
    private static AdgDebugWindow debugWindow = null!;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        P = this;
        ECommonsMain.Init(pluginInterface, this, Module.SplatoonAPI);
        Config = EzConfig.Init<Config>();

        Vnav = new VnavIPC();
        RotationSolver = new RotationSolverIPC();
        WrathCombo = new WrathComboIPC();
        BossMod = new BossModIPC();
        PalacePal = new PalacePalReader();

        WarnIfMissing(Vnav);
        WarnIfMissing(RotationSolver);
        WarnIfMissing(WrathCombo);
        WarnIfMissing(BossMod);
        WarnIfMissing(PalacePal);

        Lifecycle = new RunLifecycle();
        SaveFiles = new SaveFileManager();
        Deaths = new DeathHandler();
        KillSwitch = new KillSwitch();
        PassageState = new PassageStateTracker();
        Floor = new FloorScanner();
        Exec = new Executor();
        Planner = new PathPlanner();

        Overlay = new AdgSplatoonOverlay();

        EzConfigGui.Init(AdgConfigWindow.Draw, windowType: EzConfigGui.WindowType.Both);

        safetyModal = new AdgSafetyModal();
        EzConfigGui.WindowSystem.AddWindow(safetyModal);
        if (!Config.ToSAccepted)
        {
            safetyModal.IsOpen = true;
        }

        debugWindow = new AdgDebugWindow();
        EzConfigGui.WindowSystem.AddWindow(debugWindow);

        EzCmd.Add("/adg", OnCommand,
            "AutoDeepDungeon. Subcommands: start | stop | status | config | debug. No arg opens config.");

        Svc.Log.Information(
            "AutoDeepDungeon loaded. Master toggle is OFF by default; re-arm per session. " +
            "Kill-switch: Ctrl+Shift+Pause halts automation instantly.");
    }

    public void Dispose()
    {
        Overlay?.Dispose();
        Planner?.Dispose();
        Exec?.Dispose();
        Floor?.Dispose();
        PassageState?.Dispose();
        KillSwitch?.Dispose();
        Deaths?.Dispose();
        SaveFiles?.Dispose();
        Lifecycle?.Dispose();
        PalacePal?.Dispose();
        ECommonsMain.Dispose();
    }

    internal static void SaveConfig() => EzConfig.Save();

    internal static void OpenSafetyModal()
    {
        if (safetyModal != null) safetyModal.IsOpen = true;
    }

    internal static void OpenDebugWindow()
    {
        if (debugWindow != null) debugWindow.IsOpen = true;
    }

    private static void WarnIfMissing(IIpcSubscriber sub)
    {
        if (sub.IsReady) return;
        var reason = sub.LastError is null ? "plugin not installed or not loaded" : sub.LastError;
        Svc.Log.Warning($"IPC subscriber '{sub.Name}' not ready: {reason}");
    }

    private void OnCommand(string command, string args)
    {
        var sub = args?.Trim().ToLowerInvariant() ?? string.Empty;
        switch (sub)
        {
            case "":
            case "config":
                EzConfigGui.Open();
                return;
            case "debug":
                OpenDebugWindow();
                return;
            case "start":
                Lifecycle.Start();
                return;
            case "stop":
                Lifecycle.Stop();
                return;
            case "status":
                PrintStatus();
                return;
            default:
                Svc.Chat.PrintError($"[adg] Unknown subcommand '{sub}'. Try: start | stop | status | config | debug.");
                return;
        }
    }

    private static void PrintStatus()
    {
        var inDd = DDStateHelper.IsInDeepDungeon();
        var floor = DDStateHelper.CurrentFloor();
        var kind = DDStateHelper.CurrentDDKind();
        var since = (DateTime.UtcNow - Lifecycle.StageEnteredAt).TotalSeconds;
        Svc.Chat.Print(
            $"[adg] stage={Lifecycle.CurrentStage} ({since:F0}s) " +
            $"| master={Config.MasterEnabled} | ToS={Config.ToSAccepted} " +
            $"| inDD={inDd} | floor={floor} | kind={kind?.ToString() ?? "n/a"}");
    }
}
