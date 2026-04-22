using System;
using System.Collections.Generic;
using System.Numerics;
using AutoDeepDungeon.Data;
using AutoDeepDungeon.Helpers;
using AutoDeepDungeon.IPC;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;

namespace AutoDeepDungeon.Managers;

/// <summary>
/// Per-tick pass over <see cref="Svc.Objects"/> that classifies entities via DataIds and
/// builds a <see cref="FloorState"/> snapshot. Throttled to ~10Hz — the planner doesn't
/// need per-frame resolution for M1.
/// </summary>
public sealed class FloorScanner : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(100);

    public FloorState Current { get; private set; } = FloorState.Empty;
    private DateTime nextTick = DateTime.MinValue;

    public FloorScanner()
    {
        Svc.Framework.Update += Tick;
        Svc.ClientState.TerritoryChanged += OnTerritoryChanged;
    }

    public void Dispose()
    {
        Svc.Framework.Update -= Tick;
        Svc.ClientState.TerritoryChanged -= OnTerritoryChanged;
    }

    private static void OnTerritoryChanged(ushort newTerritory)
    {
        // Drop cached PalacePal snapshots — another client (or PalacePal's server-sync) may
        // have added discoveries to the new territory while we were elsewhere.
        Plugin.PalacePal?.InvalidateCache();
    }

    private void Tick(IFramework framework)
    {
        var now = DateTime.UtcNow;
        if (now < nextTick) return;
        nextTick = now + Interval;

        try
        {
            Current = Scan();
        }
        catch (Exception ex)
        {
            Svc.Log.Warning($"[FloorScanner] scan failed: {ex.Message}");
        }
    }

    private static FloorState Scan()
    {
        if (!DDStateHelper.IsInDeepDungeon())
            return FloorState.Empty;

        var kind = DDStateHelper.CurrentDDKind();
        var floor = DDStateHelper.CurrentFloor();
        var passageProgress = DDStateHelper.CurrentPassageProgress();
        var territory = Svc.ClientState.TerritoryType;
        var self = Svc.ClientState.LocalPlayer?.Position ?? Vector3.Zero;

        var mobs = new List<MobEntity>();
        var traps = new List<EObjEntity>();
        var coffers = new List<EObjEntity>();
        var hoards = new List<EObjEntity>();
        PassageEntity? passage = null;

        foreach (var obj in Svc.Objects)
        {
            if (obj == null) continue;

            switch (obj.ObjectKind)
            {
                case ObjectKind.EventObj:
                    ClassifyEObj(obj, kind, passageProgress, traps, coffers, hoards, ref passage);
                    break;
                case ObjectKind.BattleNpc when obj is IBattleNpc battle:
                    if (battle.IsDead) break;
                    // Don't list the local player or party members as mobs.
                    if (battle.SubKind == (byte)BattleNpcSubKind.Pet ||
                        battle.SubKind == (byte)BattleNpcSubKind.Chocobo) break;

                    uint baseId;
                    unsafe
                    {
                        var chara = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)battle.Address;
                        baseId = chara == null ? 0 : chara->BaseId;
                    }

                    var isMimic =
                        DataIds.IsMimicBaseId(baseId, kind, floor) ||
                        battle.NameId == DataIds.MimicBNpcName;

                    mobs.Add(new MobEntity(
                        ObjectId: battle.GameObjectId,
                        NameId: battle.NameId,
                        BaseId: baseId,
                        Name: battle.Name.TextValue,
                        Position: battle.Position,
                        Rotation: battle.Rotation,
                        CurrentHp: battle.CurrentHp,
                        MaxHp: battle.MaxHp,
                        InCombat: battle.StatusFlags.HasFlag(Dalamud.Game.ClientState.Objects.Enums.StatusFlags.InCombat),
                        IsMimicCandidate: isMimic));
                    break;
            }
        }

        var (persistentTraps, persistentHoards) = LoadPersistent(territory);

        return new FloorState(
            InDeepDungeon: true,
            Kind: kind,
            Floor: floor,
            PassageProgress: passageProgress,
            TerritoryType: territory,
            SelfPosition: self,
            Mobs: mobs,
            Traps: traps,
            Coffers: coffers,
            Hoards: hoards,
            PersistentTraps: persistentTraps,
            PersistentHoards: persistentHoards,
            Passage: passage);
    }

    private static (IReadOnlyList<Vector3> traps, IReadOnlyList<Vector3> hoards) LoadPersistent(uint territory)
    {
        var pp = Plugin.PalacePal;
        if (pp == null || !pp.IsReady || territory == 0)
            return (Array.Empty<Vector3>(), Array.Empty<Vector3>());

        var rows = pp.QueryByTerritory(territory);
        if (rows.Count == 0) return (Array.Empty<Vector3>(), Array.Empty<Vector3>());

        var traps = new List<Vector3>();
        var hoards = new List<Vector3>();
        foreach (var row in rows)
        {
            switch (row.Type)
            {
                case PalacePalReader.TypeTrap:  traps.Add(row.Position); break;
                case PalacePalReader.TypeHoard: hoards.Add(row.Position); break;
            }
        }
        return (traps, hoards);
    }

    private static void ClassifyEObj(
        IGameObject obj,
        TargetDungeon? kind,
        byte passageProgress,
        List<EObjEntity> traps,
        List<EObjEntity> coffers,
        List<EObjEntity> hoards,
        ref PassageEntity? passage)
    {
        var dataId = obj.DataId;

        if (DataIds.IsTrap(dataId))
        {
            traps.Add(new EObjEntity(obj.GameObjectId, dataId, obj.Position));
            return;
        }
        if (DataIds.IsHoard(dataId))
        {
            hoards.Add(new EObjEntity(obj.GameObjectId, dataId, obj.Position));
            return;
        }
        if (DataIds.IsCoffer(dataId, kind))
        {
            coffers.Add(new EObjEntity(obj.GameObjectId, dataId, obj.Position));
            return;
        }
        if (DataIds.IsPassage(dataId))
        {
            // PassageProgress is a byte on InstanceContentDeepDungeon. Non-zero means the
            // floor has been cleared enough for the exit to light up. Exact thresholds per
            // DD are pinned during M1 Day 6 in-game testing.
            passage = new PassageEntity(obj.GameObjectId, dataId, obj.Position, Active: passageProgress > 0);
        }
    }
}
