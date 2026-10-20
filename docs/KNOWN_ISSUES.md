# Known issues & open gaps (truthful status)

Severity: **B** = blocks release, **M** = major, **m** = minor.

| ID | Sev | Issue | Evidence / next action |
|---|---|---|---|
| KI-01 | **B** | **Not verified inside Unity.** No Unity Editor/licence in the build environment. Presentation code (≈4k lines) compiles only against hand-written API stubs (`tools/dotnet/PresentationCheck`); Editor scripts (`Assets/Forgebound/Editor/**`) were not compiled at all. | Open the project in Unity 2022.3 LTS, fix compile/runtime errors, run EditMode tests via Test Runner, play a full run, capture screenshots. |
| KI-02 | **B** | **No player builds produced.** Windows/Android/iOS builds require Unity; build methods exist (`BuildScript`) but were never executed. iOS needs macOS + Xcode signing. | Run the batch commands in docs/BUILD.md on a machine with Unity + modules. |
| KI-03 | M | Built-in render pipeline instead of URP 2D (D-010). Blade trails / particles are pooled sprite VFX, no Shader Graph. | Create URP 2D asset in editor, port VFX to particle systems with the same budget setting. |
| KI-04 | M | Art batch 1: 450 sprites (weapons 12×12, 6 board icons, blacksmith, 4 enemy archetypes). Batch 2 (all forgemasters, all pieces, item icons, per-enemy/boss art, biome backdrops) — see docs/PROGRESS.md for its current status. Remaining gaps fall back to archetype sprites or procedural primitives tracked in `SpriteLibrary.Missing`. | Human aesthetic review of contact sheets in `reports/art/`; never treat fallbacks as final. |
| KI-05 | M | Balance (bots, normal difficulty, 3200 runs): 38.2% win rate (95% CI 35.8–40.6%); forgemaster spread 27% (runecrafter, blacksmith) – 54.5% (warden). Storm Citadel bosses remain spikes (~38% of losses each). Crossbow frame and fire/life cores flagged as overperformers. | Continue passes with `forgebound sim` + tournament; human playtests required — bot results are trends only. |
| KI-06 | M | Audio is procedurally synthesised (67 clips). Functional, rate-limited, provenance-clean, but not production-mixed. Biome ambience clips are not produced. | Sound designer pass; listen-through checklist in docs/AUDIO.md. |
| KI-07 | M | Performance numbers are headless CPU simulation timings (median 2.9 ms per *whole battle*). No device FPS profiling was possible (no device attached). | Profile on a mid-range Android phone; validate 60/30 FPS targets and the particle budget setting. |
| KI-08 | m | Localisation complete for EN/VI keys (0 missing), but UI overflow was only budget-checked (name length), not visually tested at all text scales. | Visual pass at 80–150% text scale on phone resolutions. |
| KI-09 | m | Onboarding is contextual hints only; no scripted first-run tutorial encounter. | Add guided tutorial trial using the Trials system. |
| KI-10 | m | `Taunt` enemy ability is declared but unused/unimplemented. | Implement or remove from enum. |
| KI-11 | m | Mid-battle save is not supported: suspend/quit during a battle resumes at the pre-battle state (battle re-simulates identically from the seed). | Acceptable by spec ("if practical"); documented in UI. |
| KI-12 | m | Colour-blind modes are stored and every status has a glyph, but palette swapping per mode is not implemented. | Implement palette remap in `Theme`. |
| KI-13 | m | Endless mode not implemented (spec: optional, only if validated). | Post-release. |
