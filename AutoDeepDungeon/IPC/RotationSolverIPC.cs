using EcRsr = ECommons.IPC.Subscribers.RotationSolverReborn.RotationSolverRebornIPC;
using EcSafeWrapper = ECommons.EzIpcManager.SafeWrapper;

namespace AutoDeepDungeon.IPC;

public sealed class RotationSolverIPC : IIpcSubscriber
{
    public string Name => "RotationSolver";

    private readonly EcRsr subscriber;

    public RotationSolverIPC()
    {
        subscriber = new EcRsr(EcSafeWrapper.AnyException);
    }

    public EcRsr Raw => subscriber;

    public bool IsReady => subscriber.Available;
    public string? LastError { get; private set; }

    public void Stop()
    {
        if (!IsReady) return;
        try { subscriber.ChangeOperatingMode?.Invoke(EcRsr.StateCommandType.Off); }
        catch (System.Exception ex) { LastError = ex.Message; }
    }

    public void EngageAuto()
    {
        if (!IsReady) return;
        try { subscriber.ChangeOperatingMode?.Invoke(EcRsr.StateCommandType.AutoDuty); }
        catch (System.Exception ex) { LastError = ex.Message; }
    }
}
