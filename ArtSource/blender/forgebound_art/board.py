"""Forge Board piece icons (relay / support pieces).

All pieces share a hexagonal forged base plate; the centrepiece shape carries
the function.  Energy colour grammar: relay pieces (move/route energy) use
aqua, support pieces (modify/buff) use forge amber.  Pieces are modelled with Z
up and viewed by the 30 degree pitched board_icon camera.
"""
from __future__ import annotations

import math
import random

from . import geometry as G
from . import materials as M

T = G.trs

RELAY = {"conduit", "echo_relay", "splitter"}
ENERGY = {"relay": ("#5CE1E6", "#1E6A80", "#D8FFFF"), "support": ("#FFB347", "#8A3A10", "#FFF0C0")}


def _mats(piece, seed):
    fam = "relay" if piece in RELAY else "support"
    prim, dark, glow = ENERGY[fam]
    rim = glow
    sfx = f"_board_{fam}"
    return {
        "iron": M.library_material("dark_iron", rim=rim, seed=seed, suffix=sfx),
        "steel": M.library_material("forged_steel", rim=rim, seed=seed, suffix=sfx),
        "gold": M.library_material("gold_trim", rim=rim, seed=seed, suffix=sfx),
        "stone": M.library_material("stone", rim=rim, seed=seed, suffix=sfx),
        "slate": M.toon_material("FB_slate" + sfx, "#5C6170", "#262834", "#8E94A6", rim=rim, rim_strength=0.6, paint=0.35,
                                 spec_amt=0.1, seed=seed),
        "wood": M.library_material("wood", rim=rim, seed=seed, suffix=sfx),
        "energy": M.toon_material(f"FB_energy_{fam}", prim, dark, glow, rim=glow, emit=0.85, glow=glow, paint=0.2),
        "glass": M.toon_material(f"FB_glass_{fam}", prim, dark, glow, rim=glow, rim_strength=1.0, metal=0.4, spec_amt=1.0,
                                 spec_size=0.1, emit=0.3, glow=glow),
    }


def base_plate(root, m):
    G.lathe("plate", [(0, 0), (0.7, 0), (0.74, 0.07), (0.7, 0.15), (0, 0.15)], m["iron"], segs=6, smooth=False, parent=root)
    G.lathe("plate_top", [(0, 0.15), (0.6, 0.15), (0.58, 0.18), (0, 0.18)], m["slate"], segs=6, smooth=False, parent=root)
    for i in range(6):
        a = math.radians(30 + 60 * i)
        G.uvsphere(f"rivet{i}", 0.035, m["gold"], m=T((math.cos(a) * 0.66, math.sin(a) * 0.66, 0.15)), parent=root)


def piece_conduit(root, m, rng):
    G.tube("pipe", [(-0.55, 0, 0.32), (0.45, 0, 0.32)], 0.12, m["steel"], segs=12, parent=root)
    for x in (-0.45, -0.1, 0.25):
        G.torus(f"collar{x}", 0.125, 0.035, m["gold"], m=T((x, 0, 0.32), (0, 90, 0)), segs=14, parent=root)
    for x in (-0.28, 0.07):
        G.tube(f"glow{x}", [(x - 0.08, 0, 0.32), (x + 0.08, 0, 0.32)], 0.128, m["energy"], segs=12, parent=root, outline=False)
    for i, x in enumerate((0.55, 0.72)):
        G.slab(f"chevron{i}", [(-0.08, 0.15), (0.0, 0.15), (0.12, 0.0), (0.0, -0.15), (-0.08, -0.15), (0.04, 0.0)], 0.06,
               m["energy"], m=T((x - 0.05, 0, 0.32)), parent=root)
    G.box("support", (0.16, 0.26, 0.18), m["iron"], m=T((-0.1, 0, 0.22)), bevel=0.02, parent=root)


def piece_echo_relay(root, m, rng):
    G.lathe("pylon", [(0, 0.18), (0.16, 0.18), (0.11, 0.35), (0.08, 0.85), (0.13, 0.95), (0, 1.0)], m["steel"], segs=8,
            smooth=False, parent=root)
    G.uvsphere("node", 0.13, m["glass"], m=T((0, 0, 1.03)), parent=root)
    for i, (r, z) in enumerate(((0.32, 0.42), (0.45, 0.62), (0.56, 0.84))):
        G.torus(f"ring{i}", r, 0.03 - 0.004 * i, m["energy"] if i != 1 else m["gold"], m=T((0, 0, z), (90, 0, 0)), segs=32,
                parent=root)


def piece_splitter(root, m, rng):
    G.tube("in", [(-0.6, 0, 0.3), (-0.1, 0, 0.3)], 0.1, m["steel"], segs=12, parent=root)
    for sy in (-1, 1):
        G.tube(f"out{sy}", [(0.0, 0, 0.3), (0.25, 0.18 * sy, 0.3), (0.55, 0.42 * sy, 0.3)], 0.085, m["steel"], segs=12, parent=root)
        G.cone(f"tip{sy}", 0.12, 0.0, 0.16, m["energy"], m=T((0.55, 0.42 * sy, 0.3), (0, 90, 0)) @ T((0, 0, 0), (0, 0, 0))
               @ T((0, 0, 0), (-math.degrees(math.atan2(0.24, 0.3)) * sy, 0, 0)), segs=8, parent=root)
    G.ico("prism", 0.22, m["glass"], m=T((-0.05, 0, 0.38)), subdiv=0, scale=(1, 1, 1.3), parent=root)
    G.torus("prism_ring", 0.25, 0.03, m["gold"], m=T((-0.05, 0, 0.36), (90, 0, 0)), segs=24, parent=root)


def piece_amplifier(root, m, rng):
    G.lathe("pedestal", [(0, 0.18), (0.24, 0.18), (0.16, 0.3), (0.12, 0.5), (0, 0.5)], m["iron"], segs=8, smooth=False, parent=root)
    horn = T((0.0, 0, 0.55), (0, 50, 0))
    G.lathe("horn", [(0, 0.0), (0.1, 0.0), (0.12, 0.25), (0.2, 0.5), (0.4, 0.75), (0.36, 0.78), (0, 0.6)], m["gold"], segs=16,
            m=horn, parent=root)
    G.lathe("lens", [(0, 0.7), (0.36, 0.7), (0, 0.76)], m["energy"], segs=16, m=horn, parent=root, outline=False)
    for i in range(3):
        G.torus(f"band{i}", 0.12 + 0.05 * i, 0.022, m["iron"], m=horn @ T((0, 0, 0.15 + 0.15 * i), (90, 0, 0)), segs=16,
                parent=root)


def piece_capacitor(root, m, rng):
    G.lathe("cap_bottom", [(0, 0.18), (0.32, 0.18), (0.34, 0.3), (0, 0.3)], m["iron"], segs=12, smooth=False, parent=root)
    G.lathe("jar", [(0, 0.3), (0.25, 0.3), (0.27, 0.6), (0.25, 0.92), (0, 0.92)], m["glass"], segs=16, parent=root)
    G.tube("core", [(0, 0, 0.32), (0, 0, 0.9)], 0.09, m["energy"], segs=10, parent=root, outline=False)
    for i in range(4):
        G.torus(f"coil{i}", 0.285, 0.026, m["gold"], m=T((0, 0, 0.4 + 0.15 * i), (90, 0, 0)), segs=24, parent=root)
    G.lathe("cap_top", [(0, 0.92), (0.32, 0.92), (0.3, 1.02), (0.12, 1.06), (0, 1.06)], m["iron"], segs=12, smooth=False, parent=root)
    G.tube("terminal", [(0, 0, 1.06), (0, 0, 1.2)], 0.04, m["steel"], segs=8, parent=root)
    G.uvsphere("terminal_ball", 0.07, m["energy"], m=T((0, 0, 1.24)), parent=root)


def piece_whetstone(root, m, rng):
    G.box("stand", (0.9, 0.42, 0.12), m["wood"], m=T((0, 0, 0.24)), bevel=0.03, parent=root)
    for sx in (-1, 1):
        G.box(f"leg{sx}", (0.1, 0.4, 0.1), m["wood"], m=T((0.35 * sx, 0, 0.18)), bevel=0.02, parent=root)
    G.box("stone", (0.7, 0.3, 0.2), m["stone"], m=T((0, 0, 0.4)), bevel=0.05, segs=2, parent=root)
    G.blade("blade", [(0, 0.0), (0, 0.5), (0, 0.9)], [0.09, 0.085, 0.07], [0.035, 0.035, 0.03], m["steel"],
            tip=(0, 1.08), m=T((-0.42, -0.06, 0.56), (0, 66, 0)), parent=root)
    G.box("guard", (0.34, 0.1, 0.08), m["gold"], m=T((-0.42, -0.06, 0.56), (0, 66, 0)), bevel=0.02, parent=root)
    G.tube("grip", [(-0.42 - 0.2 * 0.914, -0.06, 0.56 - 0.2 * 0.407), (-0.42, -0.06, 0.56)], 0.04, m["wood"], segs=8, parent=root)
    for i in range(7):
        x, z = 0.2 + rng.uniform(-0.15, 0.25), 0.62 + rng.uniform(0.0, 0.35)
        r = 0.035 + rng.uniform(0, 0.03)
        G.slab(f"spark{i}", G.star2d(4, r, r * 0.3, rng.uniform(0, 0.6)), 0.015, m["energy"], m=T((x, -0.25, z), (-30, 0, 0)),
               parent=root, outline=False)


PIECES = {"conduit": piece_conduit, "echo_relay": piece_echo_relay, "splitter": piece_splitter,
          "amplifier": piece_amplifier, "capacitor": piece_capacitor, "whetstone": piece_whetstone}


def build_piece(root, piece, seed):
    rng = random.Random(seed)
    m = _mats(piece, seed)
    base_plate(root, m)
    PIECES[piece](root, m, rng)
