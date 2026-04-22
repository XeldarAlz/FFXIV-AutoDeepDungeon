namespace AutoDeepDungeon.Data;

public enum CombatDriverChoice
{
    AutoDetect,
    RotationSolver,
    WrathCombo,
}

public enum TargetDungeon
{
    PalaceOfTheDead,
    HeavenOnHigh,
    EurekaOrthos,
    PilgrimTraverse,
}

public enum StopCondition
{
    OneClear,
    NRuns,
    UntilInterrupted,
}

public enum SaveBehavior
{
    New,
    Continue,
    Interactive,
}

public enum OnDeathBehavior
{
    Stop,
    RequeueSameFloor,
    RequeueReset,
    Revive,
}

public enum HoardBehavior
{
    DigWhenIntuitionAvailable,
    SkipEntirely,
}

public enum MagiciteBehavior
{
    BossesOnly,
    EmergencyOnly,
    Never,
}

public enum RaisingBehavior
{
    AutoReviveOnDeath,
    StockOnly,
    NeverUse,
}

public enum KillPolicy
{
    Never,
    OnPathOnly,
    Aggressive,
}

public enum MultiPullTolerance
{
    Strict,
    Relaxed,
    Aggressive,
}

public enum Stage
{
    Idle,
    Queueing,
    Entering,
    Planning,
    Executing,
    Combat,
    Panic,
    FloorClear,
    Descending,
    Dead,
}
