using EcBossMod = ECommons.IPC.Subscribers.BossMod.BossModIPC;
using EcSafeWrapper = ECommons.EzIpcManager.SafeWrapper;

namespace AutoDeepDungeon.IPC;

public sealed class BossModIPC : IIpcSubscriber
{
    public string Name => "BossMod";

    private readonly EcBossMod subscriber;

    public BossModIPC()
    {
        subscriber = new EcBossMod(EcSafeWrapper.AnyException);
    }

    public EcBossMod Raw => subscriber;

    public bool IsReady => subscriber.Available;
    public string? LastError { get; private set; }

    public bool HasModuleByDataId(uint bnpcDataId)
    {
        if (!IsReady) return false;
        try { return subscriber.HasModuleByDataId?.Invoke(bnpcDataId) ?? false; }
        catch (System.Exception ex) { LastError = ex.Message; return false; }
    }
}
