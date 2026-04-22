# AutoDeepDungeon

A Dalamud plugin that auto-runs FFXIV Deep Dungeons solo — queue.

Pre-alpha.

## Scope

| Target                          | Status      |
|---------------------------------|-------------|
| Palace of the Dead 1–50 (MVP)   | In progress |
| PotD 51–200 / HoH / Orthos      | Planned     |
| Pilgrim Traverse                | TBD         |

Delegates to: [vnavmesh](https://github.com/awgil/ffxiv_navmesh) (pathing),
[RotationSolver](https://github.com/ArchiDog1998/RotationSolver) or
[WrathCombo](https://github.com/PunishXIV/WrathCombo) (rotation),
[BossMod](https://github.com/awgil/ffxiv_bossmod) (boss mechanics),
[PalacePal](https://github.com/carvelli/PalacePal) (trap + hoard data).

## Install

Custom Dalamud repo only. URL will be published once the first release is tagged.

## Commands

| Command       | Effect                        |
|---------------|-------------------------------|
| `/adg`        | Open config window            |
| `/adg start`  | Begin autopilot               |
| `/adg stop`   | Halt automation               |
| `/adg status` | Dump state to chat            |

Kill-switch hotkey: `Ctrl+Shift+Pause`.

## Building

```bash
git clone --recurse-submodules https://github.com/XeldarAlz/FFXIV-AutoDeepDungeon
```

Open `AutoDeepDungeon.sln` in Visual Studio 2022 or Rider and build. Requires .NET 10 SDK and a working XIVLauncher + Dalamud install.

## Contributing

[Issues](https://github.com/XeldarAlz/FFXIV-AutoDeepDungeon/issues) for bugs and feature requests.
[Discussions](https://github.com/XeldarAlz/FFXIV-AutoDeepDungeon/discussions) for ideas and questions.

## License

See [LICENSE.md](LICENSE.md).
