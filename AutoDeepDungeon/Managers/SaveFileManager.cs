using System;
using AutoDeepDungeon.Data;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using ECommons.DalamudServices;

namespace AutoDeepDungeon.Managers;

public sealed class SaveFileManager : IDisposable
{
    // The PotD save-file prompt addon. Confirm exact name during in-game testing; if it
    // differs across PotD/HoH/Orthos we'll add sibling entries.
    private const string SaveAddonName = "DeepDungeonSaveData";

    public SaveFileManager()
    {
        Svc.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, SaveAddonName, OnSaveAddonSetup);
    }

    public void Dispose()
    {
        Svc.AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, SaveAddonName, OnSaveAddonSetup);
    }

    private void OnSaveAddonSetup(AddonEvent type, AddonArgs args)
    {
        var behavior = Plugin.Config.SaveBehavior;
        Svc.Log.Information($"[SaveFileManager] {SaveAddonName} appeared; behavior={behavior}.");

        // Interactive leaves the addon alone for the user to click.
        // New / Continue will send the right Callback selection once addon offsets are
        // confirmed via in-game inspection — handled in M0 Day 5 / M1.
        switch (behavior)
        {
            case SaveBehavior.Interactive:
                break;
            case SaveBehavior.New:
            case SaveBehavior.Continue:
                Svc.Log.Debug($"[SaveFileManager] Auto-handler for {behavior} not implemented yet.");
                break;
        }
    }
}
