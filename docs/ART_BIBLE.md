# Forgebound Art Bible

Forgebound is a weapon-engineering roguelike with side-view auto-combat. Its look is **premium stylized "hand-painted" fantasy**: chunky readable shapes, warm forge light, painted value breakup and clean dark outlines. All IP is original. Do not reference, trace or imitate any other game's characters, emblems, weapons or UI.

The values in this document live in code at `ArtSource/blender/forgebound_art/spec.py` and `palette.py`. If the two disagree, the code is authoritative; update this document when the code changes.

## 1. Proportions and shape language

| Asset | Rule |
|---|---|
| Weapons | Chunky and toy-like. Grips, shafts and guards are about 1.5x realistic thickness, because a thin shaft disappears at 48 px. The business end (blade, head, orb) gets at least 40% of the silhouette. Pole weapons use shortened shafts so the head reads. |
| Frames | Each of the 12 Frames must be identifiable from its black silhouette alone: sword (cross guard + straight blade), greatsword (wide blade, parrying lugs, angular swept guard), bow (recurve arc + string), staff (three-prong claw holding a large Core gem), hammer (block head, back spike, top spike), dagger (short curved single-edged blade + ring pommel), spear (leaf head, side lugs, tassel), crossbow (T-shape: stock + prod + stirrup), scythe (crescent blade at right angle to the shaft), bulwark (heater shield with side spikes and a bottom blade), whip (handle + coiled lash), tome (book with emblem, clasp and corner caps). |
| Characters | Stocky "heroic chibi" build, about 3.5 heads tall. Big hands and forearms on the Forgemaster. Feet planted on the ground line. |
| Enemies | One readable gimmick each: slime (squash/stretch blob with big eyes), skeleton (ribs + rusty sword), golem (boulder fists + glowing rune), bat (big membranes). |
| Board pieces | A shared hex forged base plate. The function is carried by the centre shape (pipe, rings, fork, horn, coil jar, whetstone). |

## 2. Shading (stylized hand-painted look)

There is no GPU here and EEVEE needs one, so all shading is a deterministic NPR shader rendered in Cycles as an Emission closure (`materials.py`, node group `FB_PaintedToon`). This is what gives the "painted" look:

1. **Two-band soft toon ramp** driven by N·L (key light). The terminator is soft-stepped (smoothstep, 0.02 to 0.16).
2. **Painted noise**: 4D object-space noise, seeded per asset, offsets N·L before the ramp. Terminators wobble like brush strokes; this approximates painted roughness.
3. **Hue-shifted shadows**: shadows are cooler and more saturated than the base, never just a darker base. Each library material defines `base / shadow / highlight` by hand.
4. **Top light band** (N·L > 0.62) blends toward the highlight colour.
5. **Metals**: a stylised "horizon" environment band (light above / dark below on N.z) gives the classic two-tone blade, plus a hard specular blob from the Blinn half-vector.
6. **Rim light** from behind-right (fresnel × rim direction), **tinted with the Core glow colour**. This is the main way Core identity shows on the silhouette.
7. **Outline**: an inverted hull (vertices pushed along their normals, faces flipped, back-face-only material) in `#1B1412`. Widths in final pixels: icons and characters 2.0 px, in-game weapon sprites 1.6 px.

Material library (controlled, `palette.MATERIALS`): `forged_steel`, `dark_iron`, `gold_trim`, `leather_wrap`, `wood`, `parchment`, `cloth_red`, `stone`, `bone`, `skin`, `beard`, `apron`, `shirt`, `slime`, `bat_skin`, `rust_iron`. Generated per Core: `gem_<core>`, `rune_<core>` (emissive), `accent_<core>`, `herald_<core>` (shield faces, tome covers, grip wraps). Do not create ad-hoc colours in builders; add them to the palette instead.

## 3. Camera (fixed, orthographic)

Every camera is orthographic, looks down +Y (from -Y), and is created by `scene.setup_camera(preset)`.

| Preset | Final px | ortho_scale (world units across) | centre (x, z) | pitch |
|---|---|---|---|---|
| `weapon_icon` | 256×256 | 2.0 | 0, 0 | 0° |
| `weapon_ingame` | 128×128 | 1.28 (= 100 px/unit) | 0, 0 | 0° |
| `board_icon` | 256×256 | 2.0 | 0, 0.12 | 30° down |
| `character` | 256×256 | 2.56 (= 100 px/unit) | 0, 1.04 (ground at 24 px from the bottom) | 0° |

- Weapons are modelled along +Z (grip at the origin), scaled uniformly and centred from their **base** geometry. Core, tier and legendary extras do not change the scale, so every variant of a Frame is registered identically.
- Icon pose: rotated about Y by `icon_rot` (45° by default, tip to the upper right), plus a 0-28° spin about the long axis to show form. In-game pose: horizontal, pointing right (+90°). Bulwark and tome stay upright.
- Characters face right (+X) and are yawed -25° toward the camera. Enemies face left (toward the player) and are yawed 205-245°.

## 4. Light rig (camera-relative, identical for every asset)

The rig is defined in `spec.LIGHT_RIG` and evaluated in-shader. Unit "toward light" vectors use world axes with the camera on -Y:

| Light | Direction | Intensity | Colour |
|---|---|---|---|
| Key | (-0.55, -0.65, 0.75): upper-left-front | 1.0 | warm `#FFF3E0` (baked into the material ramps) |
| Fill | (0.70, -0.55, -0.25): right-front-low | 0.30 screen blend | cool `#B9C8FF` |
| Rim | (0.75, 0.45, 0.45): behind-right-up | material `RimStrength` | Core glow colour (neutral `#FFE8C8` without a Core) |
| Specular | Blinn half-vector of key and view | material `Spec` / `SpecSize` | highlight colour |

Rule: light always comes from the upper left. Never flip a sprite horizontally in-engine for a lit asset without accepting that the light flips too. Enemies are rendered facing left so they never need flipping.

## 5. Colour management and alpha

- Cycles CPU, Standard view transform (no Filmic/AgX), look None, exposure 0, gamma 1, sRGB display, dithering 0.
- Film is transparent. Raw renders are straight-alpha 8-bit RGBA at 2× supersampling.
- Post step (`postprocess.py`): box downsample in **premultiplied** space, un-premultiply, then zero alpha below 1/255. Next, **colour bleed** RGB into fully transparent texels, so bilinear filtering, mipmaps and atlases never pull in black. Output is a metadata-free PNG.
- Unity imports with `alphaIsTransparency = true` and no mipmaps for sprites.

## 6. Core palette (12 Cores)

Each Core is identifiable by **gem shape** (for colour-blind players) and by colour (gem, rune inlays, grip wrap, rim light, heraldry).

| Core | Primary | Secondary | Dark | Glow | Gem shape | Legendary crown |
|---|---|---|---|---|---|---|
| fire | `#F05A28` | `#FFC53D` | `#6E1A0E` | `#FFB347` | flame | flame fan |
| frost | `#8FD8FF` | `#FFFFFF` | `#2A5C9E` | `#D8F7FF` | hex crystal | ice shards |
| lightning | `#FFE83A` | `#7B61FF` | `#3B2A8A` | `#FFF7A0` | zig-zag bolt | bolts |
| earth | `#B5844E` | `#6FA35A` | `#4A3220` | `#E8C27A` | rough boulder | orbiting rocks |
| life | `#3BD66A` | `#F5F0A0` | `#14603A` | `#A8FFC0` | leaf | leaves |
| shadow | `#6B5C9A` | `#1A1028` | `#22183A` | `#B4A0FF` | crescent | thorns |
| venom | `#A6E22E` | `#4E7A1A` | `#2B3A10` | `#D4FF5A` | droplet | fangs + drips |
| arcane | `#4D6BFF` | `#4DE8FF` | `#1A2470` | `#A8C4FF` | 5-point star | rune rings |
| wind | `#9FF0DC` | `#FFFFFF` | `#2E7A72` | `#E6FFF8` | swirl | gust swooshes |
| steel | `#A8B8CC` | `#DDE6F0` | `#3A4658` | `#E8F0FF` | faceted ingot | blade fan |
| solar | `#FFC93D` | `#FFF4D0` | `#9A5A10` | `#FFFBE0` | sun disc + rays | halo + ray star |
| void | `#3A1450` | `#FF3DCB` | `#0E0414` | `#FF5AD8` | dark orb + magenta ring | rift shards + ring |

Hue plan: fire 15°, solar 45°, lightning 55° (with violet arcs), venom 85°, life 140°, wind 165° (very light), frost 200°, arcane 230°, shadow 255° (desaturated), void 290° (near-black with magenta rim). Earth and steel are low-saturation. The close pairs (solar/lightning, wind/frost, shadow/void) are separated by value and by gem shape.

## 7. VFX colour grammar

- **Glow colour = Core glow.** Runes, inlays, gem cores and legendary crowns use the `rune_<core>` emissive material (mostly unlit, painted noise toward highlight).
- **Glow is never baked into the base sprite.** Legendary items ship a separate additive layer (`state = glow`, `blend = additive`, same canvas and pivot). It is rendered by holding out all non-emissive geometry, then blurred (σ 6 + σ 3). Base sprites keep crisp edges so the halo check stays meaningful.
- Board energy: relay pieces (conduit, echo_relay, splitter) use aqua `#5CE1E6`; support pieces (amplifier, capacitor, whetstone) use forge amber `#FFB347`.
- Budget: fewer than 12% of opaque texels may sit at full saturation and value (validator `oversaturation`). An additive layer may saturate its alpha on fewer than 25% of its area.

## 8. Tiers and legendary

- **Tier 1**: dark-iron fittings, leather or Core-herald grip, Core gem + rune inlay.
- **Tier 2**: steel fittings, gold trims and caps (built, not rendered in the default matrix).
- **Tier 3**: gold fittings plus ornaments that extend the silhouette (guard wings, gilded ricasso, spikes, crest, chain).
- **Legendary**: tier 3, a Core-tinted blade alloy, wider inlays, plus a **Core-specific crown accent** that changes the silhouette and an additive glow layer.

## 9. Units, sizes, pivots

- **PPU = 100** for every sprite.
- Weapon icon: 256×256, centred, pivot (0.5, 0.5); fits about 80% of the frame. Weapon in-game sprite: 128×128 canvas, long axis at most 116 px, pivot = **grip point** (projected from the Frame's `grip` socket).
- Character and enemy frames: 256×256, pivot = ground point under the root, (0.5, 0.0938). Every frame of every clip shares the canvas and pivot. Grounded assets keep their feet on the pivot line within 6 px.
- Board icons: 256×256, pivot centre.
- Clips: idle = 8 frames at 8 fps (loop), attack = 8 frames at 12 fps (one-shot). Animation is **only** authored as Actions on an Armature (rigid bone-parented parts); frames are never edited one by one.

## 10. Naming and paths

`fb_<category>_<id>[_<variant>]_<state>_<frame>.png`

- `category`: `weapon`, `board`, `char`, `enemy`.
- `id`: lowercase snake case (`echo_relay`).
- `variant`: `<core>-<t1|t3|leg>` for weapons; omitted otherwise.
- `state`: `icon`, `ingame`, `glow`, `idle`, `attack`.
- `frame`: two digits.
- Paths: `Assets/Forgebound/Resources/Art/Generated/weapon/{icon,ingame,glow}/`, `board/icon/`, `char/<id>/`, `enemy/<id>/`, `atlases/`.
- Example: `weapon/icon/fb_weapon_sword_fire-t1_icon_00.png`, `char/blacksmith/fb_char_blacksmith_attack_05.png`.

## 11. Benchmark definition

The **style benchmark** is the set used to judge any style change before it is rolled out:

- Weapons: sword, dagger and staff × all 12 Cores at tier 1, tier 3 and legendary (with glow layer).
- The Forgemaster (`blacksmith`) idle and attack.
- One board piece per energy family (conduit, amplifier).

A style change passes when the benchmark has 0 validator FAILs and a human review signs off on every checkpoint in §12, compared side by side with the previous benchmark contact sheets.

## 12. Qualitative review checkpoints

**Automated checks do not certify aesthetics.** The validator proves technical correctness only. A human must review these points (also listed in the validator report):

1. Silhouette: each Frame reads from its black silhouette alone.
2. Core identity: recognisable by gem shape as well as colour (check in greyscale and a colour-blind simulation).
3. Value structure: clear light/shadow split, key light from the upper left everywhere, no muddy mid-tones.
4. Hand-painted feel: noise breakup visible but not dirty; outlines even and unbroken.
5. Tier read: tier 3 visibly richer than tier 1 without noise.
6. Legendary read: special and distinct, but the Frame is still identifiable.
7. Animation: idle loops without a pop; attack shows anticipation, impact and recovery.
8. In context: over real battle backgrounds and UI panels, at phone scale (48-64 px).
9. Consistency: weapons, board pieces and characters share outline weight, rig and palette.
10. Originality: no resemblance to existing game IP.
