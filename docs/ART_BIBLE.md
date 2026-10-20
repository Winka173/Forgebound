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

## 13. Regeneration batch 2 (full runtime coverage)

Batch 2 gives every runtime sprite lookup (`Presentation/Art/SpriteLibrary.cs`, `BattleWorld.cs`) generated art. It keeps §1-§12 unchanged: same NPR shader, light rig, outline colour, PPU, pivots and character camera. Only additions and the fixes listed here changed.

### 13.1 What was fixed from the batch-1 review

- **Blacksmith colour blocking.** The muddy brown/blue mass is replaced by value-stepped materials (`palette.MATERIALS` `bs_*`): light cornflower shirt, near-black apron, warm grey trousers, ginger beard. Skin, shirt, apron and boots now step down in value. The idle breathes 1.5x deeper.
- **Pale board plates / low contrast.** The shader's rim fresnel used a constant view vector, so every upward face seen by the 30° board camera got full rim light. It now uses `Geometry.Incoming`, which is identical for the un-pitched cameras, so weapons and characters are unchanged. Relay plates are dark slate. The conduit, echo_relay and splitter contrast warnings are gone.
- **Small board centrepieces.** The centrepiece is scaled 1.32x for relays and 1.12x for supports, relative to the base.

### 13.2 Naming added in batch 2

| Asset | File | Size |
|---|---|---|
| Forgemaster clips | `char/<id>/fb_char_<id>_{idle,attack}_00..07.png` | 256² |
| Board piece | `board/icon/fb_board_<id>_icon_00.png` | **128²** (was 256²) |
| Core / Modifier / Rune / Blessing icon | `<cat>/icon/fb_<cat>_<id>_icon.png` (**no frame suffix**) | 128² |
| Enemy idle | `enemy/<id>/fb_enemy_<id>_idle_00..07.png` | 256² |
| Background | `env/fb_env_<biome>_bg.png` (no suffix, opaque RGB) | 1920×1080 |

Single-image categories (`core modifier rune blessing env`, `spec.UNFRAMED`) carry no `_NN` suffix, because the runtime looks them up by exact name (`fb_<cat>_<id>_icon`, `fb_env_<biome>_bg`). `SpriteLibrary.cs` is unchanged.

### 13.3 Forgemasters (8)

All eight use the blacksmith's rig (`characters.HUMANOID`), camera, yaw (-25°), scale (about 1.1) and ground line. Each one has a silhouette feature taken from `forgemasters.json` "silhouette", a dominant hue and the real tier-1 Frame from `weapons.py` in their Core, bone-parented to the forearm:

| id | silhouette features | hue | weapon / core | attack template |
|---|---|---|---|---|
| blacksmith | broad, apron, hammer | cornflower + black apron | own hammer | slam (batch 1) |
| runecrafter | hood with peak, long robe, 3 floating rune tablets (own bones, bob out of phase) | indigo + cream, yellow glow | sword / lightning | swing |
| alchemist | knee coat with tails, diagonal bandolier of 4 glowing vials, goggles on a cap | green coat, tan leather | staff / venom | cast |
| artificer | big brass goggles, **brass mechanical front arm with claw**, boiler backpack, toolbelt | teal shirt + brass | bow / solar | shoot (bow kept square to the straight arm) |
| hexblade | ragged-hem cloak, cracked white mask with violet eye slits, chained gauntlet | black-violet | scythe / shadow | reap |
| summoner | flared robe and tall collar, 3 spectral blades orbiting on an `orbit` bone (they flare out on the cast) | white + royal blue | tome / arcane | cast_tome |
| duelist | slim (narrower shoulders), plumed wide hat, red cape, tall boots | crimson + black + white | dagger / steel | thrust (lunge) |
| warden | heavy plate, big pauldrons, visored helm with crest, tower shield, **lantern on a crook pole over the back** | steel + green tabard | bulwark / earth | bash |

Attack templates (`forgemasters.ATTACKS`) key the upper-arm swing and the weapon's world angle per frame (ready, anticipation ×2, top, strike, impact, recovery ×2). The forearm bend is derived from these, so a rigid weapon follows a designed arc.

### 13.4 Board pieces (36)

- **Relays** (23) sit on a dark square forged plate, 1 or 2 cells to match `footprint`. Every port is an etched aqua channel from the centre to that edge, with an inward chevron (`in`), an outward arrow (`out`) or a diamond (`both`). West is left, east is right, north is back and south is front: relays are not yawed. Centrepieces: pipe (conduit), elbow (bent_conduit), 4-way hub (junction), pylon + concentric arcs (echo family; the finial names the variant: crit crystal, skull, flask, blade; echo_coil has two pylons and a coil), fork + prism (splitter, tri_splitter), horn (amplifier), coil jar (capacitor; the crit variant has a red crystal), caged Core gem in the element's palette (the four converters), hourglass coil (delay), prism with split rays, flyball governor, funnel drain (siphon), lantern tower (beacon).
- **Supports** (13) are forge tools on a round wooden stump with amber accents: whetstone, bellows, lens, anvil, quench basin, rune lamp, focus lens (red crit crystal), fletching rack (2 cells), tesla coil, blood basin, grindstone, heavy anvil (2 cells), resonator.

### 13.5 Item icons (219): one carrier + relief grammar

All item icons are a forged carrier with an embossed symbol (`symbols.py`, a 2D vocabulary of more than 100 symbols extruded in relief by `icons.relief`). They are toon-lit and outlined at 1.6 px, at 128 px.

- **Cores (12):** the Core's own gem mesh (`weapons.gem_shape`, the same one set into weapons) on a gold claw bezel with rune studs.
- **Modifiers (45):** plate shape = `stage` (split = swallowtail, pierce = lozenge, target = round, timing = gear, spawn = twin plate, impact = octagon). Enamel colour = first tag. Motif = a unique symbol per modifier (`symbols.MODIFIER_MOTIFS`). Rim metal = rarity (iron, steel, gold).
- **Runes (52):** tablet shape and glow colour = trigger family (strike orange, crit red, reap violet, affliction green, breaker cyan, guard blue, time teal, herald gold). Tablet material = rarity (stone, dark iron, obsidian with gold). Glyph = a procedurally generated stave-and-stroke glyph on a 3×4 lattice, seeded from the rune id. Uniqueness is enforced: `all_rune_glyphs` re-rolls on a signature collision.
- **Blessings (110), the compact system:**
  - Frame: gold scalloped medallion = blessing; black iron thorn star with violet glowing cracks = curse; curse frame + chain ring = challenge-only trial.
  - Enamel colour = stat family (life, blood, might, precision, speed, status, arcane, guard, hunt, wealth, element, rule).
  - Main symbol = primary stat, or the rune trigger for rule blessings.
  - Small badge, bottom right = secondary stat or effect.
  - Gems on the top rim = rarity (0, 1 or 3).
  - Siblings that would get an identical design (same symbol + badge + family + frame) get a hue step on the enamel (`variant_index`). This keeps the validator's near-duplicate check clean without exemptions.

### 13.6 Enemies (56 ids + 4 archetype stand-ins)

`enemy_recipes.py` (data) and `enemykit.py` (Blender) assemble every enemy from three layers:

1. **Faction kit** (`palette.FACTION_KITS`): the body, armour, cloth, accent and glow materials plus a motif. Slagborn: basalt + magma cracks, ember glow. Overgrowth: bark, leaves, moss, pink blooms, spore glow. Rime court: ice, silver plate, royal blue cloth, white fur. Hollow dead: bone, rusty iron, grave-violet cloth, violet soul glow. Tempest order: brass and storm steel, royal blue cloth, yellow lightning.
2. **Role archetype** (`ROLE_DEFAULTS`): fixes the silhouette class (body plan + build + size).
   - Body plans: `biped`, `quad`, `blob`, `wing`, `wisp`, `serpent`, `dummy`.
   - Builds: normal, bulky, slim, robe, imp, golem, tree, ape, skeleton, ghoul, construct, crawler, toad, beast, mech.
   - Role mapping: swarm = small quad or blob; tank = bulky + shield; ranged = slim + bow or staff; support/healer = robe + staff + aura; flyer = wisp or wing; assassin = slim + hunch + twin blades; bruiser = bulky + hunch; summoner = robe + orbiting minions; splitter = blob; elite = big biped (scale 1.3); boss = scale 1.45-1.6.
3. **Parts** (`RECIPES[id]`): 3-5 tokens from about 130 parts (`head:`, `held:`, `offhand:`, `back:`, `shoulder:`, `aura:`, `extra:`, `body:`, `top:`). No two enemies of a faction share both plan and parts.

**Variation rules:** inside a faction, every enemy differs in at least two of plan/build, head, held item and back silhouette. Bosses use boss-only parts and their own plan (slag titan golem with chimneys and lava core, Kaldur with an anvil crown and forge maul, Rootmother tree-woman with root skirt, Hollow Bloom carnivorous bulb, Frost Sovereign, Glass Wyrm crystal serpent, Lich Regent floating with phylactery and soul orbs, Drowned Choir with three drowned singers, Storm Tyrant with storm rings and glaive, Clockwork Colossus with gear array and pistons). Enemies face -X, yawed 200-235°. Each idle loop is fitted to the 256 px canvas (`fit_to_canvas`: the rig shrinks about the ground pivot until all 8 frames clear a 10 px margin), and outlines are added after fitting, so they stay at 2 px. Orbiting auras rotate 120°, or 72° / 60°, per loop with 3-, 5- or 6-fold symmetry, so the loop is seamless.

**Training dummies:** each `dummy_*` id gets its own small variant: plain, three mini dummies, bucket helm + plate, shield + barrier ring, painted runes + rune circle. They are not copies, so `validator_config.duplicate_exempt_ids` stays empty. If copies are ever wanted, list the ids there; the validator then skips them in the duplicate check.

The four batch-1 archetypes (slime, skeleton, golem, bat) are kept. The runtime uses them as stand-ins (`SpriteLibrary.Archetype`), and `skeleton` is also the content enemy of that id.

### 13.7 Backgrounds (5)

The camera is the `env` preset: orthographic, 100 px/unit, 1920×1080, with the ground line (Z = 0) at 35 % height. Flat depth layers:

| Layer | Depth (y) | Treatment |
|---|---|---|
| Sky | 60 | Posterised 9-band gradient + glow bands |
| Far silhouettes | 45 | Unlit, faded toward the horizon colour |
| Mid structures | 30 | Toon-lit, fill damped to 0.25 with the new `FillAmt` socket, faded |
| Near props | 10-12 | Outlined 2.5 px, kept at the frame edges, out of the play band |
| Ground | 0 | Front face with lip, strata and a few stones |
| Particles | -6 | Embers, leaves, snow, wisps, rain |

The layer boundaries are where a parallax rig would split the image. Palettes come from `biomes.json` through `palette.BIOME_SCENES`:

- **ember_foundry:** furnace halls, chimneys, a giant gear, lava seam.
- **verdant_ruins:** columns with ivy, an arch, a great tree.
- **frostspire:** ice castle with spires, crystals, snow drifts.
- **sunken_crypt:** arcades, tombstones, candles, a violet water strip.
- **storm_citadel:** brass-domed towers with lightning rods, floating isles, a bolt, rain.

### 13.8 Still needs human aesthetic review (batch 2)

The validator only proves technical health. A human should check:

1. **Forgemaster faces and proportions.** Heads are simple spheres. The summoner and runecrafter read mostly by costume, and the artificer's palette is busy (brass, leather and teal compete).
2. **Bow and tome holds.** The artificer's idle bow crosses the body diagonally; the summoner's tome reads as a blue square at 64 px.
3. **Enemy quality varies.** The kit enemies are clean and readable but simpler than the batch-1 legacy set. Weakest at phone scale: `static_wisp`, `clockwork_drone`, `ash_wisp`, `snow_sprite` (small flyers), `drowned_choir` (dark), `vine_crawler` (thin). Bosses are capped by the 256 px canvas, so in-game boss scale must come from the runtime (`radius`).
4. **Faction look-alikes:** forge_bandit, ember_slinger and bellows_tender share the red cloth body and differ by head, prop and robe; check them in battle.
5. **Backgrounds.** Composition and palettes are on-brief, but they are flat and graphic rather than painterly. The frostspire snow drifts read as discs; the storm citadel's rain is heavy over the towers.
6. **Blessing icon set.** Families are coded by enamel colour, and the gold frame is pale. Check the curse frames on light panels, and check that hue-stepped siblings are distinguishable enough to players.
7. **Rune glyphs** are random by construction. Check that none accidentally resembles a real-world symbol or letter that carries meaning.
