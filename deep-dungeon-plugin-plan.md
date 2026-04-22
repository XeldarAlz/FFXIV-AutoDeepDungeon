# FFXIV Deep Dungeon Autopilot — Plugin Plan

## Purpose

A standalone Dalamud C# plugin that auto-runs FFXIV Deep Dungeon solo, end-to-end from Duty Finder queue through floor clears until a stop condition is met. Rule-based. No ML. Plays carefully: avoids known traps, plans single pulls, uses defensive tools at the right time, doesn't die.

## Target dungeons & phasing

| Phase | Dungeon | Floors | Notes |
|---|---|---|---|
| 1 (MVP) | **Palace of the Dead** | 1–50 | Ship target. ≥90% clear rate over 20 consecutive runs. |
| 2 | PotD | 51–100 | Tighter aggro, more mimics, magicite floors. |
| 3 | PotD | 101–200 | Post-MVP. |
| 4 | Heaven-on-High | 1–100 | Port: new DataIDs + bosses. |
| 5 | Eureka Orthos | 1–100 | Port. |
| 6 | Pilgrim Traverse | TBD | After prior ports stabilize. |

## Non-goals

- Not a rotation optimizer — delegate combat to RotationSolver or WrathCombo.
- Not a custom navmesh — delegate pathing to vnavmesh.
- Not a boss-mechanics learner — delegate to BossMod where supported; script gaps manually.
- Not a party runner. Solo only in v1.
- Not an aetherpool farmer. Aetherpool accrues as a side effect; the objective is floor clears.
- Not ML/RL. Deterministic state machines + classical planning.
- Not a PalacePal replacement — consume its data, not replace its UI.
- Not submittable to the official Dalamud plugin registry. Custom repo only.

## Repository & tech stack

- **Repo:** https://github.com/XeldarAlz/FFXIV-AutoDeepDungeon
- **Local path:** `C:\Users\xelda\Desktop\FFXIV-AutoDeepDungeon`
- **Plugin name / assembly:** `AutoDeepDungeon`
- **Command:** `/adg` (with subcommands: `start | stop | config | status`)
- **SDK:** `Dalamud.NET.Sdk/14.0.2` (or latest stable at implementation time)
- **Target framework:** `net10.0-windows8.0`, x64, AnyCPU off, x64 only
- **Exemplar:** AutoDuty's ECommons-based pattern (state machine + `NeoTaskManager` + `ECommons.Configuration` + `EzIpcManager`). NOT ffxiv-rota.

### Dependencies

| Dependency | Source | Purpose |
|---|---|---|
| ECommons | Git submodule (`NightmareXIV/ECommons`) | Dalamud service locator, task manager, config, IPC helpers |
| ECommons.IPC | Git submodule (`NightmareXIV/ECommons.IPC`) | Typed IPC wrappers for common Dalamud plugins |
| NightmareUI | Git submodule (`NightmareXIV/NightmareUI`) | ImGui window + component helpers |
| Pictomancy | NuGet `Pictomancy 0.0.*` | World-space overlay rendering (debug planner visualization) |
| WrathCombo.API | NuGet `WrathCombo.API 0.5.*` | Typed WrathCombo IPC |
| Microsoft.Data.Sqlite | NuGet | Read PalacePal SQLite DB directly |

### Project layout

```text
AutoDeepDungeon/                        (repo root)
├─ AutoDeepDungeon.sln
├─ AutoDeepDungeon.json                 (repo-level manifest for the custom plugin repo)
├─ AutoDeepDungeon/                     (main project)
│  ├─ AutoDeepDungeon.cs                (IDalamudPlugin entry)
│  ├─ AutoDeepDungeon.csproj
│  ├─ AutoDeepDungeon.json              (Dalamud plugin manifest)
│  ├─ Configuration/
│  │  ├─ Config.cs                      (ECommons.Configuration)
│  │  └─ ConfigurationMigrator.cs
│  ├─ Data/
│  │  ├─ DataIds.cs                     (all EObj + BNpc DataID tables)
│  │  └─ Enums.cs                       (Stage, AggroType, PomanderKind, ...)
│  ├─ Helpers/
│  │  ├─ DDStateHelper.cs               (in-DD detection + floor number)
│  │  ├─ PomanderHelper.cs              (inventory state)
│  │  └─ CombatHelper.cs                (HP%, MP%, aggro count)
│  ├─ IPC/
│  │  ├─ VnavIPC.cs
│  │  ├─ RotationSolverIPC.cs
│  │  ├─ WrathComboIPC.cs
│  │  ├─ BossModIPC.cs
│  │  ├─ PalacePalReader.cs             (SQLite reader, not IPC)
│  │  └─ TextAdvanceIPC.cs              (optional; cutscene skip)
│  ├─ Managers/
│  │  ├─ RunLifecycle.cs                (top state machine)
│  │  ├─ FloorScanner.cs
│  │  ├─ AggroMap.cs
│  │  ├─ CostGraph.cs
│  │  ├─ PathPlanner.cs
│  │  ├─ Executor.cs
│  │  ├─ CombatDriver.cs
│  │  ├─ PomanderPolicy.cs
│  │  ├─ MagiciteHandler.cs
│  │  ├─ HoardHandler.cs
│  │  ├─ SaveFileManager.cs
│  │  └─ DeathHandler.cs
│  ├─ Bosses/
│  │  ├─ IBossHandler.cs
│  │  └─ {one file per floor boss}
│  └─ Windows/
│     ├─ ConfigWindow.cs
│     ├─ DebugWindow.cs                 (planner visualization, state dump)
│     └─ SafetyModal.cs
├─ ECommons/                            (submodule)
├─ ECommons.IPC/                        (submodule)
└─ NightmareUI/                         (submodule)
```

### Branching & iteration model

- Feature branches per milestone: `feat/m0-scaffold`, `feat/m1-perception`, …
- PR-based merges to `main` after each milestone's exit criteria are met
- Commits are scoped: one sub-system or concern per commit, descriptive messages
- Tag releases at MVP (`v0.1.0-mvp`), PotD-100 (`v0.2.0`), HoH (`v0.3.0`), etc.
- Automation off by default on every plugin load; master toggle persists across reloads but must be re-enabled manually per session (paranoid default)

## External plugin dependencies

All integration via Dalamud IPC. One `IPCSubscriber` class per external plugin, `IsReady` gate, graceful degradation. Pattern copied from AutoDuty `IPCSubscriber.cs:55-366`.

| Plugin | Role | Required? | Fallback |
|---|---|---|---|
| **vnavmesh** | Path queries + movement execution | Required | Plugin disabled if missing |
| **RotationSolver** or **WrathCombo** | Combat rotation | One required | User selects; plugin detects availability |
| **BossMod** | Floor-boss mechanic dodging | Strongly recommended | Manual per-boss scripts where BossMod missing |
| **PalacePal** | Persistent trap + hoard data | Required | Without it, trap data is live-only (Pomander of Sight or walk-into) |

## Data sources

### Persistent — PalacePal SQLite

Read directly via `Microsoft.Data.Sqlite` in WAL mode. Safe for concurrent access.

- **Path:** `%APPDATA%\XIVLauncher\pluginConfigs\PalacePal\palace-pal.data.sqlite3` (verified in M0). Daily `backup-YYYY-MM-DD.data.sqlite3` snapshots live in the same folder and must be skipped.
- **Table:** `Locations` (verified in M0 — `ClientLocation` was the older Pal-assumed name; real schema uses `Locations`)
- **Schema columns:** `LocalId INTEGER, TerritoryType INTEGER, Type INTEGER, X REAL, Y REAL, Z REAL, Seen INTEGER, Source INTEGER, SinceVersion TEXT`
- **Query:** `SELECT TerritoryType, Type, X, Y, Z FROM Locations WHERE TerritoryType = @current AND Type IN (1, 2)`
- **Type=1 (Trap):** EObj DataIDs `2007182, 2007183, 2007184, 2007185, 2007186, 2009504`
- **Type=2 (Hoard):** EObj DataIDs `2007542, 2007543`
- **Cache:** re-query on territory change, hold in memory for the floor. Failures are also cached per territory to avoid retry-spam every frame.
- **Schema-pinning:** lock against specific PalacePal migration version in plugin manifest; disable gracefully on mismatch.

### Ephemeral — live IObjectTable scan

Every frame in `Framework.Update`, enumerate `ObjectTable` and classify by DataID.

| Type | DataID(s) | Behavior |
|---|---|---|
| Silver Coffer | `2007357` | Open if detour cost ≤ threshold |
| Gold Coffer | `2007358` | Always open; may contain magicite |
| Bronze Coffer | `782–1554` (full list in `ExternalUtils.cs:15`) | Open if adjacent on path |
| Mimic Coffer | `2006020` | Treat as dangerous mob, skip unless forced |
| Passage (PotD) | `2007188` | Goal tile. Active when `ObjectEffectData1 == 4 && ObjectEffectData2 == 8` |
| Passage (HoH) | `2009507` | Same |
| Passage (Orthos) | `2013287` | Same |
| Mimic BNpcs (PotD 1–50) | `5831–5835` | Dangerous mob |
| Mimic BNpcs (PotD 51–200) | `6359–6373` | Dangerous mob |
| Mimic BNpcs (HoH 1–30) | `9042–9044` | Dangerous mob |
| Mimic BNpcs (HoH 31–100) | `9045–9051` | Dangerous mob |
| Mimic BNpcs (Orthos 1–30) | `15996–15998` | Dangerous mob |
| Mimic BNpcs (Orthos 31–100) | `15999–16005` | Dangerous mob |

### Aggro geometry (per-BNpc)

- **Aggro radius:** Lumina `BNpcBase` (aggro column — verify exact field name during impl). Fallback constant `10` yalms if absent.
- **Aggro type:** sight / sound / proximity. Source: `BNpcBase.Aggression` or hardcoded table copied from RadarPlugin's `allMobs.json`.
- **Cone angle:** `π/2` (90°) for sight; `2π` (full circle) for sound/proximity.
- **Facing:** `IGameObject.Rotation` (radians; 0 = south).

## Architecture

### Top-level state machine

```
Idle
 └─► Queueing ─► Entering ─► Planning ─► Executing ─► FloorClear ─► Descending ─┐
                                  ▲          │                                   │
                                  │          ▼                                   │
                                  │       Combat ──► Panic (HP critical)         │
                                  │          │          │                        │
                                  │          ▼          ▼                        │
                                  └──────── Recover ── Dead ─► DeathHandler      │
                                                                  │              │
                                                                  ▼              │
                                                            (requeue/stop)       │
                                                                                 │
                                                         ◄───────────────────────┘
```

| State | Responsibility |
|---|---|
| Idle | Waiting for user trigger |
| Queueing | Duty Finder solo queue + accept |
| Entering | Cutscene skip (TextAdvance IPC if installed), save-file prompt, territory load |
| Planning | Full floor scan → build cost graph → plan path to passage |
| Executing | Follow planned waypoints via vnavmesh; open coffers on path |
| Combat | Entered on aggro; delegate rotation, drive own defensives + pomanders |
| Panic | HP/MP critical or surrounded; override planner with safety moves |
| FloorClear | Passage active; walk into it |
| Descending | Floor transition; wait for layer-change signal |
| Dead | Run ended; consult death-handler setting |

### Continuous re-planner

Core of the plugin. Runs every ~200ms and on events (mob moved, HP threshold crossed, pomander consumed, coffer opened, passage state changed).

```csharp
void Replan() {
    var floor = FloorScanner.Snapshot();   // mobs + traps + coffers + passage + self
    var graph = CostGraph.Build(floor);
    // Base edge cost = distance on navmesh
    // + penalty entering any mob's aggro cone (scales with fight difficulty)
    // + ∞ for trap tiles (unless Safety pomander active)
    // + negative cost (reward) near unopened coffers, scaled by config weight
    var path = AStar(graph, from: self.Position, to: floor.Passage.Position);
    Executor.Replace(PostProcess(path));
}
```

Re-plan hysteresis: only swap the active path if the new path's cost beats the current by > 20%. Prevents patrol-induced jitter.

## Sub-systems

1. **`IPCSubscriber/`** — one class per external plugin. Copies AutoDuty pattern.
2. **`FloorScanner.cs`** — object-table enumeration, PalacePal SQLite query, builds `FloorState`.
3. **`AggroMap.cs`** — per-mob geometry. Spatial index (grid) for O(log n) cone-containment lookups.
4. **`CostGraph.cs`** — constrained navmesh-graph builder. Owns the cost-function weights exposed to config.
5. **`PathPlanner.cs`** — A* over the cost graph. 200ms cadence + event-driven.
6. **`Executor.cs`** — wraps `SimpleMove.PathfindAndMoveTo` / `Path.MoveTo`. Stuck-recovery timeout on top of vnavmesh's built-in.
7. **`CombatDriver.cs`** — on aggro: enable RS/Wrath IPC; drive defensives directly:
   - Arm's Length on multi-mob pulls
   - Second Wind / Bloodbath at HP < 50%
   - Rampart-equivalent at HP < 30%
   - DD consumables (Sustaining Potion, Elixir Field, Burst of Inspiration, etc.) — full trigger table in appendix, populated during impl
8. **`PomanderPolicy.cs`** — static priority table keyed on (floor range, HP%, MP%, slot state). Table below.
9. **`MagiciteHandler.cs`** — rules: bosses only by default; emergency use at HP < 20% if configured.
10. **`HoardHandler.cs`** — when hoard setting enabled: use Intuition → path to hinted tile → `/dig`. Defer if enemies within 5y.
11. **`Bosses/`** — one file per floor-boss. Delegates to BossMod; overrides where BossMod is weak.
12. **`SaveFileManager.cs`** — `New | Continue | Interactive` behavior.
13. **`DeathHandler.cs`** — `Stop | RequeueSameFloor | RequeueReset | Revive`.
14. **`RunLifecycle.cs`** — queue → enter → play → exit → (requeue). Session stats logging.
15. **`SafetyModal.cs`** — ToS-risk modal on first install and on master-toggle enable.

### Pomander priority table (v1)

| Pomander | Trigger |
|---|---|
| Safety | Floor 51+ start; or floor flagged trap-dense in PalacePal data |
| Strength | Pre-floor-boss (every 10th floor) |
| Steel | Heavy-physical-mob floor |
| Resolution | HP < 15% emergency (overrides all other panic actions) |
| Witching | Surrounded AND Arm's Length on cooldown |
| Affluence | Stuck > config-timeout; also emergency escape |
| Flight | Same as Affluence |
| Alteration | Never auto-use in v1 |
| Sight | Floor start if user enables "eager trap reveal" |
| Intuition | Before hoard dig if hoard handler enabled |
| Raising | Stocked for revive-on-death if death-handler = Revive |
| Rage / Fortune / Lust | Never auto-use in v1 |

## Configuration surface

Single ImGui config window. All settings persist per-character.

### General
- **Enable autopilot** (master toggle; requires ToS-accept)
- **Combat driver:** `RotationSolver | WrathCombo | Auto-detect`

### Run goals
- **Target dungeon:** `PotD | HoH | Orthos | Pilgrim Traverse` (as supported by current build)
- **Floor range:** start/end sliders (e.g., 1–50)
- **Stop condition:** `One clear | N runs | Until interrupted`

### Save file
- **Behavior:** `New | Continue | Interactive`

### Death
- **On death:** `Stop | RequeueSameFloor | RequeueReset | Revive`
- **Requeue delay:** 0–30 seconds

### Pomanders & consumables
- **Accursed Hoard:** `Dig when Intuition available | Skip entirely`
- **Magicite:** `Bosses only | Emergency (HP<20%) | Never`
- **Raising pomander:** `Auto-revive on death | Stock | Never use`
- **Conservative mode:** `On` pops defensive pomanders one floor early

### Planner weights (farming ↔ speed)
- **Coffer detour tolerance:** 0–50 yalms (0 = speed-only; 50 = collect everything)
- **Kill-for-aetherpool:** `Never | On-path only | Aggressive`
- **Multi-pull tolerance:** `Strict (1 pack) | Relaxed (2 packs) | Aggressive (unlimited)`

### Safety
- **HP emergency threshold:** 10–30%
- **Stuck timeout:** 15–120 seconds
- **Auto-stop on unexpected state:** `On | Off`

### Diagnostics
- **Death log** → `pluginconfigs/DDAutopilot/deaths/`
- **Run log** → `pluginconfigs/DDAutopilot/runs/`
- **Debug overlay** (planned path, aggro cones, goal marker)

## Milestones & roadmap

### Summary

| M | Deliverable | Duration |
|---|---|---|
| M0 | Foundation — scaffold, ECommons + deps wired, IPC stubs, config, ToS modal, lifecycle skeleton | 1 week |
| M1 | Perception — FloorScanner + AggroMap + PalacePal reader + debug overlay | 1 week |
| M2 | Planning — cost graph + A* + executor + stuck recovery | 1–2 weeks |
| M3 | Combat — CombatDriver + defensive CDs + DD consumable auto-use | 1–2 weeks |
| M4 | Pomanders + Panic state | 1 week |
| M5 | Floor bosses 10/20/30/40/50 | 1–2 weeks |
| M6 | Hoard + Magicite handlers | 1 week |
| **M7 (MVP)** | PotD 1-50 end-to-end autopilot, ≥ 90% clear over 20 runs | — |
| M8 | PotD 51-100 extension | 2–3 weeks |
| M9 | PotD 101-200 | 3–4 weeks post-MVP |
| M10 | HoH port | 3 weeks |
| M11 | Orthos port | 3 weeks |
| M12 | Pilgrim Traverse | TBD |

**MVP (M0–M7): ~8–10 focused weeks.**

---

### M0 — Foundation (1 week)

**Goal:** project scaffold with all dependencies wired, config + ToS modal working, IPC stubs reporting readiness, lifecycle skeleton that can queue/enter/exit DD safely without attempting any gameplay.

**Branch:** `feat/m0-scaffold`

**Day 1 — Restructure & dependencies**
- Remove `SamplePlugin/` scaffold; restructure repo to the layout in "Repository & tech stack"
- Add submodules: `git submodule add https://github.com/NightmareXIV/ECommons ECommons`, same for `ECommons.IPC` and `NightmareUI`
- Create `AutoDeepDungeon.sln` referencing the 4 projects (main + 3 submodules)
- Create `AutoDeepDungeon.csproj` matching AutoDuty's SDK + target settings
- Create `AutoDeepDungeon.json` (Dalamud plugin manifest) with name, author, repo URL, API level
- Create empty folder scaffold per layout
- Verify: `dotnet build` succeeds

**Day 2 — Entry point + configuration + UI**
- `AutoDeepDungeon.cs` — `IDalamudPlugin` entry with ECommons `ECommonsMain.Init(...)`, `[PluginService]` injection for required services, command handler registration
- `Configuration/Config.cs` — every setting from the plan's config surface, using `ECommons.Configuration.EzConfig`
- `Windows/SafetyModal.cs` — ToS-risk modal on first launch, blocks the master toggle until accepted
- `Windows/ConfigWindow.cs` — render full config surface (General / Run goals / Save / Death / Pomanders / Planner weights / Safety / Diagnostics)
- Verify: plugin loads in Dalamud, config window opens, settings persist across reload

**Day 3 — IPC subscribers (stubs only)**
- `IPC/VnavIPC.cs` — typed wrappers for `Nav.Pathfind`, `Path.MoveTo`, `SimpleMove.PathfindAndMoveTo`, status polling via ECommons `EzIPC`
- `IPC/RotationSolverIPC.cs` — typed wrappers; no engagement logic yet
- `IPC/WrathComboIPC.cs` — using `WrathCombo.API` NuGet
- `IPC/BossModIPC.cs` — `HasModuleByDataId`, preset/strategy config
- `IPC/PalacePalReader.cs` — `Microsoft.Data.Sqlite` connection to PalacePal DB in WAL-read mode; auto-detect DB path; test query on `ClientLocation`
- All subscribers expose `IsReady` + `LastError`; log to Dalamud log on mismatch
- Verify: status panel shows `Ready: true` for installed plugins, `Ready: false` + reason for absent ones

**Day 4 — Lifecycle skeleton**
- `Data/Enums.cs` — `Stage` enum (Idle / Queueing / Entering / Planning / Executing / Combat / Panic / FloorClear / Descending / Dead)
- `Helpers/DDStateHelper.cs` — `IsInDeepDungeon()`, `CurrentFloor()`, `CurrentDDKind()` via game memory / `AgentHUD` / whatever source we find (fill in during this day — one of the open research items)
- `Managers/RunLifecycle.cs` — state machine with `Framework.Update` tick; transitions queue → enter → idle, no gameplay logic
- `Managers/SaveFileManager.cs` — reads config setting; handles the save-file prompt addon if detected
- Verify: `/adg start` inside DD lobby queues + enters; `/adg stop` exits cleanly

**Day 5 — Command handler + safety + build pipeline**
- Command handler `/adg` with subcommands `start | stop | config | status`
- `Windows/DebugWindow.cs` — status panel showing: Stage, In-DD, CurrentFloor, IPC readiness, config-snapshot summary
- Kill-switch hotkey (default `Ctrl+Shift+Pause`) halts automation instantly
- Humanized action timing helper (400–1200ms log-normal jitter)
- Automation is off by default on every plugin load; master toggle must be re-enabled per session
- Verify: fresh load → kill-switch works; `/adg status` prints sensible state; death-handler stub reads correct setting

**M0 exit criteria**
- Plugin loads cleanly in Dalamud with no exceptions
- All config settings persist across reload
- ToS modal blocks master-toggle until accepted
- Four IPC subscribers report readiness correctly (including when plugins are absent)
- PalacePal SQLite reader returns non-zero trap + hoard counts inside any PotD territory
- `/adg start` inside PotD lobby queues, enters floor 1, and idles; `/adg stop` exits cleanly
- Debug window displays live state

---

### M1 — Perception (1 week)

**Goal:** the plugin knows everything about the current floor — entities, aggro geometry, traps, hoards, coffers, passage — with debug overlays proving it.

**Branch:** `feat/m1-perception`

**Tasks**
- `Data/DataIds.cs` — all EObj + BNpc tables from the plan's data-sources section (traps, hoards, coffers, mimics, passages)
- `Managers/FloorScanner.cs` — enumerate `IObjectTable` every frame, classify via DataID tables, merge with PalacePal SQLite results, build `FloorState`
- `Managers/AggroMap.cs` — per-mob aggro geometry with spatial index (grid-based); resolve aggro type + radius via Lumina `BNpcBase` with RadarPlugin's JSON as fallback table
- `Windows/DebugWindow.cs` — live entity list with classification; per-mob aggro cone rendered with Pictomancy in world space
- Passage active-state detection: read `ObjectEffectData1/2` fields on the passage EObj

**Exit criteria**
- On entering any PotD floor, the debug overlay displays: mobs with aggro cones, known traps (from PalacePal seed), hoards, all coffer types, and the passage with active/inactive state
- Aggro cone orientation matches mob facing visually
- Passage correctly flips from inactive → active when the floor is cleared

---

### M2 — Planning (1–2 weeks)

**Goal:** the plugin can path from spawn to the passage, avoiding known traps and mob aggro, with sensible stuck recovery.

**Branch:** `feat/m2-planning`

**Tasks**
- `Managers/CostGraph.cs` — build navmesh-augmented cost graph (edges into aggro cones get penalty; trap tiles infinite unless Safety active; near-coffer nodes get negative cost scaled by config)
- `Managers/PathPlanner.cs` — A* over the cost graph; 200ms tick + event-driven replan; hysteresis threshold (20% cost improvement to swap)
- `Managers/Executor.cs` — wraps `SimpleMove.PathfindAndMoveTo`; stuck-timeout fallback on top of vnavmesh's built-in
- Handle dynamic re-plan on: mob moved significantly, pomander consumed, coffer opened, passage activated

**Exit criteria**
- Enter PotD floor 1 → plugin paths to passage without user input
- Plugin detours around known traps (verified by disabling collision and walking the planned path visually)
- Plugin refuses paths through mob aggro cones unless explicitly commanded
- After forced combat: plugin resumes pathing correctly
- 10 consecutive successful PotD floor-1 traversals from spawn to passage (manual combat)

---

### M3 — Combat (1–2 weeks)

**Goal:** the plugin handles combat end-to-end using RS/Wrath for rotation, drives its own defensives, and auto-uses DD consumables at the right triggers.

**Branch:** `feat/m3-combat`

**Tasks**
- `Managers/CombatDriver.cs` — on aggro detected: enable RS or Wrath IPC; drive own defensive CDs on HP thresholds
- DD consumables table (populate during this milestone from research): Sustaining Potion, Elixir Field, Burst of Inspiration, etc. with item IDs + trigger conditions
- `Helpers/CombatHelper.cs` — HP%, MP%, aggro-count from `FloorState`
- Pre-pull setup: target lowest-HP mob, Arm's Length if multi-pull
- Post-combat cleanup: disable rotation IPC, return to Executing state

**Exit criteria**
- PotD floors 1-10 solo clear, zero deaths over 5 consecutive runs
- Defensive CDs fire at expected thresholds (log verified)
- Sustaining Potion auto-uses when HP < 40%, no false positives

---

### M4 — Pomanders + Panic state (1 week)

**Goal:** the plugin uses pomanders strategically by the priority table, and the Panic state overrides the planner when HP is critical or the player is surrounded.

**Branch:** `feat/m4-pomanders-panic`

**Tasks**
- `Managers/PomanderPolicy.cs` — priority table from plan; keyed on (floor range, HP%, MP%, slot state)
- `Helpers/PomanderHelper.cs` — read pomander inventory slot state from game memory
- Panic state: HP < emergency threshold OR aggro count > 3 → override planner with defensive move (kite to doorway, pop emergency pomander, fire Raising if death imminent + configured)
- Conservative mode: pops defensive pomanders one floor early

**Exit criteria**
- PotD floors 1-30 clear rate ≥ 90% over 10 consecutive runs
- Pomanders are consumed only at correct triggers (log audit)
- Panic state fires on synthetic low-HP events; plugin recovers correctly

---

### M5 — Floor bosses 10 / 20 / 30 / 40 / 50 (1–2 weeks)

**Goal:** each named boss on floors 10, 20, 30, 40, 50 of PotD has a scripted handler that delegates to BossMod where supported and overrides gaps.

**Branch:** `feat/m5-bosses`

**Tasks**
- `Bosses/IBossHandler.cs` — common interface (`Enter`, `OnCombatTick`, `Exit`)
- Per-boss handlers:
  - `PalaceDeathgaze.cs` (floor 10)
  - `EddaPureheart.cs` (floor 20)
  - `NybethObdilord.cs` (floor 30)
  - `Arioch.cs` (floor 40)
  - `ThePalaceOfTheDead.cs` (floor 50 — final boss)
- Each handler first checks `BossModIPC.HasModuleByDataId(bossId)`; if yes, delegate; else fall back to scripted response
- Pre-boss setup hook: PomanderPolicy's pre-boss triggers (e.g., Strength)

**Exit criteria**
- 50 consecutive floor-50 clears
- Each boss's mechanic-dodging visually verified (no preventable death on 20 consecutive encounters per boss)

---

### M6 — Hoard + Magicite (1 week)

**Goal:** Accursed Hoard collection works end-to-end when enabled; magicite is used correctly on required bosses.

**Branch:** `feat/m6-hoard-magicite`

**Tasks**
- `Managers/HoardHandler.cs` — when setting enabled: on floor start, check PalacePal data for hoard candidates; use Pomander of Intuition; path to hinted tile; `/dig`; defer if enemies within 5y
- `Managers/MagiciteHandler.cs` — track inventory; use rules by config (bosses only / emergency / never)
- Wire HoardHandler into the planner as a goal alongside the passage (goal-switch when hoard is accessible safely)

**Exit criteria**
- 5 hoard-available floors handled correctly (dug + looted, or deferred correctly if unsafe)
- 3 magicite-mandatory bosses (e.g., PotD 171 Owain — post-MVP, sim in earlier floors) beaten correctly
- User-setting changes take effect immediately without reload

---

### M7 — MVP ship (PotD 1-50)

**Goal:** end-to-end autopilot for PotD 1-50 meets the ship bar.

**Branch:** `release/mvp` (merge all M0-M6 PRs, final polish)

**Tasks**
- Full-run integration testing
- Polish death logs / run logs
- Debug-mode off by default
- Written user guide (README update + screenshots)
- Tag release `v0.1.0-mvp`

**Exit criteria**
- **≥ 90% clear rate over 20 consecutive PotD 1-50 runs** from queue to floor-50 clear
- Average time-per-floor reasonable (< 3 min on floors 1-30, < 5 min on 31-50)
- No unrecoverable failures (plugin self-stops correctly on unexpected state)
- Death logs capture enough info to diagnose any failure post-hoc

---

### Post-MVP roadmap

#### M8 — PotD 51-100 (2–3 weeks)
- New mob DataIDs for 51-200 range; more mimics
- Magicite floors (every 10 from 51)
- Floor bosses 60 / 70 / 80 / 90 / 100 scripted
- Tighter aggro pulls; dangerous-floor pomander rules

#### M9 — PotD 101-200 (3–4 weeks)
- Additional boss scripts
- Tuning for Trials of the Braves (if relevant to solo runs)
- Final boss floor 200

#### M10 — Heaven-on-High 1-100 port (3 weeks)
- HoH-specific DataIDs (passage `2009507`, mimic BNpcs `9042-9051`)
- HoH floor bosses
- HoH pomander pool differences

#### M11 — Eureka Orthos 1-100 port (3 weeks)
- Orthos DataIDs (passage `2013287`, mimic BNpcs `15996-16005`)
- Orthos floor bosses
- Orthos-specific action/ability set

#### M12 — Pilgrim Traverse (TBD)
- Scope determined after prior ports stabilize
- DataID research pass first

## Open risks & required verifications

### Pre-code items (1 resolved)
1. ~~vnavmesh cache-key behavior on DD floor transitions.~~ **Resolved 2026-04-22.** Tested empirically on PotD floors 1-3: cache key is constant across all 10 floors of a territory range, but pathing via `/vnav moveto` works correctly end-to-end on each floor regardless. The cached mesh is compatible across the range at vnavmesh's resolution. No rebuild trigger needed. Known quirk: occasional sticking at sharp corners, handled by vnavmesh's built-in stuck recovery + our Executor timeout + Panic-layer escape pomander.
2. **PalacePal SQLite DB path + schema version.** Confirm exact path; pin migration version in manifest.
3. **RotationSolver / Wrath behavior under DD action sync.** Verify neither queues abilities unavailable due to level-sync.

### Architecture risks
4. **Aggro data fidelity.** Lumina columns may be incomplete for DD mobs → planner over-avoids (slow) or under-avoids (dies). Fallback: hardcoded radius table per floor range.
5. **Patroller re-plan churn.** Mitigated by 20% cost-improvement threshold before path swap.
6. **Mimic-chest timing.** If opening a coffer reveals a mimic, response must come within one GCD. Classify `2006020` as dangerous pre-open.
7. **Floor-boss gaps.** Some bosses not in BossMod, or regressions in coverage. Each needs a fallback script.

### Operational risks
8. **PalacePal schema migrations** invalidate our readers — pin version + detect-and-disable.
9. **FFXIV ToS.** Automation violates ToS; users accept risk explicitly. Humanized action timing (400–1200ms log-normal jitter) to reduce obvious signatures.
10. **Dalamud API churn** — routine.

## Distribution & safety

- Custom repo only.
- ToS-risk modal: first install + on master-toggle enable.
- Kill-switch hotkey (default `Ctrl+Shift+Pause`): halts automation instantly.
- Humanized action timing: 400–1200ms jitter per action, log-normal distribution.
- Automation off by default on every plugin load.

## Appendix — open research (non-blocking)

Resolved during implementation:

- Exact Lumina column for BNpc aggro radius.
- Full DD-consumable table (Sustaining Potion, Elixir Field, Burst of Inspiration, etc.) with item IDs and trigger conditions.
- Full PotD 1–100 floor-boss list with BossMod coverage status.
- Whether Pomander of Rage / Fortune / Witching should be promoted from "never auto-use" in v1.1.
- Pilgrim Traverse specifics (user-confirmed scope; no immediate impact on MVP).
