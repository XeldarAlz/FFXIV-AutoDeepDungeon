using AutoDeepDungeon.Data;
using Newtonsoft.Json;

namespace AutoDeepDungeon.Configuration;

public sealed class Config
{
    public int Version { get; set; } = 1;

    // Paranoid: master toggle is runtime-only, always starts false.
    // User has to re-arm every session so we can never auto-run on plugin load.
    [JsonIgnore] public bool MasterEnabled;

    // ToS acceptance gates the master toggle. Persisted so we only nag once.
    public bool ToSAccepted;

    // General
    public CombatDriverChoice CombatDriver = CombatDriverChoice.AutoDetect;

    // Run goals
    public TargetDungeon TargetDungeon = TargetDungeon.PalaceOfTheDead;
    public int FloorStart = 1;
    public int FloorEnd = 50;
    public StopCondition StopCondition = StopCondition.OneClear;
    public int StopAfterRuns = 1;

    // Save file
    public SaveBehavior SaveBehavior = SaveBehavior.Interactive;

    // Death
    public OnDeathBehavior OnDeath = OnDeathBehavior.Stop;
    public int RequeueDelaySeconds = 10;

    // Pomanders & consumables
    public HoardBehavior Hoard = HoardBehavior.SkipEntirely;
    public MagiciteBehavior Magicite = MagiciteBehavior.BossesOnly;
    public RaisingBehavior Raising = RaisingBehavior.StockOnly;
    public bool ConservativeMode = true;

    // Planner weights
    public int CofferDetourYalms = 10;
    public KillPolicy KillForAetherpool = KillPolicy.OnPathOnly;
    public MultiPullTolerance MultiPullTolerance = MultiPullTolerance.Strict;

    // PathCost tuning (M2). Total = Length + (cones crossed × penalty) − (coffers seen × reward),
    // or +∞ if any segment passes within TrapAvoidRadius of a known trap.
    public float PlannerAggroPenalty = 50f;
    public float PlannerCofferReward = 25f;
    public float PlannerTrapAvoidRadius = 1.5f;
    // Replan only swaps the active path if a candidate's Total beats the current by this ratio.
    public float PlannerHysteresisRatio = 0.80f;

    // Safety
    public int HpEmergencyThresholdPct = 20;
    public int StuckTimeoutSeconds = 45;
    public bool AutoStopOnUnexpectedState = true;

    // Diagnostics
    public bool EnableDeathLog = true;
    public bool EnableRunLog = true;
    public bool EnableDebugOverlay;
}
