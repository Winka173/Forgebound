"""Battle backgrounds (batch 2): fb_env_<biome>_bg, 1920x1080, opaque.

Side-view, parallax-friendly composition built from flat depth layers that a
future parallax rig can split along the same boundaries:

    sky (posterised gradient + glow bands)  y = 60
    far silhouettes (unlit, atmospheric)    y = 45
    mid structures (toon-lit, faded)        y = 30
    near props (toon-lit, outlined)         y = 12
    ground (front face, top lip at Z = 0)   y = 0     <- ground line at 35 % height
    foreground particles / tufts            y = -5

The camera preset `env` is orthographic, 100 px/unit, centred so Z = 0 lands
at 35 % of the image height.  Colours come from Content/biomes.json palettes
via palette.BIOME_SCENES.  Values behind the play band (Z 0..2.5) are kept
dark and low-contrast so sprites (2 px dark outline, lit from upper left)
stay readable on top.
"""
from __future__ import annotations

import math
import random

import bpy

from . import geometry as G
from . import materials as M
from . import palette, scene as S, spec

T = G.trs
W = 9.8          # half width covered (+ margin)
BOTTOM = -3.9
TOP = 7.2


def flat(name, hexc):
    if name in bpy.data.materials:
        return bpy.data.materials[name]
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    em = nt.nodes.new("ShaderNodeEmission")
    em.inputs["Color"].default_value = palette.hex_to_linear(hexc)
    nt.links.new(em.outputs[0], out.inputs["Surface"])
    return m


def gradient(name, stops, z0, z1, bands=8):
    """Posterised vertical gradient (object Z) — painterly bands, compresses well."""
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    tc = nt.nodes.new("ShaderNodeTexCoord")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    mr = nt.nodes.new("ShaderNodeMapRange")
    mr.inputs[1].default_value, mr.inputs[2].default_value = z0, z1
    mr.clamp = True
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    cr = ramp.color_ramp
    cr.interpolation = "CONSTANT"
    while len(cr.elements) > 1:
        cr.elements.remove(cr.elements[-1])
    for i in range(bands):
        t = i / bands
        # piecewise-linear colour through `stops` [(t, hex)]
        for (ta, ha), (tb, hb) in zip(stops, stops[1:]):
            if ta <= t <= tb:
                c = palette.mix_hex(ha, hb, (t - ta) / max(1e-6, tb - ta))
                break
        el = cr.elements[0] if i == 0 else cr.elements.new(t)
        el.position = t
        el.color = palette.hex_to_linear(c)
    em = nt.nodes.new("ShaderNodeEmission")
    nt.links.new(tc.outputs["Object"], sep.inputs[0])
    nt.links.new(sep.outputs["Z"], mr.inputs[0])
    nt.links.new(mr.outputs[0], ramp.inputs[0])
    nt.links.new(ramp.outputs[0], em.inputs[0])
    nt.links.new(em.outputs[0], out.inputs["Surface"])
    return m


def toon(name, hexc, fog, k, rim="#FFE8C8", rs=0.5, paint=0.35, metal=0.0, emit=0.0, glow=None):
    """Toon material faded toward the fog colour by k (aerial perspective)."""
    base = palette.mix_hex(hexc, fog, k)
    return M.toon_material(name, base, palette.mix_hex(palette.mix_hex(hexc, "#000010", 0.55), fog, k),
                           palette.mix_hex(palette.mix_hex(hexc, "#FFFFFF", 0.35), fog, k), rim=rim, rim_strength=rs, metal=metal,
                           spec_amt=0.2, spec_size=0.04, paint=paint, emit=emit, glow=glow or base, fill=0.25)


def ridge(rng, z, amp, n=40, jag=0.5, x0=-W, x1=W, bottom=BOTTOM):
    pts = []
    ph = rng.uniform(0, 10)
    for i in range(n + 1):
        x = x0 + (x1 - x0) * i / n
        h = z + amp * (0.55 * math.sin(x * 0.45 + ph) + 0.3 * math.sin(x * 1.3 + ph * 2) + jag * 0.25 * rng.uniform(-1, 1))
        pts.append((x, h))
    return pts + [(x1, bottom), (x0, bottom)]


def slab_at(name, pts, y, mat, thick=0.4, outline=False, bevel=0.0):
    pts2 = pts if _area(pts) > 0 else pts[::-1]
    return G.slab(name, pts2, thick, mat, m=T((0, y, 0)), bevel=bevel, outline=outline)


def _area(pts):
    return 0.5 * sum(x0 * y1 - x1 * y0 for (x0, y0), (x1, y1) in zip(pts, pts[1:] + pts[:1]))


def sky(P, glow_xz, glow_r):
    top, hor = P["sky"]
    m = gradient("FB_sky", [(0.0, palette.mix_hex(hor, P["light"], 0.25)), (0.35, hor), (1.0, top)], -0.5, TOP, bands=9)
    G.box("sky", (2 * W + 4, 0.2, TOP - BOTTOM + 4), m, m=T((0, 60, (TOP + BOTTOM) / 2)), outline=False)
    # glow bands (sun / furnace / moon): concentric flat discs, brightening inward
    gx, gz = glow_xz
    for i in range(5):
        r = glow_r * (1.0 - 0.17 * i)
        c = palette.mix_hex(palette.mix_hex(hor, P["light"], 0.3), P["light"], 0.18 * (i + 1))
        G.slab(f"glow{i}", [(gx + math.cos(a) * r, gz + math.sin(a) * r) for a in [2 * math.pi * k / 48 for k in range(48)]], 0.05,
               flat(f"FB_glowband{i}", c), m=T((0, 58 - i * 0.2, 0)), outline=False)


def ground(P, rng, lip_hex=None, body_hex=None, stones=True):
    lip = lip_hex or P["ground"]
    body = body_hex or P["near"]
    gm = gradient("FB_ground_body", [(0.0, palette.mix_hex(body, "#000000", 0.45)), (1.0, body)], BOTTOM, -0.2, bands=6)
    G.box("ground", (2 * W + 2, 4.0, -BOTTOM + 0.2), gm, m=T((0, 2.0, (BOTTOM - 0.2) / 2)), outline=False)
    lipm = toon("FB_ground_lip", lip, P["near"], 0.0, paint=0.4)
    pts = [(-W - 1, -0.22)]
    for i in range(61):
        x = -W - 1 + (2 * W + 2) * i / 60
        pts.append((x, 0.0 + 0.03 * math.sin(x * 2.3) + 0.015 * rng.uniform(-1, 1)))
    pts.append((W + 1, -0.22))
    slab_at("ground_lip", pts, -0.3, lipm, thick=0.6)
    # strata: two darker wavy bands so the ground face reads as layered earth, not a flat fill
    for k, (z0, dz) in enumerate(((-1.0, 0.16), (-2.2, 0.22))):
        top = [(x, z0 + 0.12 * math.sin(x * 0.7 + k * 2) + 0.04 * rng.uniform(-1, 1)) for x in [-W - 1 + (2 * W + 2) * i / 40 for i in range(41)]]
        slab_at(f"stratum{k}", top + [(x, z - dz) for x, z in reversed(top)], -0.35,
                flat(f"FB_stratum{k}", palette.mix_hex(body, "#000000", 0.25 + 0.1 * k)), thick=0.05)
    if stones:
        sm = toon("FB_ground_stone", palette.mix_hex(body, lip, 0.18), P["near"], 0.0, paint=0.45)
        for i in range(12):
            x = rng.uniform(-W, W)
            z = rng.uniform(-3.4, -0.6)
            r = rng.uniform(0.12, 0.3)
            n = rng.randint(5, 7)
            pts = [(x + math.cos(2 * math.pi * k / n + rng.uniform(-0.2, 0.2)) * r * 1.4,
                    z + math.sin(2 * math.pi * k / n + rng.uniform(-0.2, 0.2)) * r) for k in range(n)]
            slab_at(f"stone{i}", pts, -0.45, sm, thick=0.2, outline=True, bevel=0.03)


def particles(P, rng, kind, n=40, zmin=0.3, zmax=6.5):
    gm = flat("FB_particle", palette.mix_hex(P["accent"], "#FFFFFF", 0.3 if kind != "snow" else 0.85))
    for i in range(n):
        x, z = rng.uniform(-W, W), rng.uniform(zmin, zmax)
        r = rng.uniform(0.03, 0.08)
        if kind == "rain":
            G.slab(f"rain{i}", [(x, z), (x + 0.03, z), (x - 0.25, z - 0.9), (x - 0.28, z - 0.9)], 0.02, gm, m=T((0, -6, 0)), outline=False)
        elif kind == "leaf":
            G.slab(f"leaf{i}", [(x, z), (x + r * 1.5, z + r), (x + r * 3, z), (x + r * 1.5, z - r)], 0.02,
                   flat(f"FB_leafp{i % 3}", ["#7DFF8A", "#C6A15B", "#A8E070"][i % 3]), m=T((0, -6, 0), (0, rng.uniform(0, 360), 0)), outline=False)
        else:
            G.slab(f"p{i}", [(x + math.cos(a) * r, z + math.sin(a) * r) for a in [2 * math.pi * k / 8 for k in range(8)]], 0.02, gm,
                   m=T((0, -6, 0)), outline=False)


# ------------------------------------------------------------------ biomes
def ember_foundry(P, rng):
    sky(P, (3.5, 1.2), 4.2)
    far = flat("FB_far", palette.mix_hex(P["far"], P["sky"][1], 0.35))
    slab_at("far_mtn", ridge(rng, 2.6, 1.6, jag=1.0), 45, far)
    lava = flat("FB_far_lava", palette.mix_hex(P["accent"], P["sky"][1], 0.3))
    for i, x in enumerate((-6.5, -1.0, 5.5)):
        z = 3.2 + rng.uniform(0, 0.8)
        slab_at(f"lava_flow{i}", [(x, z), (x + 0.25, z), (x + 0.6, 0.6), (x - 0.3, 0.6)], 44.5, lava)
    fog = P["sky"][1]
    mid = toon("FB_mid", "#5A3026", fog, 0.3, rim=P["light"], rs=0.6)
    midglow = toon("FB_mid_glow", P["accent"], fog, 0.15, emit=0.9, glow=P["light"])
    for i, (x, w, h) in enumerate(((-7.5, 2.4, 2.4), (-3.8, 3.0, 3.0), (1.0, 2.2, 2.0), (6.2, 3.2, 2.7))):
        G.box(f"hall{i}", (w, 2, h), mid, m=T((x, 30, h / 2)), bevel=0.05, outline=False)
        G.slab(f"roof{i}", [(x - w / 2 - 0.2, h), (x + w / 2 + 0.2, h), (x, h + 0.9)], 2, mid, m=T((0, 30, 0)), outline=False)
        for k in range(int(w / 0.8)):
            wx = x - w / 2 + 0.45 + 0.8 * k
            G.box(f"win{i}_{k}", (0.32, 0.1, 0.5), midglow, m=T((wx, 28.9, h * 0.55)), outline=False)
    for i, (x, h) in enumerate(((-5.6, 5.2), (-2.4, 6.0), (3.0, 4.6), (7.6, 5.4))):
        G.cylinder(f"chimney{i}", 0.35, 0.0, h, mid, m=T((x, 31, 0)), segs=12, outline=False)
        G.cylinder(f"chimney_cap{i}", 0.45, h - 0.2, h, mid, m=T((x, 31, 0)), segs=12, outline=False)
        for k in range(3):
            G.uvsphere(f"smoke{i}{k}", 0.5 + 0.25 * k, flat(f"FB_smoke{k}", palette.mix_hex(P["far"], P["sky"][0], 0.3 + 0.2 * k)),
                       m=T((x - 0.4 * k, 32, h + 0.5 + 0.7 * k)), outline=False)
        G.uvsphere(f"chimney_fire{i}", 0.3, midglow, m=T((x, 30.5, h + 0.05)), scale=(1, 1, 0.5), outline=False)
    gear = toon("FB_gear", "#4A2A22", fog, 0.25, rim=P["light"])
    G.gear("big_gear", 1.6, 1.3, 14, 0.3, gear, m=T((-0.9, 25, 3.6)), outline=False)
    G.uvsphere("gear_hub", 0.45, midglow, m=T((-0.9, 24.6, 3.6)), outline=False)
    near = toon("FB_near", P["ground"], P["near"], 0.1, rim=P["light"], rs=0.7)
    iron = toon("FB_near_iron", "#3A3436", P["near"], 0.0, rim=P["light"], rs=0.8, metal=0.6)
    glow = toon("FB_near_glow", P["accent"], P["near"], 0.0, emit=0.95, glow=P["light"])
    for i, x in enumerate((-8.2, 7.9)):   # crucibles framing the edges (out of the play band)
        G.lathe(f"crucible{i}", [(0, 0), (0.7, 0), (0.9, 0.9), (0.95, 1.4), (0, 1.4)], iron, m=T((x, 10, 0)), segs=18)
        G.lathe(f"crucible_lava{i}", [(0, 1.34), (0.88, 1.34), (0, 1.42)], glow, m=T((x, 10, 0)), segs=18, outline=False)
    for i, x in enumerate((-4.6, 4.4)):
        prof = [(-0.7, 0.0), (0.5, 0.0), (0.8, 0.1), (1.0, 0.25), (0.5, 0.28), (0.4, 0.4), (-0.45, 0.4), (-0.5, 0.25), (-0.7, 0.18)]
        G.slab(f"anvil{i}", [(px + x, pz + 0.7) for px, pz in prof], 0.5, iron, m=T((0, 12, 0)), bevel=0.02)
        G.lathe(f"anvil_base{i}", [(0, 0), (0.45, 0), (0.25, 0.4), (0.3, 0.7), (0, 0.7)], iron, m=T((x, 12, 0)), segs=4, smooth=False)
    ground(P, rng)
    # lava seam along the ground front
    pts = [(-W - 1, -0.55)] + [(x, -0.38 + 0.05 * math.sin(x * 1.7)) for x in [-W - 1 + (2 * W + 2) * i / 40 for i in range(41)]] + \
          [(W + 1, -0.55), (W + 1, -0.62)] + [(x, -0.6 + 0.04 * math.sin(x * 2.1)) for x in [W + 1 - (2 * W + 2) * i / 40 for i in range(41)]]
    slab_at("lava_seam", pts, -0.5, glow, thick=0.1)
    particles(P, rng, "ember", 60)


def verdant_ruins(P, rng):
    sky(P, (-4.0, 3.5), 3.6)
    far = flat("FB_far", palette.mix_hex(P["far"], P["sky"][1], 0.4))
    slab_at("far_hills", ridge(rng, 1.8, 0.9, jag=0.2), 45, far)
    fog = P["sky"][1]
    for i in range(16):
        x = -W + i * 1.3 + rng.uniform(-0.3, 0.3)
        r = rng.uniform(0.6, 1.1)
        G.uvsphere(f"far_tree{i}", r, far, m=T((x, 44, 2.3 + rng.uniform(0, 0.8))), scale=(1, 0.3, 1.1), outline=False)
    stone = toon("FB_mid_stone", "#7A8070", fog, 0.45, rim=P["light"], rs=0.5)
    leaf = toon("FB_mid_leaf", "#3E7A44", fog, 0.4, rim=P["accent"], rs=0.4)
    for i, x in enumerate((-7.0, -5.0, 2.0, 4.0, 6.0)):
        h = [3.6, 2.4, 4.2, 1.6, 3.0][i]
        G.cylinder(f"column{i}", 0.42, 0.0, h, stone, m=T((x, 30, 0)), segs=10, outline=False)
        G.box(f"capital{i}", (1.1, 1.1, 0.3), stone, m=T((x, 30, h)), bevel=0.05, outline=False)
        for k in range(3):
            G.uvsphere(f"ivy{i}_{k}", 0.32 - 0.06 * k, leaf, m=T((x + 0.25 - 0.2 * k, 29.4, h * (0.85 - 0.25 * k))), scale=(1.3, 0.3, 0.8),
                       outline=False)
    G.torus("arch", 1.0, 0.3, stone, m=T((5.0, 30, 3.0)), arc=0.5, segs=16, outline=False)
    # great tree, left
    bark = toon("FB_bark", "#5A4030", fog, 0.25, rim=P["light"], rs=0.5)
    G.tube("trunk", [(-2.6, 26, 0), (-2.4, 26, 2.0), (-2.9, 26, 4.2), (-2.6, 26, 5.4)], [0.75, 0.55, 0.45, 0.3], bark, segs=10, outline=False)
    for i in range(7):
        a = 2 * math.pi * i / 7
        G.uvsphere(f"crown{i}", rng.uniform(1.2, 1.7), toon("FB_canopy", "#2E6A3A", fog, 0.25, rim=P["accent"], rs=0.5),
                   m=T((-2.6 + math.cos(a) * 1.7, 25.5, 5.6 + math.sin(a) * 1.0)), scale=(1.1, 0.4, 0.8), outline=False)
    near_stone = toon("FB_near_stone", "#8A8A78", P["near"], 0.1, rim=P["light"], rs=0.6)
    G.box("broken_wall", (2.6, 1.0, 1.1), near_stone, m=T((-7.8, 10, 0.55)), bevel=0.08)
    G.box("fallen_col", (2.2, 0.8, 0.7), near_stone, m=T((7.6, 10, 0.35), (0, 8, 0)), bevel=0.1)
    moss = toon("FB_moss_near", "#4E8E3C", P["near"], 0.0, rim=P["accent"], rs=0.5)
    G.uvsphere("moss1", 0.6, moss, m=T((-7.6, 9.4, 1.1)), scale=(1.8, 0.6, 0.5))
    ground(P, rng)
    grass = toon("FB_grass", "#3E7A3A", P["near"], 0.0, rim=P["accent"], rs=0.6)
    for i in range(30):
        x = -W + i * 0.68 + rng.uniform(-0.2, 0.2)
        h = rng.uniform(0.18, 0.4)
        G.slab(f"tuft{i}", [(x - 0.2, -0.05), (x - 0.12, h), (x - 0.02, 0.05), (x + 0.06, h * 1.2), (x + 0.12, 0.05), (x + 0.2, h * 0.8),
                            (x + 0.25, -0.05)], 0.1, grass, m=T((0, -0.65, 0)))
    for i in range(8):
        x = rng.uniform(-W, W)
        G.uvsphere(f"flower{i}", 0.08, flat(f"FB_flower{i % 2}", ["#FF8AC0", "#FFF27A"][i % 2]), m=T((x, -0.8, rng.uniform(0.1, 0.3))), outline=False)
    particles(P, rng, "ember", 30, zmin=0.5, zmax=4.5)
    particles(P, rng, "leaf", 14)


def frostspire(P, rng):
    sky(P, (4.5, 4.5), 2.2)
    far = flat("FB_far", palette.mix_hex(P["far"], P["sky"][1], 0.3))
    snow_far = flat("FB_far_snow", palette.mix_hex("#E8F6FF", P["sky"][1], 0.45))
    pts = ridge(rng, 3.0, 2.0, jag=1.2, n=26)
    slab_at("far_mtn", pts, 45, far)
    for i, (x, z) in enumerate(pts[:-2]):
        if z > 3.4:
            slab_at(f"snowcap{i}", [(x - 0.5, z - 0.6), (x, z), (x + 0.5, z - 0.6)], 44.6, snow_far)
    fog = P["sky"][1]
    ice = toon("FB_mid_ice", "#7EB8DC", fog, 0.35, rim="#E8FAFF", rs=0.8, metal=0.3)
    wall = toon("FB_mid_wall", "#2E4662", fog, 0.3, rim="#E8FAFF", rs=0.6)
    for i, (x, h, w) in enumerate(((-6.8, 5.6, 0.9), (-4.6, 4.0, 0.7), (3.4, 6.4, 1.0), (5.6, 4.4, 0.8), (7.8, 3.4, 0.6))):
        G.box(f"tower{i}", (w, 1.2, h), wall, m=T((x, 30, h / 2)), outline=False)
        G.cone(f"spire{i}", w * 0.75, 0.0, h * 0.45, ice, m=T((x, 30, h)), segs=6, outline=False)
    G.box("castle_wall", (16.0, 1.0, 1.6), wall, m=T((0.5, 31, 0.8)), outline=False)
    for i in range(12):
        x = -7.5 + i * 1.4
        G.box(f"merlon{i}", (0.6, 1.0, 0.4), wall, m=T((x, 31, 1.8)), outline=False)
    crystal = toon("FB_near_crystal", "#8FD8FF", P["near"], 0.0, rim="#FFFFFF", rs=1.0, metal=0.3, emit=0.15, glow="#E8F6FF")
    for i, (x, h) in enumerate(((-8.3, 2.0), (-7.6, 1.2), (7.8, 1.8), (8.6, 1.1), (-4.9, 0.7), (4.6, 0.8))):
        G.lathe(f"crystal{i}", [(0, 0), (0.25 * h, 0.15 * h), (0.2 * h, h * 0.75), (0, h)], crystal, m=T((x, 10, 0), (0, rng.uniform(-15, 15), 0)),
                segs=6, smooth=False)
    ground(P, rng, lip_hex="#E8F4FF", body_hex="#2A4462")
    snow = toon("FB_snowdrift", "#E8F4FF", P["near"], 0.0, rim="#FFFFFF", rs=0.5)
    for i in range(6):
        x = -W + i * 3.6 + rng.uniform(-0.5, 0.5)
        G.uvsphere(f"drift{i}", 0.9, snow, m=T((x, -0.8, -0.3)), scale=(2.2, 0.4, 0.45))
    particles(P, rng, "snow", 90)


def sunken_crypt(P, rng):
    sky(P, (0.0, 5.0), 1.6)
    far = flat("FB_far", palette.mix_hex(P["far"], P["sky"][1], 0.25))
    pts = [(-W, 1.2)]
    for x, h in ((-7, 3.2), (-6.2, 4.6), (-5.6, 3.2), (-3.0, 3.0), (-2.2, 5.8), (-1.6, 3.0), (2.4, 3.4), (3.4, 6.4), (4.4, 3.4), (7.0, 2.8)):
        pts += [(x - 0.3, pts[-1][1]), (x, h)]
    pts += [(W, 2.0), (W, BOTTOM), (-W, BOTTOM)]
    slab_at("far_cathedral", pts, 45, far)
    fog = P["sky"][1]
    stone = toon("FB_crypt_stone", "#4A4660", fog, 0.35, rim=P["accent"], rs=0.7)
    for i, x in enumerate((-6.0, -2.0, 2.0, 6.0)):
        G.box(f"pillar_l{i}", (0.6, 0.8, 3.6), stone, m=T((x - 1.2, 30, 1.8)), outline=False)
        G.box(f"pillar_r{i}", (0.6, 0.8, 3.6), stone, m=T((x + 1.2, 30, 1.8)), outline=False)
        G.torus(f"arch{i}", 1.2, 0.32, stone, m=T((x, 30, 3.6)), arc=0.5, segs=16, outline=False)
        G.box(f"keystone{i}", (0.4, 0.8, 0.5), stone, m=T((x, 29.8, 4.9)), outline=False)
    glow = toon("FB_crypt_glow", P["accent"], P["near"], 0.0, emit=0.9, glow="#C8A8FF")
    tomb = toon("FB_tomb", "#5A5A70", P["near"], 0.15, rim=P["accent"], rs=0.8)
    for i, (x, h) in enumerate(((-8.2, 1.2), (-6.9, 0.9), (6.8, 1.1), (8.3, 0.8), (-4.5, 0.6))):
        G.slab(f"tomb{i}", [(x - 0.3, 0), (x + 0.3, 0), (x + 0.3, h)] + [(x + math.cos(a) * 0.3, h + math.sin(a) * 0.3) for a in
                                                                      [math.radians(t) for t in range(20, 180, 20)]] + [(x - 0.3, h)], 0.25, tomb,
               m=T((0, 10, 0)), bevel=0.02)
    for i, x in enumerate((-5.6, 5.4)):
        G.cylinder(f"candle{i}", 0.12, 0.0, 0.5, toon("FB_wax", "#E8E0C8", P["near"], 0.0), m=T((x, 9, 0)), segs=10)
        G.slab(f"flame{i}", [(x, 0.5), (x + 0.08, 0.62), (x, 0.82), (x - 0.08, 0.62)], 0.05, glow, m=T((0, 8.8, 0)))
        G.uvsphere(f"halo{i}", 0.35, flat("FB_candle_halo", palette.mix_hex(P["accent"], P["near"], 0.55)), m=T((x, 9.5, 0.66)), scale=(1, 0.1, 1),
                   outline=False)
    ground(P, rng, lip_hex="#3E3E52", body_hex="#16131F")
    water = flat("FB_crypt_water", palette.mix_hex(P["accent"], "#120F1A", 0.7))
    pts = [(-W - 1, -0.3)] + [(x, -0.26 + 0.03 * math.sin(x * 3)) for x in [-W - 1 + (2 * W + 2) * i / 40 for i in range(41)]] + \
          [(W + 1, -0.3), (W + 1, -1.2), (-W - 1, -1.2)]
    slab_at("water", pts, -0.55, water, thick=0.1)
    for i in range(18):
        x = rng.uniform(-W, W)
        slab_at(f"ripple{i}", [(x - 0.4, -0.5 - 0.03 * (i % 3) - 0.18 * (i % 4)), (x + 0.4, -0.5 - 0.03 * (i % 3) - 0.18 * (i % 4)),
                               (x + 0.4, -0.47 - 0.18 * (i % 4)), (x - 0.4, -0.47 - 0.18 * (i % 4))], -0.62,
                flat("FB_ripple", palette.mix_hex(P["light"], "#120F1A", 0.5)), thick=0.02)
    particles(P, rng, "wisp", 26, zmin=0.6, zmax=5.0)


def storm_citadel(P, rng):
    sky(P, (-3.0, 4.2), 2.6)
    fog = P["sky"][1]
    far = flat("FB_far", palette.mix_hex(P["far"], P["sky"][1], 0.3))
    for i, (x, z, w) in enumerate(((-6.5, 4.6, 2.2), (0.5, 5.6, 1.6), (6.0, 4.2, 2.6))):
        slab_at(f"isle{i}", [(x - w, z), (x + w, z), (x + w * 0.5, z - 0.8), (x, z - 1.6), (x - w * 0.6, z - 0.7)], 45, far)
        G.box(f"isle_tower{i}", (0.4, 0.4, 1.2), far, m=T((x, 45, z + 0.6)), outline=False)
    slab_at("far_ridge", ridge(rng, 1.4, 0.7, jag=0.6), 44, far)
    bolt = flat("FB_bolt", "#FFF7A0")
    pts = [(2.8, 7.2), (3.2, 7.2), (2.6, 5.6), (3.1, 5.6), (2.2, 3.6), (2.5, 5.2), (2.0, 5.2)]
    slab_at("lightning", pts, 43, bolt)
    wall = toon("FB_cit_wall", "#3E4670", fog, 0.35, rim=P["accent"], rs=0.7)
    brass = toon("FB_cit_brass", "#C8963C", fog, 0.35, rim=P["accent"], rs=0.8, metal=0.8)
    for i, (x, h) in enumerate(((-7.2, 4.6), (-3.6, 5.8), (3.0, 5.0), (7.0, 4.0))):
        G.cylinder(f"tower{i}", 0.75, 0.0, h, wall, m=T((x, 30, 0)), segs=12, outline=False)
        G.uvsphere(f"dome{i}", 0.85, brass, m=T((x, 30, h)), scale=(1, 1, 0.8), outline=False)
        G.tube(f"rod{i}", [(x, 30, h + 0.6), (x, 30, h + 1.6)], 0.06, brass, segs=6, outline=False)
        G.uvsphere(f"rod_ball{i}", 0.16, flat("FB_rodglow", P["accent"]), m=T((x, 29.8, h + 1.65)), outline=False)
    G.box("bridge", (12.0, 0.6, 0.35), wall, m=T((0.0, 31, 3.0)), outline=False)
    for i in range(9):
        G.box(f"bridge_post{i}", (0.15, 0.6, 0.6), wall, m=T((-5.6 + i * 1.4, 31, 3.4)), outline=False)
    near = toon("FB_cit_near", "#5A5A7A", P["near"], 0.0, rim=P["accent"], rs=0.8)
    nbrass = toon("FB_cit_near_brass", "#C8963C", P["near"], 0.0, rim=P["accent"], rs=0.8, metal=0.9)
    for i, x in enumerate((-8.0, 8.0)):
        G.box(f"pylon{i}", (0.6, 0.6, 2.4), near, m=T((x, 10, 1.2)), bevel=0.05)
        G.torus(f"pylon_ring{i}", 0.45, 0.07, nbrass, m=T((x, 9.6, 2.0)))
        G.uvsphere(f"pylon_orb{i}", 0.3, toon("FB_orb", P["accent"], P["near"], 0.0, emit=0.9, glow="#FFF7A0"), m=T((x, 10, 2.7)))
    G.gear("floor_gear", 0.9, 0.72, 12, 0.2, nbrass, m=T((-5.2, 9, 0.2)))
    G.gear("floor_gear2", 0.6, 0.48, 10, 0.2, nbrass, m=T((5.0, 9, 0.3)))
    ground(P, rng, lip_hex="#6A6A8A", body_hex="#1A1E3A")
    rail = toon("FB_rail", "#C8963C", P["near"], 0.0, rim=P["accent"], rs=0.8, metal=0.9)
    G.box("rail", (2 * W + 2, 0.1, 0.06), rail, m=T((0, -0.7, -0.35)))
    particles(P, rng, "rain", 70)


BIOMES = {"ember_foundry": ember_foundry, "verdant_ruins": verdant_ruins, "frostspire": frostspire, "sunken_crypt": sunken_crypt,
          "storm_citadel": storm_citadel}


def build_env(scene, job):
    scene.render.film_transparent = False
    rng = random.Random(job["seed"])
    P = palette.BIOME_SCENES[job["biome"]]
    BIOMES[job["biome"]](P, rng)
    # outline only near props/ground details (fb_outline set) — width in final pixels per spec.OUTLINE_PX["env"]
    obs = [o for o in S.mesh_objects() if o.get("fb_outline", True) and o.location.y < 20 and _near(o)]
    S.add_outlines(obs, "env")


def _near(o):
    ys = [(o.matrix_world @ v.co).y for v in o.data.vertices[:16]]
    return bool(ys) and min(ys) < 20
