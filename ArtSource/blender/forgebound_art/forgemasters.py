"""The seven batch-2 Forgemasters (the blacksmith lives in characters.py).

Same rig (characters.HUMANOID), camera, scale and ground line as the
blacksmith benchmark.  Each hero is built from shared body helpers plus the
silhouette features named in Content/forgemasters.json ("silhouette"):

    runecrafter  hooded robe, floating rune tablets          sword  / lightning
    alchemist    long coat, bandolier of vials, goggles cap  staff  / venom
    artificer    goggles, brass mechanical arm, toolbelt     bow    / solar
    hexblade     tattered cloak, cracked mask, chained arm   scythe / shadow
    summoner     wide robes, orbiting spectral blades        tome   / arcane
    duelist      slim, cape, plumed hat, fencing stance      dagger / steel
    warden       heavy plate, tower shield, lantern          bulwark/ earth

Each owns one dominant hue (palette.MATERIALS rc_/al_/ar_/hx_/sm_/du_/wd_)
so the 8 heroes also separate by colour.  The held weapon is the real Frame
from weapons.py (tier 1, the hero's Core), bone-parented to the forearm.

Attack clips are templates keyed on armature-space angles (forward positive):
`swing` (sword/scythe), `thrust` (dagger), `cast` (staff/tome), `shoot`
(bow) and `bash` (bulwark); each has anticipation, impact and recovery.
"""
from __future__ import annotations

import math

import bpy
from mathutils import Matrix, Vector

from . import characters as C
from . import geometry as G
from . import materials as M
from . import palette, spec, weapons

T = G.trs

HERO = {
    #               frame      core        attack    weapon length, rest angle (deg from down, fwd +), twist, scale
    "runecrafter": ("sword", "lightning", "swing", 0.95, 150, 0, 1.1),
    "alchemist":   ("staff", "venom", "cast", 1.35, 172, 0, 1.1),
    "artificer":   ("bow", "solar", "shoot", 1.45, 128, 180, 1.1),  # bow 90 deg to the forearm: vertical when the arm is level
    "hexblade":    ("scythe", "shadow", "reap", 1.45, 165, 0, 1.1),
    "summoner":    ("tome", "arcane", "cast_tome", 0.42, 175, 25, 1.1),
    "duelist":     ("dagger", "steel", "thrust", 0.78, 100, 0, 1.12),
    "warden":      ("bulwark", "earth", "bash", 1.05, 180, 55, 1.08),
}
YAW = -25.0


def _lib(key, seed, rim="#FFD9A0"):
    return M.library_material(key, rim=rim, rim_strength=0.7, seed=seed, suffix="_char")


def _glow(name, glow, dark):
    return M.toon_material(name, glow, dark, palette.mix_hex(glow, "#FFFFFF", 0.6), rim=glow, rim_strength=0.4, emit=0.85,
                           glow=palette.mix_hex(glow, "#FFFFFF", 0.4), paint=0.2)


class Body:
    """Collects (object, bone) pairs; helpers build the shared anatomy."""

    def __init__(self, seed, sh_y=0.34):
        self.parts = []
        self.seed = seed
        self.sh_y = sh_y
        self.eye = M.toon_material("FB_eye", "#1B1412", "#000000", "#FFFFFF", rim_strength=0.0, spec_amt=1.0, spec_size=0.2)

    def P(self, o, b):
        self.parts.append((o, b))
        return o

    # -- anatomy ---------------------------------------------------------
    def head(self, skin, r=0.21, z=1.38, nose=True, eyes=True, ears=True):
        self.P(G.uvsphere("head", r, skin, m=T((0.06, 0, z)), scale=(1.0, 0.95, 1.05)), "head")
        if nose:
            self.P(G.uvsphere("nose", 0.055, skin, m=T((0.06 + r * 0.98, 0, z - 0.02)), scale=(1.1, 0.9, 1.0)), "head")
        if ears:
            for sy in (-1, 1):
                self.P(G.uvsphere(f"ear{sy}", 0.05, skin, m=T((0.04, r * 0.95 * sy, z)), scale=(0.8, 0.5, 1.1)), "head")
        if eyes:
            self.P(G.uvsphere("eye", 0.03, self.eye, m=T((0.06 + r * 0.86, -0.09, z + 0.06))), "head")

    def torso(self, mat, chest_mat=None, w=1.0, belly=None, z=0.96):
        self.P(G.uvsphere("torso", 0.28, mat, m=T((0.02, 0, z)), scale=(1.05, 1.15 * w, 1.2)), "spine")
        self.P(G.uvsphere("chest", 0.24, chest_mat or mat, m=T((0.07, 0, z + 0.1)), scale=(1.05, 1.2 * w, 0.9)), "spine")
        self.P(G.uvsphere("hip", 0.26, belly or mat, m=T((0.04, 0, 0.76)), scale=(1.0, 1.05 * w, 0.9)), "hips")

    def arm(self, side, sleeve, hand_mat, r=0.1, cuff=None, shoulder=None, glove=None):
        y = -1 if side == "f" else 1
        sh = Vector((0.0, self.sh_y * y, 1.08))
        el = Vector((0.06 if side == "f" else -0.04, (self.sh_y + 0.02) * y, 0.8))
        ha = Vector((0.2 if side == "f" else 0.04, (self.sh_y + 0.04) * y, 0.62 if side == "f" else 0.6))
        self.P(G.uvsphere(f"shoulder_{side}", r * 1.2, shoulder or sleeve, m=T(tuple(sh))), f"arm_{side}")
        self.P(G.tube(f"upper_{side}", [tuple(sh), tuple(el)], [r, r * 0.88], sleeve, segs=10), f"arm_{side}")
        self.P(G.tube(f"fore_{side}", [tuple(el), tuple(ha)], [r * 0.9, r * 0.95], sleeve, segs=10), f"fore_{side}")
        if cuff:
            self.P(G.tube(f"cuff_{side}", [tuple(el.lerp(ha, 0.55)), tuple(el.lerp(ha, 0.85))], r * 1.15, cuff, segs=10), f"fore_{side}")
        hand = ha + (ha - el).normalized() * 0.05
        self.P(G.uvsphere(f"hand_{side}", r * 0.82, glove or hand_mat, m=T(tuple(hand))), f"fore_{side}")
        return sh, el, ha

    def leg(self, side, mat, boot, r=0.1, boot_h=0.14, hip_y=0.14):
        y = -1 if side == "f" else 1
        self.P(G.tube(f"thigh_{side}", [(0.0, hip_y * y, 0.62), (0.02, hip_y * y, 0.32)], [r * 1.15, r], mat, segs=10), f"thigh_{side}")
        self.P(G.tube(f"shin_{side}", [(0.02, hip_y * y, 0.34), (0.0, hip_y * y, 0.12)], [r * 0.95, r * 0.9], mat, segs=10), f"shin_{side}")
        self.P(G.box(f"boot_{side}", (0.28, 0.15, boot_h), boot, m=T((0.06, hip_y * y, boot_h / 2)), bevel=0.05, segs=2, smooth=True),
               f"shin_{side}")

    def skirt(self, mat, top=0.86, bottom=0.04, r_top=0.3, r_bot=0.46, name="skirt", sx=1.0, x=0.03, bone="hips"):
        prof = [(0, top), (r_top, top), (r_top + (r_bot - r_top) * 0.4, top - (top - bottom) * 0.5), (r_bot, bottom + 0.04),
                (r_bot - 0.02, bottom), (0, bottom)]
        return self.P(G.lathe(name, prof, mat, segs=20, scale_xy=(sx, 1.0), m=T((x, 0, 0))), bone)

    def cape(self, mat, pts, width=0.62, name="cape", bone="spine"):
        """Profile polygon in XZ (behind the body), extruded across both shoulders."""
        return self.P(G.slab(name, pts, width, mat, bevel=0.02), bone)


def _weapon(body, frame, core, seed, length, theta, twist, hand, bone="fore_f", tier=1):
    """Build the real Frame and bone-parent it so its grip sits in `hand`,
    pointing `theta` degrees from straight down toward +X (forward)."""
    root = bpy.data.objects.new("FB_WRoot", None)
    bpy.context.scene.collection.objects.link(root)
    sockets = weapons.build_weapon(root, frame, core, tier, False, seed)
    objs = [o for o in bpy.context.scene.objects if o.type == "MESH" and o.parent == root]
    zs = []
    for o in objs:
        for v in o.data.vertices:
            zs.append((o.matrix_local @ v.co).z)
    s = length / max(1e-6, (max(zs) - min(zs)))
    t = math.radians(theta)
    d = Vector((math.sin(t), 0.0, -math.cos(t)))
    up = Vector((0, 1, 0))
    up = Matrix.Rotation(math.radians(twist), 3, d) @ up
    y_ax = (up - d * up.dot(d)).normalized()
    x_ax = y_ax.cross(d).normalized()
    R = Matrix((x_ax, y_ax, d)).transposed().to_4x4()
    root.matrix_world = Matrix.Translation(hand) @ R @ Matrix.Diagonal((s, s, s, 1)) @ Matrix.Translation(-Vector(sockets["grip"]))
    bpy.context.view_layer.update()
    for o in objs:
        body.P(o, bone)
    return root


# ------------------------------------------------------------------ heroes
def build_runecrafter(seed, body):
    robe, trim, skin = _lib("rc_robe", seed), _lib("rc_trim", seed), _lib("skin_pale", seed)
    leather = _lib("leather_wrap", seed)
    glow = M.core_rune_material("lightning", 0.9)
    stone = M.toon_material("FB_tablet", "#8A8696", "#3A3646", "#C4C0D0", rim="#FFF7A0", rim_strength=0.9, paint=0.4)
    b = body
    b.torso(robe, chest_mat=robe)
    b.skirt(robe, top=0.9, bottom=0.03, r_top=0.29, r_bot=0.44)
    b.P(G.box("sash", (0.08, 0.5, 0.1), trim, m=T((0.3, 0, 0.82), (0, -10, 0)), bevel=0.02), "hips")
    b.P(G.box("robe_trim", (0.05, 0.08, 0.7), trim, m=T((0.4, 0, 0.42), (0, -14, 0)), bevel=0.015), "hips")
    b.head(skin, r=0.19)
    # hood: a cowl around the head, peaked toward the back
    b.P(G.uvsphere("hood", 0.25, robe, m=T((0.0, 0, 1.42)), scale=(1.0, 1.0, 1.05)), "head")
    b.P(G.cone("hood_peak", 0.16, 0.0, 0.34, robe, m=T((-0.12, 0, 1.52), (0, -62, 0)), segs=10, smooth=True), "head")
    b.P(G.torus("hood_rim", 0.2, 0.035, trim, m=T((0.15, 0, 1.38), (0, 90, 0)) @ T((0, 0, 0), (0, 0, 0), (1.15, 1, 1)), segs=20), "head")
    b.P(G.uvsphere("face_shadow", 0.17, M.toon_material("FB_hood_dark", "#1E1A34", "#0A0814", "#3A3460", rim_strength=0.0),
                   m=T((0.1, 0, 1.38)), scale=(0.7, 0.95, 1.0)), "head")
    b.P(G.uvsphere("eye_glow_f", 0.03, glow, m=T((0.25, -0.07, 1.42))), "head")
    b.P(G.uvsphere("eye_glow_b", 0.026, glow, m=T((0.24, 0.07, 1.42))), "head")
    for side in ("f", "b"):
        b.arm(side, robe, skin, r=0.1, cuff=trim)
    for side in ("f", "b"):
        b.leg(side, robe, leather, r=0.09)
    # floating rune tablets (each on its own bone so they bob out of phase)
    for i, (x, y, z, rot) in enumerate(((-0.42, 0.25, 1.55, -12), (-0.55, -0.1, 1.15, 8), (0.05, 0.42, 1.85, 18))):
        tab = G.slab(f"tablet{i}", [(-0.1, -0.14), (0.1, -0.14), (0.1, 0.08)] + [(math.cos(a) * 0.1, 0.08 + math.sin(a) * 0.1)
                                                                              for a in (0.6, 1.2, 1.94, 2.5)] + [(-0.1, 0.08)], 0.05, stone,
                     m=T((x, y, z), (0, rot, 0)), bevel=0.015)
        b.P(tab, f"float{i}")
        b.P(G.slab(f"tablet_glyph{i}", [(-0.02, -0.09), (0.02, -0.09), (0.05, 0.05), (0.0, 0.1), (-0.05, 0.05)], 0.02, glow,
                   m=T((x, y - 0.035, z), (0, rot, 0))), f"float{i}")
    return [("float0", (-0.42, 0.25, 1.4)), ("float1", (-0.55, -0.1, 1.0)), ("float2", (0.05, 0.42, 1.7))]


def build_alchemist(seed, body):
    coat, leather, skin = _lib("al_coat", seed), _lib("al_leather", seed), _lib("skin_tan", seed)
    shirt = _lib("parchment", seed)
    hair = _lib("hair_dark", seed)
    brass = _lib("ar_brass", seed)
    b = body
    b.torso(coat, chest_mat=shirt)
    # long coat: open at the front (coat panels front + long tails behind)
    b.skirt(coat, top=0.86, bottom=0.2, r_top=0.29, r_bot=0.4, name="coat_skirt")
    b.cape(coat, [(-0.05, 1.12), (-0.3, 1.0), (-0.42, 0.5), (-0.5, 0.12), (-0.25, 0.16), (-0.12, 0.6)], width=0.5, name="coat_tail")
    for sy in (-1, 1):
        b.P(G.box(f"lapel{sy}", (0.04, 0.1, 0.36), coat, m=T((0.3, 0.1 * sy, 1.05), (0, -14, 0)), bevel=0.015), "spine")
    # bandolier with vials, diagonal across the chest
    band = [(0.32, -0.2, 1.28), (0.34, -0.05, 1.1), (0.33, 0.1, 0.92), (0.28, 0.22, 0.78)]
    b.P(G.tube("bandolier", band, 0.04, leather, segs=8, flat_y=1.0), "spine")
    vial_cols = [("#A6E22E", "#2E4A0E"), ("#FF8A3A", "#6A2208"), ("#C47AFF", "#3A1466"), ("#5CE1E6", "#0E4A5A")]
    for i, (gc, dc) in enumerate(vial_cols):
        p = Vector(band[0]).lerp(Vector(band[-1]), 0.15 + 0.23 * i) + Vector((0.05, 0, 0))
        vm = _glow(f"FB_vial{i}", gc, dc)
        b.P(G.lathe(f"vial{i}", [(0, -0.07), (0.045, -0.06), (0.05, 0.02), (0.02, 0.05), (0.02, 0.09), (0, 0.09)], vm, segs=10,
                    m=T(tuple(p))), "spine")
        b.P(G.uvsphere(f"cork{i}", 0.022, leather, m=T(tuple(p + Vector((0, 0, 0.1))))), "spine")
    b.head(skin, r=0.2)
    b.P(G.uvsphere("hair", 0.21, hair, m=T((0.02, 0, 1.43)), scale=(1.0, 1.0, 0.85)), "head")
    b.P(G.box("goggle_band", (0.42, 0.44, 0.06), leather, m=T((0.05, 0, 1.5)), bevel=0.02, smooth=True), "head")
    for sy in (-1, 1):
        b.P(G.lathe(f"goggle{sy}", [(0, 0), (0.06, 0), (0.065, 0.05), (0, 0.06)], brass, segs=12,
                    m=T((0.2, 0.08 * sy, 1.53), (0, 70, 0))), "head")
        b.P(G.uvsphere(f"lens{sy}", 0.045, _glow("FB_lens_green", "#A6E22E", "#2E4A0E"), m=T((0.255, 0.08 * sy, 1.555))), "head")
    for side in ("f", "b"):
        b.arm(side, coat, skin, r=0.095, cuff=leather)
    for side in ("f", "b"):
        b.leg(side, _lib("du_vest", seed), leather, r=0.09, boot_h=0.2)
    return []


def build_artificer(seed, body):
    shirt, brass, skin = _lib("ar_shirt", seed), _lib("ar_brass", seed), _lib("skin_dark", seed)
    leather, iron = _lib("leather_wrap", seed), _lib("dark_iron", seed)
    hair = _lib("hair_dark", seed)
    b = body
    b.torso(shirt, belly=_lib("al_leather", seed))
    b.P(G.box("vest", (0.06, 0.42, 0.34), leather, m=T((0.3, 0, 1.0), (0, -10, 0)), bevel=0.02, smooth=True), "spine")
    # toolbelt with pouches + wrench
    b.P(G.lathe("belt", [(0, 0.72), (0.31, 0.72), (0.32, 0.8), (0, 0.8)], leather, segs=16, scale_xy=(1.0, 1.08), m=T((0.04, 0, 0))), "hips")
    for i, (x, y) in enumerate(((0.3, -0.2), (0.12, -0.33), (-0.18, -0.28))):
        b.P(G.box(f"pouch{i}", (0.12, 0.08, 0.14), _lib("al_leather", seed), m=T((x, y, 0.68)), bevel=0.025, smooth=True), "hips")
    b.P(G.tube("wrench", [(0.32, 0.25, 0.85), (0.38, 0.27, 0.55)], 0.025, iron, segs=6), "hips")
    b.P(G.torus("wrench_head", 0.05, 0.02, iron, m=T((0.32, 0.25, 0.88)), segs=10), "hips")
    b.skirt(_lib("al_leather", seed), top=0.8, bottom=0.55, r_top=0.3, r_bot=0.34, name="shorts")
    b.head(skin, r=0.2)
    b.P(G.uvsphere("hair", 0.2, hair, m=T((-0.02, 0, 1.47)), scale=(1.0, 1.0, 0.8)), "head")
    b.P(G.cone("hair_tuft", 0.1, 0.0, 0.18, hair, m=T((-0.02, 0, 1.6), (0, -30, 0)), segs=8, smooth=True), "head")
    # big brass goggles over the eyes
    b.P(G.box("goggle_strap", (0.4, 0.44, 0.07), leather, m=T((0.04, 0, 1.43)), bevel=0.025, smooth=True), "head")
    for sy in (-1, 1):
        b.P(G.lathe(f"goggle{sy}", [(0, 0), (0.08, 0), (0.085, 0.07), (0, 0.08)], brass, segs=14,
                    m=T((0.22, 0.09 * sy, 1.43), (0, 80, 0))), "head")
        b.P(G.uvsphere(f"lens{sy}", 0.06, _glow("FB_lens_solar", "#FFC93D", "#9A5A10"), m=T((0.29, 0.09 * sy, 1.435)), scale=(0.5, 1, 1)),
            "head")
    b.arm("b", shirt, skin, r=0.095, cuff=leather, glove=leather)
    # mechanical FRONT arm (camera side, so it reads): brass segments, piston, gear shoulder, claw hand
    sh, el, ha = Vector((0.0, -0.36, 1.08)), Vector((0.06, -0.38, 0.8)), Vector((0.2, -0.4, 0.62))
    b.P(G.gear("shoulder_gear", 0.17, 0.13, 8, 0.08, brass, m=T(tuple(sh + Vector((0, -0.06, 0))))), "arm_f")
    b.P(G.uvsphere("shoulder_hub", 0.07, iron, m=T(tuple(sh + Vector((0, -0.1, 0))))), "arm_f")
    b.P(G.tube("mech_upper", [tuple(sh), tuple(el)], 0.075, brass, segs=8), "arm_f")
    b.P(G.tube("mech_piston", [tuple(sh + Vector((0.07, -0.05, -0.05))), tuple(el + Vector((0.07, -0.05, 0.05)))], 0.03, iron, segs=6), "arm_f")
    b.P(G.uvsphere("mech_elbow", 0.085, iron, m=T(tuple(el))), "fore_f")
    b.P(G.tube("mech_fore", [tuple(el), tuple(ha)], [0.08, 0.1], brass, segs=8), "fore_f")
    b.P(G.torus("mech_ring", 0.1, 0.022, iron, m=T(tuple(el.lerp(ha, 0.6))) @ T((0, 0, 0), (90, 0, 0)) @ T((0, 0, 0), (0, 38, 0)), segs=14),
        "fore_f")
    hd = (ha - el).normalized()
    for i, a in enumerate((-40, 0, 40)):
        d = (Matrix.Rotation(math.radians(a), 3, "Y") @ hd)
        b.P(G.tube(f"claw{i}", [tuple(ha), tuple(ha + d * 0.1 + Vector((0, -0.02, 0))), tuple(ha + d * 0.17 + Vector((0.03, -0.02, 0.03)))],
                   [0.032, 0.026, 0.012], iron, segs=6), "fore_f")
    # backpack: boiler with a gear and an exhaust pipe (silhouette behind the shoulders)
    b.P(G.lathe("pack", [(0, 0.86), (0.13, 0.86), (0.15, 1.0), (0.13, 1.2), (0, 1.22)], brass, segs=12, m=T((-0.32, 0.05, 0))), "spine")
    b.P(G.tube("exhaust", [(-0.36, 0.08, 1.15), (-0.4, 0.08, 1.45)], 0.04, iron, segs=8), "spine")
    b.P(G.lathe("exhaust_cap", [(0, 1.45), (0.06, 1.45), (0.05, 1.5), (0, 1.5)], iron, segs=8, m=T((-0.4, 0.08, 0))), "spine")
    b.P(G.gear("pack_gear", 0.13, 0.1, 8, 0.05, iron, m=T((-0.3, -0.18, 1.0))), "spine")
    for side in ("f", "b"):
        b.leg(side, _lib("du_vest", seed), leather, r=0.09, boot_h=0.22)
    return []


def build_hexblade(seed, body):
    cloak, mask, skin = _lib("hx_cloak", seed, rim="#B4A0FF"), _lib("hx_mask", seed), _lib("skin_pale", seed)
    iron = _lib("dark_iron", seed, rim="#B4A0FF")
    glow = M.core_rune_material("shadow", 0.9)
    under = _lib("du_vest", seed, rim="#B4A0FF")
    b = body
    b.torso(under)
    b.skirt(cloak, top=0.86, bottom=0.3, r_top=0.29, r_bot=0.38, name="tabard")
    # tattered cloak: ragged hem polygon behind the body, wide over both shoulders
    hem = [(-0.05, 1.22), (-0.3, 1.12), (-0.44, 0.7), (-0.52, 0.18), (-0.42, 0.3), (-0.36, 0.06), (-0.27, 0.22), (-0.2, 0.0),
           (-0.12, 0.28), (-0.05, 0.12), (0.0, 0.5), (0.02, 0.9)]
    b.cape(cloak, hem, width=0.72, name="cloak")
    b.P(G.uvsphere("hood", 0.24, cloak, m=T((-0.02, 0, 1.42)), scale=(1.0, 1.0, 1.08)), "head")
    b.head(skin, r=0.18, nose=False, eyes=False, ears=False)
    # cracked white mask with glowing eye slits
    b.P(G.uvsphere("mask", 0.19, mask, m=T((0.14, 0, 1.38)), scale=(0.5, 0.95, 1.1)), "head")
    b.P(G.box("crack", (0.02, 0.012, 0.22), M.toon_material("FB_crack", "#2A1A3A", "#120A1A", "#3A2A4A", rim_strength=0.0),
              m=T((0.235, 0.03, 1.38), (14, 0, 0))), "head")
    b.P(G.box("eye_slit", (0.03, 0.08, 0.025), glow, m=T((0.23, -0.07, 1.42), (0, 0, 8))), "head")
    b.P(G.box("eye_slit2", (0.03, 0.07, 0.025), glow, m=T((0.22, 0.08, 1.42), (0, 0, -8))), "head")
    sh, el, ha = b.arm("f", under, skin, r=0.095, glove=iron)
    # chained gauntlet: dark plate forearm + chain loops
    b.P(G.tube("gauntlet", [tuple(el.lerp(ha, 0.2)), tuple(ha)], [0.11, 0.125], iron, segs=8), "fore_f")
    for i in range(4):
        p = el.lerp(ha, 0.1 + 0.22 * i) + Vector((0, -0.12, 0))
        b.P(G.torus(f"chain{i}", 0.04, 0.013, glow if i % 2 else iron, m=T(tuple(p), (90 * (i % 2), 0, 0)), segs=10, rsegs=6), "fore_f")
    b.arm("b", cloak, skin, r=0.095)
    for side in ("f", "b"):
        b.leg(side, under, iron, r=0.085, boot_h=0.18)
    return []


def build_summoner(seed, body):
    robe, sash, skin = _lib("sm_robe", seed, rim="#A8C4FF"), _lib("sm_sash", seed, rim="#A8C4FF"), _lib("skin_tan", seed)
    hair = _lib("hair_white", seed)
    gold = _lib("gold_trim", seed)
    spectral = M.toon_material("FB_spectral", "#8EB4FF", "#2A3A9A", "#E8F0FF", rim="#E8F0FF", rim_strength=1.0, emit=0.75, glow="#C8DCFF",
                               paint=0.15)
    b = body
    b.torso(robe, chest_mat=robe)
    b.skirt(robe, top=0.9, bottom=0.02, r_top=0.3, r_bot=0.52, name="robe")
    b.P(G.lathe("sash", [(0, 0.76), (0.32, 0.76), (0.33, 0.86), (0, 0.86)], sash, segs=16, m=T((0.04, 0, 0))), "hips")
    b.P(G.box("sash_tail", (0.06, 0.12, 0.5), sash, m=T((0.36, -0.05, 0.55), (0, -10, 0)), bevel=0.02), "hips")
    # tall flared collar (silhouette)
    b.P(G.lathe("collar", [(0, 1.12), (0.22, 1.12), (0.3, 1.3), (0.33, 1.42), (0.28, 1.42), (0.18, 1.2), (0, 1.2)], sash, segs=16,
                m=T((-0.04, 0, 0)), scale_xy=(0.8, 1.1)), "spine")
    b.head(skin, r=0.19)
    b.P(G.uvsphere("hair", 0.2, hair, m=T((-0.02, 0, 1.44)), scale=(1.0, 1.0, 0.9)), "head")
    b.P(G.tube("hair_tail", [(-0.15, 0, 1.4), (-0.3, 0, 1.2), (-0.32, 0, 1.0)], [0.08, 0.06, 0.03], hair, segs=8), "head")
    b.P(G.torus("circlet", 0.19, 0.022, gold, m=T((0.04, 0, 1.47), (90, 0, 0))), "head")
    b.P(G.ico("circlet_gem", 0.04, M.core_gem_material("arcane"), m=T((0.23, 0, 1.49)), subdiv=1), "head")
    for side in ("f", "b"):
        b.arm(side, robe, skin, r=0.11, cuff=sash)
    for side in ("f", "b"):
        b.leg(side, robe, sash, r=0.09)
    # orbiting spectral blades on the 'orbit' bone (ring of 3 at 120 deg)
    for i in range(3):
        a = math.radians(120 * i + 30)
        c = Vector((math.cos(a) * 0.6, math.sin(a) * 0.6, 1.0 + 0.12 * math.sin(a * 2)))
        bl = Matrix.Translation(c) @ Matrix.Rotation(-a, 4, "Z") @ Matrix.Rotation(math.radians(15), 4, "X")
        b.P(G.blade(f"sblade{i}", [(0, -0.22), (0, 0.22)], [0.095, 0.08], [0.022, 0.02], spectral, tip=(0, 0.46), m=bl), "orbit")
        b.P(G.box(f"sguard{i}", (0.24, 0.05, 0.05), spectral, m=bl @ T((0, 0, -0.22))), "orbit")
        b.P(G.tube(f"sgrip{i}", [tuple(bl @ Vector((0, 0, -0.22))), tuple(bl @ Vector((0, 0, -0.36)))], 0.03, spectral, segs=6), "orbit")
    return []


def build_duelist(seed, body):
    cape, vest, shirt, skin = _lib("du_cape", seed), _lib("du_vest", seed), _lib("du_shirt", seed), _lib("skin_pale", seed)
    hair = _lib("bs_beard", seed)
    leather = _lib("leather_wrap", seed)
    gold = _lib("gold_trim", seed)
    b = body
    b.sh_y = 0.29
    b.torso(shirt, chest_mat=vest, w=0.82)
    b.P(G.box("vest_front", (0.05, 0.32, 0.36), vest, m=T((0.28, 0, 1.0), (0, -8, 0)), bevel=0.02, smooth=True), "spine")
    b.P(G.lathe("belt", [(0, 0.74), (0.27, 0.74), (0.28, 0.8), (0, 0.8)], leather, segs=16, m=T((0.04, 0, 0))), "hips")
    b.P(G.box("buckle", (0.05, 0.1, 0.08), gold, m=T((0.31, 0, 0.77)), bevel=0.015), "hips")
    # flowing cape behind, over one shoulder
    b.cape(cape, [(0.0, 1.2), (-0.26, 1.16), (-0.48, 0.8), (-0.62, 0.32), (-0.42, 0.36), (-0.3, 0.6), (-0.1, 0.9)], width=0.5)
    b.head(skin, r=0.18)
    b.P(G.uvsphere("hair", 0.185, hair, m=T((0.0, 0, 1.43)), scale=(1.0, 1.0, 0.85)), "head")
    b.P(G.box("goatee", (0.05, 0.05, 0.08), hair, m=T((0.22, 0, 1.25)), bevel=0.02, smooth=True), "head")
    # wide-brimmed plumed hat
    b.P(G.lathe("brim", [(0, 1.5), (0.36, 1.5), (0.38, 1.53), (0, 1.54)], vest, segs=20, m=T((0.04, 0, 0)) @ T((0, 0, 1.52), (0, -8, 0)) @
                T((0, 0, -1.52)), scale_xy=(1.0, 0.9)), "head")
    b.P(G.lathe("crown", [(0, 1.52), (0.17, 1.52), (0.15, 1.68), (0, 1.7)], vest, segs=14, m=T((0.02, 0, 0))), "head")
    b.P(G.tube("plume", [(-0.05, 0.08, 1.62), (-0.25, 0.1, 1.78), (-0.48, 0.1, 1.74), (-0.6, 0.1, 1.6)], [0.05, 0.07, 0.05, 0.015], cape,
               segs=8, flat_y=0.5), "head")
    for side in ("f", "b"):
        b.arm(side, shirt, skin, r=0.085, cuff=vest, glove=leather)
    for side in ("f", "b"):
        b.leg(side, vest, leather, r=0.08, boot_h=0.3, hip_y=0.12)
    return []


def build_warden(seed, body):
    plate, tabard, skin = _lib("wd_plate", seed, rim="#E8C27A"), _lib("wd_tabard", seed), _lib("skin_tan", seed)
    iron = _lib("dark_iron", seed)
    gold = _lib("gold_trim", seed)
    b = body
    b.sh_y = 0.38
    b.torso(plate, chest_mat=plate, w=1.12)
    b.skirt(tabard, top=0.9, bottom=0.34, r_top=0.32, r_bot=0.38, name="tabard_skirt")
    b.P(G.box("tabard_front", (0.05, 0.3, 0.62), tabard, m=T((0.34, 0, 0.8), (0, -6, 0)), bevel=0.02), "hips")
    b.P(G.slab("tabard_emblem", [(0, 0.08), (0.06, 0), (0, -0.08), (-0.06, 0)], 0.03, gold, m=T((0.37, 0, 0.92), (0, 0, 90))), "hips")
    # helmet with visor + crest
    b.head(skin, r=0.19, nose=False, eyes=False)
    b.P(G.lathe("helm", [(0, 1.62), (0.14, 1.6), (0.22, 1.5), (0.23, 1.32), (0.2, 1.24), (0, 1.24)], plate, segs=16, m=T((0.05, 0, 0))),
        "head")
    b.P(G.box("visor", (0.06, 0.3, 0.05), M.toon_material("FB_visor_dark", "#141018", "#000000", "#2A2430", rim_strength=0.0),
              m=T((0.27, 0, 1.4))), "head")
    b.P(G.slab("crest", [(-0.2, 0.0), (0.08, 0.0), (0.04, 0.12), (-0.24, 0.16)], 0.05, tabard, m=T((0.03, 0, 1.6))), "head")
    for side in ("f", "b"):
        sh, el, ha = b.arm(side, iron, iron, r=0.105, cuff=plate)
        # big pauldrons
        b.P(G.uvsphere(f"pauldron_{side}", 0.19, plate, m=T(tuple(sh + Vector((0, 0, 0.04)))), scale=(1.1, 1.0, 0.75)), f"arm_{side}")
        b.P(G.torus(f"pauldron_rim_{side}", 0.17, 0.025, gold, m=T(tuple(sh + Vector((0, 0, -0.03)))) @ T((0, 0, 0), (0, 0, 0), (1.1, 1.0, 1)),
                    segs=18), f"arm_{side}")
    for side in ("f", "b"):
        b.leg(side, iron, plate, r=0.1, boot_h=0.2, hip_y=0.16)
    # lantern hung from a crook pole strapped across the back (silhouette above the helmet)
    wood = _lib("wood", seed)
    b.P(G.tube("lantern_pole", [(-0.3, 0.2, 0.7), (-0.22, 0.2, 1.4), (-0.08, 0.2, 1.86), (0.12, 0.2, 1.9), (0.2, 0.2, 1.84)], 0.03, wood,
               segs=8), "spine")
    b.P(G.tube("lantern_hook", [(0.2, 0.2, 1.84), (0.2, 0.2, 1.74)], 0.012, iron, segs=5), "spine")
    lc = Vector((0.2, 0.2, 1.6))
    b.P(G.lathe("lantern_cap", [(0, 0.14), (0.1, 0.08), (0.11, 0.06), (0, 0.06)], iron, segs=8, m=T(tuple(lc))), "spine")
    b.P(G.lathe("lantern_glass", [(0, -0.12), (0.09, -0.12), (0.095, 0.06), (0, 0.06)], _glow("FB_lantern", "#FFC060", "#8A3A10"), segs=8,
                m=T(tuple(lc))), "spine")
    b.P(G.lathe("lantern_base", [(0, -0.17), (0.11, -0.17), (0.1, -0.12), (0, -0.12)], iron, segs=8, m=T(tuple(lc))), "spine")
    return []


BUILDERS = {"runecrafter": build_runecrafter, "alchemist": build_alchemist, "artificer": build_artificer, "hexblade": build_hexblade,
            "summoner": build_summoner, "duelist": build_duelist, "warden": build_warden}


def build(asset, seed):
    extra_bones = []
    body = Body(seed)
    floats = BUILDERS[asset](seed, body)
    bones = list(C.HUMANOID)
    for name, head in floats:
        bones.append((name, head, (head[0], head[1], head[2] + 0.15), "root"))
    if asset == "summoner":
        bones.append(("orbit", (0, 0, 0.9), (0, 0, 1.1), "root"))
    arm = C.make_armature(f"rig_{asset}", bones)
    frame, core, _att, length, theta, twist, scale = HERO[asset]
    hand = Vector((0.2, -(body.sh_y + 0.04), 0.62)) + (Vector((0.2, -(body.sh_y + 0.04), 0.62)) - Vector((0.06, -(body.sh_y + 0.02), 0.8))).normalized() * 0.05
    _weapon(body, frame, core, seed, length, theta, twist, hand)
    for o, bname in body.parts:
        C.bind(arm, o, bname)
    arm.rotation_euler = (0, 0, math.radians(YAW))
    arm.scale = (scale, scale, scale)
    bpy.context.view_layer.update()
    arm["fb_floats"] = [f[0] for f in floats]
    del extra_bones
    return arm


# ----------------------------------------------------------------- actions
# Attack templates: per frame (upper arm swing U, weapon angle W, spine, head, back arm, lean).
# Angles in degrees from straight down, forward positive; bend = W - rest_theta - U.
ATTACKS = {
    #          ready  antic. antic. top    strike impact recov. recov.
    "swing": {"U": [10, 70, 130, 150, 95, 25, 18, 12], "W": [150, 200, 250, 275, 150, 80, 100, 140],
              "spine": [0, -6, -12, -14, 10, 18, 10, 3], "arm_b": [8, 20, 30, 35, -15, -25, -8, 4],
              "lean": [0, -0.02, -0.03, -0.03, 0.04, 0.06, 0.03, 0.01]},
    "reap":  {"U": [10, 40, 90, 120, 80, 30, 20, 12], "W": [165, 200, 235, 250, 140, 75, 110, 150],
              "spine": [0, -6, -12, -14, 8, 16, 8, 3], "arm_b": [8, 15, 25, 30, -10, -20, -5, 4],
              "lean": [0, -0.02, -0.03, -0.03, 0.04, 0.06, 0.03, 0.01]},
    "thrust": {"U": [20, 0, -15, -20, 80, 95, 70, 35], "W": [100, 95, 90, 90, 92, 92, 95, 100],
               "spine": [0, -4, -8, -10, 10, 16, 8, 2], "arm_b": [10, 25, 35, 40, -20, -30, -10, 5],
               "lean": [0, -0.03, -0.05, -0.06, 0.08, 0.12, 0.06, 0.02]},
    "cast":  {"U": [10, 20, 45, 60, 100, 110, 70, 30], "W": [172, 185, 195, 200, 150, 140, 160, 172],
              "spine": [0, -4, -8, -10, 6, 10, 5, 1], "arm_b": [8, -10, -30, -45, -60, -55, -25, 0],
              "lean": [0, -0.01, -0.02, -0.02, 0.03, 0.04, 0.02, 0.0]},
    "cast_tome": {"U": [10, 25, 50, 70, 95, 100, 65, 25], "W": [175, 180, 185, 190, 175, 170, 175, 175],
                  "spine": [0, -4, -8, -10, 6, 10, 5, 1], "arm_b": [8, -20, -50, -70, -80, -70, -35, 0],
                  "lean": [0, -0.01, -0.02, -0.02, 0.03, 0.04, 0.02, 0.0]},
    # shoot keys the FOREARM world angle F (bow stays square to the straight arm)
    "shoot": {"U": [25, 55, 72, 75, 78, 76, 55, 30], "F": [60, 80, 90, 92, 94, 92, 75, 58],
              "spine": [0, -4, -6, -8, -2, 2, 0, 0], "arm_b": [10, -60, -85, -80, -40, -20, -5, 5],
              "fore_b": [-14, -110, -140, -150, -40, -20, -14, -14], "lean": [0, -0.01, -0.02, -0.03, 0.0, 0.01, 0.0, 0.0]},
    "bash":  {"U": [20, 5, -10, -15, 55, 70, 45, 25], "W": [180, 180, 180, 180, 180, 180, 180, 180],
              "spine": [0, -6, -10, -12, 10, 16, 8, 2], "arm_b": [10, 15, 20, 22, 0, -10, 0, 8],
              "lean": [0, -0.03, -0.05, -0.06, 0.08, 0.11, 0.05, 0.02]},
}


def _floats(arm, n, f, ph):
    locs = {}
    for i, name in enumerate(arm.get("fb_floats", [])):
        locs[name] = (0.0, 0.0, 0.035 * math.sin(ph + i * 2.1))
    return locs


def apply_action(arm, asset, clip):
    n, fps, loop = spec.CLIPS[clip]
    C.new_action(arm, f"{asset}_{clip}")
    scene = bpy.context.scene
    scene.frame_start, scene.frame_end = 1, n
    frame, core, att, length, theta, twist, scale = HERO[asset]
    for f in range(n):
        ph = 2 * math.pi * f / n
        b = math.sin(ph)
        locs = _floats(arm, n, f, ph)
        rots = {}
        if asset == "summoner":
            rots["orbit"] = [("Z", (120.0 / n) * f)]
        if clip == "idle":
            hold = {"swing": (-8, -10), "reap": (-6, -6), "thrust": (-25, -20), "cast": (-10, -8), "cast_tome": (-35, -30),
                    "shoot": (-10, -10), "bash": (-30, -15)}[att]
            rots.update({
                "spine": [("Y", 2.2 * b)], "head": [("Y", -2.5 * math.sin(ph - 0.6))],
                "arm_f": [("Y", hold[0] + 3 * math.sin(ph - 0.4))], "fore_f": [("Y", hold[1] + 2 * math.sin(ph - 0.8))],
                "arm_b": [("Y", 6 - 3 * math.sin(ph - 0.4))], "fore_b": [("Y", -12)],
                "thigh_f": [("Y", -4)], "shin_f": [("Y", 6)], "thigh_b": [("Y", 6)], "shin_b": [("Y", 4)],
            })
            C._key(arm, f + 1, rots=rots, scales={"spine": (1.0 + 0.015 * b, 1.0 + 0.015 * b, 1.0 + 0.02 * b)}, root_loc=(0, 0, 0),
                   locs=locs)
        else:
            A = ATTACKS[att]
            U = A["U"][f]
            W = A["W"][f] if "W" in A else theta + A["F"][f] - 38.0  # forearm rest angle is ~38 deg forward
            lean = A["lean"][f]
            rots.update({
                "spine": [("Y", A["spine"][f])], "head": [("Y", -0.5 * A["spine"][f])],
                "arm_f": [("Y", -U)], "fore_f": [("Y", -(W - theta - U))],
                "arm_b": [("Y", A["arm_b"][f])], "fore_b": [("Y", A.get("fore_b", [-14] * 8)[f])],
                "thigh_f": [("Y", -10 - 200 * max(0, lean))], "shin_f": [("Y", 10 + 150 * max(0, lean))],
                "thigh_b": [("Y", 10 + 60 * max(0, lean))], "shin_b": [("Y", 6)],
            })
            if asset == "summoner":
                # blades flare outward and forward on the cast
                k = [0, 0.1, 0.2, 0.25, 1.0, 0.8, 0.4, 0.1][f]
                scales = {"orbit": (1 + 0.35 * k, 1, 1 + 0.35 * k)}  # bone-local X and Z = world X and Y
                locs["orbit"] = (0.35 * k, 0, 0.05 * k)
            else:
                scales = {}
            C._key(arm, f + 1, rots=rots, scales=scales, root_loc=(lean, 0, 0), locs=locs)
    for fc in arm.animation_data.action.fcurves:
        for kp in fc.keyframe_points:
            kp.interpolation = "LINEAR"
