# Architecture

```
Assets/Forgebound/
  Core/                 engine-free C# (asmdef noEngineReferences) — compiled by Unity AND tools/dotnet
    Simulation/         DetRandom (xoshiro128**), FMath (int helpers)
    Content/            JSON DOM, defs, ContentDatabase, TuningDef, ContentValidator + ProcGraphAnalyzer
    Weapons/            WeaponInstance, AssemblyRules, LegendaryRules, WeaponCompiler → WeaponSpec
    ForgeBoard/         BoardState, BoardEditor (undo), BoardGeometry, BoardAnalyzer (ports, links, capacity, adjacency)
    Combat/             Battle (fixed tick), units, projectiles, zones, statuses, damage pipeline, runes/links, enemy AI, report
    Run/                RunState (serialisable), MapGenerator, RunController (all mutations, action log, replay), BattleBuilder
    Economy/            pricing, shop generation (pity + build memory)
    Progression/        Profile, Workshop session, Trials, LoadoutProbe (measured previews), BlueprintCodec
    Save/               SaveSystem (checksum, tmp→bak→main atomic swap, migrations)
    Localization/       Loc tables, TooltipBuilder (descriptions generated from data)
    Balance/            BotAgent, BalanceSimulator, BuildTournament, Scenarios
  Presentation/         MonoBehaviours + runtime-built uGUI (GameRoot, UiRoot, screens, BoardWidget, BattleView, VFX, audio, sprites)
  Editor/               build script, validation menu, art importer
  Resources/            Content/*.json, Localization/*.json, Art/Generated/**, Audio/*.wav
  Tests/EditMode/       NUnit tests (Unity Test Runner + `dotnet test`)
tools/dotnet/           Forgebound.Core (same sources), Forgebound.Tests, Forgebound.Cli (`forgebound`), PresentationCheck (stub compile)
tools/art, ArtSource/   Blender pipeline, atlas packer, art validator
tools/audio/            synthesiser + validator
tools/content/          authoring scripts (events, UI strings)
```

Data flow: content JSON → `ContentDatabase` → `RunController` (validated actions, logged) → `BattleBuilder` compiles an immutable
`BattleSetup` (weapons → `WeaponSpec`, board → `CompiledLink`s) → `Battle.Step()` emits `CombatEvent`s → `BattleView` renders,
`BattleReport` aggregates analytics. The view never writes to the simulation; 2x speed = two steps per frame (tested invariant).
