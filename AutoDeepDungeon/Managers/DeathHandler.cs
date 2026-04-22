using System;
using AutoDeepDungeon.Data;
using AutoDeepDungeon.Helpers;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;

namespace AutoDeepDungeon.Managers;

/// <summary>
/// Watches for player death inside a Deep Dungeon and dispatches based on Config.OnDeath.
/// M0 behavior:
///   - Stop: honoured (calls Lifecycle.Stop).
///   - RequeueSameFloor / RequeueReset / Revive: logged; actual handling in M4.
/// The goal of the M0 stub is to prove the setting is wired end-to-end.
/// </summary>
public sealed class DeathHandler : IDisposable
{
    private bool wasDeadLastTick;

    public DeathHandler()
    {
        Svc.Framework.Update += Tick;
    }

    public void Dispose()
    {
        Svc.Framework.Update -= Tick;
    }

    private void Tick(IFramework framework)
    {
        var player = Svc.ClientState.LocalPlayer;
        var dead = player is { IsDead: true } || (player != null && player.CurrentHp == 0);

        if (dead && !wasDeadLastTick && DDStateHelper.IsInDeepDungeon())
        {
            OnDeath();
        }
        wasDeadLastTick = dead;
    }

    private static void OnDeath()
    {
        var behavior = Plugin.Config.OnDeath;
        Svc.Log.Information($"[DeathHandler] Player died in DD. Config.OnDeath={behavior}.");

        switch (behavior)
        {
            case OnDeathBehavior.Stop:
                Plugin.Lifecycle.Stop();
                break;
            case OnDeathBehavior.RequeueSameFloor:
            case OnDeathBehavior.RequeueReset:
            case OnDeathBehavior.Revive:
                Svc.Log.Debug($"[DeathHandler] {behavior} handler not implemented yet (lands in M4).");
                break;
        }
    }
}
