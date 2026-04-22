namespace AutoDeepDungeon.IPC;

public interface IIpcSubscriber
{
    string Name { get; }
    bool IsReady { get; }
    string? LastError { get; }
}
