using AutoDeepDungeon.Data;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;

namespace AutoDeepDungeon.Helpers;

public static unsafe class DDStateHelper
{
    // DeepDungeonId values seen in game data. Falls back to territory ranges when the id
    // isn't set yet (brief window during load).
    private const byte IdPotD = 1;
    private const byte IdHoH = 2;
    private const byte IdOrthos = 3;
    private const byte IdPilgrim = 4;

    public static InstanceContentDeepDungeon* GetInstancePtr()
    {
        var ef = EventFramework.Instance();
        return ef == null ? null : ef->GetInstanceContentDeepDungeon();
    }

    public static bool IsInDeepDungeon() => GetInstancePtr() != null;

    public static byte CurrentFloor()
    {
        var dd = GetInstancePtr();
        return dd == null ? (byte)0 : dd->Floor;
    }

    public static byte CurrentPassageProgress()
    {
        var dd = GetInstancePtr();
        return dd == null ? (byte)0 : dd->PassageProgress;
    }

    public static TargetDungeon? CurrentDDKind()
    {
        var dd = GetInstancePtr();
        if (dd != null)
        {
            return dd->DeepDungeonId switch
            {
                IdPotD => TargetDungeon.PalaceOfTheDead,
                IdHoH => TargetDungeon.HeavenOnHigh,
                IdOrthos => TargetDungeon.EurekaOrthos,
                IdPilgrim => TargetDungeon.PilgrimTraverse,
                _ => KindFromTerritory(Svc.ClientState.TerritoryType),
            };
        }
        return KindFromTerritory(Svc.ClientState.TerritoryType);
    }

    private static TargetDungeon? KindFromTerritory(uint territory)
    {
        // PotD 1-50:   561-565
        // PotD 51-200: 593-607
        if (territory is >= 561 and <= 565 or >= 593 and <= 607)
            return TargetDungeon.PalaceOfTheDead;
        // HoH 1-100: 770-779
        if (territory is >= 770 and <= 779)
            return TargetDungeon.HeavenOnHigh;
        // Orthos 1-100: 1099-1108
        if (territory is >= 1099 and <= 1108)
            return TargetDungeon.EurekaOrthos;
        // Pilgrim Traverse territories: not yet pinned down; add when confirmed in testing.
        return null;
    }
}
