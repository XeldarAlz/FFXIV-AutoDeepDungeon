using System;
using System.Collections.Generic;
using System.Numerics;
using AutoDeepDungeon.Data;
using AutoDeepDungeon.Helpers;
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
    }

    public void Dispose()
    {
        Svc.Framework.Update -= Tick;
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
                    ClassifyEObj(obj, kind, traps, coffers, hoards, ref passage);
                    break;
                case ObjectKind.BattleNpc when obj is IBattleNpc battle:
                    if (battle.IsDead) break;
                    // Don't list the local player or party members as mobs.
                    if (battle.SubKind == (byte)BattleNpcSubKind.Pet ||
                        battle.SubKind == (byte)BattleNpcSubKind.Chocobo) break;

                    var isMimic =
                        DataIds.IsMimicBaseId(battle.DataId, kind, floor) ||
                        battle.NameId == DataIds.MimicBNpcName;

                    mobs.Add(new MobEntity(
                        ObjectId: battle.GameObjectId,
                        NameId: battle.NameId,
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

        return new FloorState(
            InDeepDungeon: true,
            Kind: kind,
            Floor: floor,
            TerritoryType: territory,
            SelfPosition: self,
            Mobs: mobs,
            Traps: traps,
            Coffers: coffers,
            Hoards: hoards,
            Passage: passage);
    }

    private static void ClassifyEObj(
        IGameObject obj,
        TargetDungeon? kind,
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
            // Active-state detection (ObjectEffectData1==4 && Data2==8) lands in M1 Day 3
            // once we confirm the struct layout; default to Active=false for now.
            passage = new PassageEntity(obj.GameObjectId, dataId, obj.Position, Active: false);
        }
    }
}
