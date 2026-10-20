# Forgebound art validation

**Verdict: PASS** - 450 files: 446 PASS / 4 WARN / 0 FAIL (0 fail findings, 4 warn findings). Generated PNG total: 9.22 MB.

> Automated checks verify technical correctness (format, alpha, halos, cropping, provenance, budgets, duplicates, legibility heuristics). They do NOT certify aesthetic quality; a human art review against the qualitative checkpoints is required before assets are considered final.

## Findings by check

| check | WARN | FAIL |
|---|---|---|
| contrast | 3 | 0 |
| oversaturation | 1 | 0 |

## FAIL / WARN details (first 80)

- **WARN** `contrast` board/icon/fb_board_conduit_icon_00.png: only 40% of sprite texels contrast with a light background
- **WARN** `contrast` board/icon/fb_board_echo_relay_icon_00.png: only 38% of sprite texels contrast with a light background
- **WARN** `contrast` board/icon/fb_board_splitter_icon_00.png: only 40% of sprite texels contrast with a light background
- **WARN** `oversaturation` weapon/glow/fb_weapon_dagger_solar-leg_glow_00.png: additive glow saturates alpha on 27% of its area

## Qualitative review checkpoints (human)

- [ ] Silhouette: each Frame reads as its weapon class from silhouette alone (fill shapes black and compare).
- [ ] Core identity: each Core is recognisable by gem SHAPE as well as colour (check in greyscale / colour-blind simulation).
- [ ] Value structure: clear light/shadow split, no muddy mid-tone soup; key light from upper-left on everything.
- [ ] Hand-painted feel: painted-noise breakup visible but not dirty; outlines even and not broken.
- [ ] Tier read: tier 3 visibly richer than tier 1 (gold trim, ornaments) without becoming noisy.
- [ ] Legendary read: unique accent silhouette + glow layer feels special yet the Frame is still identifiable.
- [ ] Animation: idle loops without a pop between last and first frame; attack has clear anticipation, impact and recovery.
- [ ] In-context: view sprites over the actual battle backgrounds and UI panels at phone scale (48-64 px icons).
- [ ] Style consistency: weapons, board pieces and characters look like one game (same outline weight, light rig, palette).
- [ ] Originality: no resemblance to existing game IP (shapes, emblems, characters).

Contact sheets and phone-scale previews: `art_validation.html` (and `contact_*.png`).
