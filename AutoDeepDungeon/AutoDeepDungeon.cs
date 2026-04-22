using AutoDeepDungeon.Configuration;
using Dalamud.Plugin;
using ECommons;
using ECommons.Configuration;
using ECommons.DalamudServices;
using ECommons.SimpleGui;
using AdgConfigWindow = AutoDeepDungeon.Windows.ConfigWindow;
using AdgSafetyModal = AutoDeepDungeon.Windows.SafetyModal;

namespace AutoDeepDungeon;

public sealed class Plugin : IDalamudPlugin
{
    internal static Plugin P = null!;
    internal static Config Config = null!;

    private static AdgSafetyModal safetyModal = null!;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        P = this;
        ECommonsMain.Init(pluginInterface, this);
        Config = EzConfig.Init<Config>();

        EzConfigGui.Init(AdgConfigWindow.Draw);

        safetyModal = new AdgSafetyModal();
        EzConfigGui.WindowSystem.AddWindow(safetyModal);
        if (!Config.ToSAccepted)
        {
            safetyModal.IsOpen = true;
        }

        EzCmd.Add("/adg", OnCommand,
            "Open AutoDeepDungeon. Subcommands: start | stop | config | status (wired in a later milestone).");

        Svc.Log.Information("AutoDeepDungeon loaded. Master toggle is OFF by default; re-arm per session.");
    }

    public void Dispose()
    {
        ECommonsMain.Dispose();
    }

    internal static void SaveConfig() => EzConfig.Save();

    internal static void OpenSafetyModal()
    {
        if (safetyModal != null) safetyModal.IsOpen = true;
    }

    private void OnCommand(string command, string args)
    {
        EzConfigGui.Open();
    }
}
