# Combat rules (as implemented)

* **Tick**: 30 Hz. Order per tick: waves → statuses → hero (movement + weapons) → familiars → enemies → projectiles → zones → scheduled
  (repeats, delayed links) → rune timers → legendary signatures → trigger queue → cleanup/analytics → end check.
* **Movement**: the primary weapon's stance decides spacing (melee closes to range, ranged/mid kite with hysteresis); secondaries
  attack independently from hovering rigs; the hero steps out of telegraphed enemy slams after an 8-tick reaction delay.
* **Targeting**: Nearest / LowestHp / HighestThreat / Cluster / Marked / Farthest; ties → distance, then unit id.
* **Damage pipeline**: evasion → crit (Mark +20%) → conditional bonuses (focus, vs-status, shatter, execute, flyers 70% for non-reach
  melee) → Shock (+15%/stack, consumed) → Curse (+6%/stack) → Fortify (−25%) → armor (`dmg·100/(100+armor−break·5−pen)`) for
  physical or resist (clamped −50..90%) → shield → HP → events (OnHit/OnCrit/OnDamageTaken/OnBlock/OnShieldBreak/OnLowHp) → death/OnKill.
* **Statuses**: Burn (10 stacks), Poison (20), Bleed (5) DoTs every 0.5 s on enemies, every 1 s and max 3 stacks on the hero;
  Chill 5 stacks → Freeze (45 ticks, then 60-tick immunity); bosses take 1/3 duration hard CC.
* **Leech** only from non-proc direct hits, capped per second (tuning `leechCapPerSecond`, default 4% max HP).
* **Proc safety**: self-trigger guard; proc events only trigger runes with `allowFromProc` while depth < 3; ≤48 activations per tick;
  projectile (160) and zone (40) budgets; board links charge only from non-proc events → no recirculation. All suppressions are counted.
* **Board links**: see docs/DECISIONS.md D-006. Inactive links (capacity, no destination) are never compiled, so disconnected
  circuits can't apply effects (test `DisconnectedRelayNeverApplies_ConnectedRelayAltersAttacks`).
* **Soft limit**: after an encounter's `softLimit` enemies gain +10% damage every 10 s; hard cap 240 s = timeout loss.
