# Content schema (Resources/Content/*.json)

Every file: `{"version": 1, "items": [...]}`; `//` line comments allowed. Units: seconds → ticks (×30), multipliers → permille,
world units → milli-units, chances 0..1 → permille. Parsing lives in `Core/Content/ContentDefs.cs`; `ContentValidator` enforces:
unique ids, references, recipe feasibility (tags, conflicts, slots), dead runes (unproducible statuses), clones (identical ops /
behaviour / stat blocks), footprint/port sanity, unbounded summons, impossible encounters, biome coverage per chapter, event
soft-locks, localisation coverage, and proc-graph cycles (warnings).

| File | Key fields |
|---|---|
| frames | stance, range, minRange, baseDamage, cooldown, windup, steps[kind, shape, damage, hits, width, maxTargets, pierce, stagger, armorBreak, shield, pull, status, summon], footprint, ports[cell, dir, kind], modifierSlots, runeSlots |
| cores | damageType, status{status,chance,stacks,duration}, stats[StatMod], runes[RuneDef] |
| modifiers | stage (spawn/split/target/pierce/impact/timing), ops[{op,value,tag}], requiresTags, excludesTags, conflictGroup, slotCost |
| runes | trigger, interval, condition{damageType,statusIs,targetHasStatus,targetHpBelow,heroHpBelow,everyN,allowFromProc,onlyPrimary,targetIsElite}, effects[{type,target,power,flat,fromMaxHp,damageType,status,stacks,duration,radius,count,chance}], icd, maxPerBattle, chance |
| pieces | kind relay/support, footprint, ports, relay{type,listen,threshold,power,capacity,delay,maxPerBattle,convert,status,radius}, adjacent[StatMod], maxAdjacent |
| legendaries | frame, core, requiresModifiers, requiresAnyRunes, minTier, essence, signature, signatureValue, ops, runes |
| forgemasters | maxHp, armor, perks[StatMod], runes, startFrame, startCore, startPieces, startItems, startGold, unlock |
| enemies | rank, hp, armor, shield, evasion, resist{type:0..1}, speed, radius, flying, attack{...}, abilities[{type,cooldown,windup,value,power,radius,enemy,count,cap,status,delay}], phases[{hpBelow,abilities,speed,damage,attackSpeed,name,resist}], gold, threat, role, tags |
| encounters | biome, kind, style, minChapter, maxChapter, weight, softLimit, waves[{delay, spawns[{enemy,count}]}] |
| events | biome, minChapter, weight, options[{text, requireGold/Essence/Hp/Tag, success, outcomes[], fail[]}] |
| blessings | curse, stats[StatMod], runes, rarity, group (anti-duplication), tags (challenge_only) |
| biomes / challenges / tuning | see files; tuning knobs documented in docs/BALANCE.md |
