# Forgebound Art Pipeline

A reproducible procedural pipeline: **Blender (headless, Cycles CPU) → post-process (Python + Pillow + numpy) → atlas packer → validator → Unity importer**. See `docs/ART_BIBLE.md` for the style rules this pipeline enforces.

## Requirements

- Blender 4.0.x on `PATH` (or set `BLENDER=/path/to/blender`). Tested with **Blender 4.0.2**, headless, `--factory-startup`.
  - This build has **no OpenImageDenoise** and **EEVEE fails without a display** (no libEGL). The pipeline therefore uses **Cycles CPU** with an emission-only NPR shader. Samples (8 per pixel at 2× supersampling, so 32 effective) only anti-alias; there is no lighting noise, so no denoiser is needed.
- Python ≥ 3.10 with `Pillow` and `numpy`; `pytest` for the tests (`pip install pillow numpy pytest`).
- 4 CPU cores were used for the timings below.

## Commands

```bash
# 1. Render + post-process everything (Assets/Forgebound/Resources/Art/Generated/** + manifest.json)
python3 ArtSource/blender/build_all.py                      # full build, 4 Blender workers
python3 ArtSource/blender/build_all.py --scope smoke        # 46 files, ~30 s sanity run
python3 ArtSource/blender/build_all.py --only weapon        # rebuild categories: weapon board item char enemy env (manifest is merged)
python3 ArtSource/blender/build_all.py --only enemy --match 'slag|cinder'   # re-render only matching jobs, keep all other outputs
python3 ArtSource/blender/build_all.py --post-only          # redo post step from cached raw renders

# 2. Pack atlases (Generated/atlases/fb_atlas_<group>.png + .json)
python3 tools/art/atlas_packer.py                           # every group starting with weapon_ingame, board_icon, char_, enemy

# 3. Validate (exit 0 = no FAIL, 1 = FAIL, 2 = usage error)
python3 tools/art/art_validator.py                          # reports/art/art_validation.{json,csv,html,md}

# 4. Tests for the validator and packer
python3 -m pytest tools/art/tests -q
```

In Unity, use **Forgebound ▸ Art ▸ Run Art Validator / Open Validation Report / Reimport Generated Art**. The menu runs `python3` by default; set the EditorPrefs key `Forgebound.Art.PythonExe` to override.

## Layout

| Path | What |
|---|---|
| `ArtSource/blender/build_all.py` | CLI: job list → parallel Blender workers → post-process → manifest |
| `ArtSource/blender/forgebound_art/spec.py` | Single source of truth: ids, sizes, cameras, PPU, light rig, naming, seeds |
| `…/palette.py` | Core palettes and material swatches (sRGB hex) |
| `…/materials.py` | `FB_PaintedToon` NPR node group and the controlled material library |
| `…/geometry.py` | bmesh builders (lathe, tube, blade, slab, gear, …), tagging (`extra`, `glow`) |
| `…/weapons.py` | 12 Frame builders, Core gems and inlays, tier details, legendary crowns |
| `…/board.py` | All 36 Forge Board pieces (relay plates with port channels, support tools) |
| `…/characters.py` | Armatures, rigid binding, Actions (blacksmith + legacy enemy archetypes) |
| `…/forgemasters.py` | Batch 2: the 7 other Forgemasters, held real Frames, attack templates |
| `…/content.py` | Reads `Resources/Content/*.json` (ids, silhouette, stage, trigger, stats, factions, palettes) |
| `…/symbols.py` | 2D symbol vocabulary, modifier motifs, blessing design rules, rune glyph generator (pure python) |
| `…/icons.py` | Core / Modifier / Rune / Blessing icons (carrier + relief) |
| `…/enemy_recipes.py` + `…/enemykit.py` | Enemy kit: faction kits × role archetypes × parts; canvas fitting |
| `…/environments.py` | `fb_env_<biome>_bg` 1920×1080 layered backgrounds |
| `…/scene.py` | Render settings, colour management, cameras, inverted-hull outlines |
| `…/worker.py` | Blender entry point (renders jobs to `ArtSource/renders_cache/raw`) |
| `…/postprocess.py` | Premultiplied downsample, colour bleed, glow layer, PNG write, sha256 |
| `ArtSource/renders_cache/` | Raw 2× renders and worker logs (git-ignored via `ArtSource/.gitignore`, about 23 MB) |
| `Assets/Forgebound/Resources/Art/Generated/` | Committed outputs plus `manifest.json` |
| `tools/art/atlas_packer.py` | Skyline packer, trimmed sprites, pivots re-expressed per rect, clip timings |
| `tools/art/art_validator.py` + `validator_config.json` | Validator and its warn/fail thresholds |
| `tools/art/tests/` | 26 pytest cases with synthetic good/bad sprites |
| `Assets/Forgebound/Editor/Art/` | `ForgeboundArtImporter.cs` (AssetPostprocessor) + editor-only asmdef |

## What a full build produces

Regeneration batch 2 (pipeline 1.1.0). Every runtime lookup in `SpriteLibrary.cs` and `BattleWorld.cs` now has art.

| Set | Count | Size | Disk |
|---|---|---|---|
| Weapon icons, tier 1 (12 × 12) + tier 3 / legendary benchmark (36 + 36) | 216 | 256² | 4.8 MB |
| Weapon in-game sprites, tier 1 | 144 | 128² | 1.0 MB |
| Legendary additive glow layers | 36 | 256² | 1.0 MB |
| Forge Board pieces (all of `pieces.json`) | 36 | **128²** | 0.5 MB |
| Core icons `fb_core_<id>_icon` | 12 | 128² | 0.2 MB |
| Modifier icons `fb_modifier_<id>_icon` | 45 | 128² | 0.6 MB |
| Rune icons `fb_rune_<id>_icon` | 52 | 128² | 0.6 MB |
| Blessing icons `fb_blessing_<id>_icon` | 110 | 128² | 1.4 MB |
| Forgemasters, 8 × (idle 8 + attack 8) | 128 | 256² | 3.4 MB |
| Enemies: 56 content ids (10 bosses, 5 elites, 36 minions, 5 dummies) + 4 legacy archetypes, idle 8 each | 480 | 256² | 9.9 MB |
| Backgrounds `fb_env_<biome>_bg` (opaque RGB) | 5 | 1920×1080 | 1.3 MB |
| **Total sprites** | **1,264** | | **about 25 MB** |
| Atlases: 18 groups (weapon_ingame, board_icon, char_×8, enemy legacy, enemy_<faction>×5, enemy_training, enemy_boss), one page each, largest 1920×1664 | 18 pages + JSON | | about 10 MB |

Generated PNG total, as the validator measures it (sprites + atlases): **35.0 MB** (`du` 41 MB). The budget is 60 MB. Reports in `reports/art/`: about 9 MB. The HTML now links the contact sheets instead of embedding them, and phone-scale previews show frame 00 of each clip. Raw render cache: about 71 MB (git-ignored).

UI icons (weapon/core/modifier/rune/blessing) and backgrounds are not atlased. The atlas packer packs only the in-battle and board groups.

## Measured timings (4-core container, Blender 4.0.2, Cycles CPU, full quality, 8 spp at 2× supersampling)

The full build ran at full quality; nothing was reduced for time.

| Step | Time |
|---|---|
| Full build, `python3 ArtSource/blender/build_all.py`, render wall (4 workers × 1 thread, longest-job-first scheduling) | 490 s |
| … post-process (1,264 files) | 144 s |
| … **total (wall)** | **10 min 34 s** |
| Worker-seconds per category: weapon 388, enemy 641, env 319, char 218, blessing 103, modifier 45, rune 47, board 26, core 11 | |
| Per icon (incl. scene build) | about 0.9-1.1 s |
| Per character / enemy frame | about 1.3-1.7 s |
| Per 1920×1080 background (3840×2160 raw) | about 60-70 s |
| `--only item` (219 icons) | about 70 s wall |
| `--only enemy` (480 frames) | about 4 min wall |
| Atlas packer (18 groups) | about 50 s |
| Validator (1,264 files + 18 atlases, HTML report) | about 95 s |
| Tests (26) | about 75 s |

## Determinism

Seeds come from `sha256` of the asset id (`spec.stable_seed`), the Cycles seed is fixed, dithering and file stamps are off, and the PNGs carry no metadata. Two consecutive full builds gave **443/450 byte-identical files**. The other 7 differed by at most **1/255 on a single texel**: floating-point nondeterminism inside Cycles/BVH, which persists even with 1 render thread. Content is reproducible; byte identity is not guaranteed. After any rebuild, the manifest's sha256 values are regenerated with the build, so provenance stays consistent.

## How to change or extend

- **New Frame**: add `frame_<id>(root, k)` in `weapons.py` returning the socket dict (`gem`, `gem_size`, `inlays`, `crown`, `grip`, `icon_rot`, `spin`), register it in `FRAME_BUILDERS`, and add the id to `spec.FRAMES`. Wrap tier ornaments in `with G.tagged(extra=True):` so they don't affect scale normalisation.
- **New Core**: add a palette row (with a `gem` and `crown` shape) in `palette.py`, add the shapes in `gem_shape` / `add_crown`, and add the id to `spec.CORES`.
- **New animation**: add a clip to `spec.CLIPS` and `validator_config.json["clips"]`, then write an action function in `characters.py` that keys pose bones only.
- Never hand-edit generated PNGs. They fail the sha256 provenance check. Regenerate instead.
- Thresholds live in `tools/art/validator_config.json`. Do not loosen them to make art pass; fix the art, or record the justification here.

## Validator coverage

Per file: format/RGBA, alpha present, unintended opaque background (border opacity, corners), cropped content touching the border, empty or nearly empty frames, halo/fringe, contrast against dark and light UI backgrounds, 48 px phone-scale legibility (opaque pixel count and L* spread), emissive oversaturation, file size, naming regex plus filename reconstructed from manifest fields, size/PPU/pivot vs spec, provenance (required fields, licence, source script exists, sha256 matches, orphan PNGs).

Groups: required clips per category, frame numbering vs `frame_count`, fps vs spec, identical pivots, feet-line drift and distance from the pivot ground line, bbox popping, static (identical-frame) animations, byte duplicates and near-duplicates (64-bit DCT pHash pre-filter plus the changed-texel fraction), total budget, and atlases (bounds, overlap/padding, excess padding, packing efficiency, page size).

Halo method: matte reconstruction. An edge texel with 0.25 ≤ α ≤ 0.85 is a dark halo if it equals α·C_interior, the signature of premultiplied colour stored as straight alpha, and a white fringe if it equals α·C + (1-α)·white. Intended dark outlines are constant in colour, so they don't match. **Limitation**: a dark halo on top of an already dark outline is undetectable, and also invisible. Tests prove detection on unoutlined sprites.

### Batch-2 validator changes (documented, not loosened)

- **Unframed names.** `core modifier rune blessing env` (`unframed_categories`) carry no `_NN` suffix, because the runtime resolves them by exact name. The regex accepts an optional frame. For these categories the expected name is rebuilt without it, and they must have `frame` 0.
- **`category_rules.env`.** Backgrounds are full-frame opaque paintings, so the sprite checks (alpha, border opacity, crop, empty, halo, sprite contrast, 48 px legibility) are skipped for `env` only. RGB is allowed and the file budget is 900/1500 KB. A replacement check WARNs on a flat (featureless) background. A test proves that an opaque image in a sprite category still FAILs.
- **`duplicate_exempt_ids`** (empty). This is the mechanism for intentionally shared art. It is not used, because each training dummy is rendered as its own variant (see ART_BIBLE §13.6).
- **Total budget 30/40 MB → 50/60 MB (warn/fail).** The asset count went from 450 to 1,264 sprites plus 18 atlases, and the project budget for committed art is about 60 MB. Per-file thresholds are unchanged.
- **Board icon size 256² → 128²**, as specified for the batch-2 board set.

### Current result (batch 2, final build)

**`PASS`: 1,264 files, 1,208 PASS / 56 WARN / 0 FAIL**, atlases included, 35.0 MB. `pytest tools/art/tests`: **26 passed**.

Remaining WARNs, left visible on purpose:
- **55 × contrast on a *light* UI background** (not dark). These are the light-coloured sets by design: rime_court enemies (ice and white fur: frost_wraith, glacier_knight, glass_wyrm, ice_mauler, shardling, snow_sprite) and the white-robed summoner (16 frames), plus 1 blessing icon. Every one of them has the 2 px dark outline. They are fine over the battle backgrounds; check them if a light panel shows battle sprites.
- 1 × oversaturation: the solar legendary dagger glow layer (27 % alpha-clipped), unchanged from batch 1.

Real defects the validator caught in batch 2, all fixed in the art:
- **Blessing near-duplicates (3 FAIL, 17 WARN).** Stats missing from the symbol table fell back to the same symbol. `boardWidth` and `boardHeight` shared one design. Rule blessings with the same trigger differed only by the small badge. Fixes: added symbols; rule blessings now use effect = main symbol and trigger = badge + trigger-family enamel; siblings get a 0.16 hue step plus pips.
- **Feet below the ground line.** ice_mauler fists and feet, bone_colossus club and the warden's long shield dipped 6-10 px under the pivot. The arms, club, feet and shield length were adjusted.
- **Dark halo (28-32 %) on clockwork_drone.** Rotor blades rendered edge-on as sub-pixel slivers; they are now pitched and thicker.

The first halo metric (luminance vs neighbours) flagged intended sub-pixel outlines. It was replaced by the matte-reconstruction test above, which still catches synthetic dark and white halos in the tests.

## KEEP / IMPROVE / REGENERATE audit (after batch 2)

| Asset set | Verdict | Notes |
|---|---|---|
| Weapon icons / in-game / benchmark / glow (432) | **KEEP** | Re-rendered unchanged. The shader change (true view vector, `FillAmt` socket) is a no-op for un-pitched cameras. |
| Board pieces (36) | **KEEP** | Batch 2. Dark plates, port channels, enlarged centrepieces. Contrast WARNs fixed. |
| Item icons (219) | **KEEP**, review | Batch 2 carrier + relief system. Blessing frames are pale gold, and the hue-stepped siblings need a player-facing check. |
| Forgemasters (8 × 16) | **IMPROVE** | Distinct silhouettes and palettes, and the blacksmith's colour blocking is fixed. Faces and proportions are simple, and the artificer's palette is busy. |
| Enemies (56 kit + 4 legacy) | **IMPROVE** (kit quality) | All distinct and validator-clean. Small flyers (static_wisp, clockwork_drone, ash_wisp, snow_sprite), drowned_choir and vine_crawler are weak at phone scale. Bosses are limited by the 256 px canvas. |
| Backgrounds (5) | **IMPROVE** | Correct composition and palettes, but graphic rather than painterly. |
| Tier 2 weapons | **REGENERATE when needed** | Not in the default matrix. |

## Not covered or still open

- `ItemCategory.Frame` items look up `fb_frame_<id>_icon` (`SpriteLibrary.Icon`). No such icons are produced (Frames have weapon icons), so Frame rewards still show the fallback. Either render `fb_frame_*` icons or map Frame to `Weapon(...)` in the runtime.

- The Unity importer was written against the 2022.3 API but **not compiled** here (no Unity in the container). Verify on first import.
- Aesthetic sign-off: see the checkpoints in ART_BIBLE §12. The validator does not judge beauty.
