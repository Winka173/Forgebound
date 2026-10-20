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
python3 ArtSource/blender/build_all.py --only weapon        # rebuild one category (manifest is merged)
python3 ArtSource/blender/build_all.py --post-only          # redo post step from cached raw renders

# 2. Pack atlases (Generated/atlases/fb_atlas_<group>.png + .json)
python3 tools/art/atlas_packer.py                           # groups: weapon_ingame board_icon char_blacksmith enemy

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
| `…/board.py` | 6 Forge Board pieces |
| `…/characters.py` | Armatures, rigid binding, Actions (Forgemaster idle/attack, enemy idles) |
| `…/scene.py` | Render settings, colour management, cameras, inverted-hull outlines |
| `…/worker.py` | Blender entry point (renders jobs to `ArtSource/renders_cache/raw`) |
| `…/postprocess.py` | Premultiplied downsample, colour bleed, glow layer, PNG write, sha256 |
| `ArtSource/renders_cache/` | Raw 2× renders and worker logs (git-ignored via `ArtSource/.gitignore`, about 23 MB) |
| `Assets/Forgebound/Resources/Art/Generated/` | Committed outputs plus `manifest.json` |
| `tools/art/atlas_packer.py` | Skyline packer, trimmed sprites, pivots re-expressed per rect, clip timings |
| `tools/art/art_validator.py` + `validator_config.json` | Validator and its warn/fail thresholds |
| `tools/art/tests/` | 23 pytest cases with synthetic good/bad sprites |
| `Assets/Forgebound/Editor/Art/` | `ForgeboundArtImporter.cs` (AssetPostprocessor) + editor-only asmdef |

## What a full build produces

| Set | Count | Size |
|---|---|---|
| Weapon icons, tier 1, 12 Frames × 12 Cores | 144 | 256² |
| Weapon in-game sprites, tier 1, 12 × 12 | 144 | 128² (pivot = grip) |
| Benchmark tier 3 icons (sword, dagger, staff × 12 Cores) | 36 | 256² |
| Benchmark legendary icons | 36 | 256² |
| Legendary additive glow layers | 36 | 256² |
| Forge Board piece icons (conduit, echo_relay, splitter, amplifier, capacitor, whetstone) | 6 | 256² |
| Forgemaster `blacksmith` idle 8 + attack 8 | 16 | 256² |
| Enemies slime, skeleton, golem, bat: idle 8 each | 32 | 256² |
| **Total sprites** | **450** | about 7.4 MB PNG |
| Atlases (weapon_ingame 1536×768, board 640×512, blacksmith 640×768, enemy 768×1024) | 4 pages + JSON | about 1.8 MB |

Generated folder total (PNGs + atlases + manifest): about 11 MB (budget 40 MB). Reports in `reports/art/`: about 5 MB, mostly the HTML with embedded contact sheets.

Every manifest entry records: `file, id, category, variant, state, frame, frame_count, fps, loop, size, pivot, ppu, blend, grounded, group, core, tier, legendary, source{script,function,postprocess}, parameters, seed, render_seconds, blender_version, pipeline_version, git_commit, license, sha256`. `git_commit` is `null` until the repo has a commit.

## Measured timings (4-core container, Blender 4.0.2, Cycles CPU)

| Step | Time |
|---|---|
| Full build, render wall (4 workers × 1 thread) | 159-195 s across 5 runs |
| … post-process (450 files, `optimize=True` PNG) | 45-50 s |
| … total | **about 3.5-4.1 min** |
| Per weapon image (worker time incl. scene build) | about 1.0 s |
| Per character / enemy frame | about 1.2-2.0 s |
| Smoke build | about 28 s |
| Atlas packer (4 groups) | about 7 s |
| Validator (450 files + atlases, HTML report) | about 27 s |
| Tests (23) | about 35 s |

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

### Current result (final build)

`PASS`: 450 files, 446 PASS / 4 WARN / 0 FAIL, atlases included. Remaining WARNs, left visible on purpose:
- 3 × contrast: the conduit, echo_relay and splitter board icons have only 38-40% of texels contrasting with a *light* UI background (pale aqua/slate plates). Fine on the dark board UI; revisit if a light panel is used.
- 1 × oversaturation: the solar legendary dagger glow layer saturates alpha on about 27% of its area.

Defects the validator caught during development, all fixed in the art rather than in the thresholds:
- Legendary staff crowns and their glow layers cropped at the icon border.
- Slime idle frames 0/4, 1/3 and 5/7 byte-identical (a symmetric sine loop); a phase-shifted sway was added.
- Forgemaster attack frame 3 hammer cropped at the left edge; the wind-up was re-posed so the hammer folds behind the back.
- 128 px in-game outlines too thin to have an opaque core; outline raised from 1.25 to 1.6 px.
- Atlas packing efficiency 34-59% with the shelf packer; replaced by a skyline packer with multiple-of-128 pages (now 64-85%).

The first halo metric (luminance vs neighbours) flagged intended sub-pixel outlines. It was replaced by the matte-reconstruction test above, which still catches synthetic dark and white halos in the tests.

## KEEP / IMPROVE / REGENERATE audit

The repository started empty, so every asset is new and generated by this pipeline.

| Asset set | Verdict | Notes |
|---|---|---|
| Weapon icons, tier 1 (144) | **KEEP** | Distinct Frame silhouettes. Core shows through gem shape, grip colour, runes and rim. |
| Weapon in-game sprites (144) | **KEEP** | Clean at 128 px with grip pivots. Pole weapons are thin by nature. |
| Tier 3 / legendary benchmark (72) + glow (36) | **IMPROVE** | Staff legendary crowns are small inside the gold claw, and the steel crown (blade fan) barely reads. Legendary dagger inlays are busy. |
| Board pieces (6) | **IMPROVE** | Readable, but the plates are pale on light backgrounds and the splitter fork reads weakly from 30°. |
| Forgemaster idle/attack (16) | **IMPROVE** | Poses read (anticipation, impact, recovery), but the body is a muddy brown/blue mass with low value separation, and the idle motion is subtle. Needs a human art pass on proportions and colour blocking. |
| Enemies (32) | **KEEP** (placeholder quality) | Slime and skeleton read well. Golem is a generic rock pile, and the bat's membrane flap reads but is simple. |
| Tier 2 | **REGENERATE when needed** | Builder support exists; not rendered in the default matrix. |

## Not covered or still open

- The Unity importer was written against the 2022.3 API but **not compiled** here (no Unity in the container). Verify on first import.
- Aesthetic sign-off: see the checkpoints in ART_BIBLE §12. The validator does not judge beauty.
