# Localization

Forgebound ships English (`en`) and Vietnamese (`vi`). All player-facing text is looked up by key at runtime;
tooltips for game content are generated from content data by `TooltipBuilder`
(`Assets/Forgebound/Core/Localization/Loc.cs`), so the string tables hold names, lore and sentence
*templates*, never hand-written stat claims.

## Files

| File | Contents |
| --- | --- |
| `Assets/Forgebound/Resources/Localization/<lang>.json` | Content names, lore, tooltip templates, enum names, insights, event texts. |
| `Assets/Forgebound/Resources/Localization/ui_<lang>.json` | Screen / HUD / menu strings used by Presentation code. Loaded after `<lang>.json`; a key defined in both resolves to the `ui_` value. |
| `tools/content/loc_events_<lang>.json` | Source for event titles, bodies and options; merged into `<lang>.json`. |

Format (UTF-8, keys sorted alphabetically):

```json
{ "language": "en", "strings": { "frame.sword.name": "Tempered Longsword", "...": "..." } }
```

## Key families

| Family | Example | Notes |
| --- | --- | --- |
| `<category>.<id>.name` | `modifier.split.name` | category ∈ frame, core, modifier, rune, piece, legendary, forgemaster, enemy, blessing, biome, challenge. The name should hint at the mechanic. |
| `legendary.<id>.lore` | | 1–2 sentences of in-world lore that hint at the signature. |
| `forgemaster.<id>.desc` | | One sentence: playstyle plus a summary of the perks. |
| `challenge.<id>.desc` | | The trial's rules in one sentence. |
| `event.<id>.title / .body / .optN` | | Merged from `tools/content/loc_events_*.json`. |
| `tip.*` | `tip.frame.stats` | Tooltip line templates (see the placeholder table below). |
| `effect.<EffectType>` | `effect.Nova` | Rune effect phrases, written as a lowercase verb phrase because they follow `Trigger: `. `effect.CooldownReduceFlat` is a variant the builder also uses. |
| `op.<ModOp>` | `op.PierceAdd` | Modifier/legendary operation lines. |
| `stat.<StatType>` | `stat.AttackSpeed` | Stat display names; the value and sign come from `tip.stat`. |
| `trigger.<TriggerType>` | `trigger.OnHit` | Starts a rune line (`On hit`) and is used in `tip.relay.charge`. |
| `cond.*` | `cond.everyN` | Rune conditions. Each one is appended after a space, so it must not start with punctuation. |
| `target.<EffectTarget>` | `target.Nearest` | Noun phrases used inside effects ("the nearest enemy"). |
| `status.*`, `damage.*`, `stance.*`, `step.*`, `stage.0-5`, `conflict.*` | | Enum display names (lowercase enum names). |
| `relay.<RelayType>.desc` | `relay.Echo.desc` | One line on what the relay does. |
| `signature.<LegendarySignature>` | `signature.BladeStorm` | Describes exactly what Battle.Hero.cs / Battle.Effects.cs do for the signature. |
| `ability.<EnemyAbilityType>` | `ability.Slam` | Written as `Name (short description)` because phase lines join several with `, `. |
| `tag.<tag>` | `tag.projectile` | Display names for weapon tags (used by `tip.requires` and tagged stats). |
| `insight.*` | `insight.weapon_low_share` | Post-battle insights. The record is `key|arg0|arg1...`; frame, rune and enemy ids are turned into names. |
| `phase.<boss>.<n>` | `phase.lich.2` | Boss phase banners. |
| `ui.*`, `battle.*`, `slot.*`, `error.*`, `hint.*`, ... | | UI strings, mostly in `ui_<lang>.json`. |

## Placeholders

* Placeholders are `{0}`, `{1}`, … and are filled by `Loc.Format`. Index `n` is always the n-th argument the
  code passes, so a translation may reorder them (for example `ui.weapon.name_core`) but must keep the
  same set. `loc-check` reports any key whose placeholder set differs from English.
* A template can leave out an argument it does not need (for example `effect.Summon` ignores the target).
  It must then do so in every language.
* Values arrive already formatted:
  * percentages already include `%` (`35%`, `-4%`). Do not add another `%`, and do not hard-code a `+`
    in front of a value that can be negative (`op.CritChanceAdd`).
  * seconds are bare numbers. Add the unit yourself: `s` in English (`{0}s`), `giây` in Vietnamese
    (`{0} giây`).
  * distances are world units (`1.5`). Write "units" / "đơn vị".
  * The game formats numbers with the invariant culture, so the decimal separator is always `.`.
    Use `.` in hand-written numbers as well, Vietnamese included, so every number in a tooltip looks the same.
* Most important argument lists:

| Key | Arguments |
| --- | --- |
| `tip.frame.stats` | 0 base damage, 1 cooldown (s), 2 range, 3 stance |
| `tip.step` | 0 step number, 1 step kind, 2 damage % |
| `tip.applies` | 0 chance, 1 stacks, 2 status, 3 duration (s) |
| `tip.frame.slots` | 0 modifier slots, 1 rune slots, 2 board cells |
| `tip.relay.charge` | 0 trigger phrase, 1 threshold, 2 power % |
| `tip.support.adjacent` | 0 stat line, 1 max adjacent weapons |
| `tip.legendary.recipe` | 0 parts joined by ` + `, 1 min tier, 2 Essence |
| `tip.enemy.base` | 0 HP, 1 armor, 2 damage, 3 melee/ranged word |
| `tip.enemy.phase` | 0 phase number, 1 HP threshold %, 2 ability list |
| `effect.ApplyStatus` / `effect.Mark` | 0 stacks, 1 status, 2 duration, 3 target |
| `effect.Nova` | 0 amount, 1 damage type, 2 radius |
| `effect.Chain` | 0 amount, 1 damage type, 2 bounce count |
| `effect.Projectile` | 0 count, 1 amount, 2 target |
| `effect.Damage` | 0 amount, 1 damage type, 2 target |
| `op.BonusVsStatus` | 0 percent, 1 status/tag name |
| `signature.*` | 0 the legendary's SignatureValue, or an empty string. Only BladeStorm, Overclock and Gearstorm use it, and for them it is a plain count. |
| `insight.weapon_low_share` | 0 weapon, 1 damage share (number, no `%`) |
| `insight.rune_icd_wasted` | 0 rune, 1 blocked count |
| `insight.link_never_fired` | 0 link index, 1 listen trigger (raw enum name) |
| `insight.top_damage_source` | 0 enemy, 1 damage |

* The builder joins fragments with a single space (step riders, rune conditions, `tip.icd`, `tip.maxPerBattle`,
  `tip.chains`). These fragments must not start or end with spaces or punctuation that needs no space before it.
  `tip.and` is the only template with leading and trailing spaces.

## Fallback behaviour

`Loc.Get(key)` resolves **current language → English → the key itself**. A key missing in Vietnamese shows
the English text. A key missing everywhere shows the raw key. Both cases are recorded in `Loc.MissingKeys`,
so a missing translation is visible but never crashes the game.

## Checking

```sh
cd tools/dotnet/Forgebound.Cli && dotnet build
dotnet run --no-build -- loc-check --show 2000   # missing keys per language + placeholder mismatches
dotnet run --no-build -- tooltips --lang en      # writes reports/loc/tooltips_en.md
dotnet run --no-build -- tooltips --lang vi      # writes reports/loc/tooltips_vi.md
```

`loc-check` requires every key from `ContentValidator.RequiredLocKeys` (content, templates, tags, insights,
events) and every literal key found in Presentation code. Code such as `L("ui.end." + outcome)` registers the
literal prefix (`ui.end.`) as a key, so these prefixes carry a generic fallback value. The concrete keys
(`ui.end.Victory`, …) should be defined as well. Both tooltip commands must end with `unresolved keys: 0`.
After a check passes, read through the tooltip reports for double spaces, missing units and awkward joins.

## Length budget

* Names (`*.name`): **28 characters or fewer recommended** so they fit cards and buttons in both languages.
  Vietnamese runs about 20–30% longer than English, so check both.
* The content validator warns (`loc_overflow`) when a name is **over 90 characters**, which is the hard UI budget.
* Lore and descriptions: one or two sentences.

## Adding a language

1. Add the code to `Loc.Languages` (`Assets/Forgebound/Core/Localization/Loc.cs`) and to the language toggle in
   the settings screen.
2. Copy `en.json` and `ui_en.json` to `<lang>.json` / `ui_<lang>.json`, set `"language"`, and translate the values.
   Keep the keys and placeholders.
3. Translate `tools/content/loc_events_en.json` to `loc_events_<lang>.json` and merge it into `<lang>.json`.
4. Run `loc-check` until it reports 0 missing and 0 placeholder mismatches, then render and proofread
   `tooltips --lang <lang>`.

## Vietnamese glossary

| English | Tiếng Việt |
| --- | --- |
| Frame / Core / Modifier / Rune | Khung / Lõi / Bổ trợ / Cổ tự |
| Forge Board / board piece / relay / support | Bàn rèn / ô bàn rèn / rơle / ô hỗ trợ |
| Essence / Legendary / Forgemaster | Tinh chất / Huyền thoại / Thợ cả |
| proc | hiệu ứng phụ |
| cooldown | hồi chiêu |
| crit | chí mạng |
| stagger | loạng choạng |
| elite / boss | tinh anh / trùm |
| familiar | linh thú |
| Blessing / Curse | Phước lành / Lời nguyền |
