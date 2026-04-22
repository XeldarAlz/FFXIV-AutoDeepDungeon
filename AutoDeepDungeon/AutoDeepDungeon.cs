using AutoDeepDungeon.Configuration;
using AutoDeepDungeon.IPC;
using Dalamud.Plugin;
using ECommons;
using ECommons.Configuration;
using ECommons.DalamudServices;
using ECommons.SimpleGui;
using AdgConfigWindow = AutoDeepDungeon.Windows.ConfigWindow;
using AdgDebugWindow = AutoDeepDungeon.Windows.DebugWindow;
using AdgSafetyModal = AutoDeepDungeon.Windows.SafetyModal;

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

    private static AdgSafetyModal safetyModal = null!;
    private static AdgDebugWindow debugWindow = null!;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        P = this;
        ECommonsMain.Init(pluginInterface, this);
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

        EzConfigGui.Init(AdgConfigWindow.Draw);

        safetyModal = new AdgSafetyModal();
        EzConfigGui.WindowSystem.AddWindow(safetyModal);
        if (!Config.ToSAccepted)
        {
            safetyModal.IsOpen = true;
        }

        debugWindow = new AdgDebugWindow();
        EzConfigGui.WindowSystem.AddWindow(debugWindow);

        EzCmd.Add("/adg", OnCommand,
            "Open AutoDeepDungeon. Subcommands: start | stop | config | status | debug (wired in a later milestone).");

        Svc.Log.Information("AutoDeepDungeon loaded. Master toggle is OFF by default; re-arm per session.");
    }

    public void Dispose()
    {
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
        var trimmed = args?.Trim() ?? string.Empty;
        if (string.Equals(trimmed, "debug", System.StringComparison.OrdinalIgnoreCase))
        {
            OpenDebugWindow();
            return;
        }
        EzConfigGui.Open();
    }
}
