using System.Linq;
using ECommons.DalamudServices;

namespace AutoDeepDungeon.IPC;

public sealed class WrathComboIPC : IIpcSubscriber
{
    public string Name => "WrathCombo";

    public bool IsReady =>
        Svc.PluginInterface.InstalledPlugins.Any(p => p.InternalName == "WrathCombo" && p.IsLoaded);

    public string? LastError { get; private set; }

    // Full typed surface lives in WrathCombo.API.WrathIPCWrapper. Call sites should invoke it
    // directly once IsReady == true. Lease registration + combat driver wiring lands in M3.
}
