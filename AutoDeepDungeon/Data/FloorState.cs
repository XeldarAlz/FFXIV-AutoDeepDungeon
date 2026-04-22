using System.Collections.Generic;
using System.Numerics;

namespace AutoDeepDungeon.Data;

/// <summary>
/// Snapshot of one pass of the object-table scan. Immutable record — planner code can
/// capture a reference and reason about it without worrying about concurrent mutation.
/// </summary>
public sealed record FloorState(
    bool InDeepDungeon,
    TargetDungeon? Kind,
    byte Floor,
    uint TerritoryType,
    Vector3 SelfPosition,
    IReadOnlyList<MobEntity> Mobs,
    IReadOnlyList<EObjEntity> Traps,
    IReadOnlyList<EObjEntity> Coffers,
    IReadOnlyList<EObjEntity> Hoards,
    PassageEntity? Passage)
{
    public static FloorState Empty { get; } = new(
        false, null, 0, 0, Vector3.Zero,
        System.Array.Empty<MobEntity>(),
        System.Array.Empty<EObjEntity>(),
        System.Array.Empty<EObjEntity>(),
        System.Array.Empty<EObjEntity>(),
        null);
}

/// <summary>Mob in range of the object table. NameId is the BNpcName; BaseId is populated
/// later (M1 Day 4) once we reach into FFXIVClientStructs for the BNpcBase backing row.</summary>
public sealed record MobEntity(
    ulong ObjectId,
    uint NameId,
    string Name,
    Vector3 Position,
    float Rotation,
    uint CurrentHp,
    uint MaxHp,
    bool InCombat,
    bool IsMimicCandidate);

public sealed record EObjEntity(
    ulong ObjectId,
    uint DataId,
    Vector3 Position);

public sealed record PassageEntity(
    ulong ObjectId,
    uint DataId,
    Vector3 Position,
    bool Active);
