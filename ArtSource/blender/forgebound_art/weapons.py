"""Modular procedural weapons: 12 Frames x 12 Cores x tiers 1..3 + legendary.

Every Frame builder creates its parts (parented to `root`) and returns a
dict of sockets:
    gem:    (Matrix)            where the Core gem sits (gem faces -Y / camera)
    gem_size: float
    inlays: [[(x,y,z), ...]]    polylines that receive emissive Core runes
    crown:  ((x,z), base_angle_deg, spread_deg, size)  legendary accent fan
    grip:   (x,y,z)             hand position -> in-game pivot
    icon_rot / spin / tilt:     presentation angles (degrees)
"""
from __future__ import annotations

import math
import random

from mathutils import Matrix, Vector

from . import geometry as G
from . import materials as M
from . import palette

T = G.trs


class Kit:
    """Materials for one weapon build (core-tinted rim on everything)."""

    def __init__(self, core: str, tier: int, legendary: bool, seed: int):
        self.core, self.tier, self.legendary = core, tier, legendary
        self.rng = random.Random(seed)
        cp = palette.CORE_PALETTES[core] if core else None
        rim = cp["glow"] if cp else "#FFE8C8"
        sfx = f"_{core or 'none'}"
        self.steel = M.library_material("forged_steel", rim=rim, rim_strength=0.85, seed=seed, suffix=sfx)
        if legendary and cp:
            self.steel = M.toon_material(f"FB_steel_leg_{core}", palette.mix_hex("#AEB8C4", cp["primary"], 0.28),
                                         palette.mix_hex("#505A6E", cp["dark"], 0.5), "#FFFFFF", rim=cp["glow"],
                                         rim_strength=1.0, metal=1.0, spec_amt=1.0, spec_size=0.07, paint=0.2, seed=seed)
        self.iron = M.library_material("dark_iron", rim=rim, seed=seed, suffix=sfx)
        self.gold = M.library_material("gold_trim", rim=rim, seed=seed, suffix=sfx)
        self.leather = M.library_material("leather_wrap", rim=rim, rim_strength=0.5, seed=seed, suffix=sfx)
        self.wood = M.library_material("wood", rim=rim, rim_strength=0.5, seed=seed, suffix=sfx)
        self.parch = M.library_material("parchment", rim=rim, rim_strength=0.4, seed=seed, suffix=sfx)
        self.cloth = M.core_accent_material(core) if core else M.library_material("cloth_red", seed=seed)
        self.gem = M.core_gem_material(core) if core else self.gold
        self.rune = M.core_rune_material(core, 0.9) if core else self.gold
        # heraldic leather/cloth (shield faces, tome covers): core colour, deepened
        if cp:
            hb = palette.mix_hex(cp["primary"], cp["dark"], 0.45)
            self.herald = M.toon_material(f"FB_herald_{core}", hb, palette.mix_hex(cp["dark"], "#000000", 0.35),
                                          palette.mix_hex(cp["primary"], "#FFFFFF", 0.25), rim=cp["glow"], rim_strength=0.7,
                                          metal=0.0, spec_amt=0.15, spec_size=0.03, paint=0.35, seed=seed)
        else:
            self.herald = M.library_material("cloth_red", seed=seed)
        # metal used for guards / fittings: iron at T1, steel T2, gold T3+
        self.fitting = self.iron if tier <= 1 else (self.steel if tier == 2 else self.gold)
        self.trim = self.gold if tier >= 2 else self.iron


def _wrap_profile(z0, z1, r, bumps, amp=0.18):
    prof = [(0, z0), (r, z0)]
    n = bumps * 2
    for i in range(1, n):
        z = z0 + (z1 - z0) * i / n
        prof.append((r * (1 + (amp if i % 2 else 0.0)), z))
    prof += [(r, z1), (0, z1)]
    return prof


def grip_wrap(name, z0, z1, r, k, root, bumps=6):
    # grips are wrapped in the Core's heraldic colour so identity reads at phone size
    return G.lathe(name, _wrap_profile(z0, z1, r, bumps), k.herald if k.core else k.leather, segs=12, parent=root)


# --------------------------------------------------------------------- frames
def frame_sword(root, k):
    blade = G.blade("blade", [(0, 0.04), (0, 0.4), (0, 1.15), (0, 1.5)], [0.115, 0.11, 0.1, 0.075],
                    [0.045, 0.044, 0.04, 0.032], k.steel, tip=(0, 1.78), parent=root)
    G.box("guard_core", (0.24, 0.14, 0.15), k.fitting, bevel=0.025, parent=root)
    G.tube("guard", [(-0.36, 0, 0.13), (-0.33, 0, 0.05), (-0.18, 0, 0.0), (0, 0, -0.01), (0.18, 0, 0.0), (0.33, 0, 0.05), (0.36, 0, 0.13)],
           [0.04, 0.052, 0.055, 0.058, 0.055, 0.052, 0.04], k.fitting, flat_y=0.8, parent=root)
    grip_wrap("grip", -0.36, -0.05, 0.05, k, root)
    G.uvsphere("pommel", 0.088, k.trim if k.tier >= 2 else k.iron, m=T((0, 0, -0.41)), scale=(1, 0.8, 1.05), parent=root)
    if k.tier >= 2:
        with G.tagged(extra=True):
            for sx in (-1, 1):
                G.uvsphere(f"guard_tip{sx}", 0.04, k.gold, m=T((0.37 * sx, 0, 0.15)), parent=root)
    if k.tier >= 3:
        with G.tagged(extra=True):
            for sx in (-1, 1):
                G.slab(f"wing{sx}", [(0, 0), (0.16 * sx, 0.04), (0.2 * sx, 0.16), (0.07 * sx, 0.07)], 0.03, k.gold,
                       m=T((0.04 * sx, 0, 0.04)), bevel=0.006, parent=root)
            G.blade("edge_gild", [(0, 0.06), (0, 0.3)], [0.122, 0.118], [0.022, 0.022], k.gold, parent=root)
    return {"gem": T((0, -0.075, 0.0)), "gem_size": 0.1,
            "inlays": [[(0, -0.046, 0.2), (0, -0.042, 1.3)]],
            "crown": ((0, 0.12), 0.0, 120.0, 0.32), "grip": (0, 0, -0.2), "icon_rot": 45, "spin": 18, "tilt": 0}


def frame_greatsword(root, k):
    st = [(0, 0.04), (0, 0.24), (0, 0.27), (0, 0.33), (0, 0.37), (0, 1.0), (0, 1.75), (0, 2.0)]
    hw = [0.07, 0.07, 0.2, 0.2, 0.16, 0.17, 0.15, 0.11]
    G.blade("blade", st, hw, [0.05] * 5 + [0.048, 0.042, 0.034], k.steel, tip=(0, 2.3), parent=root)
    guard = [(-0.5, 0.02), (-0.46, -0.08), (-0.12, -0.04), (0, -0.07), (0.12, -0.04), (0.46, -0.08), (0.5, 0.02),
             (0.4, 0.06), (0.12, 0.06), (0, 0.1), (-0.12, 0.06), (-0.4, 0.06)]
    G.slab("guard", guard, 0.13, k.fitting, bevel=0.018, parent=root)
    grip_wrap("grip", -0.62, -0.06, 0.058, k, root, bumps=9)
    G.lathe("pommel", [(0, -0.78), (0.05, -0.78), (0.1, -0.72), (0.08, -0.66), (0.04, -0.62), (0, -0.62)], k.fitting,
            segs=8, smooth=False, parent=root)
    if k.tier >= 2:
        with G.tagged(extra=True):
            G.box("guard_band", (1.0, 0.11, 0.025), k.gold, m=T((0, 0, 0.0)), bevel=0.008, parent=root)
    if k.tier >= 3:
        with G.tagged(extra=True):
            for sx in (-1, 1):
                G.spike(f"guard_spike{sx}", (0.47 * sx, 0, 0.0), (0.62 * sx, 0, 0.12), 0.04, k.gold, parent=root)
            G.blade("ricasso_gild", [(0, 0.27), (0, 0.33)], [0.21, 0.21], [0.03, 0.03], k.gold, parent=root)
    return {"gem": T((0, -0.085, 0.01)), "gem_size": 0.12,
            "inlays": [[(0, -0.054, 0.45), (0, -0.05, 1.85)]],
            "crown": ((0, 0.12), 0.0, 130.0, 0.4), "grip": (0, 0, -0.3), "icon_rot": 45, "spin": 18, "tilt": 0}


def frame_dagger(root, k):
    c = [(0, 0.04), (0.012, 0.22), (0.04, 0.42), (0.085, 0.58)]
    G.blade("blade", c, [0.085, 0.085, 0.072, 0.05], [0.04, 0.038, 0.032, 0.026], k.steel, section="single",
            tip=(0.16, 0.73), parent=root)
    G.tube("guard", [(-0.15, 0, -0.06), (-0.08, 0, 0.0), (0, 0, 0.012), (0.08, 0, 0.0), (0.14, 0, 0.07)],
           [0.034, 0.042, 0.046, 0.042, 0.032], k.fitting, flat_y=0.8, parent=root)
    grip_wrap("grip", -0.23, -0.01, 0.042, k, root, bumps=4)
    G.torus("pommel_ring", 0.065, 0.024, k.trim if k.tier >= 2 else k.iron, m=T((0, 0, -0.3)), parent=root)
    if k.tier >= 3:
        with G.tagged(extra=True):
            G.spike("guard_fang", (-0.13, 0, -0.06), (-0.2, 0, -0.17), 0.022, k.gold, parent=root)
            G.blade("spine_gild", [(0.02, 0.06), (0.03, 0.3)], [0.05, 0.05], [0.03, 0.03], k.gold, section="single", parent=root)
    return {"gem": T((0, -0.06, 0.01)), "gem_size": 0.075,
            "inlays": [[(0.0, -0.04, 0.1), (0.018, -0.038, 0.3), (0.045, -0.032, 0.48)]],
            "crown": ((0, 0.06), 0.0, 120.0, 0.22), "grip": (0, 0, -0.12), "icon_rot": 45, "spin": 16, "tilt": 0}


def frame_staff(root, k):
    shaft = [(0, 0, -0.85), (0.01, 0, -0.4), (0.0, 0, 0.2), (0.015, 0, 0.8), (0.0, 0, 1.0)]
    G.tube("shaft", shaft, [0.05, 0.056, 0.06, 0.066, 0.072], k.wood, segs=10, parent=root)
    G.lathe("ferrule", [(0, -0.97), (0.03, -0.97), (0.062, -0.85), (0.062, -0.76), (0, -0.76)], k.iron, segs=8, parent=root)
    grip_wrap("grip", -0.15, 0.2, 0.07, k, root, bumps=5)
    G.lathe("collar", [(0, 0.93), (0.085, 0.93), (0.105, 1.02), (0.085, 1.1), (0, 1.1)], k.fitting, segs=10, parent=root)
    orb_c = Vector((0, 0, 1.36))
    for i, ang in enumerate((-60, 60, 180)):
        a = math.radians(ang)
        d = Vector((math.sin(a), math.cos(a) * 0.6, 0))
        pts = [Vector((0, 0, 1.06)) + d * 0.05, orb_c + d * 0.26 + Vector((0, 0, -0.2)), orb_c + d * 0.31 + Vector((0, 0, 0.03)),
               orb_c + d * 0.21 + Vector((0, 0, 0.25)), orb_c + d * 0.08 + Vector((0, 0, 0.33))]
        G.tube(f"prong{i}", [tuple(p) for p in pts], [0.042, 0.04, 0.036, 0.028, 0.016], k.fitting, segs=8, parent=root)
    if k.tier >= 2:
        with G.tagged(extra=True):
            for z in (0.3, -0.3):
                G.torus(f"band{z}", 0.068, 0.018, k.gold, m=T((0, 0, z), (90, 0, 0)), parent=root)
    if k.tier >= 3:
        with G.tagged(extra=True):
            for sx in (-1, 1):
                G.slab(f"wing{sx}", [(0, 0), (0.22 * sx, 0.1), (0.26 * sx, 0.3), (0.1 * sx, 0.12)], 0.03, k.gold,
                       m=T((0.05 * sx, 0, 0.95)), bevel=0.006, parent=root)
    return {"gem": T(tuple(orb_c)), "gem_size": 0.2,
            "inlays": [[(0.0, -0.064, 0.3), (0.014, -0.068, 0.85)]],
            "crown": ((0, 1.34), 0.0, 200.0, 0.3), "grip": (0, 0, 0.0), "icon_rot": 45, "spin": 10, "tilt": 0}


def frame_hammer(root, k):
    G.tube("handle", [(0, 0, -0.75), (0, 0, 0.0), (0, 0, 0.62)], [0.056, 0.06, 0.066], k.wood, parent=root)
    grip_wrap("grip", -0.72, -0.3, 0.07, k, root, bumps=5)
    G.uvsphere("knob", 0.08, k.iron, m=T((0, 0, -0.79)), parent=root)
    G.box("head", (0.5, 0.28, 0.32), k.iron, m=T((0, 0, 0.78)), bevel=0.04, segs=2, parent=root)
    G.lathe("face", [(0, 0.24), (0.17, 0.24), (0.19, 0.3), (0.16, 0.36), (0, 0.36)], k.steel, segs=8, smooth=False,
            m=T((0, 0, 0.78), (0, 90, 0)), parent=root)
    G.cone("back_spike", 0.13, 0.0, 0.4, k.steel, m=T((-0.24, 0, 0.78), (0, -90, 0)), segs=6, parent=root)
    G.cone("top_spike", 0.06, 0.0, 0.16, k.steel, m=T((0, 0, 0.93)), segs=6, parent=root)
    for x in (-0.13, 0.13):
        G.box(f"strap{x}", (0.05, 0.3, 0.34), k.fitting, m=T((x, 0, 0.78)), bevel=0.01, parent=root)
    if k.tier >= 2:
        with G.tagged(extra=True):
            G.torus("collar", 0.055, 0.016, k.gold, m=T((0, 0, 0.6), (90, 0, 0)), parent=root)
    if k.tier >= 3:
        with G.tagged(extra=True):
            for sz in (-1, 1):
                G.slab(f"flange{sz}", [(-0.12, 0), (0.12, 0), (0.06, 0.12 * sz), (-0.06, 0.12 * sz)], 0.06, k.gold,
                       m=T((0, 0, 0.78 + 0.16 * sz)), bevel=0.01, parent=root)
    return {"gem": T((0, -0.15, 0.78)), "gem_size": 0.095,
            "inlays": [[(-0.06, -0.145, 0.68), (-0.06, -0.145, 0.88)], [(0.06, -0.145, 0.68), (0.06, -0.145, 0.88)]],
            "crown": ((0, 0.95), 0.0, 140.0, 0.34), "grip": (0, 0, -0.5), "icon_rot": 40, "spin": 12, "tilt": 0}


def frame_spear(root, k):
    G.tube("shaft", [(0, 0, -0.95), (0, 0, 0.0), (0, 0, 0.92)], [0.046, 0.05, 0.05], k.wood, parent=root)
    G.lathe("butt", [(0, -1.05), (0.03, -1.05), (0.058, -0.95), (0.058, -0.87), (0, -0.87)], k.iron, segs=8, parent=root)
    grip_wrap("grip", -0.35, 0.05, 0.06, k, root, bumps=5)
    G.lathe("socket", [(0, 0.8), (0.068, 0.8), (0.08, 0.9), (0.055, 1.02), (0, 1.04)], k.fitting, segs=8, smooth=False, parent=root)
    G.blade("head", [(0, 1.0), (0, 1.12), (0, 1.32), (0, 1.48)], [0.065, 0.16, 0.13, 0.065], [0.045, 0.045, 0.038, 0.03], k.steel,
            tip=(0, 1.66), parent=root)
    for sx in (-1, 1):
        G.slab(f"lug{sx}", [(0, 0), (0.12 * sx, 0.05), (0.14 * sx, 0.1), (0.03 * sx, 0.06)], 0.035, k.fitting,
               m=T((0.02 * sx, 0, 0.86)), bevel=0.006, parent=root)
    for i, x in enumerate((-0.03, 0.0, 0.03)):
        G.tube(f"tassel{i}", [(x, -0.01, 0.8), (x * 2.2 - 0.03, -0.02, 0.62), (x * 3 - 0.07, -0.02, 0.45)], [0.03, 0.026, 0.014],
               k.cloth, segs=6, parent=root)
    G.torus("tassel_ring", 0.058, 0.016, k.trim, m=T((0, 0, 0.8), (90, 0, 0)), parent=root)
    if k.tier >= 3:
        with G.tagged(extra=True):
            G.blade("head_gild", [(0, 1.02), (0, 1.1)], [0.08, 0.11], [0.034, 0.034], k.gold, parent=root)
            for z in (0.0, 0.3):
                G.torus(f"band{z}", 0.04, 0.012, k.gold, m=T((0, 0, z), (90, 0, 0)), parent=root)
    return {"gem": T((0, -0.08, 0.92)), "gem_size": 0.065,
            "inlays": [[(0, -0.05, 1.08), (0, -0.046, 1.42)]],
            "crown": ((0, 1.0), 0.0, 150.0, 0.3), "grip": (0, 0, -0.15), "icon_rot": 45, "spin": 15, "tilt": 0}


def frame_crossbow(root, k):
    stock = [(-0.07, -0.75), (0.07, -0.75), (0.1, -0.55), (0.06, -0.3), (0.05, 0.6), (-0.05, 0.6), (-0.06, -0.3), (-0.1, -0.55)]
    G.slab("stock", stock, 0.14, k.wood, bevel=0.02, parent=root)
    G.box("rail", (0.05, 0.05, 0.75), k.iron, m=T((0, -0.08, 0.2)), bevel=0.01, parent=root)
    prod = []
    for i in range(13):
        u = -1 + 2 * i / 12
        prod.append((u * 0.62, 0, 0.5 - 0.16 * u * u + 0.05 * u ** 4))
    G.tube("prod", prod, [0.022 + 0.022 * (1 - abs(-1 + 2 * i / 12)) for i in range(13)], k.steel, segs=8, parent=root)
    tipl, tipr = prod[0], prod[-1]
    nut = (0, -0.03, 0.08)
    G.tube("string_l", [tipl, nut], 0.009, k.iron, segs=5, parent=root)
    G.tube("string_r", [tipr, nut], 0.009, k.iron, segs=5, parent=root)
    G.tube("bolt", [(0, -0.12, 0.08), (0, -0.12, 0.7)], 0.016, k.wood, segs=6, parent=root)
    G.cone("bolt_head", 0.04, 0.0, 0.11, k.steel, m=T((0, -0.12, 0.7)), segs=4, parent=root)
    G.torus("stirrup", 0.11, 0.018, k.fitting, m=T((0, 0, 0.62)), arc=0.5, segs=12, parent=root)
    G.box("trigger", (0.03, 0.04, 0.12), k.iron, m=T((0, 0, -0.25), (0, 20, 0)), parent=root)
    for sx in (-1, 1):
        G.box(f"clamp{sx}", (0.1, 0.17, 0.08), k.fitting, m=T((0, 0, 0.47)), bevel=0.01, parent=root)
    if k.tier >= 2:
        with G.tagged(extra=True):
            for p in (tipl, tipr):
                G.uvsphere(f"prod_cap{p[0]:.2f}", 0.035, k.gold, m=T(p), parent=root)
    if k.tier >= 3:
        with G.tagged(extra=True):
            G.slab("butt_plate", [(-0.1, -0.78), (0.1, -0.78), (0.12, -0.6), (-0.12, -0.6)], 0.16, k.gold, bevel=0.01, parent=root)
    return {"gem": T((0, -0.085, -0.45)), "gem_size": 0.085,
            "inlays": [[(-0.035, -0.075, -0.25), (-0.035, -0.075, 0.4)], [(0.035, -0.075, -0.25), (0.035, -0.075, 0.4)]],
            "crown": ((0, 0.5), 0.0, 150.0, 0.32), "grip": (0, 0, -0.3), "icon_rot": 45, "spin": 8, "tilt": 0}


def frame_bow(root, k):
    pts, rad = [], []
    n = 21
    for i in range(n):
        u = -1 + 2 * i / (n - 1)
        x = -0.3 * (1 - u * u)
        if abs(u) > 0.82:  # recurve tips flick forward
            x += 0.5 * (abs(u) - 0.82) ** 1.4
        pts.append((x, 0, u * 1.05))
        rad.append(0.078 - 0.046 * abs(u) ** 1.2)
    G.tube("limbs", pts, rad, k.wood, segs=10, flat_y=0.7, parent=root)
    tip_l, tip_r = pts[0], pts[-1]
    G.tube("string", [(tip_l[0], 0, tip_l[2]), (tip_r[0], 0, tip_r[2])], 0.007, k.iron, segs=5, parent=root)
    grip_wrap("grip", -0.16, 0.16, 0.075, k, root, bumps=4)
    for p in (tip_l, tip_r):
        G.uvsphere(f"nock{p[2]:.1f}", 0.04, k.trim, m=T(p), parent=root)
    if k.tier >= 2:
        with G.tagged(extra=True):
            for z in (0.4, -0.4):
                x = -0.3 * (1 - (z / 1.05) ** 2)
                G.torus(f"band{z}", 0.045, 0.012, k.gold, m=T((x, 0, z), (90, 0, 0)), parent=root)
    if k.tier >= 3:
        with G.tagged(extra=True):
            for sz in (-1, 1):
                G.slab(f"limb_blade{sz}", [(0, 0), (-0.14, 0.06 * sz), (-0.16, 0.2 * sz), (-0.05, 0.1 * sz)], 0.03, k.gold,
                       m=T((-0.22, 0, 0.4 * sz)), bevel=0.006, parent=root)
    return {"gem": T((-0.3, -0.08, 0.0)), "gem_size": 0.085,
            "inlays": [[(p[0], -0.035, p[2]) for p in pts[3:8]], [(p[0], -0.035, p[2]) for p in pts[13:18]]],
            "crown": ((-0.3, 0.0), -90.0, 140.0, 0.3), "grip": (-0.3, 0, 0.0), "icon_rot": 45, "spin": 0, "tilt": 0}


def frame_scythe(root, k):
    G.tube("shaft", [(0, 0, -0.95), (0.03, 0, -0.5), (0, 0, 0.1), (-0.02, 0, 0.7), (0.02, 0, 1.05)],
           [0.05, 0.052, 0.054, 0.054, 0.056], k.wood, parent=root)
    G.lathe("butt", [(0, -1.02), (0.03, -1.02), (0.06, -0.95), (0, -0.9)], k.iron, segs=8, parent=root)
    G.tube("peg", [(0.0, 0, 0.0), (0.2, 0, 0.07)], 0.036, k.wood, segs=8, parent=root)
    grip_wrap("grip", -0.7, -0.4, 0.062, k, root, bumps=4)
    c = [(0.05, 1.04), (-0.25, 1.12), (-0.55, 1.1), (-0.82, 0.97), (-1.0, 0.78)]
    G.blade("blade", c, [0.15, 0.165, 0.14, 0.1, 0.06], [0.044, 0.04, 0.035, 0.028, 0.022], k.steel, section="single",
            tip=(-1.07, 0.6), parent=root)
    G.box("collar", (0.16, 0.14, 0.22), k.fitting, m=T((0.02, 0, 1.03)), bevel=0.02, parent=root)
    G.cone("top_spike", 0.05, 0.0, 0.14, k.fitting, m=T((0.04, 0, 1.12), (0, 15, 0)), segs=6, parent=root)
    if k.tier >= 3:
        with G.tagged(extra=True):
            G.blade("spine_gild", [(0.0, 1.12), (-0.3, 1.2), (-0.55, 1.18)], [0.04, 0.04, 0.03], [0.034, 0.03, 0.028], k.gold,
                    section="single", m=T((0, 0, -0.035)), parent=root)
    if k.tier >= 2:
        with G.tagged(extra=True):
            for z in (-0.3, -0.75):
                G.torus(f"band{z}", 0.06, 0.016, k.gold, m=T((0.02, 0, z), (90, 0, 0)), parent=root)
    return {"gem": T((0.02, -0.08, 1.03)), "gem_size": 0.08,
            "inlays": [[(-0.2, -0.03, 1.15), (-0.5, -0.026, 1.13), (-0.75, -0.022, 1.02)]],
            "crown": ((0.02, 1.12), 20.0, 140.0, 0.34), "grip": (0, 0, -0.6), "icon_rot": 35, "spin": 8, "tilt": 0}


def _shield_width(z):
    # heater shape: flat-ish top (z=0.6) -> point (z=-0.75)
    if z > 0.45:
        return 0.55 - (z - 0.45) * 0.4
    t = (0.45 - z) / 1.2
    return 0.55 * math.sqrt(max(0.0, 1 - t ** 2.2))


def frame_bulwark(root, k):
    import bmesh
    zs = [0.62 - i * (1.37 / 24) for i in range(25)]
    cols = 11
    bm = bmesh.new()
    front, back = [], []
    for z in zs:
        w = max(_shield_width(z), 0.002)
        rf, rb = [], []
        for j in range(cols):
            u = -1 + 2 * j / (cols - 1)
            x = u * w
            yb = -0.12 * (1 - u * u)
            rf.append(bm.verts.new((x, yb - 0.04, z)))
            rb.append(bm.verts.new((x, yb + 0.04, z)))
        front.append(rf)
        back.append(rb)
    for a in range(len(zs) - 1):
        for j in range(cols - 1):
            bm.faces.new((front[a][j], front[a + 1][j], front[a + 1][j + 1], front[a][j + 1]))
            bm.faces.new((back[a][j], back[a][j + 1], back[a + 1][j + 1], back[a + 1][j]))
    for a in range(len(zs) - 1):
        for j in (0, cols - 1):
            bm.faces.new((front[a][j], back[a][j], back[a + 1][j], front[a + 1][j]))
    for j in range(cols - 1):
        bm.faces.new((front[0][j], front[0][j + 1], back[0][j + 1], back[0][j]))
        bm.faces.new((front[-1][j], back[-1][j], back[-1][j + 1], front[-1][j + 1]))
    face = G._finish(bm, "face", k.herald, True, parent=root)
    rim = []
    for z in zs[:-1]:
        rim.append((-_shield_width(z), -0.0, z))
    rim.append((0, 0, zs[-1] - 0.01))
    rim += [(_shield_width(z), 0.0, z) for z in reversed(zs[:-1])]
    rim.append(rim[0])
    G.tube("rim", rim, 0.045, k.fitting, segs=8, flat_y=1.3, parent=root)
    G.box("bar_h", (1.02, 0.05, 0.08), k.iron, m=T((0, -0.17, 0.25)), bevel=0.012, parent=root)
    G.box("bar_v", (0.08, 0.05, 1.2), k.iron, m=T((0, -0.17, -0.05)), bevel=0.012, parent=root)
    G.lathe("boss", [(0, 0.0), (0.2, 0.0), (0.18, 0.06), (0.1, 0.11), (0, 0.12)], k.steel, segs=16,
            m=T((0, -0.13, 0.12), (90, 0, 0)), parent=root)
    G.blade("spike", [(0, -0.72), (0, -0.85)], [0.08, 0.06], [0.03, 0.025], k.steel, tip=(0, -1.0),
            m=Matrix.Identity(4), parent=root)
    for sx in (-1, 1):
        G.spike(f"side_spike{sx}", (0.5 * sx, 0, 0.58), (0.72 * sx, 0, 0.74), 0.045, k.steel, parent=root)
    if k.tier >= 2:
        with G.tagged(extra=True):
            for x, z in ((-0.42, 0.5), (0.42, 0.5), (-0.3, -0.35), (0.3, -0.35)):
                G.uvsphere(f"rivet{x}{z}", 0.03, k.gold, m=T((x, -0.12 * (1 - (x / 0.55) ** 2) - 0.05, z)), parent=root)
    if k.tier >= 3:
        with G.tagged(extra=True):
            G.slab("crest", [(-0.24, 0.6), (-0.1, 0.66), (0, 0.8), (0.1, 0.66), (0.24, 0.6), (0, 0.62)], 0.05, k.gold,
                   bevel=0.008, parent=root)
    return {"gem": T((0, -0.27, 0.12)), "gem_size": 0.12,
            "inlays": [[(-0.38, -0.12, 0.42), (0, -0.17, 0.45), (0.38, -0.12, 0.42)], [(0, -0.2, -0.15), (0, -0.2, -0.55)]],
            "crown": ((0, 0.62), 0.0, 120.0, 0.36), "grip": (0, 0, 0.1), "icon_rot": -8, "spin": -14, "tilt": 0,
            "ingame_rot": -8}


def frame_whip(root, k):
    grip_wrap("handle", -0.6, -0.02, 0.062, k, root, bumps=6)
    G.uvsphere("knob", 0.085, k.fitting, m=T((0, 0, -0.67)), scale=(1, 1, 0.8), parent=root)
    G.lathe("collar", [(0, -0.04), (0.078, -0.04), (0.09, 0.02), (0.06, 0.09), (0, 0.09)], k.fitting, segs=10, parent=root)
    pts, rad = [], []
    n = 60
    for i in range(n + 1):
        t = i / n
        if t < 0.25:  # rises out of the handle and arcs right
            s = t / 0.25
            p = (0.35 * s * s, 0, 0.08 + 0.75 * s)
        else:  # loop: spiral around a centre, shrinking
            s = (t - 0.25) / 0.75
            ang = math.pi + s * math.pi * 2.6
            r = 0.42 * (1 - 0.55 * s)
            cx, cz = 0.75, 0.8
            p = (cx + math.cos(ang) * r * 0.95 + (1 - s) * 0.02, 0.05 * math.sin(s * 9), cz + math.sin(ang) * r)
        pts.append(p)
        rad.append(0.062 * (1 - t) + 0.016)
    # blend the joint between the two pieces
    G.tube("lash", pts, rad, k.leather, segs=8, parent=root)
    G.cone("cracker", 0.035, 0.0, 0.15, k.cloth, m=T(pts[-1], (0, -60, 0)), segs=6, parent=root)
    if k.tier >= 2:
        with G.tagged(extra=True):
            for z in (-0.2, -0.45):
                G.torus(f"band{z}", 0.056, 0.013, k.gold, m=T((0, 0, z), (90, 0, 0)), parent=root)
    if k.tier >= 3:
        with G.tagged(extra=True):
            for i in range(5):
                p = pts[18 + i * 7]
                G.spike(f"barb{i}", p, (p[0] + 0.0, p[1] - 0.02, p[2] + 0.09), 0.02, k.gold, parent=root)
    return {"gem": T((0, -0.09, 0.02)), "gem_size": 0.08,
            "inlays": [[(0, -0.068, -0.55), (0, -0.068, -0.08)]],
            "crown": ((0, 0.1), 0.0, 120.0, 0.24), "grip": (0, 0, -0.3), "icon_rot": 35, "spin": 0, "tilt": 0}


def frame_tome(root, k):
    W, H = 0.85, 1.12
    G.box("pages", (W - 0.06, 0.2, H - 0.06), k.parch, m=T((0.02, 0, 0)), bevel=0.01, parent=root)
    cover = k.herald
    G.box("cover_f", (W, 0.05, H), cover, m=T((0, -0.125, 0)), bevel=0.018, segs=2, parent=root)
    G.box("cover_b", (W, 0.05, H), cover, m=T((0, 0.125, 0)), bevel=0.018, segs=2, parent=root)
    G.lathe("spine", [(0, -H / 2), (0.15, -H / 2), (0.15, H / 2), (0, H / 2)], cover, segs=12, m=T((-W / 2 + 0.03, 0, 0)),
            scale_xy=(0.5, 1.0), parent=root)
    for sx, sz in ((1, 1), (1, -1), (-1, 1), (-1, -1)):
        pts = [(0, 0), (-0.17 * sx, 0), (0, -0.17 * sz)]
        if sx * sz < 0:
            pts = list(reversed(pts))
        G.slab(f"corner{sx}{sz}", pts, 0.07, k.fitting, m=T((W / 2 * sx + 0.01 * sx, -0.13, H / 2 * sz + 0.01 * sz)),
               bevel=0.008, parent=root)
    G.box("clasp", (0.2, 0.3, 0.12), k.leather, m=T((W / 2, 0, 0)), bevel=0.02, parent=root)
    G.box("clasp_lock", (0.07, 0.08, 0.14), k.fitting, m=T((W / 2 + 0.06, -0.15, 0)), bevel=0.01, parent=root)
    G.torus("emblem_ring", 0.2, 0.03, k.fitting, m=T((0, -0.165, 0.05)), parent=root)
    for i in range(4):
        a = math.radians(45 + 90 * i)
        G.slab(f"emblem_spoke{i}", [(-0.025, 0.2), (0.025, 0.2), (0.0, 0.34)], 0.03, k.fitting,
               m=T((0, -0.16, 0.05), (0, math.degrees(a), 0)), parent=root)
    if k.tier >= 2:
        with G.tagged(extra=True):
            G.box("band_top", (W + 0.01, 0.06, 0.04), k.gold, m=T((0, -0.13, H / 2 - 0.12)), parent=root)
            G.box("band_bot", (W + 0.01, 0.06, 0.04), k.gold, m=T((0, -0.13, -H / 2 + 0.12)), parent=root)
    if k.tier >= 3:
        with G.tagged(extra=True):
            G.tube("chain", [(-W / 2, -0.05, -H / 2 + 0.1), (-W / 2 - 0.12, -0.05, -H / 2 - 0.05), (-W / 2 - 0.05, -0.05, -H / 2 - 0.22)],
                   0.022, k.gold, segs=6, parent=root)
    return {"gem": T((0, -0.19, 0.05)), "gem_size": 0.14,
            "inlays": [[(-0.3, -0.155, -0.42), (0.3, -0.155, -0.42)], [(-0.3, -0.155, 0.42), (0.3, -0.155, 0.42)]],
            "crown": ((0, 0.56), 0.0, 120.0, 0.34), "grip": (0, 0, 0), "icon_rot": -10, "spin": -28, "tilt": 0,
            "ingame_rot": -10}


FRAME_BUILDERS = {
    "sword": frame_sword, "greatsword": frame_greatsword, "bow": frame_bow, "staff": frame_staff,
    "hammer": frame_hammer, "dagger": frame_dagger, "spear": frame_spear, "crossbow": frame_crossbow,
    "scythe": frame_scythe, "bulwark": frame_bulwark, "whip": frame_whip, "tome": frame_tome,
}


# ---------------------------------------------------------------- core gems
def gem_shape(shape, name, size, mat, m, root, rng):
    s = size
    if shape == "flame":
        pts = [(0, 1.3), (0.25, 0.75), (0.55, 0.35), (0.6, -0.15), (0.35, -0.55), (0, -0.65), (-0.35, -0.55), (-0.6, -0.15),
               (-0.5, 0.3), (-0.2, 0.55), (-0.15, 0.95)]
        return G.slab(name, [(x * s, z * s) for x, z in pts], s * 0.6, mat, m=m, bevel=s * 0.15, parent=root)
    if shape == "crystal":
        return G.lathe(name, [(0, -1.25 * s), (0.5 * s, -0.55 * s), (0.5 * s, 0.55 * s), (0, 1.35 * s)], mat, segs=6,
                       smooth=False, m=m, parent=root)
    if shape == "bolt":
        pts = [(-0.15, 1.3), (0.5, 1.3), (0.15, 0.3), (0.55, 0.3), (-0.3, -1.3), (-0.05, -0.15), (-0.5, -0.15)]
        return G.slab(name, [(x * s, z * s) for x, z in pts], s * 0.45, mat, m=m, bevel=s * 0.08, parent=root)
    if shape == "boulder":
        return G.ico(name, s * 0.85, mat, m=m, subdiv=1, jitter=0.18, rng=rng, scale=(1.1, 0.8, 0.95), parent=root)
    if shape == "leaf":
        pts = []
        for i in range(16):
            a = 2 * math.pi * i / 16
            pts.append((math.sin(a) * 0.55 * abs(math.sin(a)) ** 0.3, math.cos(a) * 1.15))
        return G.slab(name, [(x * s, z * s) for x, z in pts], s * 0.45, mat, m=m @ T((0, 0, 0), (0, 25, 0)), bevel=s * 0.12,
                      parent=root)
    if shape == "crescent":
        pts = []
        for i in range(13):
            a = math.radians(-150 + 300 * i / 12)
            pts.append((math.sin(a) * 1.0, math.cos(a) * 1.0))
        for i in range(13):
            a = math.radians(150 - 300 * i / 12)
            pts.append((math.sin(a) * 0.7 + 0.32, math.cos(a) * 0.72))
        return G.slab(name, [(x * s, z * s) for x, z in pts], s * 0.5, mat, m=m @ T((0, 0, 0), (0, 20, 0)), bevel=s * 0.1, parent=root)
    if shape == "drop":
        return G.lathe(name, [(0, -0.75 * s), (0.45 * s, -0.6 * s), (0.62 * s, -0.2 * s), (0.45 * s, 0.35 * s), (0.18 * s, 0.85 * s),
                              (0, 1.25 * s)], mat, segs=12, m=m @ T((0, 0, 0), (0, 0, 0), (1, 0.75, 1)), parent=root)
    if shape == "star":
        return G.slab(name, G.star2d(5, 1.15 * s, 0.5 * s, rot=math.pi / 2), s * 0.5, mat, m=m, bevel=s * 0.12, parent=root)
    if shape == "swirl":
        pts = []
        for i in range(28):
            t = i / 27
            a = t * math.pi * 2.6
            r = 0.15 + 0.95 * t
            pts.append((math.cos(a) * r * s, -0.1 * s, math.sin(a) * r * s))
        G.uvsphere(name + "_core", 0.4 * s, mat, m=m, scale=(1, 0.7, 1), parent=root)
        return G.tube(name, [tuple(m @ Vector(p)) for p in pts], [0.26 * s * (1 - 0.7 * i / 27) for i in range(28)], mat, segs=6,
                      flat_y=0.6, parent=root)
    if shape == "ingot":
        return G.lathe(name, [(0, -1.25 * s), (0.62 * s, -0.25 * s), (0.62 * s, 0.25 * s), (0, 1.25 * s)], mat, segs=4,
                       smooth=False, m=m @ T((0, 0, 0), (0, 0, 45)), scale_xy=(1.0, 0.6), parent=root)
    if shape == "void_orb":
        o = G.uvsphere(name, 0.7 * s, mat, m=m, parent=root)
        G.torus(name + "_ring", 1.0 * s, 0.09 * s, M.core_rune_material("void", 0.95), m=m @ T((0, 0, 0), (70, 0, -25)), parent=root)
        return o
    if shape == "sun":
        G.slab(name + "_rays", G.star2d(8, 1.25 * s, 0.6 * s), s * 0.3, mat, m=m @ T((0, 0.08 * s, 0)), bevel=s * 0.05, parent=root)
        return G.uvsphere(name, 0.6 * s, mat, m=m @ T((0, -0.1 * s, 0)), scale=(1, 0.7, 1), parent=root)
    raise ValueError(shape)


def add_core(root, k, sockets):
    cp = palette.CORE_PALETTES[k.core]
    gm = sockets["gem"]
    s = sockets["gem_size"] * 1.25
    with G.tagged(extra=True):
        # bezel behind the gem
        G.lathe("bezel", [(0, 0.0), (1.05 * s, 0.0), (1.1 * s, 0.25 * s), (0.9 * s, 0.35 * s), (0, 0.3 * s)], k.trim, segs=12,
                m=gm @ T((0, 0.25 * s, 0), (90, 0, 0)), parent=root)
    with G.tagged(extra=True, glow=True):
        gem_shape(cp["gem"], "gem", s, k.gem, gm @ T((0, -0.12 * s, 0)), root, k.rng)
        for li, path in enumerate(sockets["inlays"]):
            _rune_dashes(f"inlay{li}", path, k, root, wide=k.legendary)


def _rune_dashes(name, path, k, root, wide=False):
    """Emissive rune inlay: alternating long dashes and small diamond glyphs."""
    pts = [Vector(p) for p in path]
    # resample polyline
    total = sum((b - a).length for a, b in zip(pts, pts[1:]))
    def at(d):
        for a, b in zip(pts, pts[1:]):
            l = (b - a).length
            if d <= l:
                return a.lerp(b, d / max(l, 1e-6))
            d -= l
        return pts[-1]
    r = 0.019 if not wide else 0.026
    fwd = Vector((0, -0.006, 0))
    with G.tagged(glow=False):
        G.tube(f"{name}_groove", [tuple(p + Vector((0, 0.004, 0))) for p in pts], r * 1.9, k.iron, segs=8, flat_y=0.35,
               parent=root, outline=False)
    G.tube(f"{name}_line", [tuple(p + fwd) for p in pts], r * 0.75, k.rune, segs=6, flat_y=0.5, parent=root, outline=False)
    n = max(2, int(total / 0.22))
    step = total / n
    for i in range(n):
        c = at(i * step + step * 0.5) + fwd * 1.5
        G.ico(f"{name}_g{i}", r * 1.45, k.rune, m=T(tuple(c)), subdiv=0, scale=(1, 0.5, 1.5), parent=root, outline=False)


# ---------------------------------------------------------- legendary crowns
def add_crown(root, k, sockets):
    cp = palette.CORE_PALETTES[k.core]
    (cx, cz), base, spread, size = sockets["crown"]
    size *= 1.35
    shape = cp["crown"]
    rune = M.core_rune_material(k.core, 0.75)
    accent = M.core_accent_material(k.core)
    n = 5
    with G.tagged(extra=True, glow=True):
        for i in range(n):
            ang = base - spread / 2 + spread * i / (n - 1)
            centre = abs(i - (n - 1) / 2) / ((n - 1) / 2)  # 0 centre .. 1 edge
            L = size * (1.0 - 0.35 * centre)
            a = math.radians(ang)
            d = Vector((math.sin(a), 0, math.cos(a)))
            p = Vector((cx, 0.02 * (i % 2), cz)) + d * size * 0.25
            m = Matrix.Translation(p) @ Matrix.Rotation(a, 4, "Y")
            nm = f"crown{i}"
            if shape == "flames":
                pts = [(0, 1.0), (0.12, 0.6), (0.22, 0.3), (0.2, 0.05), (0, -0.05), (-0.2, 0.05), (-0.16, 0.35), (-0.05, 0.55)]
                G.slab(nm, [(x * L, z * L) for x, z in pts], L * 0.12, rune, m=m, bevel=L * 0.02, parent=root)
            elif shape == "ice_shards":
                G.lathe(nm, [(0, 0), (0.1 * L, 0.2 * L), (0.08 * L, 0.75 * L), (0, 1.0 * L)], rune, segs=5, smooth=False, m=m, parent=root)
            elif shape == "bolts":
                pts = [(-0.05, 0), (0.12, 0.45), (0.0, 0.45), (0.1, 1.0), (-0.12, 0.38), (0.0, 0.38), (-0.14, 0.0)]
                G.slab(nm, [(x * L, z * L) for x, z in pts], L * 0.08, rune, m=m, bevel=L * 0.01, parent=root)
            elif shape == "rocks":
                G.ico(nm, 0.16 * L + 0.02, accent if i % 2 else k.gem, m=m @ T((0, 0, 0.75 * L)), subdiv=1, jitter=0.25, rng=k.rng,
                      parent=root)
                G.lathe(nm + "s", [(0, 0), (0.07 * L, 0.1 * L), (0, 0.6 * L)], rune, segs=5, smooth=False, m=m, parent=root)
            elif shape == "leaves":
                pts = [(math.sin(t) * 0.22 * abs(math.sin(t)) ** 0.3, 0.5 + math.cos(t) * 0.5) for t in
                       [2 * math.pi * j / 14 for j in range(14)]]
                G.slab(nm, [(x * L, z * L) for x, z in pts], L * 0.06, rune if i % 2 else accent, m=m, bevel=L * 0.015, parent=root)
            elif shape == "thorns":
                G.tube(nm, [(0, 0, 0), (0.08 * L, 0, 0.5 * L), (-0.02 * L, 0, 1.0 * L)], [0.07 * L, 0.04 * L, 0.004], accent,
                       segs=6, m=m, parent=root)
            elif shape == "fangs":
                G.tube(nm, [(0, 0, 0), (0.1 * L, 0, 0.55 * L), (0.0, 0, 0.9 * L)], [0.09 * L, 0.05 * L, 0.004],
                       M.library_material("bone"), segs=6, m=m, parent=root)
                G.uvsphere(nm + "drip", 0.05 * L, rune, m=m @ T((0.0, -0.02, 0.92 * L)), scale=(1, 1, 1.5), parent=root)
            elif shape == "rune_rings":
                if i in (0, 2):
                    G.torus(nm, size * (0.55 + 0.25 * i / 2), 0.022, rune, m=T((cx, 0, cz + size * 0.3), (70 - 40 * i / 2, 0, 20 * i)),
                            segs=32, parent=root, outline=False)
                G.slab(nm + "g", G.star2d(4, 0.07 * L + 0.02, 0.025, 0.0), 0.02, rune, m=m @ T((0, 0, 0.9 * L)), parent=root)
            elif shape == "gusts":
                pts = [(0, 0, 0), (0.18 * L, 0, 0.35 * L), (0.05 * L, 0, 0.7 * L), (-0.15 * L, 0, 0.85 * L), (-0.22 * L, 0, 0.7 * L)]
                G.tube(nm, [tuple(m @ Vector(p)) for p in pts], [0.06 * L, 0.05 * L, 0.035 * L, 0.02 * L, 0.006], rune if i % 2 else accent,
                       segs=6, flat_y=0.5, parent=root)
            elif shape == "blades":
                G.blade(nm, [(0, 0), (0, 0.6 * L)], [0.07 * L, 0.06 * L], [0.02 * L, 0.018 * L], M.library_material("forged_steel", rim=cp["glow"], suffix="_crown"),
                        tip=(0, 0.95 * L), m=m, parent=root)
                G.box(nm + "g", (0.18 * L, 0.04 * L, 0.04 * L), k.gold, m=m, parent=root)
            elif shape == "rifts":
                if i == 2:
                    G.torus(nm, size * 0.7, 0.024, rune, m=T((cx, 0, cz + size * 0.3), (72, 0, 15)), segs=32, parent=root)
                G.lathe(nm + "s", [(0, 0), (0.09 * L, 0.25 * L), (0.05 * L, 0.7 * L), (0, 0.9 * L)], k.gem, segs=4, smooth=False, m=m,
                        parent=root)
            elif shape == "halo":
                if i == 2:
                    G.torus(nm, size * 0.62, 0.028, rune, m=T((cx, 0.04, cz + size * 0.35)), segs=32, parent=root)
                    G.slab(nm + "r", G.star2d(12, size * 0.95, size * 0.66), 0.02, accent, m=T((cx, 0.08, cz + size * 0.35)),
                           parent=root)
            else:
                raise ValueError(shape)


def build_weapon(root, frame, core, tier, legendary, seed):
    k = Kit(core, max(tier, 3) if legendary else tier, legendary, seed)
    sockets = FRAME_BUILDERS[frame](root, k)
    if core:
        add_core(root, k, sockets)
    if legendary and core:
        add_crown(root, k, sockets)
    return sockets
