using System.Collections.Generic;

namespace AutoDeepDungeon.Data;

/// <summary>
/// DataID tables for Deep Dungeon entity classification. Values verified against Lumina
/// (EObjName, BNpcBase, BNpcName sheets) during M1 Day 1 unless marked TODO.
///
/// Two ID spaces matter for us:
/// 1. EObj (ObjectKind.EventObj) — traps, coffers, hoards, passages. Classified by DataId.
/// 2. BNpc (ObjectKind.BattleNpc) — mobs and mimics. We check BNpcBase.RowId (plan's
///    "Mimic BNpcs" column is BNpcBase RowIds, not BNpcName as its name suggests).
/// </summary>
public static class DataIds
{
    // ─── EObj: PotD traps (invisible; no display name) ────────────────────────────────
    public static readonly HashSet<uint> TrapEObjs = new()
    {
        2007182, 2007183, 2007184, 2007185, 2007186, 2009504,
    };

    // ─── EObj: Accursed Hoard ────────────────────────────────────────────────────────
    // 2007542 = pre-dig marker; 2007543 = "banded coffer" revealed after /dig.
    public static readonly HashSet<uint> HoardEObjs = new()
    {
        2007542, 2007543,
    };

    // ─── EObj: Cairns/Beacons/Pylons of Passage (floor exit) ─────────────────────────
    // Per-DD. Active-state readable via ObjectEffectData1==4 && ObjectEffectData2==8.
    public const uint PassagePotD   = 2007188; // "Cairn of Passage"
    public const uint PassageHoH    = 2009507; // "Beacon of Passage"
    public const uint PassageOrthos = 2013287; // "Pylon of Passage"
    // PassagePilgrim — TODO: fill in once in-game verification is done.
    public static readonly HashSet<uint> PassageEObjs = new()
    {
        PassagePotD, PassageHoH, PassageOrthos,
    };

    // ─── EObj: treasure coffers (all named "treasure coffer") ────────────────────────
    // PotD uses 2007357 (silver) and 2007358 (gold). Bronze in PotD has historically
    // been listed as 2006020-2006022 by other plugins, but those EObj rows are ALSO
    // used in non-DD content — we scope bronze detection to when the current DD is
    // PotD (via DDStateHelper.CurrentDDKind) to avoid false positives outside the
    // dungeon.
    // TODO M1 Day 6: verify all three ranks in-game on PotD floors 1-10.
    public const uint CofferMimicLikely = 2006020; // mimic-spawning treasure coffer (PotD 11+)

    public static readonly HashSet<uint> CofferEObjsPotD = new()
    {
        2006020, 2006021, 2006022,    // shared DD "bronze/silver/gold" range
        2007357, 2007358,             // PotD explicit silver/gold
    };

    public static readonly HashSet<uint> CofferEObjsHoH = new()
    {
        2009530, 2009531, 2009532,    // HoH gold/silver/bronze
    };

    public static readonly HashSet<uint> CofferEObjsOrthos = new()
    {
        // TODO M1 Day 6: confirm which Orthos "treasure coffer" rows we actually see.
        // Candidates from EObjName scan: 2012402-2012405, 2013491-2013492, 2013698-2013701.
        2012402, 2012403, 2012404, 2012405,
        2013491, 2013492,
    };

    /// <summary>
    /// Returns true if <paramref name="dataId"/> looks like a coffer in the given DD.
    /// </summary>
    public static bool IsCoffer(uint dataId, TargetDungeon? dd) => dd switch
    {
        TargetDungeon.PalaceOfTheDead => CofferEObjsPotD.Contains(dataId),
        TargetDungeon.HeavenOnHigh    => CofferEObjsHoH.Contains(dataId),
        TargetDungeon.EurekaOrthos    => CofferEObjsOrthos.Contains(dataId),
        _ => false,
    };

    public static bool IsTrap(uint dataId)    => TrapEObjs.Contains(dataId);
    public static bool IsHoard(uint dataId)   => HoardEObjs.Contains(dataId);
    public static bool IsPassage(uint dataId) => PassageEObjs.Contains(dataId);

    // ─── BNpc: mimics ────────────────────────────────────────────────────────────────
    // The "mimic" IDs in the plan are BNpcBase RowIds (shared enemy templates), not
    // BNpcName RowIds. A bnpc matches a mimic when its BaseId is in one of these
    // ranges. BNpcName 2566 ("mimic") is a secondary check but not all DD mimics
    // display that exact name string, so we prefer BaseId.
    //
    // Ranges taken from the plan; per-DD ranges will be narrowed once we can observe
    // real mimics spawn in each floor band.
    public static readonly (uint start, uint end) MimicBasePotD_1_50 = (5831, 5835);
    public static readonly (uint start, uint end) MimicBasePotD_51_200 = (6359, 6373);
    public static readonly (uint start, uint end) MimicBaseHoH_1_30 = (9042, 9044);
    public static readonly (uint start, uint end) MimicBaseHoH_31_100 = (9045, 9051);
    public static readonly (uint start, uint end) MimicBaseOrthos_1_30 = (15996, 15998);
    public static readonly (uint start, uint end) MimicBaseOrthos_31_100 = (15999, 16005);

    public const uint MimicBNpcName = 2566; // "mimic"

    public static bool IsMimicBaseId(uint baseId, TargetDungeon? dd, int floor)
    {
        bool InRange(uint id, (uint s, uint e) r) => id >= r.s && id <= r.e;
        return dd switch
        {
            TargetDungeon.PalaceOfTheDead => floor <= 50
                ? InRange(baseId, MimicBasePotD_1_50)
                : InRange(baseId, MimicBasePotD_51_200),
            TargetDungeon.HeavenOnHigh => floor <= 30
                ? InRange(baseId, MimicBaseHoH_1_30)
                : InRange(baseId, MimicBaseHoH_31_100),
            TargetDungeon.EurekaOrthos => floor <= 30
                ? InRange(baseId, MimicBaseOrthos_1_30)
                : InRange(baseId, MimicBaseOrthos_31_100),
            _ => false,
        };
    }

    /// <summary>
    /// Active-state flags on a passage EObj. Per plan: the exit is ready when both
    /// ObjectEffectData1 == <see cref="PassageActiveData1"/> and
    /// ObjectEffectData2 == <see cref="PassageActiveData2"/>.
    /// </summary>
    public const byte PassageActiveData1 = 4;
    public const byte PassageActiveData2 = 8;
}
