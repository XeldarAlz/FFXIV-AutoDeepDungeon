using ECommons.DalamudServices;
using EcVnav = ECommons.IPC.Subscribers.Vnavmesh.VnavmeshIPC;
using EcSafeWrapper = ECommons.EzIpcManager.SafeWrapper;

namespace AutoDeepDungeon.IPC;

public sealed class VnavIPC : IIpcSubscriber
{
    public string Name => "vnavmesh";

    private readonly EcVnav subscriber;

    public VnavIPC()
    {
        subscriber = new EcVnav(EcSafeWrapper.AnyException);
    }

    public EcVnav Raw => subscriber;

    public bool IsReady
    {
        get
        {
            if (!subscriber.Available) return false;
            try
            {
                return subscriber.IsReady?.Invoke() ?? false;
            }
            catch (System.Exception ex)
            {
                LastError = ex.Message;
                return false;
            }
        }
    }

    public string? LastError { get; private set; }

    public float BuildProgress
    {
        get
        {
            if (!subscriber.Available) return 0f;
            try { return subscriber.BuildProgress?.Invoke() ?? 0f; }
            catch (System.Exception ex) { LastError = ex.Message; return 0f; }
        }
    }
}
