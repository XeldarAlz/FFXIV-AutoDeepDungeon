using System;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;

namespace AutoDeepDungeon.Managers;

/// <summary>
/// Global Ctrl+Shift+Pause halts automation, regardless of stage or config.
/// Polls key state on Framework.Update — cheap, and works whether or not the game window
/// has focus of ImGui text input.
/// </summary>
public sealed class KillSwitch : IDisposable
{
    private bool wasHeldLastTick;

    public KillSwitch()
    {
        Svc.Framework.Update += Tick;
    }

    public void Dispose()
    {
        Svc.Framework.Update -= Tick;
    }

    private void Tick(IFramework framework)
    {
        var held = Svc.KeyState[VirtualKey.CONTROL]
                && Svc.KeyState[VirtualKey.SHIFT]
                && Svc.KeyState[VirtualKey.PAUSE];

        if (held && !wasHeldLastTick)
        {
            Svc.Log.Warning("Kill-switch triggered (Ctrl+Shift+Pause) — halting automation.");
            Svc.Chat.PrintError("[adg] Kill-switch: automation halted.");
            Plugin.Config.MasterEnabled = false;
            Plugin.Lifecycle.Stop();
        }
        wasHeldLastTick = held;
    }
}
