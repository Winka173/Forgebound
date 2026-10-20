"""Forge Board piece icons (all 36 pieces of Content/pieces.json).

Visual grammar (ART_BIBLE §13 "Regeneration batch 2"):

* RELAY pieces (route energy) stand on a dark, rune-etched SQUARE metal plate
  (one or two cells, matching the footprint).  Every port of the piece is an
  engraved glowing channel from the centre to that edge: an inward chevron for
  `in`, an outward arrow for `out`, a diamond for `both`.  West = left,
  east = right, north = back, south = front (the board camera is pitched 30°
  and not yawed, so directions read like the board).  The centrepiece names
  the relay type: pipe (conduit), concentric arcs (echo), fork + prism
  (splitter), horn (amplifier), coil jar (capacitor), caged element gem
  (converter, tinted by element), ...
* SUPPORT pieces (buff neighbours) are small forge tools on a ROUND wooden
  stump base with forge-amber accents (whetstone, bellows, lens, anvil, ...).

Energy colour grammar: relay channels aqua, support accents amber,
converters use their element's Core colours.  Plates are dark (slate/iron)
so icons keep contrast on light UI panels too (batch-1 contrast warnings).
"""
from __future__ import annotations

import math
import random

from mathutils import Vector

from . import geometry as G
from . import materials as M
from . import palette, weapons

T = G.trs

ENERGY = {"relay": ("#5CE1E6", "#1E6A80", "#D8FFFF"), "support": ("#FFB347", "#8A3A10", "#FFF0C0")}
DIRS = {"east": (1, 0), "west": (-1, 0), "north": (0, 1), "south": (0, -1)}
CONVERTER_CORE = {"fire_converter": "fire", "frost_converter": "frost", "storm_converter": "lightning", "shade_converter": "shadow"}
ACCENT = {"crit": ("#FF4A5A", "#6A0E1A", "#FFD0D4"), "kill": ("#C47AFF", "#3A1466", "#F0E0FF"), "status": ("#A6E22E", "#2E4A0E", "#F0FFC0"),
          "blood": ("#E0303A", "#5A0A12", "#FFB0B0"), "water": ("#4AB0F0", "#123A6A", "#D0F0FF"), "storm": ("#FFE24A", "#7A5A08", "#FFFBC8"),
          "life": ("#3BD66A", "#14603A", "#C8FFD8")}


def _mats(piece, fam, seed):
    prim, dark, glow = ENERGY[fam]
    core = CONVERTER_CORE.get(piece)
    if core:
        cp = palette.CORE_PALETTES[core]
        prim, dark, glow = cp["primary"], cp["dark"], cp["glow"]
    rim = glow
    sfx = f"_board_{piece}"
    m = {
        "iron": M.library_material("dark_iron", rim=rim, seed=seed, suffix=sfx),
        "steel": M.library_material("forged_steel", rim=rim, seed=seed, suffix=sfx),
        "gold": M.library_material("gold_trim", rim=rim, seed=seed, suffix=sfx),
        "stone": M.library_material("stone", rim=rim, seed=seed, suffix=sfx),
        "slate": M.toon_material("FB_slate2" + sfx, "#3E4452", "#181A24", "#68708A", rim=rim, rim_strength=0.6, paint=0.35, spec_amt=0.1,
                                 seed=seed),
        "groove": M.toon_material("FB_groove" + sfx, "#14161E", "#08080C", "#2A2E3A", rim_strength=0.0, paint=0.1),
        "wood": M.library_material("wood", rim=rim, seed=seed, suffix=sfx),
        "stump": M.toon_material("FB_stump" + sfx, "#6A4428", "#2E1A12", "#A0704A", rim=rim, rim_strength=0.5, paint=0.4, seed=seed),
        "ring": M.toon_material("FB_stump_ring" + sfx, "#C8945A", "#7A5030", "#EAC08A", rim_strength=0.2, paint=0.35, seed=seed),
        "leather": M.library_material("leather_wrap", rim=rim, seed=seed, suffix=sfx),
        "bone": M.library_material("bone", rim=rim, seed=seed, suffix=sfx),
        "energy": M.toon_material(f"FB_energy{sfx}", prim, dark, glow, rim=glow, emit=0.85, glow=glow, paint=0.2),
        "glass": M.toon_material(f"FB_glass{sfx}", prim, dark, glow, rim=glow, rim_strength=1.0, metal=0.4, spec_amt=1.0, spec_size=0.1,
                                 emit=0.3, glow=glow),
    }
    for k, (a, d, g) in ACCENT.items():
        m["acc_" + k] = M.toon_material(f"FB_acc_{k}{sfx}", a, d, g, rim=g, rim_strength=1.0, metal=0.3, spec_amt=1.0, spec_size=0.1,
                                        emit=0.45, glow=g)
    return m


# --------------------------------------------------------------- bases
def _rounded_rect(w, h, r, n=4):
    pts = []
    for cx, cy, a0 in ((w / 2 - r, h / 2 - r, 0), (-w / 2 + r, h / 2 - r, 90), (-w / 2 + r, -h / 2 + r, 180), (w / 2 - r, -h / 2 + r, 270)):
        for i in range(n + 1):
            a = math.radians(a0 + 90 * i / n)
            pts.append((cx + math.cos(a) * r, cy + math.sin(a) * r))
    return pts


def relay_plate(root, m, cells, ports, rng):
    """Square forged plate (1 or 2 cells along +X) + etched port channels."""
    w = 1.3 * cells
    cx0 = (cells - 1) * 0.65  # plate centre x offset (cell 0 at x=0)
    G.slab("plate", [(x + cx0, y) for x, y in _rounded_rect(w, 1.3, 0.16)], 0.16, m["iron"], m=T((0, 0, 0.08)), plane="XY", bevel=0.03,
           parent=root)
    G.slab("plate_top", [(x + cx0, y) for x, y in _rounded_rect(w - 0.2, 1.1, 0.1)], 0.04, m["slate"], m=T((0, 0, 0.17)), plane="XY",
           bevel=0.012, parent=root)
    for i, (sx, sy) in enumerate(((-1, -1), (1, -1), (1, 1), (-1, 1))):
        G.uvsphere(f"rivet{i}", 0.045, m["gold"], m=T((cx0 + sx * (w / 2 - 0.1), sy * 0.55, 0.17)), segs=10, rings=6, parent=root)
    # small etched rune ticks along the rim (decor, not ports)
    for i in range(int(6 * cells)):
        x = cx0 - w / 2 + 0.22 + (w - 0.44) * i / max(1, int(6 * cells) - 1)
        G.box(f"tick{i}", (0.03, 0.08, 0.02), m["groove"], m=T((x, 0.5, 0.195)), parent=root, outline=False)
    for pi, p in enumerate(ports):
        dx, dy = DIRS[p["dir"]]
        cx = 1.3 * p.get("cell", 0)
        start = Vector((cx, 0, 0.2))
        end = Vector((cx + dx * 0.55, dy * 0.55, 0.2))
        G.tube(f"groove{pi}", [tuple(start), tuple(end)], 0.075, m["groove"], segs=8, flat_y=1.0, parent=root, outline=False,
               m=T((0, 0, 0)) @ T((0, 0, -0.02), (0, 0, 0), (1, 1, 0.35)))
        G.tube(f"chan{pi}", [tuple(start), tuple(end)], 0.042, m["energy"], segs=8, parent=root, outline=False,
               m=T((0, 0, 0.0)) @ T((0, 0, 0), (0, 0, 0), (1, 1, 0.5)))
        # port marker at the plate edge
        ang = math.degrees(math.atan2(dy, dx))
        mk = T((cx + dx * 0.56, dy * 0.56, 0.2), (0, 0, ang))
        kind = p["kind"]
        if kind == "out":
            pts = [(-0.06, 0.14), (0.1, 0.0), (-0.06, -0.14), (-0.01, 0.0)]
        elif kind == "in":
            pts = [(0.08, 0.14), (-0.08, 0.0), (0.08, -0.14), (0.03, 0.0)]
        else:
            pts = [(0.0, 0.12), (0.12, 0.0), (0.0, -0.12), (-0.12, 0.0)]
        G.slab(f"port{pi}", pts, 0.05, m["energy"], m=mk, plane="XY", parent=root)


def support_base(root, m, cells=1):
    """Round wooden stump base (supports)."""
    for c in range(cells):
        x = 1.3 * c
        G.lathe(f"stump{c}", [(0, 0), (0.6, 0), (0.64, 0.05), (0.6, 0.2), (0, 0.2)], m["stump"], segs=20, m=T((x, 0, 0)), smooth=False,
                parent=root)
        G.lathe(f"stump_top{c}", [(0, 0.2), (0.56, 0.2), (0.5, 0.23), (0, 0.23)], m["ring"], segs=20, m=T((x, 0, 0)), smooth=False,
                parent=root, outline=False)
        for r in (0.18, 0.34):
            G.torus(f"ring{c}_{r}", r, 0.012, m["stump"], m=T((x, 0, 0.232), (90, 0, 0)), segs=20, rsegs=4, parent=root, outline=False)
    if cells > 1:
        G.box("brace", (1.3, 0.3, 0.12), m["iron"], m=T((0.65, 0, 0.12)), bevel=0.02, parent=root)


# ------------------------------------------------------------ helpers
def _echo_arcs(root, m, x=0.0, mat=None, n=3, h0=0.42):
    for i in range(n):
        r, z = 0.24 + 0.12 * i, h0 + 0.2 * i
        G.torus(f"arc{x}_{i}", r, 0.03 - 0.004 * i, (mat or m["energy"]) if i != 1 else m["gold"], m=T((x, 0, z), (90, 0, 0)), segs=32,
                parent=root)


def _pylon(root, m, x=0.0, top=1.0):
    G.lathe(f"pylon{x}", [(0, 0.2), (0.16, 0.2), (0.11, 0.35), (0.08, top - 0.15), (0.13, top - 0.05), (0, top)], m["steel"], m=T((x, 0, 0)),
            segs=8, smooth=False, parent=root)


def _pipe(root, m, pts, r=0.11, name="pipe", collars=()):
    G.tube(name, pts, r, m["steel"], segs=12, parent=root)
    for i, c in enumerate(collars):
        G.torus(f"{name}_collar{i}", r + 0.01, 0.03, m["gold"], m=c, segs=14, parent=root)


def _skull(root, mat, holes, loc, s=1.0):
    x, y, z = loc
    G.uvsphere("skull", 0.13 * s, mat, m=T((x, y, z)), scale=(1, 0.95, 1), parent=root)
    G.box("jaw", (0.14 * s, 0.11 * s, 0.07 * s), mat, m=T((x, y - 0.04 * s, z - 0.11 * s)), bevel=0.02 * s, parent=root)
    for sx in (-1, 1):
        G.uvsphere(f"eye{sx}", 0.035 * s, holes, m=T((x + 0.05 * sx * s, y - 0.11 * s, z)), parent=root, outline=False)


# --------------------------------------------------------------- relays
def r_conduit(root, m, rng, d):
    _pipe(root, m, [(-0.5, 0, 0.36), (0.5, 0, 0.36)], collars=[T((x, 0, 0.36), (0, 90, 0)) for x in (-0.35, 0.0, 0.35)])
    for x in (-0.18, 0.18):
        G.tube(f"glow{x}", [(x - 0.07, 0, 0.36), (x + 0.07, 0, 0.36)], 0.118, m["energy"], segs=12, parent=root, outline=False)
    for x in (-0.3, 0.3):
        G.box(f"strut{x}", (0.1, 0.2, 0.16), m["iron"], m=T((x, 0, 0.26)), bevel=0.02, parent=root)


def r_bent_conduit(root, m, rng, d):
    pts = [(-0.5, 0, 0.36), (-0.2, 0, 0.36)] + [(-0.2 + 0.2 * math.sin(a), -0.2 + 0.2 * math.cos(a) - 0.0, 0.36) for a in
                                                [math.radians(t) for t in (20, 45, 70)]] + [(0.0, -0.2, 0.36), (0.0, -0.5, 0.36)]
    pts = [(-0.5, 0, 0.36), (-0.25, 0, 0.36), (-0.1, -0.04, 0.36), (-0.02, -0.12, 0.36), (0.0, -0.25, 0.36), (0.0, -0.5, 0.36)]
    _pipe(root, m, pts, collars=[T((-0.35, 0, 0.36), (0, 90, 0)), T((0.0, -0.36, 0.36), (90, 0, 0))])
    G.uvsphere("elbow_glow", 0.13, m["energy"], m=T((-0.06, -0.06, 0.36)), parent=root, outline=False)
    G.box("strut", (0.18, 0.18, 0.16), m["iron"], m=T((-0.06, -0.06, 0.26)), bevel=0.02, parent=root)


def r_junction(root, m, rng, d):
    for ax in ((1, 0), (0, 1)):
        _pipe(root, m, [(-0.5 * ax[0], -0.5 * ax[1], 0.34), (0.5 * ax[0], 0.5 * ax[1], 0.34)], r=0.095, name=f"pipe{ax}")
    G.lathe("hub", [(0, 0.2), (0.22, 0.2), (0.24, 0.34), (0.2, 0.46), (0, 0.5)], m["gold"], segs=8, smooth=False, parent=root)
    G.uvsphere("knot", 0.13, m["glass"], m=T((0, 0, 0.56)), parent=root)


def r_echo(root, m, rng, d, finial=None, mat=None):
    _pylon(root, m)
    _echo_arcs(root, m, mat=mat)
    if finial == "crit":
        G.ico("finial", 0.15, m["acc_crit"], m=T((0, 0, 1.05)), subdiv=0, scale=(1, 1, 1.5), parent=root)
        G.torus("cross_h", 0.2, 0.02, m["acc_crit"], m=T((0, 0, 1.05), (90, 0, 0)), segs=24, parent=root, outline=False)
    elif finial == "kill":
        _skull(root, m["bone"], m["acc_kill"], (0, 0, 1.06), 1.1)
    elif finial == "status":
        G.lathe("flask", [(0, 0.92), (0.15, 0.95), (0.16, 1.05), (0.06, 1.16), (0.06, 1.24), (0, 1.24)], m["acc_status"], segs=12, parent=root)
    elif finial == "attack":
        G.blade("finial", [(0, 0.95), (0, 1.15)], [0.06, 0.05], [0.02, 0.02], m["steel"], tip=(0, 1.32), parent=root)
        G.box("finial_guard", (0.22, 0.06, 0.04), m["gold"], m=T((0, 0, 0.95)), parent=root)
    else:
        G.uvsphere("node", 0.13, m["glass"], m=T((0, 0, 1.03)), parent=root)


def r_echo_relay(root, m, rng, d):
    r_echo(root, m, rng, d)


def r_echo_coil(root, m, rng, d):
    for x in (0.0, 1.3):
        _pylon(root, m, x, 0.9)
        _echo_arcs(root, m, x=x, n=2)
        G.uvsphere(f"node{x}", 0.11, m["glass"], m=T((x, 0, 0.93)), parent=root)
    pts = [(0.0, 0, 0.8)] + [(0.65 + 0.65 * math.cos(math.pi * (1 - t / 10)) * 1.0, 0.1 * math.sin(t * 2.0), 0.8 + 0.25 * math.sin(math.pi * t / 10))
                             for t in range(1, 10)] + [(1.3, 0, 0.8)]
    G.tube("coil", pts, 0.035, m["gold"], segs=8, parent=root)


def r_crit_echo(root, m, rng, d):
    r_echo(root, m, rng, d, "crit", m["acc_crit"])


def r_kill_echo(root, m, rng, d):
    r_echo(root, m, rng, d, "kill", m["acc_kill"])


def r_status_echo(root, m, rng, d):
    r_echo(root, m, rng, d, "status", m["acc_status"])


def r_attack_echo(root, m, rng, d):
    r_echo(root, m, rng, d, "attack")


def _fork(root, m, outs):
    _pipe(root, m, [(-0.5, 0, 0.32), (-0.12, 0, 0.32)], r=0.1, name="in")
    for dname in outs:
        dx, dy = DIRS[dname]
        G.tube(f"out_{dname}", [(0.0, 0, 0.32), (dx * 0.25, dy * 0.25, 0.32), (dx * 0.5, dy * 0.5, 0.32)], 0.085, m["steel"], segs=12,
               parent=root)
        G.cone(f"tip_{dname}", 0.11, 0.0, 0.14, m["energy"],
               m=T((dx * 0.48, dy * 0.48, 0.32), (0, 0, math.degrees(math.atan2(dy, dx)))) @ T((0, 0, 0), (0, 90, 0)), segs=8, parent=root)
    G.ico("prism", 0.2, m["glass"], m=T((-0.04, 0, 0.42)), subdiv=0, scale=(1, 1, 1.3), parent=root)
    G.torus("prism_ring", 0.24, 0.03, m["gold"], m=T((-0.04, 0, 0.38), (90, 0, 0)), segs=24, parent=root)


def r_splitter(root, m, rng, d):
    _fork(root, m, ["east", "south"])


def r_tri_splitter(root, m, rng, d):
    _fork(root, m, ["north", "east", "south"])


def r_amplifier(root, m, rng, d):
    G.lathe("pedestal", [(0, 0.18), (0.24, 0.18), (0.16, 0.3), (0.12, 0.5), (0, 0.5)], m["iron"], segs=8, smooth=False, parent=root)
    horn = T((-0.05, 0, 0.55), (0, 70, 0))
    G.lathe("horn", [(0, 0.0), (0.1, 0.0), (0.12, 0.25), (0.2, 0.45), (0.38, 0.65), (0.34, 0.68), (0, 0.5)], m["gold"], segs=16, m=horn,
            parent=root)
    G.lathe("lens", [(0, 0.6), (0.34, 0.6), (0, 0.66)], m["energy"], segs=16, m=horn, parent=root, outline=False)
    for i in range(3):
        G.torus(f"band{i}", 0.12 + 0.05 * i, 0.022, m["iron"], m=horn @ T((0, 0, 0.12 + 0.13 * i), (90, 0, 0)), segs=16, parent=root)


def r_capacitor(root, m, rng, d, core_mat=None):
    G.lathe("cap_bottom", [(0, 0.18), (0.3, 0.18), (0.32, 0.3), (0, 0.3)], m["iron"], segs=12, smooth=False, parent=root)
    G.lathe("jar", [(0, 0.3), (0.24, 0.3), (0.26, 0.6), (0.24, 0.88), (0, 0.88)], m["glass"], segs=16, parent=root)
    if core_mat is None:
        G.tube("core", [(0, 0, 0.32), (0, 0, 0.86)], 0.09, m["energy"], segs=10, parent=root, outline=False)
    else:
        G.ico("core", 0.15, core_mat, m=T((0, -0.02, 0.6)), subdiv=0, scale=(1, 1, 1.6), parent=root)
    for i in range(4):
        G.torus(f"coil{i}", 0.27, 0.026, m["gold"], m=T((0, 0, 0.38 + 0.14 * i), (90, 0, 0)), segs=24, parent=root)
    G.lathe("cap_top", [(0, 0.88), (0.3, 0.88), (0.28, 0.98), (0.12, 1.02), (0, 1.02)], m["iron"], segs=12, smooth=False, parent=root)
    G.tube("terminal", [(0, 0, 1.02), (0, 0, 1.14)], 0.04, m["steel"], segs=8, parent=root)
    G.uvsphere("terminal_ball", 0.07, core_mat or m["energy"], m=T((0, 0, 1.18)), parent=root)


def r_crit_capacitor(root, m, rng, d):
    r_capacitor(root, m, rng, d, core_mat=m["acc_crit"])


def r_converter(root, m, rng, d):
    core = CONVERTER_CORE[d["id"]]
    cp = palette.CORE_PALETTES[core]
    for i in range(4):
        a = math.radians(45 + 90 * i)
        G.tube(f"bar{i}", [(math.cos(a) * 0.3, math.sin(a) * 0.3, 0.2), (math.cos(a) * 0.3, math.sin(a) * 0.3, 0.95)], 0.03, m["iron"],
               segs=6, parent=root)
    G.lathe("cage_base", [(0, 0.18), (0.38, 0.18), (0.36, 0.28), (0, 0.28)], m["iron"], segs=16, smooth=False, parent=root)
    G.lathe("cage_top", [(0, 0.92), (0.36, 0.92), (0.3, 1.0), (0.1, 1.06), (0, 1.06)], m["gold"], segs=16, smooth=False, parent=root)
    with G.tagged(glow=True):
        weapons.gem_shape(cp["gem"], "gem", 0.2, M.core_gem_material(core), T((0, -0.05, 0.6), (-30, 0, 0)), root, rng)


def r_delay_coil(root, m, rng, d):
    G.lathe("hourglass", [(0, 0.22), (0.28, 0.22), (0.26, 0.32), (0.06, 0.6), (0.26, 0.88), (0.28, 0.98), (0, 0.98)], m["glass"], segs=16,
            parent=root)
    G.uvsphere("sand", 0.16, m["energy"], m=T((0, 0, 0.36)), scale=(1, 1, 0.5), parent=root, outline=False)
    pts = [(0.34 * math.cos(t * 0.6), 0.34 * math.sin(t * 0.6), 0.25 + 0.72 * t / 26) for t in range(27)]
    G.tube("coil", pts, 0.028, m["gold"], segs=6, parent=root)
    for z in (0.2, 0.98):
        G.lathe(f"cap{z}", [(0, z - 0.03), (0.36, z - 0.03), (0.36, z + 0.04), (0, z + 0.04)], m["iron"], segs=12, smooth=False, parent=root)


def r_prism(root, m, rng, d):
    tri = [(math.cos(math.radians(90 + 120 * i)) * 0.36, math.sin(math.radians(90 + 120 * i)) * 0.36) for i in range(3)]
    G.slab("prism", tri, 0.6, m["glass"], m=T((0, 0, 0.58), (0, 0, 0)) @ T((0, 0, 0), (90, 0, 0)), bevel=0.02, parent=root)
    G.lathe("prism_base", [(0, 0.18), (0.36, 0.18), (0.3, 0.27), (0, 0.27)], m["gold"], segs=6, smooth=False, parent=root)
    for i, col in enumerate(("acc_crit", "acc_status", "energy")):
        a = math.radians(-20 + 20 * i)
        G.tube(f"ray{i}", [(0.25, 0, 0.6), (0.6 * math.cos(a) + 0.1, 0.0, 0.6 + 0.4 * math.sin(a))], 0.035, m[col], segs=6, parent=root,
               outline=False)


def r_governor(root, m, rng, d):
    G.tube("shaft", [(0, 0, 0.2), (0, 0, 1.0)], 0.04, m["steel"], segs=8, parent=root)
    G.gear("gear", 0.32, 0.25, 10, 0.08, m["gold"], m=T((0, 0, 0.3)) @ T((0, 0, 0), (90, 0, 0)), parent=root)
    for sx in (-1, 1):
        G.tube(f"arm{sx}", [(0, 0, 0.95), (0.32 * sx, 0, 0.62)], 0.025, m["iron"], segs=6, parent=root)
        G.uvsphere(f"ball{sx}", 0.12, m["energy"] if sx > 0 else m["glass"], m=T((0.34 * sx, 0, 0.58)), parent=root)
    G.uvsphere("cap", 0.06, m["gold"], m=T((0, 0, 1.02)), parent=root)


def r_siphon_node(root, m, rng, d):
    G.lathe("funnel", [(0, 0.2), (0.1, 0.2), (0.1, 0.45), (0.38, 0.8), (0.42, 0.86), (0.34, 0.86), (0.08, 0.52), (0, 0.52)], m["steel"],
            segs=18, parent=root)
    G.lathe("pool", [(0, 0.78), (0.32, 0.78), (0, 0.8)], m["acc_life"], segs=18, parent=root, outline=False)
    G.uvsphere("drop", 0.09, m["acc_life"], m=T((0, -0.05, 1.05)), scale=(1, 1, 1.4), parent=root)
    G.cone("drop_tip", 0.08, 0.0, 0.12, m["acc_life"], m=T((0, -0.05, 1.13)), parent=root, outline=False)


def r_beacon(root, m, rng, d):
    G.lathe("tower", [(0, 0.2), (0.3, 0.2), (0.22, 0.35), (0.16, 0.75), (0.24, 0.8), (0, 0.8)], m["stone"], segs=8, smooth=False, parent=root)
    G.lathe("lamp", [(0, 0.8), (0.16, 0.8), (0.16, 1.0), (0, 1.0)], m["glass"], segs=8, smooth=False, parent=root)
    G.cone("roof", 0.24, 0.0, 0.22, m["gold"], m=T((0, 0, 1.0)), segs=8, parent=root)
    G.slab("mark", [(0, 0.14), (0.1, 0), (0, -0.14), (-0.1, 0)], 0.03, m["energy"], m=T((0, -0.18, 0.9)), parent=root, outline=False)
    for i in range(3):
        a = math.radians(-25 + 25 * i)
        G.tube(f"beam{i}", [(0.15, 0, 0.9), (0.15 + 0.5 * math.cos(a), 0, 0.9 + 0.5 * math.sin(a))], [0.04, 0.012], m["energy"], segs=6,
               parent=root, outline=False)


# -------------------------------------------------------------- supports
def s_whetstone(root, m, rng, d):
    G.box("stand", (0.8, 0.4, 0.1), m["wood"], m=T((0, 0, 0.28)), bevel=0.03, parent=root)
    G.box("stone", (0.64, 0.3, 0.2), m["stone"], m=T((0, 0, 0.43)), bevel=0.05, segs=2, parent=root)
    bl = T((-0.36, -0.06, 0.58), (0, 66, 0))
    G.blade("blade", [(0, 0.0), (0, 0.45), (0, 0.8)], [0.09, 0.085, 0.07], [0.035, 0.035, 0.03], m["steel"], tip=(0, 0.98), m=bl, parent=root)
    G.box("guard", (0.32, 0.1, 0.08), m["gold"], m=bl, bevel=0.02, parent=root)
    G.tube("grip", [(-0.36 - 0.2 * 0.914, -0.06, 0.58 - 0.2 * 0.407), (-0.36, -0.06, 0.58)], 0.04, m["wood"], segs=8, parent=root)
    _sparks(root, m, rng, (0.2, 0.75), 6)


def _sparks(root, m, rng, c, n, spread=0.25):
    for i in range(n):
        x, z = c[0] + rng.uniform(-spread, spread), c[1] + rng.uniform(-0.05, spread * 1.4)
        r = 0.04 + rng.uniform(0, 0.03)
        G.slab(f"spark{i}", G.star2d(4, r, r * 0.3, rng.uniform(0, 0.6)), 0.015, m["energy"], m=T((x, -0.3, z), (-30, 0, 0)), parent=root,
               outline=False)


def s_bellows(root, m, rng, d):
    prof = [(-0.42, 0.0), (0.3, 0.12), (0.3, -0.12)]
    for i, z in enumerate((0.36, 0.56)):
        G.slab(f"board{i}", [(-0.45, 0.22), (0.25, 0.1), (0.25, -0.1), (-0.45, -0.22)], 0.04, m["wood"], m=T((0, 0, z + 0.06 * i)), plane="XY",
               bevel=0.01, parent=root)
    G.slab("leather", [(-0.4, 0.2), (0.22, 0.08), (0.22, -0.08), (-0.4, -0.2)], 0.2, m["leather"], m=T((0, 0, 0.47)), plane="XY", bevel=0.04,
           parent=root)
    G.tube("nozzle", [(0.22, 0, 0.47), (0.55, 0, 0.42)], [0.06, 0.035], m["gold"], segs=10, parent=root)
    for i in range(2):
        G.tube(f"handle{i}", [(-0.45, 0, 0.36 + 0.26 * i), (-0.62, 0, 0.32 + 0.36 * i)], 0.03, m["wood"], segs=6, parent=root)
    for i in range(3):
        G.uvsphere(f"puff{i}", 0.07 + 0.03 * i, m["energy"], m=T((0.62 + 0.12 * i, 0, 0.42 + 0.04 * i)), parent=root, outline=False)
    del prof


def _lens(root, m, glass, z=0.75, r=0.3, name="lens"):
    G.torus(f"{name}_rim", r, 0.045, m["gold"], m=T((0, 0, z), (0, 0, 20)), segs=28, parent=root)
    G.slab(f"{name}_glass", [(math.cos(a) * r, math.sin(a) * r) for a in [2 * math.pi * i / 24 for i in range(24)]], 0.05, glass,
           m=T((0, 0, z), (0, 0, 20)), plane="XZ", parent=root, outline=False)


def s_lens(root, m, rng, d):
    G.tube("stem", [(0, 0, 0.22), (0, 0, 0.45)], 0.05, m["iron"], segs=8, parent=root)
    G.tube("fork", [(-0.3, 0, 0.75), (-0.3, 0, 0.45), (0.3, 0, 0.45), (0.3, 0, 0.75)], 0.03, m["iron"], segs=6, parent=root)
    _lens(root, m, M.toon_material("FB_lensglass", "#9ED8F0", "#3A6A8A", "#FFFFFF", rim="#FFFFFF", rim_strength=1.0, metal=0.3, spec_amt=1.0,
                                   spec_size=0.12, emit=0.2, glow="#E0F8FF"))
    G.slab("glint", G.star2d(4, 0.1, 0.03), 0.01, m["energy"], m=T((-0.12, -0.06, 0.88)), parent=root, outline=False)


def s_focus_lens(root, m, rng, d):
    for i in range(3):
        a = math.radians(90 + 120 * i)
        G.tube(f"leg{i}", [(math.cos(a) * 0.35, math.sin(a) * 0.35, 0.22), (0, 0, 0.62)], 0.03, m["iron"], segs=6, parent=root)
    G.ico("crystal", 0.2, m["acc_crit"], m=T((0, 0, 0.82)), subdiv=0, scale=(1, 1, 1.5), parent=root)
    G.torus("crystal_ring", 0.24, 0.03, m["gold"], m=T((0, 0, 0.75), (90, 0, 0)), segs=24, parent=root)
    G.torus("reticle", 0.36, 0.02, m["acc_crit"], m=T((0, -0.1, 0.82)), segs=32, parent=root, outline=False)


def _anvil(root, m, s=1.0, x=0.0):
    prof = [(-0.55, 0.0), (0.4, 0.0), (0.62, 0.05), (0.78, 0.12), (0.4, 0.14), (0.3, 0.2), (0.3, 0.32), (-0.35, 0.32), (-0.35, 0.2),
            (-0.5, 0.14), (-0.55, 0.1)]
    top = [(px * s + x, pz * s) for px, pz in prof]
    G.slab("anvil_top", top, 0.32 * s, m["iron"], m=T((0, 0, 0.62 * s)) @ T((0, 0, 0), (0, 0, 0)), bevel=0.02, parent=root)
    G.lathe("anvil_waist", [(0, 0.22), (0.2 * s, 0.22), (0.13 * s, 0.45 * s), (0.18 * s, 0.62 * s), (0, 0.62 * s)], m["iron"],
            m=T((x, 0, 0)), segs=4, smooth=False, scale_xy=(1.4, 1.0), parent=root)
    G.box("anvil_face", (0.6 * s, 0.3 * s, 0.03), m["steel"], m=T((x - 0.1 * s, 0, 0.955 * s)), parent=root, outline=False)


def s_anvil(root, m, rng, d):
    _anvil(root, m, 0.95)
    _sparks(root, m, rng, (0.0, 1.05), 5, 0.3)


def s_heavy_anvil(root, m, rng, d):
    _anvil(root, m, 1.35, x=0.65)
    G.tube("hammer_handle", [(1.45, -0.2, 1.42), (0.75, -0.2, 1.65)], 0.04, m["wood"], segs=8, parent=root)
    G.box("hammer_head", (0.3, 0.2, 0.2), m["steel"], m=T((1.5, -0.2, 1.4), (0, 20, 0)), bevel=0.03, parent=root)
    _sparks(root, m, rng, (0.65, 1.45), 6, 0.4)


def s_quench_basin(root, m, rng, d):
    G.lathe("basin", [(0, 0.22), (0.4, 0.22), (0.5, 0.55), (0.46, 0.57), (0.38, 0.3), (0, 0.3)], m["iron"], segs=20, parent=root)
    G.lathe("water", [(0, 0.48), (0.45, 0.48), (0, 0.5)], m["acc_water"], segs=20, parent=root, outline=False)
    for sx in (-1, 1):
        G.torus(f"handle{sx}", 0.08, 0.02, m["gold"], m=T((0.52 * sx, 0, 0.48), (0, 0, 90)), segs=12, parent=root)
    for i in range(3):
        G.uvsphere(f"steam{i}", 0.08 + 0.02 * i, M.toon_material("FB_steam", "#E8F0F4", "#8AA0B0", "#FFFFFF", rim_strength=0.2, paint=0.3),
                   m=T((-0.15 + 0.15 * i, 0, 0.7 + 0.16 * i)), parent=root, outline=False)


def s_rune_lamp(root, m, rng, d):
    G.lathe("lamp_body", [(0, 0.22), (0.24, 0.22), (0.3, 0.32), (0.22, 0.42), (0.08, 0.46), (0.06, 0.5), (0, 0.5)], m["gold"], segs=16,
            parent=root)
    G.tube("spout", [(0.2, 0, 0.36), (0.42, 0, 0.44)], [0.06, 0.03], m["gold"], segs=8, parent=root)
    G.torus("lamp_handle", 0.1, 0.02, m["gold"], m=T((-0.3, 0, 0.36)), segs=12, parent=root)
    G.slab("flame", [(0, 0.3), (0.08, 0.12), (0.1, 0.0), (0, -0.06), (-0.1, 0.0), (-0.06, 0.15)], 0.06, m["energy"], m=T((0.44, 0, 0.55)),
           parent=root)
    G.slab("rune", [(0, 0.12), (0.05, 0.0), (0, -0.12), (-0.05, 0.0)], 0.02, m["energy"], m=T((0.0, -0.27, 0.33)), parent=root, outline=False)
    for i in range(3):
        a = math.radians(30 + 120 * i)
        G.slab(f"glyph{i}", [(0, 0.06), (0.03, 0), (0, -0.06), (-0.03, 0)], 0.01, m["energy"],
               m=T((math.cos(a) * 0.38, -0.2, 0.8 + 0.1 * math.sin(a))), parent=root, outline=False)


def s_fletching_rack(root, m, rng, d):
    for x in (-0.25, 1.55):
        G.box(f"post{x}", (0.08, 0.08, 0.7), m["wood"], m=T((x, 0, 0.55)), bevel=0.015, parent=root)
    G.box("rail", (1.95, 0.08, 0.07), m["wood"], m=T((0.65, 0, 0.82)), bevel=0.015, parent=root)
    G.box("rail2", (1.95, 0.08, 0.07), m["wood"], m=T((0.65, 0, 0.4)), bevel=0.015, parent=root)
    for i in range(5):
        x = 0.0 + 0.32 * i
        G.tube(f"shaft{i}", [(x, -0.06, 0.28), (x + 0.04, -0.06, 1.12)], 0.022, m["wood"], segs=6, parent=root)
        G.cone(f"head{i}", 0.05, 0.0, 0.12, m["steel"], m=T((x + 0.04, -0.06, 1.12)), segs=4, parent=root)
        G.slab(f"fletch{i}", [(-0.07, 0.0), (0.0, 0.18), (0.07, 0.0)], 0.02, m["energy"], m=T((x, -0.08, 0.3)), parent=root)


def s_tesla_coil(root, m, rng, d):
    G.lathe("tower", [(0, 0.22), (0.26, 0.22), (0.18, 0.32), (0.12, 0.85), (0, 0.85)], m["iron"], segs=12, smooth=False, parent=root)
    pts = [(0.17 * math.cos(t * 0.7), 0.17 * math.sin(t * 0.7), 0.32 + 0.5 * t / 30) for t in range(31)]
    G.tube("winding", pts, 0.025, m["gold"], segs=6, parent=root)
    G.torus("torus_top", 0.2, 0.07, m["steel"], m=T((0, 0, 0.92), (90, 0, 0)), segs=20, parent=root)
    G.uvsphere("ball", 0.1, m["acc_storm"], m=T((0, 0, 1.02)), parent=root)
    for i, a in enumerate((20, 150)):
        aa = math.radians(a)
        pts = [(0.1 * math.cos(aa), 0, 1.02)]
        for k in range(1, 4):
            pts.append((math.cos(aa) * (0.1 + 0.12 * k), (-1) ** k * 0.03, 1.02 + 0.08 * k * (1 if k % 2 else -0.4)))
        G.tube(f"arc{i}", pts, 0.022, m["acc_storm"], segs=5, parent=root, outline=False)


def s_blood_basin(root, m, rng, d):
    G.lathe("bowl", [(0, 0.22), (0.12, 0.22), (0.1, 0.36), (0.42, 0.62), (0.46, 0.66), (0.4, 0.66), (0.08, 0.42), (0, 0.42)], m["stone"],
            segs=20, parent=root)
    G.lathe("blood", [(0, 0.6), (0.4, 0.6), (0, 0.62)], m["acc_blood"], segs=20, parent=root, outline=False)
    for i in range(3):
        a = math.radians(-60 + 60 * i)
        G.uvsphere(f"drop{i}", 0.05, m["acc_blood"], m=T((math.cos(a) * 0.46, -0.1, 0.55 - 0.06 * i)), scale=(1, 1, 1.4), parent=root)
    _skull(root, m["bone"], m["acc_blood"], (0.0, -0.35, 0.38), 0.8)


def s_grindstone(root, m, rng, d):
    for sx in (-1, 1):
        G.tube(f"frame{sx}", [(-0.35, 0.2 * sx, 0.22), (0.0, 0.2 * sx, 0.72), (0.35, 0.2 * sx, 0.22)], 0.035, m["wood"], segs=6, parent=root)
    G.lathe("wheel", [(0, -0.08), (0.36, -0.08), (0.38, 0.0), (0.36, 0.08), (0, 0.08)], m["stone"], segs=24,
            m=T((0, 0, 0.72), (90, 0, 0)), smooth=False, parent=root)
    G.torus("wheel_band", 0.37, 0.02, m["iron"], m=T((0, -0.09, 0.72)), segs=24, parent=root, outline=False)
    G.tube("axle", [(0, -0.3, 0.72), (0, 0.3, 0.72)], 0.03, m["iron"], segs=6, parent=root)
    G.tube("crank", [(0, -0.3, 0.72), (0.18, -0.32, 0.6), (0.18, -0.42, 0.6)], 0.025, m["iron"], segs=6, parent=root)
    G.blade("blade", [(0, 0), (0, 0.35)], [0.06, 0.05], [0.02, 0.02], m["steel"], tip=(0, 0.48), m=T((0.32, -0.15, 0.85), (0, 50, 0)),
            parent=root)
    _sparks(root, m, rng, (0.45, 0.9), 5, 0.18)


def s_resonator(root, m, rng, d):
    G.box("block", (0.5, 0.36, 0.2), m["wood"], m=T((0, 0, 0.32)), bevel=0.04, parent=root)
    G.tube("stem", [(0, 0, 0.4), (0, 0, 0.58)], 0.05, m["steel"], segs=8, parent=root)
    G.tube("fork", [(-0.16, 0, 1.08), (-0.16, 0, 0.66)] + [(-0.16 * math.cos(a), 0, 0.66 - 0.12 * math.sin(a)) for a in
                                                           [math.radians(t) for t in (30, 60, 90, 120, 150)]] + [(0.16, 0, 0.66), (0.16, 0, 1.08)],
           0.045, m["steel"], segs=8, parent=root)
    for i, r in enumerate((0.38, 0.52)):
        for sx in (-1, 1):
            pts = [(sx * r * math.cos(math.radians(a)), -0.02, 0.9 + r * math.sin(math.radians(a))) for a in range(-30, 31, 10)]
            G.tube(f"wave{i}{sx}", pts, 0.022, m["energy"], segs=5, parent=root, outline=False)


PIECES = {
    "conduit": r_conduit, "bent_conduit": r_bent_conduit, "junction": r_junction, "echo_relay": r_echo_relay, "echo_coil": r_echo_coil,
    "crit_echo": r_crit_echo, "kill_echo": r_kill_echo, "status_echo": r_status_echo, "attack_echo": r_attack_echo,
    "splitter": r_splitter, "tri_splitter": r_tri_splitter, "amplifier": r_amplifier, "capacitor": r_capacitor,
    "crit_capacitor": r_crit_capacitor, "fire_converter": r_converter, "frost_converter": r_converter, "storm_converter": r_converter,
    "shade_converter": r_converter, "delay_coil": r_delay_coil, "prism": r_prism, "governor": r_governor, "siphon_node": r_siphon_node,
    "beacon": r_beacon,
    "whetstone": s_whetstone, "bellows": s_bellows, "lens": s_lens, "anvil": s_anvil, "quench_basin": s_quench_basin,
    "rune_lamp": s_rune_lamp, "focus_lens": s_focus_lens, "fletching_rack": s_fletching_rack, "tesla_coil": s_tesla_coil,
    "blood_basin": s_blood_basin, "grindstone": s_grindstone, "heavy_anvil": s_heavy_anvil, "resonator": s_resonator,
}


def build_piece(root, piece, seed, data=None):
    """data: the pieces.json entry (kind, ports, footprint)."""
    data = data or {"id": piece, "kind": "relay", "ports": [], "footprint": [[0, 0]]}
    data = dict(data, id=piece)
    rng = random.Random(seed)
    fam = "relay" if data.get("kind") == "relay" else "support"
    m = _mats(piece, fam, seed)
    cells = max(c[0] for c in data.get("footprint", [[0, 0]])) + 1
    if fam == "relay":
        relay_plate(root, m, cells, data.get("ports", []), rng)
    else:
        support_base(root, m, cells)
    # the centrepiece carries the function: enlarge it relative to the plate so it
    # dominates at 48-64 px (batch 1 centrepieces were too small at phone scale)
    import bpy
    k = 1.32 if fam == "relay" else 1.12
    centre = bpy.data.objects.new("FB_Centre", None)
    bpy.context.scene.collection.objects.link(centre)
    centre.parent = root
    z0 = 0.2 if fam == "relay" else 0.23
    cx = (cells - 1) * 0.65
    centre.matrix_world = T((cx * (1 - k), 0, z0 * (1 - k)), (0, 0, 0), (k, k, k))
    PIECES[piece](centre, m, rng, data)
