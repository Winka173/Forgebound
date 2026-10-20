# Forgebound

Weapon-engineering roguelike (working title): assemble weapons from **Frame + Core + Modifiers + Runes**, wire them on a
**Forge Board** with relays and supports, watch them fight in **side-view auto battles**, and archive blueprints in the
**Collection Workshop**. Unity 2022.3 LTS (Windows / Android / iOS targets), offline single-player.

* Status, evidence and gaps: [docs/PROGRESS.md](docs/PROGRESS.md) (Vietnamese), [docs/KNOWN_ISSUES.md](docs/KNOWN_ISSUES.md),
  [docs/RELEASE_CHECKLIST.md](docs/RELEASE_CHECKLIST.md).
* Architecture & rules: [ARCHITECTURE](docs/ARCHITECTURE.md), [COMBAT_RULES](docs/COMBAT_RULES.md), [ECONOMY](docs/ECONOMY.md),
  [CONTENT_SCHEMA](docs/CONTENT_SCHEMA.md), [BALANCE](docs/BALANCE.md), [DECISIONS](docs/DECISIONS.md).
* Pipelines: [ART_BIBLE](docs/ART_BIBLE.md), [ART_PIPELINE](docs/ART_PIPELINE.md), [AUDIO](docs/AUDIO.md), [LOCALIZATION](docs/LOCALIZATION.md).
* Commands: [docs/BUILD.md](docs/BUILD.md). Quick start without Unity:

```
cd tools/dotnet/Forgebound.Tests && dotnet test
cd ../Forgebound.Cli && dotnet run -- play --fm runecrafter --seed 7      # a bot plays a full run on the real rules
```

**Important:** the Unity layer has not yet been opened in the Unity Editor (none available in the build environment);
see KI-01.
