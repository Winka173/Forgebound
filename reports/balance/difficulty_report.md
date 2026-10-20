# Balance report: difficulty

Runs: 480, battles: 3952, content hash `86d1d3c46f1a3316`.
Bots are deterministic heuristic agents; these are trends, not proof of fun. Human playtests are still required.

**Overall win rate:** 48.5% (95% CI 44.1%–53.0%) (Δ vs baseline +10.3 pp)

## Win rate by Forgemaster
| key | n | win | 95% CI | Δ baseline |
|---|---:|---:|---|---:|
| alchemist | 60 | 48.3% | 36.2%–60.7% | +10.8 pp |
| artificer | 60 | 38.3% | 27.1%–51.0% | +19.6 pp |
| blacksmith | 60 | 23.3% | 14.4%–35.4% | -5.4 pp |
| duelist | 60 | 51.7% | 39.3%–63.8% | +16.7 pp |
| hexblade | 60 | 63.3% | 50.7%–74.4% | +24.6 pp |
| runecrafter | 60 | 40.0% | 28.6%–52.6% | +13.8 pp |
| summoner | 60 | 56.7% | 44.1%–68.4% | -0.8 pp |
| warden | 60 | 66.7% | 54.1%–77.3% | +2.9 pp |

## Win rate by policy
| key | n | win | 95% CI | Δ baseline |
|---|---:|---:|---|---:|
| Aggressive | 96 | 52.1% | 42.2%–61.8% | +11.5 pp |
| Balanced | 96 | 52.1% | 42.2%–61.8% | +11.5 pp |
| Cautious | 96 | 45.8% | 36.2%–55.8% | +5.2 pp |
| Circuit | 96 | 42.7% | 33.3%–52.7% | +12.2 pp |
| Status | 96 | 50.0% | 40.2%–59.8% | +10.9 pp |

## Win rate by primary frame
| key | n | win | 95% CI | Δ baseline |
|---|---:|---:|---|---:|
| bulwark | 24 | 45.8% | 27.9%–64.9% | +1.4 pp |
| crossbow | 14 | 71.4% | 45.4%–88.3% | -28.6 pp |
| dagger | 152 | 50.7% | 42.8%–58.5% | +16.0 pp |
| hammer | 105 | 41.0% | 32.0%–50.5% | +2.5 pp |
| scythe | 61 | 62.3% | 49.8%–73.4% | +22.6 pp |
| spear | 8 | 25.0% | 7.2%–59.1% | -17.9 pp |
| staff | 29 | 44.8% | 28.4%–62.5% | -1.5 pp |
| sword | 73 | 48.0% | 36.9%–59.2% | +13.8 pp |
| tome | 13 | 23.1% | 8.2%–50.3% | -11.9 pp |
| whip | 1 | 100.0% | 20.7%–100.0% |  |

## Win rate by primary core
| key | n | win | 95% CI | Δ baseline |
|---|---:|---:|---|---:|
| - | 1 | 100.0% | 20.7%–100.0% | +66.7 pp |
| arcane | 66 | 53.0% | 41.2%–64.6% | +7.3 pp |
| earth | 86 | 30.2% | 21.5%–40.6% | -5.5 pp |
| fire | 36 | 47.2% | 32.0%–63.0% | +12.3 pp |
| frost | 13 | 61.5% | 35.5%–82.3% | +4.4 pp |
| life | 28 | 75.0% | 56.6%–87.3% | +12.5 pp |
| lightning | 62 | 41.9% | 30.5%–54.3% | +6.2 pp |
| shadow | 40 | 62.5% | 47.0%–75.8% | +26.5 pp |
| solar | 10 | 60.0% | 31.3%–83.2% | +15.6 pp |
| steel | 60 | 51.7% | 39.3%–63.8% | +14.5 pp |
| venom | 52 | 48.1% | 35.1%–61.3% | +12.6 pp |
| void | 10 | 30.0% | 10.8%–60.3% | +23.3 pp |
| wind | 16 | 56.3% | 33.2%–76.9% | -10.4 pp |

## Chapter progression
- chapter1: reached 480, clear rate 95.4%
- chapter2: reached 458, clear rate 75.3%
- chapter3: reached 345, clear rate 67.5%

## Hardest encounters (loss rate, n ≥ 5)
| encounter | n | loss | median s | p90 s | median HP loss % |
|---|---:|---:|---:|---:|---:|
| storm_boss_colossus | 147 | 27.9% | 40.1 | 62.4 | 28 |
| frost_boss_wyrm | 206 | 25.7% | 28.8 | 41.1 | 40 |
| storm_boss_tyrant | 170 | 25.3% | 29 | 47.5 | 47.7 |
| storm_drone_swarm | 136 | 16.9% | 12.7 | 18.5 | 41 |
| crypt_bat_swarm | 64 | 14.1% | 22.8 | 40 | 40.9 |
| ember_wisp_flock | 80 | 12.5% | 10.6 | 80.7 | 1.8 |
| crypt_ghoul_pack | 214 | 11.2% | 7.6 | 23.5 | 5.5 |
| crypt_boss_lich | 137 | 8.0% | 22.9 | 53.3 | 15 |
| verdant_treant | 100 | 8.0% | 23.3 | 67 | 16.2 |
| frost_sprite_court | 137 | 6.6% | 12.2 | 23.6 | 19 |

## Weapon DPS by frame (median per battle)
- bow: 12.72 (p90 25.5, n 996), per cell 4.24
- bulwark: 7.24 (p90 17.45, n 661), per cell 1.81
- crossbow: 28.5 (p90 59.29, n 178), per cell 7.12
- dagger: 17.1 (p90 34.63, n 1206), per cell 8.55
- greatsword: 12.46 (p90 15.7, n 34), per cell 2.49
- hammer: 18.83 (p90 39.4, n 778), per cell 3.77
- scythe: 16.65 (p90 37.77, n 1093), per cell 4.16
- spear: 12.17 (p90 29.65, n 89), per cell 3.04
- staff: 16.92 (p90 35.21, n 806), per cell 4.23
- sword: 18.18 (p90 35.1, n 593), per cell 6.06
- tome: 13.22 (p90 25.93, n 721), per cell 6.61
- whip: 18.79 (p90 21.85, n 12), per cell 6.26

## Economy
- shopDeadOfferRate: 0.0725
- avgRerollsPerRun: 0.39
- avgGoldSpent: 171.2
- avgGoldUnspentAtEnd: 99.3
- avgItemsBought: 3.47
- runsWithLegendary: 11
- medianFirstLegendaryStep: 9
## Sustain
- healingPctOfDamageTaken: 10.5
- shieldPctOfDamageTaken: 46.5
## Diversity
- uniquePrimaryBuilds: 29
- simpsonIndex: 0.9241
- mostCommon: "dagger+steel"
- mostCommonShare: 0.125
## Procs & simulation performance
- procs.maxPerTickMedian: 2
- procs.maxPerTickP90: 4
- procs.maxPerTickMax: 13
- procs.maxChainDepth: 3
- procs.suppressedTotal: 123
- performance.battleSimMsMedian: 2.29
- performance.battleSimMsP90: 5.48
- performance.peakProjectilesP90: 4
- performance.peakProjectilesMax: 12
- performance.note: "Headless desktop CPU timing of the simulation only; not a device frame-rate measurement."
## Runes never triggered while equipped (≥3 battles)
breaker_rune, conductor, deep_freeze, shard_burst, toxin_burst
## Runes never equipped by bots
echo_glyph, star_shard, arcane_pulse, pack_leader
## Legendary outcomes
- (none): n 469, win 47.6%
- bloodmoon_scythe: n 3, win 100.0%
- earthshaker_bulwark: n 6, win 100.0%
- runeblade: n 1, win 0.0%
- worldbreaker: n 1, win 100.0%
## Flags
- underperformer: forgemaster blacksmith win 0.2333 (CI high 0.3544) vs overall 0.485
- overperformer: forgemaster warden win 0.6667 (CI low 0.5406) vs overall 0.485
- underperformer: core earth win 0.3023 (CI high 0.4061) vs overall 0.485
- overperformer: core life win 0.75 (CI low 0.5664) vs overall 0.485
