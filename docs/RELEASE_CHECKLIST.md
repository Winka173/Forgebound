# Release checklist

Legend: ✅ verified with evidence · 🟡 implemented, not verified on target · ❌ not done

| Item | Status | Evidence / blocker |
|---|---|---|
| Content targets (8/12/12/40+/50+/30+/40+/40+/10+/5/40+/100+/30+ curses) | ✅ | `dotnet test` FullReleaseCountTargetsTracked; `forgebound validate` 0 errors |
| Every legendary works in combat | ✅ | 42 `legendary_*` scenarios PASS |
| Deterministic replays / save-load mid-run | ✅ | RunTests (replay hash, save→load→continue) |
| Proc loop safety / adversarial scenarios | ✅ | 54/54 scenarios PASS |
| Balance harness & reports | ✅ | reports/balance (sim, tournament, difficulty) |
| Art pipeline reruns + validator | ✅ | reports/art; pytest 23 passed |
| Golden art benchmark reviewed at target scale | ❌ | needs human review (KI-04) |
| Audio provenance-clean + validator | ✅ | reports/audio 0 FAIL |
| EN/VI localisation coverage | ✅ | loc-check 0 missing |
| Launch / onboarding / full 3-chapter run in Unity | ❌ | KI-01 (no Unity here) |
| Collection trial in Unity | ❌ | KI-01 |
| Suspend/resume on device | 🟡 | OnApplicationPause autosave implemented; untested on device |
| UI scaling / overflow on phones | 🟡 | safe area + scaler implemented; KI-08 |
| Performance 60 FPS mid-range phone | ❌ | KI-07 |
| Crash-free soak | 🟡 | 30 000-tick headless soak test passes; no device soak |
| Windows build | ❌ | KI-02 |
| Android AAB (signed) | ❌ | KI-02 |
| iOS Xcode export | ❌ | KI-02 (needs macOS) |
| Copyright / provenance review | 🟡 | art + audio manifests with licence + sha256; human legal review of the name "Forgebound" pending |
| Store privacy compliance | 🟡 | offline-only, no data collection; store forms not prepared |
| Controller support | ❌ | not in scope yet |
| Accessibility (text scale, reduced flashes, shake, speed, glyph statuses) | 🟡 | implemented; colour-blind palettes KI-12 |
