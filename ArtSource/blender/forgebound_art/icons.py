"""Item icons (regeneration batch 2): Cores, Modifiers, Runes, Blessings.

All four share one grammar so they read as one family next to the weapon
icons: a forged carrier (plate / tablet / medallion / setting) with an
embossed symbol in relief, toon-shaded by FB_PaintedToon, outlined with the
inverted hull, rendered by the 128 px `item_icon` camera.

* Core      - the Core's own gem (weapons.gem_shape, so the gem matches the
              one set into weapons) on a gold claw setting.
* Modifier  - badge whose PLATE SHAPE encodes the modifier `stage`
              (split=swallowtail, pierce=lozenge, target=round, timing=gear,
              spawn=twin plate, impact=octagon), ENAMEL COLOUR the first tag
              family, MOTIF (symbols.MODIFIER_MOTIFS) the modifier itself and
              RIM METAL the rarity (iron / steel / gold).
* Rune      - engraved tablet: TABLET SHAPE + GLOW COLOUR = trigger family,
              MATERIAL = rarity (stone / dark iron / obsidian+gold), GLYPH =
              procedurally generated, unique per rune (symbols.all_rune_glyphs).
* Blessing  - medallion: FRAME = blessing (gold scalloped) vs curse (black
              iron thorns, violet cracks) vs trial (curse + chain), ENAMEL =
              stat family (+ hue step for siblings sharing a symbol), MAIN
              SYMBOL = primary stat / rune trigger, SMALL BADGE = secondary
              stat / effect, GEMS on top = rarity.
"""
from __future__ import annotations

import colorsys
import math
import random

from mathutils import Matrix, Vector

from . import geometry as G
from . import materials as M
from . import palette, symbols, weapons

T = G.trs


# ------------------------------------------------------------- materials
def _hex_shift(h, dh=0.0, ds=0.0, dv=0.0):
    r, g, b = palette.hex_to_srgb(h)
    hh, s, v = colorsys.rgb_to_hsv(r, g, b)
    hh = (hh + dh) % 1.0
    s = min(1, max(0, s + ds))
    v = min(1, max(0, v + dv))
    r, g, b = colorsys.hsv_to_rgb(hh, s, v)
    return "#" + "".join(f"{int(round(c * 255)):02X}" for c in (r, g, b))


def enamel(name, hexc, rim="#FFE8C8", emit=0.0, paint=0.25):
    return M.toon_material(name, hexc, palette.mix_hex(hexc, "#141026", 0.6), palette.mix_hex(hexc, "#FFFFFF", 0.45), rim=rim,
                           rim_strength=0.6, metal=0.15, spec_amt=0.5, spec_size=0.05, paint=paint, emit=emit, glow=palette.mix_hex(hexc, "#FFFFFF", 0.5))


def glow_mat(name, glow, dark):
    return M.toon_material(name, glow, dark, palette.mix_hex(glow, "#FFFFFF", 0.6), rim=glow, rim_strength=0.4, emit=0.8,
                           glow=palette.mix_hex(glow, "#FFFFFF", 0.35), paint=0.2)


def lib(key, seed, rim="#FFE8C8"):
    return M.library_material(key, rim=rim, rim_strength=0.6, seed=seed, suffix="_icon")


# ----------------------------------------------------------------- relief
def relief(prims, mat, root, scale=1.0, y=0.0, depth=0.08, hole_mat=None, name="sym", z0=0.0, x0=0.0, outline=True):
    """Extrude a symbols.* primitive list in the XZ plane, front face at y."""
    yc = y - depth / 2
    for i, pr in enumerate(prims):
        k = pr[0]
        nm = f"{name}{i}"
        if k == "poly":
            pts = [(x0 + px * scale, z0 + py * scale) for px, py in pr[1]]
            if len(pts) >= 3 and _area(pts) != 0:
                if _area(pts) < 0:
                    pts = pts[::-1]
                G.slab(nm, pts, depth, mat, m=T((0, yc, 0)), bevel=min(0.012, depth * 0.2), parent=root, outline=outline)
        elif k == "line":
            w = pr[2] * scale
            if w <= 0:
                continue
            pts = [(x0 + px * scale, yc, z0 + py * scale) for px, py in pr[1]]
            G.tube(nm, pts, w, mat, segs=8, flat_y=min(1.0, depth / (2 * w) + 0.1), parent=root, outline=outline)
            for j, p in enumerate((pts[0], pts[-1])):
                G.uvsphere(f"{nm}c{j}", w, mat, m=T(p), scale=(1, min(1.0, depth / (2 * w) + 0.1), 1), segs=10, rings=6, parent=root,
                           outline=False)
        elif k == "disc":
            r = pr[2] * scale
            if r <= 0:
                continue
            cx, cy = pr[1]
            G.slab(nm, symbols.circle(x0 + cx * scale, z0 + cy * scale, r, 20), depth, mat, m=T((0, yc, 0)), bevel=min(0.01, r * 0.2),
                   parent=root, outline=outline)
        elif k == "ring":
            R, w = pr[2] * scale, pr[3] * scale
            if w <= 0:
                continue
            cx, cy = pr[1]
            G.torus(nm, R, w, mat, m=T((x0 + cx * scale, yc, z0 + cy * scale), (0, 0, 0), (1, min(1.0, depth / (2 * w) + 0.1), 1)),
                    segs=28, rsegs=8, parent=root, outline=outline)
        elif k == "hole":
            r = pr[2] * scale
            cx, cy = pr[1]
            G.slab(nm, symbols.circle(x0 + cx * scale, z0 + cy * scale, r, 16), depth * 0.5, hole_mat or mat,
                   m=T((0, y - depth - 0.004, 0)), parent=root, outline=False)


def _area(pts):
    return 0.5 * sum(x0 * y1 - x1 * y0 for (x0, y0), (x1, y1) in zip(pts, pts[1:] + pts[:1]))


def plate(name, pts, mat, root, thickness, y=0.0, bevel=0.03, outline=True):
    if _area(pts) < 0:
        pts = pts[::-1]
    return G.slab(name, pts, thickness, mat, m=T((0, y, 0)), bevel=bevel, parent=root, outline=outline)


def _inset(pts, k):
    cx = sum(p[0] for p in pts) / len(pts)
    cy = sum(p[1] for p in pts) / len(pts)
    return [(cx + (x - cx) * k, cy + (y - cy) * k) for x, y in pts]


# --------------------------------------------------------------- cores
def build_core(root, job):
    core = job["core"]
    seed = job["seed"]
    rng = random.Random(seed)
    cp = palette.CORE_PALETTES[core]
    gold = lib("gold_trim", seed, rim=cp["glow"])
    iron = lib("dark_iron", seed, rim=cp["glow"])
    gem = M.core_gem_material(core)
    # setting: dark iron disc + gold bezel ring + four claws (the same setting language as weapon pommels)
    plate("setting_back", symbols.circle(0, 0, 0.62, 28), iron, root, 0.14, y=0.16)
    G.torus("bezel", 0.62, 0.07, gold, m=T((0, 0.06, 0)), segs=32, parent=root)
    for i in range(4):
        a = math.radians(45 + 90 * i)
        p0 = Vector((math.cos(a) * 0.62, 0.04, math.sin(a) * 0.62))
        p1 = Vector((math.cos(a) * 0.42, -0.12, math.sin(a) * 0.42))
        G.tube(f"claw{i}", [tuple(p0), tuple(p1)], [0.06, 0.035], gold, segs=8, parent=root)
    with G.tagged(glow=True):
        weapons.gem_shape(cp["gem"], "gem", 0.5, gem, T((0, -0.12, 0)), root, rng)
    # small rune studs in the core colour around the bezel
    rune = M.core_rune_material(core, 0.9)
    for i in range(4):
        a = math.radians(90 * i)
        G.ico(f"stud{i}", 0.05, rune, m=T((math.cos(a) * 0.62, -0.03, math.sin(a) * 0.62)), subdiv=0, scale=(1, 0.6, 1.4), parent=root,
              outline=False)
    return {"rot": (14, -12)}


# ------------------------------------------------------------ modifiers
MOD_TAG_COLORS = {
    "aoe": "#D2552A", "line": "#2E9AC8", "multi": "#2EA8A0", "chain": "#4F6CF0", "reliable": "#C89A2A", "reach": "#B0A040",
    "crit": "#D8A020", "single_target": "#C07A30", "speed": "#30B0C0", "burst": "#9A48D0", "echo": "#7A58E0", "control": "#5878A8",
    "frost_synergy": "#4AA8E8", "fire_synergy": "#E0602A", "dot": "#7AB030", "zone": "#6A9A3A", "onkill": "#A8384A",
    "sustain": "#38A858", "shield": "#5888C8", "execute": "#B02A3A", "ramp": "#30A0B0", "opener": "#C0882A", "armor_break": "#8A6A50",
    "status": "#86B83A", "circuit": "#6A5AE0", "risk": "#C83A5A", "mark": "#C86A2A", "summon": "#5A4AC8",
}


def _badge_shape(stage):
    if stage == "split":     # swallowtail banner
        return [(-0.72, 0.8), (0.72, 0.8), (0.72, -0.45), (0.38, -0.85), (0.0, -0.5), (-0.38, -0.85), (-0.72, -0.45)]
    if stage == "pierce":    # lozenge
        return [(0.0, 0.95), (0.8, 0.0), (0.0, -0.95), (-0.8, 0.0)]
    if stage == "target":    # round medallion
        return symbols.circle(0, 0, 0.84, 32)
    if stage == "timing":    # gear (handled separately)
        return None
    if stage == "spawn":     # twin plate (two overlapping rounded squares)
        return [(-0.85, 0.35), (-0.45, 0.82), (0.45, 0.82), (0.85, 0.35), (0.85, -0.35), (0.45, -0.82), (-0.45, -0.82), (-0.85, -0.35)]
    # impact: heavy octagon
    return [(math.cos(math.radians(22.5 + 45 * i)) * 0.9, math.sin(math.radians(22.5 + 45 * i)) * 0.9) for i in range(8)]


def build_modifier(root, job):
    seed = job["seed"]
    stage = job["stage"]
    tags = job.get("tags") or ["aoe"]
    col = MOD_TAG_COLORS.get(tags[0], "#7A7A8A")
    rim_key = {"common": "dark_iron", "uncommon": "forged_steel", "rare": "gold_trim"}.get(job.get("rarity"), "dark_iron")
    rim = lib(rim_key, seed)
    dark = lib("dark_iron", seed)
    en = enamel(f"FB_enamel_mod_{tags[0]}", palette.mix_hex(col, "#1A1622", 0.35), rim="#FFE8C8")
    motif_mat = lib("gold_trim", seed) if rim_key != "gold_trim" else M.toon_material(
        "FB_ivory_icon", "#F4ECD8", "#9A8A70", "#FFFFFF", rim_strength=0.5, spec_amt=0.4, spec_size=0.05, paint=0.2)
    shape = _badge_shape(stage)
    if shape is None:
        G.gear("plate", 0.95, 0.8, 12, 0.16, rim, m=T((0, 0.04, 0)), parent=root)
        plate("enamel", symbols.circle(0, 0, 0.7, 32), en, root, 0.08, y=-0.06, bevel=0.015)
    else:
        plate("plate", shape, rim, root, 0.16, y=0.04)
        plate("enamel", _inset(shape, 0.8), en, root, 0.08, y=-0.06, bevel=0.015)
    # rivets at the four diagonals (impact / spawn) or top/bottom (others)
    for i, a in enumerate((45, 135, 225, 315) if stage in ("impact", "spawn") else ()):
        G.uvsphere(f"rivet{i}", 0.06, dark if rim_key == "gold_trim" else lib("gold_trim", seed),
                   m=T((math.cos(math.radians(a)) * 0.74, -0.06, math.sin(math.radians(a)) * 0.74)), segs=10, rings=6, parent=root)
    prims = symbols.get(symbols.MODIFIER_MOTIFS[job["asset_id"]])
    s = 0.56 if stage != "pierce" else 0.5
    relief(prims, motif_mat, root, scale=s, y=-0.1, depth=0.07, hole_mat=dark, name="motif")
    return {"rot": (10, -10)}


# ---------------------------------------------------------------- runes
_RUNE_GLYPHS = {}


def _tablet_shape(family):
    if family == "strike":       # stele, rounded top
        return [(-0.55, -0.9), (0.55, -0.9), (0.55, 0.35)] + symbols.arc(0, 0.35, 0.55, 0, 180, 14)[1:-1] + [(-0.55, 0.35)]
    if family == "crit":         # tall diamond
        return [(0, 0.98), (0.7, 0.0), (0, -0.98), (-0.7, 0.0)]
    if family == "reap":         # coffin hexagon
        return [(-0.38, 0.95), (0.38, 0.95), (0.66, 0.45), (0.42, -0.95), (-0.42, -0.95), (-0.66, 0.45)]
    if family == "affliction":   # pebble
        return [(math.cos(a) * (0.72 + 0.06 * math.sin(3 * a)), math.sin(a) * (0.9 + 0.05 * math.cos(2 * a)))
                for a in [2 * math.pi * i / 24 for i in range(24)]]
    if family == "breaker":      # pointed shield
        return [(-0.7, 0.85), (0.7, 0.85), (0.66, -0.1), (0.0, -0.98), (-0.66, -0.1)]
    if family == "guard":        # notched square
        return [(-0.75, 0.75), (-0.25, 0.75), (-0.25, 0.62), (0.25, 0.62), (0.25, 0.75), (0.75, 0.75), (0.75, -0.75), (0.25, -0.75),
                (0.25, -0.62), (-0.25, -0.62), (-0.25, -0.75), (-0.75, -0.75)]
    if family == "time":         # disc
        return symbols.circle(0, 0, 0.85, 32)
    # herald: pennant
    return [(-0.6, 0.9), (0.6, 0.9), (0.6, -0.55), (0.0, -0.95), (-0.6, -0.55)]


def build_rune(root, job):
    seed = job["seed"]
    fam = symbols.TRIGGER_FAMILY.get(job["trigger"], "strike")
    glow, dark = symbols.TRIGGER_COLORS[fam]
    rarity = job.get("rarity", "common")
    if rarity == "rare":
        body = M.toon_material("FB_obsidian_icon", "#3A3448", "#120E1A", "#7A70A0", rim=glow, rim_strength=0.9, metal=0.5, spec_amt=0.9,
                               spec_size=0.06, paint=0.2, seed=seed)
        trim = lib("gold_trim", seed, rim=glow)
    elif rarity == "uncommon":
        body = lib("dark_iron", seed, rim=glow)
        trim = lib("forged_steel", seed, rim=glow)
    else:
        body = M.toon_material("FB_runestone_icon", "#7C7A80", "#34323C", "#B4B0B4", rim=glow, rim_strength=0.7, paint=0.45, seed=seed)
        trim = M.toon_material("FB_runestone_dark_icon", "#4E4C56", "#1E1C26", "#7E7C88", rim=glow, rim_strength=0.6, paint=0.4, seed=seed)
    shape = _tablet_shape(fam)
    plate("rim", shape, trim, root, 0.2, y=0.05, bevel=0.03)
    plate("face", _inset(shape, 0.84), body, root, 0.1, y=-0.04, bevel=0.02)
    glyph = job["glyph"]
    groove = M.toon_material(f"FB_rune_groove_{fam}", dark, palette.mix_hex(dark, "#000000", 0.5), glow, rim_strength=0.0, paint=0.1)
    rune = glow_mat(f"FB_rune_glow_{fam}", glow, dark)
    # groove (slightly wider, dark) then glowing inlay on top: reads as engraved + lit
    relief([(p[0], p[1], p[2] * 1.45) if p[0] == "line" else p for p in glyph], groove, root, scale=0.6, y=-0.095, depth=0.012,
           name="groove", outline=False)
    relief(glyph, rune, root, scale=0.6, y=-0.11, depth=0.03, name="glyph", outline=False)
    # rarity studs
    n = {"common": 0, "uncommon": 2, "rare": 4}.get(rarity, 0)
    for i in range(n):
        a = math.radians(-90 + (i - (n - 1) / 2) * 40)
        G.uvsphere(f"stud{i}", 0.055, trim, m=T((math.cos(a) * 0.62, -0.08, -0.62 + 0.0 * math.sin(a) + 0.02 * abs(i - (n - 1) / 2))),
                   segs=10, rings=6, parent=root)
    return {"rot": (12, -14)}


# ------------------------------------------------------------- blessings
def build_blessing(root, job):
    seed = job["seed"]
    curse = job.get("curse", False)
    trial = job.get("trial", False)
    fam = job["family"]
    base = symbols.FAMILY_COLORS.get(fam, "#B07ADA")
    base = _hex_shift(base, dh=0.07 * job.get("variant_index", 0), dv=-0.05 * (job.get("variant_index", 0) % 2))
    if curse:
        en_col = palette.mix_hex(base, "#2A1838", 0.55)
        frame_mat = M.toon_material("FB_curse_iron_icon", "#3E3446", "#140E1A", "#7A6A8A", rim="#C080FF", rim_strength=0.9, metal=0.6,
                                    spec_amt=0.6, spec_size=0.05, paint=0.3, seed=seed)
        sym_mat = M.toon_material("FB_curse_bone_icon", "#D8D0B8", "#7A6A70", "#FFF8E8", rim="#C080FF", rim_strength=0.8, spec_amt=0.2,
                                  paint=0.3, seed=seed)
        crack = glow_mat("FB_curse_crack_icon", "#C47AFF", "#3A1466")
    else:
        en_col = palette.mix_hex(base, "#1A1622", 0.25)
        frame_mat = lib("gold_trim", seed)
        sym_mat = M.toon_material("FB_bless_ivory_icon", "#FBF4E2", "#A8987A", "#FFFFFF", rim="#FFE8C8", rim_strength=0.6, spec_amt=0.4,
                                  spec_size=0.05, paint=0.2, seed=seed)
    en = enamel(f"FB_enamel_bless_{job['asset_id']}", en_col)
    dark = lib("dark_iron", seed)
    if curse:
        rng = random.Random(seed)
        pts = []
        n = 9
        for i in range(2 * n):
            a = math.radians(90 + 180 * i / n)
            r = (0.98 + rng.uniform(-0.05, 0.02)) if i % 2 == 0 else 0.76
            pts.append((math.cos(a) * r, math.sin(a) * r))
        plate("frame", pts, frame_mat, root, 0.16, y=0.05, bevel=0.012)
        # violet cracks through the frame
        for i in range(3):
            a = math.radians(rng.uniform(0, 360))
            c0 = (math.cos(a) * 0.72, math.sin(a) * 0.72)
            c1 = (math.cos(a + 0.18) * 0.86, math.sin(a + 0.18) * 0.86)
            c2 = (math.cos(a + 0.05) * 0.95, math.sin(a + 0.05) * 0.95)
            relief([("line", [c0, c1, c2], 0.03)], crack, root, y=-0.035, depth=0.02, name=f"crack{i}", outline=False)
    else:
        pts = []
        for i in range(48):
            a = 2 * math.pi * i / 48
            r = 0.92 + 0.05 * math.cos(12 * a)
            pts.append((math.cos(a) * r, math.sin(a) * r))
        plate("frame", pts, frame_mat, root, 0.16, y=0.05, bevel=0.015)
    plate("enamel", symbols.circle(0, 0, 0.72, 40), en, root, 0.08, y=-0.05, bevel=0.015)
    if trial:
        for i in range(10):
            a = 2 * math.pi * i / 10
            G.torus(f"chain{i}", 0.07, 0.02, dark, m=T((math.cos(a) * 0.84, -0.06, math.sin(a) * 0.84), (0, math.degrees(-a) + (90 if i % 2 else 0), 0)),
                    segs=12, rsegs=6, parent=root)
    relief(symbols.get(job["symbol"]), sym_mat, root, scale=0.5, y=-0.09, depth=0.07, hole_mat=dark, name="sym", z0=0.04)
    if job.get("secondary"):
        sec_plate = frame_mat
        G.slab("sec_plate", symbols.circle(0.56, -0.56, 0.3, 24), 0.08, sec_plate, m=T((0, -0.12, 0)), bevel=0.015, parent=root)
        G.slab("sec_en", symbols.circle(0.56, -0.56, 0.23, 24), 0.04, dark, m=T((0, -0.16, 0)), bevel=0.01, parent=root, outline=False)
        relief(symbols.get(job["secondary"]), sym_mat, root, scale=0.17, y=-0.18, depth=0.03, hole_mat=dark, name="sec", x0=0.56, z0=-0.56,
               outline=False)
    # rarity gems on the top rim
    n = {"common": 0, "uncommon": 1, "rare": 3}.get(job.get("rarity"), 0)
    gem_mat = M.core_gem_material("arcane" if curse else "solar") if n else None
    for i in range(n):
        a = math.radians(90 + (i - (n - 1) / 2) * 22)
        G.ico(f"gem{i}", 0.085, gem_mat, m=T((math.cos(a) * 0.86, -0.08, math.sin(a) * 0.86)), subdiv=1, scale=(1, 0.6, 1), parent=root)
    return {"rot": (8, -10)}


BUILDERS = {"core": build_core, "modifier": build_modifier, "rune": build_rune, "blessing": build_blessing}


def build_icon(root, job):
    return BUILDERS[job["category"]](root, job)
