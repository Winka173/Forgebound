# Balance process & current measurements

Tools (all use the shipped rules): `forgebound sim` (bot runs, CIs, baseline diff, HTML charts), `forgebound tournament`
(random legal builds vs a fixed gauntlet, component attribution), `forgebound duel` (traced single fight),
`forgebound scenarios` (adversarial invariants). Reports: `reports/balance/`.

**Bots are heuristic; numbers are trends, not proof of fun. Human playtests remain mandatory.**

Proposed target windows (to be confirmed by playtests): Story ≥ 70%, Normal 35–50%, Hard 12–25% for the reference bot;
per-Forgemaster spread within ±15 pp of the mean; no encounter loss rate > 35% except chapter-3 bosses.

Current (16 seeds × 5 policies × 8 Forgemasters, normal): **38.3% (CI 34.6–42.1%)**; Story 80.8%, Hard 16.3% (6 seeds).
Chapter clear rates 93% / 81% / 51%. Tournament (600 tier-2 builds): no component outside ±15 pp.
Open flags: artificer under, warden/summoner over, void core under (small n), storm bosses spike — see KNOWN_ISSUES KI-05.

History of passes (each verified by re-simulation): enemy DoT on hero, flyers vs melee, tuning knobs per chapter, post-battle
repair, service guarantees in maps, bot route look-ahead & spending, shield cap/decay, summon nerfs, frame/forgemaster tweaks.
