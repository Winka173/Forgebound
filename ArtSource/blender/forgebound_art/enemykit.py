"""Enemy kit assembler (batch 2): faction kit x role archetype x parts.

See enemy_recipes.py for the data.  Every enemy is modelled facing +X with
feet on Z=0 (flyers hover), rigidly bone-parented to an Armature and animated
only through Actions, exactly like the batch-1 rigs.  After the idle Action is
applied the rig is uniformly scaled so the whole loop fits the 256 px canvas
with a margin (fit_to_canvas) -- large bosses never crop.

Faction identity = materials (palette.FACTION_KITS) + a faction motif (magma
cracks, moss, ice spikes, bone, brass).  Role identity = body plan + build +
held item.  Per-enemy identity = the recipe's part list.
"""
from __future__ import annotations

import math
import random

import bpy
from mathutils import Matrix, Vector

from . import characters as C
from . import geometry as G
from . import materials as M
from . import palette, spec

T = G.trs
YAW = {"biped": 205.0, "quad": 212.0, "blob": 215.0, "wing": 235.0, "wisp": 210.0, "serpent": 200.0, "dummy": 215.0}


# ------------------------------------------------------------------ context
class Kit:
    def __init__(self, job):
        self.job = job
        self.rec = job["recipe"]
        self.fac = job.get("faction") or "training"
        self.seed = job["seed"]
        self.rng = random.Random(self.seed)
        self.plan = self.rec.get("plan", "biped")
        self.build = self.rec.get("build", "normal")
        self.parts = []
        self.A = {}
        self.B = {}
        self.bones = []
        kit = palette.FACTION_KITS[self.fac]
        glow, gdark, ghi = kit["glow"]
        sfx = f"_{self.fac}"
        def mk(name, d, rim=ghi, rs=0.75):
            return M.toon_material(f"FB_{name}{sfx}", d["base"], d["shadow"], d["highlight"], rim=rim, rim_strength=rs, metal=d["metal"],
                                   spec_amt=d["spec"], spec_size=d["spec_size"], paint=d["paint"], seed=self.seed)
        self.m = {k: mk(k, kit[k]) for k in ("body", "armor", "cloth", "accent")}
        self.m["glow"] = M.toon_material(f"FB_glow{sfx}", glow, gdark, ghi, rim=ghi, rim_strength=0.4, emit=0.85,
                                         glow=palette.mix_hex(glow, "#FFFFFF", 0.35), paint=0.2)
        self.m["eye"] = M.toon_material(f"FB_eyeglow{sfx}", kit["eye"], gdark, "#FFFFFF", rim_strength=0.0, emit=0.95, glow=kit["eye"])
        lib = lambda k: M.library_material(k, rim=ghi, rim_strength=0.7, seed=self.seed, suffix=sfx)  # noqa: E731
        for k in ("bone", "wood", "stone", "dark_iron", "gold_trim", "leather_wrap", "forged_steel", "rust_iron", "parchment"):
            self.m[k] = lib(k)
        self.m["brass"] = M.library_material("ar_brass", rim=ghi, rim_strength=0.7, seed=self.seed, suffix=sfx)
        self.m["ice"] = M.toon_material(f"FB_ice{sfx}", "#9EDCF8", "#2E5E9E", "#F0FCFF", rim="#E8FAFF", rim_strength=0.9, metal=0.3,
                                        spec_amt=1.0, spec_size=0.09, paint=0.15, emit=0.12, glow="#E8FAFF")
        self.m["leaf"] = M.toon_material(f"FB_leaf{sfx}", "#5FAE48", "#1E4A2A", "#B8F07A", rim=ghi, rim_strength=0.6, paint=0.35)
        self.m["moss"] = M.toon_material(f"FB_moss2{sfx}", "#6FA35A", "#2A4A22", "#B8E08A", rim=ghi, rim_strength=0.5, paint=0.45)
        self.m["bloom"] = M.toon_material(f"FB_bloom{sfx}", "#E8709A", "#7A2040", "#FFC0D4", rim=ghi, rim_strength=0.7, paint=0.3)
        self.m["dark"] = M.toon_material(f"FB_kitdark{sfx}", "#1E1A24", "#08060C", "#3A3444", rim_strength=0.0, paint=0.1)
        self.m["white"] = M.toon_material(f"FB_kitwhite{sfx}", "#F2F0EA", "#9A96A0", "#FFFFFF", rim=ghi, rim_strength=0.5, spec_amt=0.4,
                                          paint=0.2)
        self.m["fur"] = M.toon_material(f"FB_fur{sfx}", "#E8ECF4", "#8090B0", "#FFFFFF", rim=ghi, rim_strength=0.6, paint=0.45)
        self.m["straw"] = M.toon_material(f"FB_straw{sfx}", "#D8B060", "#7A5428", "#F8E0A0", rim_strength=0.5, paint=0.5)
        self.m["sack"] = M.toon_material(f"FB_sack{sfx}", "#C8A878", "#6A5038", "#EED8B0", rim_strength=0.5, paint=0.45)

    def P(self, o, bone):
        self.parts.append((o, bone))
        return o

    def bone(self, role):
        return self.B.get(role, self.B.get("body", "root"))


def _glow_mat(name, glow, dark):
    return M.toon_material(name, glow, dark, palette.mix_hex(glow, "#FFFFFF", 0.6), rim=glow, rim_strength=0.4, emit=0.85,
                           glow=palette.mix_hex(glow, "#FFFFFF", 0.4), paint=0.2)


def _along(theta, twist=0.0):
    """Rotation taking +Z to a direction `theta` deg from straight down toward +X
    (in the XZ plane), keeping the item's face toward the camera (-Y)."""
    t = math.radians(theta)
    d = Vector((math.sin(t), 0.0, -math.cos(t)))
    up = Matrix.Rotation(math.radians(twist), 3, d) @ Vector((0, 1, 0))
    y_ax = (up - d * up.dot(d)).normalized()
    x_ax = y_ax.cross(d).normalized()
    return Matrix((x_ax, y_ax, d)).transposed().to_4x4()


def hold(K, side="f", theta=150.0, twist=0.0):
    p = K.A["hand_" + side]
    return Matrix.Translation(p) @ _along(theta, twist)


# ===================================================================== plans
def plan_biped(K):
    b = K.build
    sh = {"bulky": 0.42, "golem": 0.5, "ape": 0.46, "slim": 0.3, "imp": 0.26, "skeleton": 0.27, "skeleton_bulky": 0.38, "ghoul": 0.3,
          "construct": 0.44, "tree": 0.42}.get(b, 0.34)
    K.bones = [(n, h, t, p) for n, h, t, p in C.HUMANOID]
    K.B = {"body": "spine", "hips": "hips", "head": "head", "hand_f": "fore_f", "hand_b": "fore_b", "arm_f": "arm_f", "arm_b": "arm_b",
           "root": "root", "orbit": "orbit"}
    K.bones.append(("orbit", (0, 0, 0.9), (0, 0, 1.1), "root"))
    m = K.m
    hc = Vector((0.06, 0, 1.38))
    hr = 0.2
    limb = {"bulky": 0.13, "golem": 0.15, "ape": 0.15, "slim": 0.08, "imp": 0.07, "skeleton": 0.04, "skeleton_bulky": 0.055, "ghoul": 0.065,
            "construct": 0.11, "tree": 0.12}.get(b, 0.1)
    skin = m["body"]
    sleeve = {"robe": m["cloth"], "slim": m["cloth"], "normal": m["cloth"]}.get(b, skin)
    # torso
    if b in ("golem", "construct"):
        K.P(G.box("torso", (0.62, 0.82, 0.6), m["armor"] if b == "construct" else skin, m=T((0.04, 0, 1.0), (0, 6, 0)), bevel=0.09,
                  segs=1 if b == "golem" else 2), "spine")
        K.P(G.box("pelvis", (0.46, 0.56, 0.28), skin, m=T((0, 0, 0.62)), bevel=0.06), "hips")
        hc = Vector((0.14, 0, 1.42))
    elif b == "tree":
        K.P(G.lathe("trunk", [(0, 0.5), (0.3, 0.5), (0.26, 0.8), (0.3, 1.1), (0.24, 1.3), (0, 1.32)], m["wood"], segs=10, smooth=False,
                    scale_xy=(1.0, 1.15)), "spine")
        K.P(G.lathe("roots_hip", [(0, 0.45), (0.34, 0.45), (0.3, 0.7), (0, 0.7)], m["wood"], segs=8, smooth=False), "hips")
        hc = Vector((0.08, 0, 1.45))
    elif b in ("skeleton", "skeleton_bulky"):
        r = 1.0 if b == "skeleton" else 1.35
        K.P(G.tube("spine_col", [(0, 0, 0.62), (0.02, 0, 0.9), (0.0, 0, 1.18)], 0.045 * r, m["bone"], segs=8), "spine")
        for i in range(4):
            K.P(G.torus(f"rib{i}", (0.2 - 0.02 * i) * r, 0.026 * r, m["bone"], m=T((0.04, 0, 1.05 - 0.08 * i), (90, 0, 0)), segs=16), "spine")
        K.P(G.uvsphere("pelvis", 0.15 * r, m["bone"], m=T((0, 0, 0.62)), scale=(1, 1.5, 0.6)), "hips")
        if b == "skeleton_bulky":
            K.P(G.uvsphere("breastplate", 0.27, m["armor"], m=T((0.08, 0, 1.0)), scale=(0.8, 1.25, 0.9)), "spine")
    elif b == "ape":
        K.P(G.uvsphere("torso", 0.36, skin, m=T((0.05, 0, 1.0)), scale=(1.1, 1.2, 1.0)), "spine")
        K.P(G.uvsphere("hip", 0.26, skin, m=T((0.0, 0, 0.66)), scale=(1.0, 1.05, 0.8)), "hips")
        hc = Vector((0.3, 0, 1.25))
    elif b == "ghoul":
        K.P(G.uvsphere("torso", 0.22, skin, m=T((0.04, 0, 0.98)), scale=(1.0, 1.1, 1.3)), "spine")
        K.P(G.uvsphere("hip", 0.18, m["cloth"], m=T((0.0, 0, 0.7)), scale=(1.0, 1.1, 0.8)), "hips")
        hc = Vector((0.2, 0, 1.3))
    elif b == "imp":
        K.P(G.uvsphere("torso", 0.22, skin, m=T((0.02, 0, 0.92)), scale=(1.0, 1.0, 1.15)), "spine")
        K.P(G.uvsphere("hip", 0.18, skin, m=T((0.0, 0, 0.72)), scale=(1.0, 1.0, 0.8)), "hips")
        hc, hr = Vector((0.08, 0, 1.3)), 0.27
    else:
        w = {"bulky": 1.25, "slim": 0.85}.get(b, 1.0)
        top = m["armor"] if b == "bulky" else m["cloth"]
        K.P(G.uvsphere("torso", 0.28, top, m=T((0.02, 0, 0.96)), scale=(1.05, 1.15 * w, 1.2)), "spine")
        K.P(G.uvsphere("chest", 0.24, top, m=T((0.07, 0, 1.06)), scale=(1.05, 1.2 * w, 0.9)), "spine")
        K.P(G.uvsphere("hip", 0.26, m["cloth"], m=T((0.04, 0, 0.76)), scale=(1.0, 1.05 * w, 0.9)), "hips")
    if b == "robe":
        K.P(G.lathe("robe", [(0, 0.88), (0.3, 0.88), (0.36, 0.45), (0.46, 0.08), (0.44, 0.03), (0, 0.03)], m["cloth"], segs=20,
                    m=T((0.03, 0, 0))), "hips")
    # arms
    for side in ("f", "b"):
        y = -1 if side == "f" else 1
        s_ = Vector((0.0, sh * y, 1.08))
        e_ = Vector((0.06 if side == "f" else -0.04, (sh + 0.02) * y, 0.8))
        h_ = Vector((0.2 if side == "f" else 0.04, (sh + 0.04) * y, 0.62 if side == "f" else 0.6))
        if b == "ape":
            e_ = Vector((0.12, (sh + 0.04) * y, 0.7))
            h_ = Vector((0.3, (sh + 0.06) * y, 0.28))
        if b == "ghoul":
            h_ = Vector((0.32, (sh + 0.04) * y, 0.45))
        mat_u = sleeve if b not in ("skeleton", "skeleton_bulky") else m["bone"]
        mat_f = skin if b not in ("skeleton", "skeleton_bulky") else m["bone"]
        if b == "tree":
            mat_u = mat_f = m["wood"]
        if b == "construct":
            mat_u, mat_f = m["armor"], m["body"]
        K.P(G.uvsphere(f"shoulder_{side}", limb * 1.25, m["armor"] if b in ("bulky", "construct") else mat_u, m=T(tuple(s_))), f"arm_{side}")
        K.P(G.tube(f"upper_{side}", [tuple(s_), tuple(e_)], [limb, limb * 0.9], mat_u, segs=10), f"arm_{side}")
        K.P(G.tube(f"fore_{side}", [tuple(e_), tuple(h_)], [limb * 0.9, limb * (1.15 if b in ("golem", "ape") else 0.95)], mat_f, segs=10),
            f"fore_{side}")
        hand = h_ + (h_ - e_).normalized() * 0.05
        hs = {"golem": 0.24, "ape": 0.2, "bulky": 0.13, "construct": 0.13}.get(b, max(0.055, limb * 0.85))
        if b == "golem":
            K.P(G.ico(f"fist_{side}", hs, skin, m=T(tuple(hand)), subdiv=1, jitter=0.12, rng=K.rng, scale=(1.1, 1, 1.0)), f"fore_{side}")
        else:
            K.P(G.uvsphere(f"hand_{side}", hs, mat_f, m=T(tuple(hand))), f"fore_{side}")
        K.A["hand_" + side] = hand
        K.A["elbow_" + side] = e_
        K.A["shoulder_" + side] = s_
    # legs
    if b != "robe":
        lr = {"bulky": 0.12, "golem": 0.14, "ape": 0.12, "slim": 0.08, "imp": 0.065, "skeleton": 0.04, "skeleton_bulky": 0.055,
              "ghoul": 0.06, "construct": 0.1, "tree": 0.12}.get(b, 0.095)
        hy = {"golem": 0.22, "bulky": 0.17, "ape": 0.2, "construct": 0.2, "tree": 0.18}.get(b, 0.14)
        for side in ("f", "b"):
            y = -1 if side == "f" else 1
            lm = m["bone"] if b in ("skeleton", "skeleton_bulky") else (m["wood"] if b == "tree" else
                                                                        (m["armor"] if b in ("bulky", "construct") else
                                                                         (skin if b in ("imp", "golem", "ape", "ghoul") else m["cloth"])))
            K.P(G.tube(f"thigh_{side}", [(0.0, hy * y, 0.62), (0.02, hy * y, 0.32)], [lr * 1.15, lr], lm, segs=10), f"thigh_{side}")
            K.P(G.tube(f"shin_{side}", [(0.02, hy * y, 0.34), (0.0, hy * y, 0.12)], [lr * 0.95, lr * 0.9], lm, segs=10), f"shin_{side}")
            if b == "golem":
                K.P(G.box(f"foot_{side}", (0.36, 0.28, 0.18), skin, m=T((0.05, hy * y, 0.09)), bevel=0.05), f"shin_{side}")
            elif b == "tree":
                for k in range(3):
                    a = math.radians(-50 + 50 * k)
                    K.P(G.tube(f"root_{side}{k}", [(0.0, hy * y, 0.14), (math.cos(a) * 0.22, hy * y + math.sin(a) * 0.12, 0.03)],
                               [0.07, 0.03], m["wood"], segs=6), f"shin_{side}")
            elif b in ("skeleton", "skeleton_bulky"):
                K.P(G.box(f"foot_{side}", (0.2, 0.09, 0.07), m["bone"], m=T((0.06, hy * y, 0.04)), bevel=0.025, smooth=True), f"shin_{side}")
            elif b in ("imp", "ghoul", "ape"):
                K.P(G.uvsphere(f"foot_{side}", lr * 1.6, skin, m=T((0.07, hy * y, 0.06)), scale=(1.6, 0.9, 0.6)), f"shin_{side}")
            else:
                K.P(G.box(f"boot_{side}", (0.3, 0.16, 0.15), m["armor"] if b in ("bulky", "construct") else m["leather_wrap"],
                          m=T((0.06, hy * y, 0.075)), bevel=0.05, segs=2, smooth=True), f"shin_{side}")
    K.A.update(head=hc, head_r=hr, back=Vector((-0.3, 0, 1.02)), chest=Vector((0.3, 0, 1.0)), top=hc + Vector((0, 0, hr)),
               belly=Vector((0.25, 0, 0.8)))


def plan_quad(K):
    b = K.build
    K.bones = [("root", (0, 0, 0), (0, 0, 0.2), None), ("body", (0, 0, 0.3), (0, 0, 0.6), "root"),
               ("head", (0.42, 0, 0.5), (0.42, 0, 0.75), "body"), ("tail", (-0.42, 0, 0.5), (-0.55, 0, 0.7), "body"),
               ("orbit", (0, 0, 0.6), (0, 0, 0.8), "root")]
    legs = [("fl", 0.3, -0.18), ("fr", 0.3, 0.18), ("bl", -0.3, -0.18), ("br", -0.3, 0.18)]
    if b == "crawler":
        legs = [("fl", 0.32, -0.22), ("fr", 0.32, 0.22), ("ml", 0.0, -0.26), ("mr", 0.0, 0.26), ("bl", -0.32, -0.22), ("br", -0.32, 0.22)]
    for n, x, y in legs:
        K.bones.append((f"leg_{n}", (x, y, 0.42), (x, y, 0.05), "body"))
    K.B = {"body": "body", "head": "head", "tail": "tail", "root": "root", "orbit": "orbit"}
    m = K.m
    skin = m["body"] if b not in ("mech",) else m["armor"]
    if b == "toad":
        K.P(G.uvsphere("body", 0.42, skin, m=T((0, 0, 0.42)), scale=(1.15, 1.0, 0.85)), "body")
        K.P(G.uvsphere("belly", 0.34, m["accent"], m=T((0.15, 0, 0.32)), scale=(1.0, 0.9, 0.7)), "body")
        head, hr = Vector((0.4, 0, 0.6)), 0.24
    elif b == "crawler":
        K.P(G.uvsphere("body", 0.3, skin, m=T((0, 0, 0.32)), scale=(1.5, 1.0, 0.55)), "body")
        head, hr = Vector((0.48, 0, 0.36)), 0.15
    elif b == "beast":
        K.P(G.uvsphere("body", 0.38, skin, m=T((0, 0, 0.6)), scale=(1.45, 0.95, 0.85)), "body")
        K.P(G.uvsphere("hump", 0.3, skin, m=T((0.18, 0, 0.82)), scale=(1.2, 0.9, 0.7)), "body")
        head, hr = Vector((0.6, 0, 0.62)), 0.2
    elif b == "mech":
        K.P(G.box("body", (0.8, 0.36, 0.3), skin, m=T((0, 0, 0.55)), bevel=0.06, segs=2), "body")
        K.P(G.box("body_plate", (0.5, 0.4, 0.12), m["body"], m=T((0.05, 0, 0.72)), bevel=0.04), "body")
        head, hr = Vector((0.5, 0, 0.68)), 0.15
    else:
        K.P(G.uvsphere("body", 0.3, skin, m=T((0, 0, 0.55)), scale=(1.45, 0.85, 0.8)), "body")
        K.P(G.uvsphere("chest", 0.26, skin, m=T((0.25, 0, 0.6)), scale=(1.0, 0.9, 1.0)), "body")
        head, hr = Vector((0.5, 0, 0.72)), 0.17
    for n, x, y in legs:
        bn = f"leg_{n}"
        top_z = 0.5 if b not in ("crawler", "toad") else 0.32
        if b == "crawler":
            K.P(G.tube(f"leg{n}", [(x, y * 0.7, top_z), (x + 0.05, y * 1.6, 0.3), (x + 0.1, y * 1.9, 0.02)], [0.05, 0.04, 0.02],
                       m["bone"] if K.fac == "hollow_dead" else m["leaf"] if K.fac == "overgrowth" else skin, segs=6), bn)
        elif b == "toad":
            big = x < 0
            K.P(G.tube(f"leg{n}", [(x, y * 1.2, 0.35), (x + (0.25 if big else 0.05), y * 1.5, 0.2 if big else 0.18),
                                    (x + (0.1 if big else 0.12), y * 1.4, 0.04)], [0.12 if big else 0.07, 0.09 if big else 0.06, 0.05],
                       skin, segs=8), bn)
            K.P(G.uvsphere(f"foot{n}", 0.08, skin, m=T((x + 0.16, y * 1.4, 0.03)), scale=(1.5, 1.1, 0.4)), bn)
        else:
            r = 0.09 if b == "beast" else 0.07
            mat = m["dark_iron"] if b == "mech" else skin
            K.P(G.tube(f"leg{n}", [(x, y, top_z), (x + 0.03, y, 0.25), (x - 0.02, y, 0.07)], [r * 1.2, r, r * 0.85], mat, segs=8), bn)
            K.P(G.uvsphere(f"paw{n}", r * 1.25, mat, m=T((x + 0.04, y, 0.05)), scale=(1.4, 1.0, 0.6)), bn)
    K.A.update(head=head, head_r=hr, back=Vector((0.0, 0, 0.82 if b != "crawler" else 0.5)), tail=Vector((-0.45, 0, 0.55)),
               top=head + Vector((0, 0, hr)), chest=Vector((0.35, 0, 0.5)), belly=Vector((0.1, 0, 0.35)),
               hand_f=Vector((0.4, -0.2, 0.1)), hand_b=Vector((0.4, 0.2, 0.1)))


def plan_blob(K):
    K.bones = [("root", (0, 0, 0), (0, 0, 0.2), None), ("body", (0, 0, 0.0), (0, 0, 0.7), "root"),
               ("top", (0, 0, 0.6), (0, 0, 0.9), "body"), ("orbit", (0, 0, 0.5), (0, 0, 0.7), "root")]
    K.B = {"body": "body", "head": "body", "top": "top", "root": "root", "orbit": "orbit"}
    K.A.update(head=Vector((0.3, 0, 0.45)), head_r=0.2, top=Vector((0, 0, 0.7)), back=Vector((-0.2, 0, 0.5)), chest=Vector((0.4, 0, 0.4)),
               belly=Vector((0.35, 0, 0.25)), hand_f=Vector((0.4, -0.3, 0.2)), hand_b=Vector((0.4, 0.3, 0.2)))


def plan_wing(K):
    K.bones = [("root", (0, 0, 0), (0, 0, 0.2), None), ("body", (0, 0, 0.95), (0, 0, 1.2), "root"),
               ("wing_f", (0, -0.12, 1.1), (0, -0.55, 1.15), "body"), ("wing_b", (0, 0.12, 1.1), (0, 0.55, 1.15), "body"),
               ("orbit", (0, 0, 1.0), (0, 0, 1.2), "root")]
    K.B = {"body": "body", "head": "body", "wing_f": "wing_f", "wing_b": "wing_b", "root": "root", "orbit": "orbit"}
    m = K.m
    if K.build == "sprite":
        K.P(G.uvsphere("body", 0.12, m["accent"], m=T((0, 0, 1.0)), scale=(0.9, 0.9, 1.3)), "body")
        K.P(G.lathe("dress", [(0, 1.02), (0.1, 1.0), (0.18, 0.82), (0, 0.84)], m["body"], segs=14), "body")
        head, hr = Vector((0.03, 0, 1.22)), 0.13
    else:
        K.P(G.uvsphere("body", 0.2, m["cloth"] if K.fac == "hollow_dead" else m["body"], m=T((0, 0, 1.05)), scale=(1.0, 0.9, 1.15)), "body")
        head, hr = Vector((0.12, 0, 1.27)), 0.15
        # bat membranes (as the batch-1 bat), in the faction cloth colour, with bone struts
        for side, sy in (("f", -1), ("b", 1)):
            w = [(0.0, 0.0), (0.14, 0.28), (0.1, 0.68), (-0.14, 0.82), (-0.17, 0.62), (-0.34, 0.56), (-0.28, 0.36), (-0.4, 0.25), (-0.22, 0.1)]
            pts = [(x, y * sy) for x, y in w]
            if sy > 0:
                pts = list(reversed(pts))
            K.P(G.slab(f"wing{sy}", pts, 0.03, m["cloth"], m=T((0, 0.1 * sy, 1.12)), plane="XY"), f"wing_{side}")
            K.P(G.tube(f"wingbone{sy}", [(0, 0.1 * sy, 1.12), (0.14, 0.38 * sy, 1.12), (0.1, 0.78 * sy, 1.12)], 0.024, m["bone"], segs=6),
                f"wing_{side}")
    K.A.update(head=head, head_r=hr, top=head + Vector((0, 0, hr)), back=Vector((-0.1, 0, 1.1)), chest=Vector((0.15, 0, 1.0)),
               belly=Vector((0.05, 0, 0.9)), hand_f=Vector((0.15, -0.1, 0.9)), hand_b=Vector((0.15, 0.1, 0.9)))


def plan_wisp(K):
    K.bones = [("root", (0, 0, 0), (0, 0, 0.2), None), ("body", (0, 0, 0.9), (0, 0, 1.2), "root"),
               ("tail", (0, 0, 0.85), (-0.1, 0, 0.45), "body"), ("orbit", (0, 0, 1.0), (0, 0, 1.2), "root")]
    K.B = {"body": "body", "head": "body", "tail": "tail", "root": "root", "orbit": "orbit", "hand_f": "body", "hand_b": "body"}
    K.A.update(head=Vector((0.05, 0, 1.15)), head_r=0.2, top=Vector((0.05, 0, 1.4)), back=Vector((-0.2, 0, 1.0)),
               chest=Vector((0.2, 0, 1.0)), belly=Vector((0.0, 0, 0.8)), hand_f=Vector((0.32, -0.25, 0.95)), hand_b=Vector((0.28, 0.25, 0.95)),
               tail=Vector((-0.1, 0, 0.55)))


def plan_serpent(K):
    n = 7
    K.bones = [("root", (0, 0, 0), (0, 0, 0.2), None), ("orbit", (0, 0, 1.0), (0, 0, 1.2), "root")]
    prev = "root"
    pts = []
    for i in range(n):
        x = 0.62 - 0.24 * i
        z = 1.1 + 0.25 * math.sin(i * 0.9 + 0.4) - 0.04 * i
        pts.append(Vector((x, 0, z)))
    for i in range(n):
        a = pts[i]
        bnext = pts[i + 1] if i + 1 < n else pts[i] + (pts[i] - pts[i - 1])
        K.bones.append((f"seg{i}", tuple(a), tuple(bnext), prev))
        prev = f"seg{i}"
    K.B = {"body": "seg1", "head": "seg0", "root": "root", "orbit": "orbit", "tail": f"seg{n - 1}"}
    m = K.m
    for i in range(n):
        r = 0.2 * (1 - i / (n + 1)) + 0.04
        bnext = pts[i + 1] if i + 1 < n else pts[i] + (pts[i] - pts[i - 1]) * 0.8
        K.P(G.tube(f"seg{i}", [tuple(pts[i]), tuple(bnext)], [r, r * 0.88], m["ice"] if i % 2 == 0 else m["body"], segs=10), f"seg{i}")
        if i > 0:
            K.P(G.lathe(f"fin{i}", [(0, 0), (0.06, 0.0), (0.0, 0.22 * (1 - i / n) + 0.06)], m["ice"], segs=4, smooth=False,
                        m=T(tuple(pts[i] + Vector((0, 0, r * 0.8))), (0, -25, 0))), f"seg{i}")
    K.A.update(head=pts[0] + Vector((0.12, 0, 0.04)), head_r=0.2, top=pts[0] + Vector((0, 0, 0.25)), back=pts[2] + Vector((0, 0, 0.2)),
               chest=pts[1], belly=pts[1], hand_f=pts[1], hand_b=pts[1], tail=pts[-1], segs=pts)


def plan_dummy(K):
    K.bones = [("root", (0, 0, 0), (0, 0, 0.2), None), ("post", (0, 0, 0.0), (0, 0, 0.6), "root"),
               ("body", (0, 0, 0.6), (0, 0, 1.3), "post"), ("orbit", (0, 0, 0.5), (0, 0, 0.7), "root")]
    K.B = {"body": "body", "head": "body", "root": "root", "orbit": "orbit", "hand_f": "body", "hand_b": "body"}
    m = K.m
    small = K.build == "swarm"
    s = 0.72 if small else 1.0
    K.P(G.box("base", (0.5 * s, 0.5 * s, 0.1), m["wood"], m=T((0, 0, 0.05)), bevel=0.02), "root")
    K.P(G.tube("post", [(0, 0, 0.08), (0, 0, 0.75 * s)], 0.06 * s, m["wood"], segs=8), "post")
    K.P(G.lathe("sack", [(0, 0.55 * s), (0.2 * s, 0.58 * s), (0.25 * s, 0.85 * s), (0.22 * s, 1.1 * s), (0, 1.18 * s)], m["sack"], segs=14),
        "body")
    K.P(G.tube("arms", [(0, -0.42 * s, 0.98 * s), (0, 0.42 * s, 0.98 * s)], 0.045 * s, m["wood"], segs=8), "body")
    for sy in (-1, 1):
        K.P(G.uvsphere(f"straw{sy}", 0.07 * s, m["straw"], m=T((0, 0.46 * s * sy, 0.98 * s)), scale=(0.8, 1.3, 0.8)), "body")
    K.P(G.uvsphere("head", 0.17 * s, m["sack"], m=T((0, 0, 1.32 * s))), "body")
    K.P(G.torus("rope", 0.19 * s, 0.022, m["straw"], m=T((0, 0, 0.95 * s), (90, 0, 0)), segs=18), "body")
    # painted target on the chest
    for i, (r, mat) in enumerate(((0.16, m["cloth"]), (0.1, m["white"]), (0.05, m["cloth"]))):
        K.P(G.slab(f"target{i}", [(math.cos(a) * r * s, math.sin(a) * r * s) for a in [2 * math.pi * k / 20 for k in range(20)]], 0.02,
                   mat, m=T((0.0, -0.22 * s - 0.01 * i, 0.82 * s), (0, 0, 90)) @ T((0, 0, 0), (0, 0, 0)), parent=None), "body")
    K.A.update(head=Vector((0, 0, 1.32 * s)), head_r=0.17 * s, top=Vector((0, 0, 1.5 * s)), back=Vector((-0.2, 0, 0.9)),
               chest=Vector((0.2, 0, 0.85)), belly=Vector((0.2, 0, 0.7)), hand_f=Vector((0.0, -0.46, 0.98)), hand_b=Vector((0.0, 0.46, 0.98)))


PLANS = {"biped": plan_biped, "quad": plan_quad, "blob": plan_blob, "wing": plan_wing, "wisp": plan_wisp, "serpent": plan_serpent,
         "dummy": plan_dummy}


# ===================================================================== parts
def _eyes(K, c, r, sep=0.08, fwd=0.9, z=0.02, mat=None, n=2):
    for i in range(n):
        y = -sep if i == 0 else sep
        if n == 1:
            y = 0
        K.P(G.uvsphere(f"eye{i}", r, mat or K.m["eye"], m=T((c.x + K.A["head_r"] * fwd, y, c.z + z))), K.bone("head"))


def _head_ball(K, mat=None, scale=(1.0, 0.95, 1.05)):
    c, r = K.A["head"], K.A["head_r"]
    K.P(G.uvsphere("head", r, mat or K.m["body"], m=T(tuple(c)), scale=scale), K.bone("head"))
    return c, r


# -- heads
def h_imp(K):
    c, r = _head_ball(K)
    for sy in (-1, 1):
        K.P(G.spike(f"horn{sy}", (c.x - 0.02, 0.12 * sy, c.z + r * 0.7), (c.x - 0.18, 0.2 * sy, c.z + r * 1.6), 0.06, K.m["dark_iron"]), "head")
        K.P(G.cone(f"ear{sy}", 0.06, 0.0, 0.18, K.m["body"], m=T((c.x - 0.02, r * 0.9 * sy, c.z), (90 * sy, 0, 0)), segs=6), "head")
    _eyes(K, c, 0.06, sep=0.1, fwd=0.85, z=0.04)
    K.P(G.box("grin", (0.04, 0.16, 0.03), K.m["glow"], m=T((c.x + r * 0.92, 0, c.z - 0.09))), "head")


def h_brute_horns(K):
    c, r = _head_ball(K, scale=(1.1, 1.0, 0.9))
    for sy in (-1, 1):
        K.P(G.tube(f"horn{sy}", [(c.x, 0.15 * sy, c.z + 0.1), (c.x + 0.05, 0.32 * sy, c.z + 0.2), (c.x + 0.18, 0.36 * sy, c.z + 0.34)],
                   [0.07, 0.05, 0.01], K.m["bone"], segs=8), "head")
    K.P(G.box("jaw", (0.2, 0.26, 0.1), K.m["body"], m=T((c.x + 0.1, 0, c.z - 0.14)), bevel=0.03), "head")
    _eyes(K, c, 0.04, sep=0.08, z=0.04)


def h_bandit_mask(K):
    c, r = _head_ball(K, mat=M.library_material("skin_tan", seed=K.seed))
    K.P(G.uvsphere("mask", r * 1.02, K.m["cloth"], m=T((c.x + 0.02, 0, c.z - 0.07)), scale=(1.0, 1.0, 0.5)), "head")
    K.P(G.uvsphere("cap", r * 1.03, K.m["cloth"], m=T((c.x - 0.02, 0, c.z + 0.08)), scale=(1.0, 1.0, 0.6)), "head")
    K.P(G.tube("knot", [(c.x - r, 0, c.z + 0.05), (c.x - r - 0.15, 0.02, c.z - 0.05), (c.x - r - 0.22, 0.02, c.z - 0.2)], [0.04, 0.03, 0.015],
               K.m["cloth"], segs=6), "head")
    _eyes(K, c, 0.03, sep=0.08, z=0.02, mat=K.m["dark"])


def h_hood(K, mat=None, peak=True, glow_eyes=True):
    c, r = K.A["head"], K.A["head_r"]
    mat = mat or K.m["cloth"]
    K.P(G.uvsphere("hood", r * 1.2, mat, m=T(tuple(c + Vector((-0.03, 0, 0.03)))), scale=(1.0, 1.0, 1.08)), "head")
    if peak:
        K.P(G.cone("hood_peak", r * 0.7, 0.0, r * 1.6, mat, m=T(tuple(c + Vector((-r * 0.6, 0, r * 0.6))), (0, -60, 0)), segs=10, smooth=True),
            "head")
    K.P(G.uvsphere("hood_face", r * 0.85, K.m["dark"], m=T(tuple(c + Vector((r * 0.45, 0, -0.01)))), scale=(0.6, 0.95, 1.0)), "head")
    if glow_eyes:
        _eyes(K, c, 0.03, sep=0.07, fwd=1.0, z=0.02)


def h_goggle_cap(K):
    c, r = _head_ball(K, mat=M.library_material("skin_tan", seed=K.seed))
    K.P(G.uvsphere("cap", r * 1.05, K.m["leather_wrap"], m=T((c.x - 0.02, 0, c.z + 0.07)), scale=(1.0, 1.0, 0.7)), "head")
    for sy in (-1, 1):
        K.P(G.lathe(f"goggle{sy}", [(0, 0), (0.06, 0), (0.065, 0.05), (0, 0.06)], K.m["brass"], segs=12,
                    m=T((c.x + r * 0.8, 0.08 * sy, c.z + 0.04), (0, 80, 0))), "head")
        K.P(G.uvsphere(f"lens{sy}", 0.045, K.m["glow"], m=T((c.x + r * 0.8 + 0.05, 0.08 * sy, c.z + 0.04))), "head")
    K.P(G.box("soot_beard", (0.12, 0.2, 0.14), K.m["dark_iron"], m=T((c.x + 0.12, 0, c.z - 0.15)), bevel=0.04, smooth=True), "head")


def h_hound(K, mech=False):
    c, r = K.A["head"], K.A["head_r"]
    mat = K.m["armor"] if mech else K.m["body"]
    K.P(G.uvsphere("skull", r, mat, m=T(tuple(c)), scale=(1.1, 0.9, 0.9)), "head")
    K.P(G.uvsphere("snout", r * 0.7, mat, m=T(tuple(c + Vector((r * 1.0, 0, -0.04)))), scale=(1.4, 0.75, 0.7)), "head")
    K.P(G.box("jaw", (r * 1.4, r * 0.9, 0.05), K.m["dark_iron"] if mech else K.m["body"], m=T(tuple(c + Vector((r * 0.9, 0, -0.13)))),
              bevel=0.02), "head")
    for sy in (-1, 1):
        K.P(G.cone(f"ear{sy}", 0.06, 0.0, 0.18, K.m["dark_iron"] if mech else K.m["body"], m=T(tuple(c + Vector((-0.04, 0.08 * sy, r * 0.6))),
                                                                                             (10 * sy, -25, 0)), segs=4), "head")
    _eyes(K, c, 0.035, sep=0.08, fwd=0.85, z=0.05)
    if mech:
        K.P(G.tube("antenna", [tuple(c + Vector((-0.05, 0, r))), tuple(c + Vector((-0.12, 0, r + 0.2)))], 0.015, K.m["dark_iron"], segs=5),
            "head")
        K.P(G.uvsphere("antenna_ball", 0.035, K.m["glow"], m=T(tuple(c + Vector((-0.12, 0, r + 0.22))))), "head")


def h_mech_hound(K):
    h_hound(K, mech=True)


def h_foreman_helm(K):
    c, r = _head_ball(K)
    K.P(G.lathe("helm", [(0, c.z + r * 1.05), (r * 1.1, c.z + r * 0.5), (r * 1.25, c.z), (r * 1.3, c.z - 0.02), (0, c.z - 0.02)],
                K.m["dark_iron"], segs=16, m=T((c.x, 0, 0))), "head")
    K.P(G.box("visor", (0.05, r * 1.6, 0.05), K.m["glow"], m=T((c.x + r * 1.0, 0, c.z - 0.03))), "head")
    K.P(G.box("jaw", (0.18, 0.26, 0.1), K.m["body"], m=T((c.x + 0.08, 0, c.z - 0.15)), bevel=0.03), "head")


def h_titan_maw(K):
    c, r = K.A["head"], K.A["head_r"] * 1.2
    K.P(G.ico("head", r, K.m["body"], m=T(tuple(c)), subdiv=1, jitter=0.1, rng=K.rng, scale=(1.1, 1.0, 0.9)), "head")
    K.P(G.box("maw", (0.05, r * 1.4, 0.1), K.m["glow"], m=T((c.x + r * 0.95, 0, c.z - 0.06))), "head")
    _eyes(K, c, 0.05, sep=0.1, z=0.07, fwd=0.85)


def h_anvil_crown(K):
    c, r = _head_ball(K, mat=K.m["armor"])
    K.P(G.box("visor", (0.05, r * 1.4, 0.04), K.m["glow"], m=T((c.x + r * 0.95, 0, c.z))), "head")
    prof = [(-0.3, 0.0), (0.22, 0.0), (0.36, 0.05), (0.42, 0.12), (0.2, 0.13), (0.15, 0.2), (-0.2, 0.2), (-0.22, 0.12), (-0.32, 0.08)]
    K.P(G.slab("anvil_crown", [(x * 0.8 + c.x, z * 0.8 + c.z + r * 0.9) for x, z in prof], 0.22, K.m["dark_iron"], bevel=0.015), "head")
    for i in range(3):
        K.P(G.spike(f"crown_spike{i}", (c.x - 0.12 + 0.12 * i, 0, c.z + r + 0.12), (c.x - 0.12 + 0.12 * i, 0, c.z + r + 0.32), 0.04,
                    K.m["gold_trim"]), "head")
    K.P(G.box("beard", (0.14, 0.24, 0.2), K.m["glow"], m=T((c.x + 0.1, 0, c.z - 0.2)), bevel=0.05, smooth=True), "head")


def h_bud(K):
    c, r = K.A["head"], K.A["head_r"]
    K.P(G.uvsphere("bud", r, K.m["leaf"], m=T(tuple(c)), scale=(1.2, 0.9, 0.9)), "head")
    for i in range(4):
        a = math.radians(90 * i + 45)
        K.P(G.slab(f"petal{i}", [(0, 0), (0.08, 0.1), (0.0, 0.2), (-0.08, 0.1)], 0.03, K.m["bloom"],
                   m=T(tuple(c + Vector((r * 0.9, 0, 0))), (math.degrees(a), 0, 0)) @ T((0, 0, 0), (0, 90, 0))), "head")
    _eyes(K, c, 0.03, sep=0.07, z=0.06)


def h_mushroom(K):
    c, r = _head_ball(K, mat=K.m["body"])
    K.P(G.lathe("cap", [(0, c.z + r * 1.6), (r * 1.6, c.z + r * 0.7), (r * 1.7, c.z + r * 0.45), (r * 0.6, c.z + r * 0.6), (0, c.z + r * 0.6)],
                K.m["bloom"], segs=20, m=T((c.x - 0.02, 0, 0))), "head")
    for i in range(5):
        a = 2 * math.pi * i / 5
        K.P(G.uvsphere(f"spot{i}", 0.04, K.m["white"], m=T((c.x + math.cos(a) * r * 1.0, math.sin(a) * r * 1.0, c.z + r * 1.15))), "head")
    _eyes(K, c, 0.035, sep=0.08, z=0.0)


def h_beast_horns(K):
    c, r = K.A["head"], K.A["head_r"]
    K.P(G.uvsphere("skull", r, K.m["body"], m=T(tuple(c)), scale=(1.25, 0.95, 0.95)), "head")
    K.P(G.uvsphere("snout", r * 0.7, K.m["accent"], m=T(tuple(c + Vector((r * 1.1, 0, -0.06)))), scale=(1.0, 0.9, 0.7)), "head")
    for sy in (-1, 1):
        K.P(G.tube(f"horn{sy}", [tuple(c + Vector((0.02, 0.1 * sy, r * 0.6))), tuple(c + Vector((0.12, 0.2 * sy, r * 1.4))),
                                 tuple(c + Vector((0.3, 0.22 * sy, r * 1.6)))], [0.05, 0.035, 0.008], K.m["bone"], segs=6), "head")
    _eyes(K, c, 0.035, sep=0.09, z=0.06, fwd=0.9)


def h_stone_block(K):
    c, r = K.A["head"], K.A["head_r"]
    K.P(G.box("head", (0.32, 0.32, 0.26), K.m["stone"], m=T(tuple(c)), bevel=0.06), "head")
    K.P(G.box("brow_moss", (0.3, 0.34, 0.06), K.m["moss"], m=T(tuple(c + Vector((0.0, 0, 0.14)))), bevel=0.03, smooth=True), "head")
    K.P(G.box("eyes", (0.05, 0.22, 0.05), K.m["glow"], m=T(tuple(c + Vector((0.16, 0, 0.0))))), "head")


def h_thorn_hood(K):
    h_hood(K, mat=K.m["leaf"], peak=False)
    c, r = K.A["head"], K.A["head_r"]
    for i in range(5):
        a = math.radians(-60 + 30 * i)
        K.P(G.spike(f"thorn{i}", tuple(c + Vector((-0.05, math.sin(a) * r, math.cos(a) * r))),
                    tuple(c + Vector((-0.22, math.sin(a) * r * 1.6, math.cos(a) * r * 1.8))), 0.035, K.m["wood"]), "head")


def h_toad(K):
    c, r = K.A["head"], K.A["head_r"]
    K.P(G.uvsphere("head", r, K.m["body"], m=T(tuple(c)), scale=(1.2, 1.25, 0.75)), "head")
    K.P(G.box("mouth", (0.03, r * 2.0, 0.025), K.m["dark"], m=T(tuple(c + Vector((r * 1.1, 0, -0.05))))), "head")
    for sy in (-1, 1):
        K.P(G.uvsphere(f"eyebulb{sy}", 0.09, K.m["body"], m=T(tuple(c + Vector((0.05, 0.14 * sy, r * 0.65))))), "head")
        K.P(G.uvsphere(f"eye{sy}", 0.055, K.m["eye"], m=T(tuple(c + Vector((0.11, 0.14 * sy, r * 0.7))))), "head")


def h_treant(K):
    c, r = K.A["head"], K.A["head_r"] * 1.2
    K.P(G.lathe("head", [(0, c.z - r), (r * 0.9, c.z - r), (r, c.z), (r * 0.7, c.z + r), (0, c.z + r * 1.1)], K.m["wood"], segs=10,
                smooth=False, m=T((c.x, 0, 0))), "head")
    K.P(G.box("mouth", (0.05, 0.2, 0.06), K.m["dark"], m=T((c.x + r * 0.95, 0, c.z - 0.1))), "head")
    _eyes(K, c, 0.04, sep=0.08, z=0.06, fwd=0.85)
    for i in range(7):
        a = 2 * math.pi * i / 7
        K.P(G.uvsphere(f"leaf{i}", 0.14, K.m["leaf"], m=T((c.x + math.cos(a) * 0.18 - 0.05, math.sin(a) * 0.18, c.z + r * 1.1 + 0.05)),
                       scale=(1.2, 1.2, 0.8)), "head")


def h_bloom_crown(K):
    c, r = _head_ball(K, mat=K.m["wood"])
    _eyes(K, c, 0.035, sep=0.08, z=0.03)
    for i in range(6):
        a = math.radians(-75 + 30 * i)
        p = c + Vector((-0.04, math.sin(a) * r * 1.1, math.cos(a) * r * 1.2))
        K.P(G.uvsphere(f"bloom{i}", 0.09, K.m["bloom"], m=T(tuple(p)), scale=(1, 1, 0.7)), "head")
        K.P(G.uvsphere(f"bloomc{i}", 0.04, K.m["glow"], m=T(tuple(p + Vector((0.02, 0, 0.05))))), "head")
    K.P(G.tube("antler_l", [tuple(c + Vector((-0.05, 0.08, r))), tuple(c + Vector((-0.1, 0.18, r + 0.35))), tuple(c + Vector((0.05, 0.25, r + 0.55)))],
               [0.04, 0.03, 0.01], K.m["wood"], segs=6), "head")
    K.P(G.tube("antler_r", [tuple(c + Vector((-0.05, -0.08, r))), tuple(c + Vector((-0.15, -0.18, r + 0.3))), tuple(c + Vector((-0.02, -0.28, r + 0.5)))],
               [0.04, 0.03, 0.01], K.m["wood"], segs=6), "head")


def h_ice_crown_small(K):
    c = K.A["head"]
    for i in range(5):
        a = math.radians(-60 + 30 * i)
        K.P(G.lathe(f"crown{i}", [(0, 0), (0.035, 0.03), (0, 0.16 + 0.06 * (i == 2))], K.m["ice"], segs=4, smooth=False,
                    m=T(tuple(c + Vector((-0.02, math.sin(a) * 0.15, 0.2 + math.cos(a) * 0.03))), (math.degrees(a) * 0.5, 0, 0))), "body")


def h_winged_helm(K):
    c, r = _head_ball(K, mat=M.library_material("skin_pale", seed=K.seed))
    K.P(G.lathe("helm", [(0, c.z + r * 1.15), (r * 1.08, c.z + r * 0.4), (r * 1.1, c.z - 0.02), (0, c.z - 0.02)], K.m["armor"], segs=16,
                m=T((c.x - 0.01, 0, 0))), "head")
    for sy in (-1, 1):
        K.P(G.slab(f"wing{sy}", [(0, 0), (-0.18, 0.12), (-0.28, 0.06), (-0.16, -0.02)], 0.03, K.m["ice"],
                   m=T((c.x - 0.02, r * 1.02 * sy, c.z + 0.08)) @ T((0, 0, 0), (0, 0, 8 * sy))), "head")
    _eyes(K, c, 0.03, sep=0.08, z=0.0, mat=K.m["dark"])


def h_great_helm(K):
    c, r = K.A["head"], K.A["head_r"]
    K.P(G.lathe("helm", [(0, c.z + r * 1.1), (r * 1.05, c.z + r * 0.8), (r * 1.15, c.z - r * 0.6), (r * 1.0, c.z - r * 1.0), (0, c.z - r)],
                K.m["armor"], segs=12, m=T((c.x, 0, 0))), "head")
    K.P(G.box("slit", (0.05, r * 1.4, 0.035), K.m["glow"], m=T((c.x + r * 1.08, 0, c.z + 0.02))), "head")
    K.P(G.slab("crest", [(-0.2, 0), (0.12, 0), (0.06, 0.12), (-0.24, 0.18)], 0.04, K.m["ice"], m=T((c.x, 0, c.z + r * 1.05))), "head")


def h_beast(K):
    c, r = K.A["head"], K.A["head_r"]
    K.P(G.uvsphere("head", r, K.m["fur"] if K.fac == "rime_court" else K.m["body"], m=T(tuple(c)), scale=(1.1, 1.0, 0.95)), "head")
    K.P(G.uvsphere("muzzle", r * 0.6, K.m["body"], m=T(tuple(c + Vector((r * 0.85, 0, -0.06)))), scale=(1.0, 1.1, 0.8)), "head")
    for sy in (-1, 1):
        K.P(G.cone(f"tusk{sy}", 0.03, 0.0, 0.12, K.m["bone"], m=T(tuple(c + Vector((r * 1.1, 0.07 * sy, -0.1))), (0, 0, 0)), segs=5), "head")
    _eyes(K, c, 0.035, sep=0.09, z=0.07, fwd=0.85)


def h_mitre(K):
    c, r = _head_ball(K, mat=M.library_material("skin_pale", seed=K.seed))
    K.P(G.slab("mitre", [(-0.16, 0), (0.16, 0), (0.12, 0.3), (0.0, 0.42), (-0.12, 0.3)], 0.26, K.m["white"],
               m=T((c.x - 0.01, 0, c.z + r * 0.6)), bevel=0.02), "head")
    K.P(G.slab("mitre_band", [(-0.04, 0.0), (0.04, 0.0), (0.04, 0.34), (-0.04, 0.34)], 0.27, K.m["glow"], m=T((c.x - 0.01, 0, c.z + r * 0.62))),
        "head")
    K.P(G.uvsphere("beard", 0.13, K.m["fur"], m=T((c.x + 0.12, 0, c.z - 0.16)), scale=(0.8, 1.1, 1.3)), "head")
    _eyes(K, c, 0.028, sep=0.08, z=0.03, mat=K.m["dark"])


def h_sprite(K):
    c, r = _head_ball(K, mat=K.m["white"])
    K.P(G.uvsphere("hair", r * 1.05, K.m["ice"], m=T(tuple(c + Vector((-0.02, 0, 0.03)))), scale=(1.0, 1.0, 0.9)), "head")
    _eyes(K, c, 0.03, sep=0.06, z=0.0)


def h_tiara(K):
    c, r = _head_ball(K, mat=M.library_material("skin_pale", seed=K.seed))
    K.P(G.uvsphere("hair", r * 1.1, K.m["fur"], m=T(tuple(c + Vector((-0.04, 0, 0.04)))), scale=(1.0, 1.0, 1.0)), "head")
    K.P(G.uvsphere("bun", r * 0.65, K.m["fur"], m=T(tuple(c + Vector((-0.12, 0, r * 0.9))))), "head")
    for i in range(5):
        a = math.radians(-50 + 25 * i)
        K.P(G.lathe(f"tiara{i}", [(0, 0), (0.03, 0.02), (0, 0.13 + 0.07 * (i == 2))], K.m["ice"], segs=4, smooth=False,
                    m=T(tuple(c + Vector((0.06, math.sin(a) * r, r * 0.8))), (math.degrees(a) * 0.6, 0, 0))), "head")
    _eyes(K, c, 0.028, sep=0.08, z=0.02)


def h_ice_crown(K):
    c, r = _head_ball(K, mat=K.m["ice"])
    K.P(G.uvsphere("beard", 0.17, K.m["fur"], m=T((c.x + 0.12, 0, c.z - 0.18)), scale=(0.8, 1.2, 1.4)), "head")
    _eyes(K, c, 0.035, sep=0.08, z=0.04)
    for i in range(7):
        a = math.radians(-75 + 25 * i)
        h = 0.22 + 0.12 * (1 - abs(i - 3) / 3)
        K.P(G.lathe(f"crown{i}", [(0, 0), (0.05, 0.04), (0.035, h * 0.7), (0, h)], K.m["ice"], segs=5, smooth=False,
                    m=T(tuple(c + Vector((0.0, math.sin(a) * r * 0.95, r * 0.75))), (math.degrees(a) * 0.5, 0, 0))), "head")


def h_wyrm(K):
    c = K.A["head"]
    K.P(G.uvsphere("skull", 0.22, K.m["ice"], m=T(tuple(c)), scale=(1.4, 0.9, 0.8)), "head")
    K.P(G.uvsphere("jaw", 0.15, K.m["body"], m=T(tuple(c + Vector((0.18, 0, -0.1)))), scale=(1.5, 0.8, 0.5)), "head")
    for sy in (-1, 1):
        K.P(G.lathe(f"horn{sy}", [(0, 0), (0.05, 0.03), (0, 0.32)], K.m["ice"], segs=5, smooth=False,
                    m=T(tuple(c + Vector((-0.12, 0.09 * sy, 0.12))), (0, -70, 0))), "head")
    _eyes(K, c, 0.04, sep=0.11, z=0.05, fwd=0.7)


def h_skull(K, mat=None):
    c, r = K.A["head"], K.A["head_r"] * 0.95
    K.P(G.uvsphere("skull", r, mat or K.m["bone"], m=T(tuple(c)), scale=(1.05, 0.95, 1.0)), "head")
    K.P(G.box("jaw", (r, r, r * 0.4), mat or K.m["bone"], m=T(tuple(c + Vector((r * 0.4, 0, -r * 0.9)))), bevel=0.03, smooth=True), "head")
    for sy in (-1, 1):
        K.P(G.uvsphere(f"socket{sy}", r * 0.3, K.m["dark"], m=T(tuple(c + Vector((r * 0.75, 0.4 * r * sy, r * 0.1)))), scale=(0.6, 1, 1)), "head")
        K.P(G.uvsphere(f"eyeglow{sy}", r * 0.15, K.m["eye"], m=T(tuple(c + Vector((r * 0.92, 0.4 * r * sy, r * 0.1))))), "head")


def h_skull_hood(K):
    h_skull(K)
    c, r = K.A["head"], K.A["head_r"]
    K.P(G.uvsphere("hood", r * 1.2, K.m["cloth"], m=T(tuple(c + Vector((-0.08, 0, 0.04)))), scale=(0.9, 1.05, 1.08)), "head")


def h_ghoul(K):
    c, r = K.A["head"], K.A["head_r"]
    K.P(G.uvsphere("head", r, K.m["accent"], m=T(tuple(c)), scale=(1.15, 0.9, 0.95)), "head")
    K.P(G.box("jaw", (0.18, 0.18, 0.06), K.m["accent"], m=T(tuple(c + Vector((0.1, 0, -0.15)))), bevel=0.02), "head")
    for i in range(4):
        K.P(G.cone(f"tooth{i}", 0.018, 0.0, 0.06, K.m["bone"], m=T(tuple(c + Vector((0.18, -0.06 + 0.04 * i, -0.12))), (180, 0, 0)), segs=4),
            "head")
    _eyes(K, c, 0.04, sep=0.08, z=0.04, fwd=0.85)
    K.P(G.tube("hair", [tuple(c + Vector((-0.1, 0, r * 0.8))), tuple(c + Vector((-0.3, 0, r * 0.2))), tuple(c + Vector((-0.34, 0, -0.2)))],
               [0.06, 0.05, 0.02], K.m["cloth"], segs=6, flat_y=0.5), "head")


def h_skull_cowl(K):
    h_skull(K)
    c, r = K.A["head"], K.A["head_r"]
    K.P(G.uvsphere("cowl", r * 1.25, K.m["cloth"], m=T(tuple(c + Vector((-0.08, 0, 0.05)))), scale=(0.9, 1.1, 1.1)), "head")
    K.P(G.cone("cowl_peak", r * 0.8, 0.0, r * 2.0, K.m["cloth"], m=T(tuple(c + Vector((-r * 0.7, 0, r * 0.7))), (0, -55, 0)), segs=10, smooth=True),
        "head")


def h_bat_skull(K):
    c, r = K.A["head"], K.A["head_r"]
    K.P(G.uvsphere("head", r, K.m["bone"], m=T(tuple(c))), "body")
    for sy in (-1, 1):
        K.P(G.cone(f"ear{sy}", 0.07, 0.0, 0.22, K.m["cloth"], m=T(tuple(c + Vector((-0.04, 0.08 * sy, 0.08))), (12 * sy, -10, 0)), segs=6), "body")
        K.P(G.uvsphere(f"eye{sy}", 0.035, K.m["eye"], m=T(tuple(c + Vector((r * 0.85, 0.06 * sy, 0.03))))), "body")
        K.P(G.cone(f"fang{sy}", 0.018, 0.0, 0.07, K.m["white"], m=T(tuple(c + Vector((r * 0.85, 0.03 * sy, -0.08))), (180, 0, 0)), segs=5), "body")


def h_skull_helm(K):
    h_skull(K)
    c, r = K.A["head"], K.A["head_r"]
    K.P(G.lathe("helm", [(0, c.z + r * 1.15), (r * 1.08, c.z + r * 0.4), (r * 1.12, c.z + 0.02), (0, c.z + 0.02)], K.m["rust_iron"], segs=12,
                m=T((c.x - 0.02, 0, 0))), "head")
    K.P(G.slab("helm_crest", [(-0.18, 0), (0.1, 0), (0.0, 0.14)], 0.04, K.m["cloth"], m=T((c.x, 0, c.z + r * 1.1))), "head")


def h_horned_skull(K):
    h_skull(K)
    c, r = K.A["head"], K.A["head_r"]
    for sy in (-1, 1):
        K.P(G.tube(f"horn{sy}", [tuple(c + Vector((-0.02, 0.12 * sy, 0.12))), tuple(c + Vector((-0.15, 0.32 * sy, 0.25))),
                                 tuple(c + Vector((-0.05, 0.4 * sy, 0.45)))], [0.06, 0.04, 0.01], K.m["bone"], segs=8), "head")


def h_lens_dome(K):
    c, r = K.A["head"], K.A["head_r"]
    K.P(G.lathe("dome", [(0, c.z - 0.08), (r * 1.15, c.z - 0.08), (r * 1.1, c.z + 0.06), (r * 0.7, c.z + r), (0, c.z + r * 1.05)],
                K.m["armor"], segs=16, m=T((c.x, 0, 0))), "head")
    K.P(G.lathe("lens", [(0, 0), (0.09, 0), (0.1, 0.04), (0, 0.06)], K.m["glow"], segs=14, m=T((c.x + r * 0.95, 0, c.z), (0, 90, 0))), "head")


def h_dome_eye(K):
    c, r = K.A["head"], K.A["head_r"]
    K.P(G.uvsphere("dome", r * 1.1, K.m["glow"], m=T(tuple(c)), scale=(1, 1, 0.9)), "head")
    K.P(G.torus("dome_ring", r * 1.1, 0.035, K.m["armor"], m=T(tuple(c + Vector((0, 0, -0.04)))) @ T((0, 0, 0), (90, 0, 0)), segs=20), "head")
    for sy in (-1, 1):
        K.P(G.tube(f"fin{sy}", [tuple(c + Vector((0, 0.14 * sy, 0.1))), tuple(c + Vector((-0.1, 0.22 * sy, 0.3)))], 0.03, K.m["armor"], segs=6),
            "head")


def h_tall_hood(K):
    h_hood(K, peak=False)
    c, r = K.A["head"], K.A["head_r"]
    K.P(G.cone("tall_peak", r * 1.0, 0.0, r * 2.6, K.m["cloth"], m=T(tuple(c + Vector((-0.06, 0, r * 0.6))), (0, -12, 0)), segs=12, smooth=True),
        "head")
    K.P(G.torus("hood_band", r * 0.95, 0.025, K.m["armor"], m=T(tuple(c + Vector((-0.04, 0, r * 0.7)))) @ T((0, 0, 0), (90, 0, 0)), segs=18),
        "head")


def h_plumed_helm(K):
    c, r = _head_ball(K, mat=M.library_material("skin_tan", seed=K.seed))
    K.P(G.lathe("helm", [(0, c.z + r * 1.15), (r * 1.08, c.z + r * 0.4), (r * 1.12, c.z - 0.04), (0, c.z - 0.04)], K.m["armor"], segs=16,
                m=T((c.x - 0.01, 0, 0))), "head")
    K.P(G.box("cheek", (0.1, r * 2.1, 0.12), K.m["armor"], m=T((c.x + 0.02, 0, c.z - 0.06)), bevel=0.03), "head")
    K.P(G.tube("plume", [(c.x, 0, c.z + r * 1.1), (c.x - 0.15, 0, c.z + r * 1.6), (c.x - 0.38, 0, c.z + r * 1.5), (c.x - 0.5, 0, c.z + r * 1.0)],
               [0.06, 0.08, 0.06, 0.02], K.m["cloth"], segs=8, flat_y=0.5), "head")
    _eyes(K, c, 0.028, sep=0.08, z=0.0, mat=K.m["dark"])


def h_storm_hood(K):
    h_hood(K, peak=True)
    c, r = K.A["head"], K.A["head_r"]
    K.P(G.box("mask", (0.04, 0.2, 0.08), K.m["armor"], m=T(tuple(c + Vector((r * 0.95, 0, -0.07))))), "head")


def h_kettle_helm(K):
    c, r = _head_ball(K, mat=M.library_material("skin_tan", seed=K.seed))
    K.P(G.lathe("helm", [(0, c.z + r * 1.05), (r * 0.95, c.z + r * 0.6), (r * 1.05, c.z + 0.04), (r * 1.6, c.z - 0.02), (r * 1.6, c.z - 0.05),
                         (0, c.z - 0.05)], K.m["armor"], segs=18, m=T((c.x - 0.01, 0, 0.0))), "head")
    _eyes(K, c, 0.028, sep=0.08, z=-0.06, mat=K.m["dark"])


def h_halo_helm(K):
    h_great_helm(K)
    c, r = K.A["head"], K.A["head_r"]
    K.P(G.torus("halo", r * 1.4, 0.03, K.m["glow"], m=T(tuple(c + Vector((-0.12, 0.0, r * 0.5)))) @ T((0, 0, 0), (0, -20, 0))), "head")


def h_storm_crown(K):
    c, r = _head_ball(K, mat=K.m["armor"])
    K.P(G.box("visor", (0.05, r * 1.5, 0.05), K.m["glow"], m=T((c.x + r * 0.95, 0, c.z))), "head")
    for i in range(5):
        a = math.radians(-60 + 30 * i)
        pts = [(0, 0), (0.05, 0.12), (0.0, 0.12), (0.06, 0.28), (-0.06, 0.1), (0.0, 0.1), (-0.04, 0.0)]
        K.P(G.slab(f"bolt{i}", [(x * 1.2, z * 1.2) for x, z in pts], 0.03, K.m["glow"],
                   m=T(tuple(c + Vector((0.0, math.sin(a) * r * 0.9, r * 0.75))), (math.degrees(a) * 0.6, 0, 90))), "head")


def h_colossus_head(K):
    c, r = K.A["head"], K.A["head_r"]
    K.P(G.box("head", (0.34, 0.36, 0.28), K.m["armor"], m=T(tuple(c)), bevel=0.06, segs=2), "head")
    K.P(G.lathe("eye", [(0, 0), (0.08, 0), (0.09, 0.05), (0, 0.06)], K.m["glow"], segs=14, m=T((c.x + 0.17, 0, c.z), (0, 90, 0))), "head")
    K.P(G.tube("chimney", [tuple(c + Vector((-0.08, 0.1, 0.12))), tuple(c + Vector((-0.12, 0.1, 0.36)))], 0.045, K.m["dark_iron"], segs=8), "head")


def h_dummy_bucket(K):
    c, r = K.A["head"], K.A["head_r"]
    K.P(G.lathe("bucket", [(0, c.z + r * 1.1), (r * 0.95, c.z + r * 1.1), (r * 1.15, c.z - r * 0.5), (0, c.z - r * 0.5)], K.m["dark_iron"],
                segs=14, m=T((c.x, 0, 0))), "body")
    K.P(G.box("slit", (0.05, r * 1.2, 0.03), K.m["dark"], m=T((c.x + r * 1.05, 0, c.z + 0.05))), "body")


def h_lich_crown(K):
    c = K.A["head"]
    for i in range(5):
        a = math.radians(-60 + 30 * i)
        K.P(G.spike(f"crown{i}", tuple(c + Vector((0.0, math.sin(a) * 0.2, 0.2))), tuple(c + Vector((-0.04, math.sin(a) * 0.24, 0.42 + 0.08 * (i == 2)))),
                    0.035, K.m["gold_trim"]), "body")
    K.P(G.torus("crown_band", 0.2, 0.03, K.m["gold_trim"], m=T(tuple(c + Vector((0, 0, 0.2)))) @ T((0, 0, 0), (90, 0, 0)), segs=20), "body")


# -- held items (front hand) and offhand
def _staff(K, theta=175, length=1.35, mat=None, side="f"):
    R = hold(K, side, theta)
    K.P(G.tube(f"staff_{side}", [tuple(R @ Vector((0, 0, -0.5))), tuple(R @ Vector((0, 0, length - 0.5)))], 0.035, mat or K.m["wood"], segs=8),
        K.bone("hand_" + side))
    return R, R @ Vector((0, 0, length - 0.5))


def i_cleaver(K, side="f"):
    R = hold(K, side, 140)
    K.P(G.tube("cleaver_grip", [tuple(R @ Vector((0, 0, -0.1))), tuple(R @ Vector((0, 0, 0.16)))], 0.035, K.m["leather_wrap"], segs=6),
        K.bone("hand_" + side))
    K.P(G.slab("cleaver", [(-0.06, 0.15), (0.2, 0.15), (0.24, 0.55), (-0.06, 0.6)], 0.04, K.m["forged_steel"], m=R, bevel=0.01),
        K.bone("hand_" + side))


def i_slag_slab(K):
    R = hold(K, "f", 150)
    K.P(G.tube("slab_grip", [tuple(R @ Vector((0, 0, -0.1))), tuple(R @ Vector((0, 0, 0.3)))], 0.05, K.m["dark_iron"], segs=6), "fore_f")
    K.P(G.box("slab", (0.32, 0.2, 0.62), K.m["dark_iron"], m=R @ T((0, 0, 0.6)), bevel=0.05), "fore_f")
    K.P(G.box("slab_glow", (0.04, 0.21, 0.4), K.m["glow"], m=R @ T((0.06, 0, 0.6))), "fore_f")


def i_sling(K):
    p = K.A["hand_f"]
    K.P(G.tube("sling", [tuple(p), tuple(p + Vector((0.1, -0.05, -0.2))), tuple(p + Vector((0.05, -0.05, -0.38)))], 0.02, K.m["leather_wrap"],
               segs=5), "fore_f")
    K.P(G.uvsphere("sling_stone", 0.08, K.m["glow"], m=T(tuple(p + Vector((0.05, -0.05, -0.42))))), "fore_f")


def i_brazier_staff(K):
    R, top = _staff(K, 172, 1.4, K.m["dark_iron"])
    K.P(G.lathe("brazier", [(0, 0), (0.12, 0.02), (0.16, 0.12), (0.12, 0.12), (0, 0.04)], K.m["dark_iron"], segs=12, m=Matrix.Translation(top)),
        "fore_f")
    K.P(G.slab("brazier_flame", [(0, 0.3), (0.1, 0.12), (0.1, 0.0), (-0.1, 0.0), (-0.06, 0.16)], 0.08, K.m["glow"],
               m=Matrix.Translation(top + Vector((0, 0, 0.08)))), "fore_f")


def i_great_hammer(K, maul=False):
    R = hold(K, "f", 150)
    L = 1.15 if not maul else 1.25
    K.P(G.tube("hammer_shaft", [tuple(R @ Vector((0, 0, -0.2))), tuple(R @ Vector((0, 0, L)))], 0.045, K.m["wood"] if not maul else K.m["dark_iron"],
               segs=8), "fore_f")
    hm = R @ T((0, 0, L))
    if maul:
        prof = [(-0.3, -0.12), (0.22, -0.12), (0.4, -0.04), (0.45, 0.06), (0.2, 0.08), (0.18, 0.16), (-0.24, 0.16), (-0.28, 0.06)]
        K.P(G.slab("maul_head", prof, 0.3, K.m["dark_iron"], m=hm @ T((0, 0, 0), (0, 90, 0)), bevel=0.02), "fore_f")
        K.P(G.box("maul_glow", (0.31, 0.31, 0.06), K.m["glow"], m=hm @ T((0, 0, 0.0))), "fore_f")
    else:
        K.P(G.box("hammer_head", (0.4, 0.22, 0.24), K.m["dark_iron"], m=hm, bevel=0.03), "fore_f")
        K.P(G.box("hammer_band", (0.08, 0.24, 0.26), K.m["glow"], m=hm), "fore_f")


def i_forge_maul(K):
    i_great_hammer(K, maul=True)


def i_spore_staff(K):
    R, top = _staff(K, 170, 1.35)
    K.P(G.lathe("staff_cap", [(0, 0), (0.18, -0.02), (0.2, 0.04), (0.12, 0.1), (0, 0.13)], K.m["bloom"], segs=14, m=Matrix.Translation(top)),
        "fore_f")
    K.P(G.uvsphere("staff_spore", 0.06, K.m["glow"], m=T(tuple(top + Vector((0, 0, 0.18))))), "fore_f")


def i_thorn_blade(K, side="f"):
    R = hold(K, side, 120 if side == "f" else 95)
    pts = [tuple(R @ Vector((0.0, 0, z))) for z in (-0.05, 0.2, 0.42)] + [tuple(R @ Vector((0.08, 0, 0.6)))]
    K.P(G.tube(f"thorn_blade_{side}", pts, [0.05, 0.045, 0.025, 0.004], K.m["wood"], segs=6), K.bone("hand_" + side))
    for i in range(2):
        K.P(G.spike(f"thorn_spur_{side}{i}", pts[1 + i], tuple(Vector(pts[1 + i]) + R.to_3x3() @ Vector((-0.1, 0, 0.06))), 0.025, K.m["wood"]),
            K.bone("hand_" + side))


def i_root_club(K):
    R = hold(K, "f", 160)
    K.P(G.tube("club", [tuple(R @ Vector((0, 0, -0.15))), tuple(R @ Vector((0.02, 0, 0.4))), tuple(R @ Vector((-0.02, 0, 0.8)))],
               [0.05, 0.09, 0.15], K.m["wood"], segs=8), "fore_f")
    K.P(G.uvsphere("club_moss", 0.12, K.m["moss"], m=R @ T((0, -0.04, 0.78)), scale=(1.2, 1, 0.7)), "fore_f")


def i_life_staff(K):
    R, top = _staff(K, 172, 1.55)
    for i in range(5):
        a = 2 * math.pi * i / 5
        K.P(G.uvsphere(f"petal{i}", 0.08, K.m["bloom"], m=T(tuple(top + Vector((math.cos(a) * 0.1, math.sin(a) * 0.1, 0.06)))), scale=(1, 1, 0.5)),
            "fore_f")
    K.P(G.uvsphere("flower_core", 0.07, K.m["glow"], m=T(tuple(top + Vector((0, 0, 0.08))))), "fore_f")


def i_ice_bow(K, bone_mat=None):
    R = hold(K, "f", 165, 180)
    pts = []
    for i in range(13):
        u = -1 + 2 * i / 12
        pts.append(tuple(R @ Vector((-0.22 * (1 - u * u), 0, u * 0.6))))
    K.P(G.tube("bow", pts, [0.03 + 0.015 * (1 - abs(-1 + 2 * i / 12)) for i in range(13)], bone_mat or K.m["ice"], segs=6), "fore_f")
    K.P(G.tube("string", [pts[0], pts[-1]], 0.006, K.m["white"], segs=4), "fore_f")


def i_bone_bow(K):
    i_ice_bow(K, K.m["bone"])


def i_ice_sword(K):
    R = hold(K, "f", 140)
    K.P(G.blade("ice_sword", [(0, 0.1), (0, 0.6)], [0.07, 0.065], [0.02, 0.02], K.m["ice"], tip=(0, 0.82), m=R), "fore_f")
    K.P(G.box("ice_guard", (0.24, 0.06, 0.05), K.m["armor"], m=R @ T((0, 0, 0.08))), "fore_f")


def i_crystal_staff(K):
    R, top = _staff(K, 172, 1.45, K.m["armor"])
    K.P(G.lathe("staff_crystal", [(0, 0), (0.09, 0.12), (0.07, 0.3), (0, 0.42)], K.m["ice"], segs=6, smooth=False, m=Matrix.Translation(top)),
        "fore_f")


def i_ice_fan(K):
    R = hold(K, "f", 130)
    pts = [(0, 0)] + [(math.sin(a) * 0.38, math.cos(a) * 0.38) for a in [math.radians(-70 + 14 * i) for i in range(11)]]
    K.P(G.slab("fan", pts, 0.02, K.m["ice"], m=R @ T((0, 0, 0.0)), bevel=0.004), "fore_f")
    for i in range(6):
        a = math.radians(-70 + 28 * i)
        K.P(G.tube(f"fan_rib{i}", [tuple(R @ Vector((0, -0.015, 0))), tuple(R @ Vector((math.sin(a) * 0.38, -0.015, math.cos(a) * 0.38)))], 0.008,
                   K.m["armor"], segs=4), "fore_f")


def i_frost_scepter(K):
    R, top = _staff(K, 165, 1.15, K.m["armor"])
    K.P(G.slab("scepter_star", G.star2d(6, 0.2, 0.08, 0.0), 0.06, K.m["ice"], m=Matrix.Translation(top + Vector((0, 0, 0.12)))), "fore_f")


def i_skull_staff(K):
    R, top = _staff(K, 172, 1.45, K.m["bone"])
    K.P(G.uvsphere("staff_skull", 0.11, K.m["bone"], m=T(tuple(top + Vector((0, 0, 0.06))))), "fore_f")
    for sy in (-1, 1):
        K.P(G.uvsphere(f"staff_eye{sy}", 0.025, K.m["eye"], m=T(tuple(top + Vector((0.09, 0.04 * sy, 0.08))))), "fore_f")
    K.P(G.torus("staff_ring", 0.18, 0.02, K.m["glow"], m=T(tuple(top + Vector((0, 0, 0.06)))) @ T((0, 0, 0), (75, 0, 0)), segs=20), "fore_f")


def i_rusty_halberd(K):
    R = hold(K, "f", 172)
    K.P(G.tube("halberd_pole", [tuple(R @ Vector((0, 0, -0.5))), tuple(R @ Vector((0, 0, 1.15)))], 0.03, K.m["wood"], segs=6), "fore_f")
    K.P(G.slab("halberd_axe", [(0.0, 0.85), (0.24, 0.8), (0.3, 1.0), (0.22, 1.12), (0.0, 1.05)], 0.03, K.m["rust_iron"], m=R, bevel=0.005),
        "fore_f")
    K.P(G.lathe("halberd_tip", [(0, 1.1), (0.04, 1.12), (0, 1.35)], K.m["rust_iron"], segs=4, smooth=False, m=R), "fore_f")


def i_phylactery(K):
    p = K.A["chest"] + Vector((0.18, -0.15, 0.05))
    K.P(G.ico("phylactery", 0.1, K.m["glow"], m=T(tuple(p)), subdiv=0, scale=(1, 1, 1.5)), "body")
    for i in range(3):
        a = math.radians(120 * i)
        K.P(G.tube(f"cage{i}", [tuple(p + Vector((math.cos(a) * 0.1, math.sin(a) * 0.1, -0.16))),
                                tuple(p + Vector((math.cos(a) * 0.13, math.sin(a) * 0.13, 0.0))),
                                tuple(p + Vector((math.cos(a) * 0.1, math.sin(a) * 0.1, 0.16)))], 0.012, K.m["gold_trim"], segs=4), "body")


def i_bolt_staff(K):
    R, top = _staff(K, 172, 1.45, K.m["armor"])
    pts = [(0.0, 0.0), (0.1, 0.18), (0.02, 0.18), (0.12, 0.42), (-0.08, 0.14), (0.0, 0.14), (-0.06, 0.0)]
    K.P(G.slab("staff_bolt", pts, 0.04, K.m["glow"], m=Matrix.Translation(top)), "fore_f")
    K.P(G.torus("staff_coil", 0.08, 0.02, K.m["brass"], m=Matrix.Translation(top) @ T((0, 0, 0), (90, 0, 0)), segs=14), "fore_f")


def i_lance(K, storm=False):
    R = hold(K, "f", 165 if storm else 140)
    K.P(G.lathe("lance", [(0, -0.3), (0.04, -0.3), (0.05, 0.1), (0.13, 0.15), (0.06, 0.25), (0.0, 1.2)],
                K.m["armor"] if not storm else K.m["forged_steel"], segs=10, smooth=False, m=R), "fore_f")
    if storm:
        K.P(G.slab("spear_bolt", [(0.0, 0.0), (0.06, 0.12), (0.0, 0.12), (0.07, 0.3), (-0.06, 0.1), (0.0, 0.1), (-0.04, 0.0)], 0.03,
                   K.m["glow"], m=R @ T((0, -0.05, 0.6))), "fore_f")
    else:
        K.P(G.slab("pennant", [(0, 0), (0.0, 0.2), (-0.3, 0.1)], 0.02, K.m["cloth"], m=R @ T((0, 0, 0.5)) @ T((0, 0, 0), (0, -90, 0))), "fore_f")


def i_storm_spear(K):
    i_lance(K, storm=True)


def i_projector(K):
    R, top = _staff(K, 150, 0.8, K.m["armor"])
    K.P(G.lathe("dish", [(0, 0), (0.16, 0.05), (0.18, 0.08), (0, 0.04)], K.m["brass"], segs=16, m=Matrix.Translation(top) @ _along(150)), "fore_f")
    K.P(G.uvsphere("dish_core", 0.05, K.m["glow"], m=T(tuple(top + Vector((0.05, 0, 0.04))))), "fore_f")


def i_dagger(K, side="f"):
    R = hold(K, side, 110 if side == "f" else 80)
    K.P(G.blade(f"dagger_{side}", [(0, 0.06), (0, 0.32)], [0.04, 0.035], [0.015, 0.015], K.m["forged_steel"], tip=(0, 0.42), m=R),
        K.bone("hand_" + side))
    K.P(G.box(f"dagger_guard_{side}", (0.14, 0.04, 0.03), K.m["brass"], m=R @ T((0, 0, 0.05))), K.bone("hand_" + side))


def i_pike(K):
    R = hold(K, "f", 172)
    K.P(G.tube("pike", [tuple(R @ Vector((0, 0, -0.5))), tuple(R @ Vector((0, 0, 1.2)))], 0.028, K.m["wood"], segs=6), "fore_f")
    K.P(G.lathe("pike_tip", [(0, 1.18), (0.05, 1.2), (0.04, 1.3), (0, 1.45)], K.m["forged_steel"], segs=4, smooth=False, m=R), "fore_f")


def i_thunder_glaive(K):
    R = hold(K, "f", 165)
    K.P(G.tube("glaive_pole", [tuple(R @ Vector((0, 0, -0.5))), tuple(R @ Vector((0, 0, 1.0)))], 0.035, K.m["dark_iron"], segs=8), "fore_f")
    K.P(G.slab("glaive_blade", [(-0.04, 0.95), (0.08, 0.95), (0.2, 1.2), (0.12, 1.45), (0.0, 1.5), (0.06, 1.2)], 0.035, K.m["forged_steel"],
               m=R, bevel=0.006), "fore_f")
    K.P(G.slab("glaive_bolt", [(0.0, 0.0), (0.06, 0.12), (0.0, 0.12), (0.07, 0.3), (-0.06, 0.1), (0.0, 0.1), (-0.04, 0.0)], 0.03, K.m["glow"],
               m=R @ T((0.05, -0.03, 1.08))), "fore_f")


def _shield(K, shape, mat, size=1.0, side="b", face=None, rim=None):
    p = K.A["hand_" + side] + Vector((0.08, -0.1 if side == "f" else 0.0, 0.08))
    # shields face forward-left (toward the camera side) like the warden's bulwark
    R = Matrix.Translation(p) @ Matrix.Rotation(math.radians(-60), 4, "Z")
    if shape == "round":
        pts = [(math.cos(a) * 0.24 * size, math.sin(a) * 0.24 * size) for a in [2 * math.pi * i / 20 for i in range(20)]]
    elif shape == "tower":
        pts = [(-0.24 * size, 0.42 * size), (0.24 * size, 0.42 * size), (0.24 * size, -0.3 * size), (0.0, -0.45 * size), (-0.24 * size, -0.3 * size)]
    elif shape == "tomb":
        pts = [(-0.22 * size, -0.4 * size), (0.22 * size, -0.4 * size), (0.22 * size, 0.2 * size)] + \
              [(math.cos(a) * 0.22 * size, 0.2 * size + math.sin(a) * 0.22 * size) for a in [math.radians(t) for t in range(20, 180, 20)]] + \
              [(-0.22 * size, 0.2 * size)]
    else:  # kite
        pts = [(-0.22 * size, 0.3 * size), (0.22 * size, 0.3 * size), (0.18 * size, -0.05 * size), (0.0, -0.45 * size), (-0.18 * size, -0.05 * size)]
    K.P(G.slab(f"shield_{shape}", pts, 0.07, mat, m=R @ T((0, 0, 0), (0, 0, 0)) @ T((0, 0, 0), (0, 0, 0)) @ Matrix.Rotation(0, 4, "X"),
               bevel=0.015), K.bone("hand_" + side))
    if face is not None:
        K.P(G.slab(f"shield_face_{shape}", [(x * 0.55, y * 0.55) for x, y in pts], 0.02, face, m=R @ T((0, -0.045, 0))), K.bone("hand_" + side))
    if rim is not None:
        K.P(G.uvsphere(f"shield_boss_{shape}", 0.06 * size, rim, m=R @ T((0, -0.05, 0))), K.bone("hand_" + side))


def o_buckler(K):
    _shield(K, "round", K.m["dark_iron"], 0.8, rim=K.m["glow"])


def o_ice_tower_shield(K):
    _shield(K, "tower", K.m["armor"], 1.15, face=K.m["ice"])


def o_tombstone_shield(K):
    _shield(K, "tomb", K.m["stone"], 1.1, face=K.m["glow"])


def o_mirror_shield(K):
    _shield(K, "round", K.m["armor"], 1.25, face=K.m["glow"], rim=K.m["brass"])


def o_kite_shield(K):
    _shield(K, "kite", K.m["cloth"], 1.1, rim=K.m["brass"])


def o_dummy_shield(K):
    _shield(K, "round", K.m["wood"], 1.2, rim=K.m["dark_iron"], side="f")


def o_thorn_blade(K):
    i_thorn_blade(K, "b")


def o_dagger(K):
    i_dagger(K, "b")


# -- back
def b_ember_wings(K):
    for sy in (-1, 1):
        pts = [(0, 0), (-0.25, 0.25), (-0.4, 0.15), (-0.3, 0.05), (-0.38, -0.05), (-0.18, -0.02)]
        K.P(G.slab(f"wing{sy}", pts, 0.025, K.m["cloth"], m=T((-0.1, 0.12 * sy, 1.0), (15 * sy, 0, 0))), "spine")


def b_scarf(K):
    K.P(G.tube("scarf", [(-0.1, 0, 1.2), (-0.3, 0.05, 1.15), (-0.45, 0.05, 0.98), (-0.5, 0.05, 0.85)], [0.06, 0.06, 0.05, 0.03], K.m["cloth"],
               segs=6, flat_y=0.5), "spine")


def b_ember_pouch(K):
    K.P(G.uvsphere("pouch", 0.14, K.m["leather_wrap"], m=T((-0.24, 0.1, 0.8)), scale=(0.8, 0.9, 1.0)), "hips")
    for i in range(3):
        K.P(G.uvsphere(f"ember{i}", 0.045, K.m["glow"], m=T((-0.22 + 0.04 * i, 0.06, 0.94))), "hips")


def b_bellows_pack(K):
    K.P(G.slab("bellows", [(-0.2, -0.2), (0.0, 0.3), (0.2, -0.2)], 0.3, K.m["leather_wrap"], m=T((-0.36, 0, 1.0), (0, 90, 0)), bevel=0.04), "spine")
    K.P(G.tube("bellows_nozzle", [(-0.36, 0, 1.25), (-0.3, 0, 1.45), (-0.15, 0, 1.55)], 0.04, K.m["brass"], segs=8), "spine")


def b_chimneys(K, big=False):
    s = 1.35 if big else 1.0
    for i, (y, h) in enumerate(((-0.14, 0.5), (0.14, 0.38))):
        base = Vector((-0.28, y * s, 1.05))
        K.P(G.lathe(f"chimney{i}", [(0, 0), (0.08 * s, 0), (0.07 * s, h * s), (0.1 * s, h * s + 0.02), (0.1 * s, h * s + 0.08), (0, h * s + 0.08)],
                    K.m["dark_iron"], segs=10, m=Matrix.Translation(base) @ Matrix.Rotation(math.radians(-15), 4, "Y")), "spine")
        K.P(G.uvsphere(f"smoke_glow{i}", 0.07 * s, K.m["glow"], m=T(tuple(base + Vector((-0.12 * s * h, 0, h * s + 0.12))))), "spine")


def b_chimneys_big(K):
    b_chimneys(K, True)


def b_royal_cape(K, mat=None):
    pts = [(-0.05, 1.22), (-0.32, 1.12), (-0.48, 0.6), (-0.56, 0.05), (-0.1, 0.12), (-0.02, 0.6)]
    K.P(G.slab("cape", pts, 0.78, mat or K.m["cloth"], bevel=0.02), "spine")
    K.P(G.torus("cape_clasp", 0.07, 0.025, K.m["gold_trim"], m=T((0.15, -0.3, 1.18)), segs=12), "spine")


def b_royal_cape_ice(K):
    b_royal_cape(K, K.m["cloth"])
    K.P(G.tube("fur_collar", [(0.0, -0.32, 1.2), (-0.15, -0.2, 1.26), (-0.2, 0.0, 1.28), (-0.15, 0.2, 1.26), (0.0, 0.32, 1.2)], 0.1, K.m["fur"],
               segs=8), "spine")


def b_storm_cape(K):
    b_royal_cape(K, K.m["cloth"])
    K.P(G.slab("cape_bolt", [(0.0, 0.0), (0.08, 0.16), (0.0, 0.16), (0.1, 0.4), (-0.08, 0.13), (0.0, 0.13), (-0.06, 0.0)], 0.8, K.m["glow"],
               m=T((-0.36, 0, 0.45))), "spine")


def b_magma_spine(K):
    for i in range(4):
        x = 0.3 - 0.2 * i
        K.P(G.ico(f"spine_rock{i}", 0.1 - 0.012 * i, K.m["body"], m=T((x, 0, K.A["back"].z + 0.05)), subdiv=0, jitter=0.15, rng=K.rng,
                  scale=(1, 0.8, 1.3)), "body")
        K.P(G.uvsphere(f"spine_glow{i}", 0.05, K.m["glow"], m=T((x, -0.04, K.A["back"].z + 0.12))), "body")


def b_leaf_frill(K):
    for i in range(5):
        a = math.radians(-60 + 30 * i)
        K.P(G.slab(f"frill{i}", [(0, 0), (0.08, 0.12), (0.0, 0.3), (-0.08, 0.12)], 0.02, K.m["leaf"],
                   m=T((K.A["head"].x - 0.12, 0, K.A["head"].z), (math.degrees(a), 0, 0)) @ T((0, 0, 0), (0, -30, 0))), "head")


def b_thorns(K):
    for i in range(6):
        x = 0.35 - 0.14 * i
        K.P(G.spike(f"thorn{i}", (x, 0, K.A["back"].z - 0.05), (x - 0.12, 0.0, K.A["back"].z + 0.28 - 0.02 * i), 0.06, K.m["wood"]), "body")
        K.P(G.spike(f"thorn_side{i}", (x, -0.15, K.A["back"].z - 0.12), (x - 0.1, -0.3, K.A["back"].z + 0.1), 0.04, K.m["wood"]), "body")


def b_moss_mound(K):
    K.P(G.uvsphere("moss_mound", 0.3, K.m["moss"], m=T((-0.18, 0, 1.25)), scale=(1.1, 1.3, 0.7)), "spine")


def b_leaf_cape(K):
    for i in range(6):
        K.P(G.slab(f"leafcape{i}", [(0, 0), (0.1, -0.12), (0.0, -0.4), (-0.1, -0.12)], 0.3 + 0.05 * (i % 2), K.m["leaf"],
                   m=T((-0.2 - 0.04 * i, 0, 1.2 - 0.12 * i), (0, -10 - 6 * i, 0))), "spine")


def b_lily(K):
    K.P(G.slab("lily", [(math.cos(a) * 0.3, math.sin(a) * 0.3) for a in [2 * math.pi * i / 16 for i in range(16)]][:-2] + [(0, 0)], 0.03,
               K.m["leaf"], m=T((-0.05, 0, K.A["back"].z - 0.02)), plane="XY"), "body")
    K.P(G.uvsphere("lily_flower", 0.08, K.m["bloom"], m=T((-0.05, 0, K.A["back"].z + 0.04)), scale=(1, 1, 0.7)), "body")


def b_branches(K, big=False):
    s = 1.3 if big else 1.0
    for i, (y, a) in enumerate(((-0.15, 30), (0.15, -25), (0.0, 5))):
        base = Vector((-0.1, y, 1.25))
        tip = base + Vector((-0.25 * s, y * 0.8, 0.55 * s))
        K.P(G.tube(f"branch{i}", [tuple(base), tuple(base.lerp(tip, 0.5) + Vector((0.05, 0, 0))), tuple(tip)], [0.07, 0.05, 0.02], K.m["wood"],
                   segs=6), "spine")
        K.P(G.uvsphere(f"branch_leaf{i}", 0.13 * s, K.m["leaf"], m=T(tuple(tip)), scale=(1.3, 1.2, 0.9)), "spine")


def b_great_branches(K):
    b_branches(K, True)


def b_quiver(K):
    K.P(G.lathe("quiver", [(0, 0), (0.07, 0), (0.08, 0.45), (0, 0.45)], K.m["leather_wrap"], segs=10,
                m=T((-0.24, 0.1, 0.75), (0, -25, 0))), "spine")
    for i in range(3):
        K.P(G.tube(f"arrow{i}", [(-0.05 - 0.02 * i, 0.08 + 0.03 * i, 1.15), (-0.0 - 0.02 * i, 0.08 + 0.03 * i, 1.32)], 0.015, K.m["white"], segs=4),
            "spine")


def b_ice_spikes(K):
    for i in range(4):
        a = math.radians(-40 + 27 * i)
        K.P(G.lathe(f"ice_spike{i}", [(0, 0), (0.06, 0.05), (0, 0.38 - 0.05 * abs(i - 1.5))], K.m["ice"], segs=5, smooth=False,
                    m=T((-0.26, math.sin(a) * 0.25, 1.15), (math.degrees(a), -40, 0))), "spine")


def b_ice_wings(K):
    # butterfly wings standing up behind the body (XZ plane: they face the camera)
    for side, sy in (("f", -1), ("b", 1)):
        up = [(0.0, 0.0), (-0.12, 0.32), (-0.36, 0.48), (-0.5, 0.36), (-0.4, 0.12)]
        lo = [(0.0, -0.02), (-0.3, -0.06), (-0.36, -0.26), (-0.18, -0.3)]
        tilt = 18 * sy
        K.P(G.slab(f"icewing_up{sy}", up, 0.02, K.m["ice"], m=T((-0.04, 0.06 * sy, 1.08), (tilt, 0, 0)), bevel=0.004), f"wing_{side}")
        K.P(G.slab(f"icewing_lo{sy}", lo[::-1], 0.02, K.m["ice"], m=T((-0.04, 0.06 * sy, 1.06), (tilt, 0, 0)), bevel=0.004), f"wing_{side}")


def b_frost_collar(K):
    for i in range(7):
        a = math.radians(-90 + 30 * i)
        K.P(G.lathe(f"collar{i}", [(0, 0), (0.05, 0.04), (0, 0.32)], K.m["ice"], segs=4, smooth=False,
                    m=T((-0.08, math.sin(a) * 0.28, 1.15 + 0.05 * math.cos(a)), (math.degrees(a) * 0.6, -30, 0))), "spine")


def b_crystal_fins(K):
    segs = K.A["segs"]
    for i in (1, 3):
        K.P(G.slab(f"crystal_fin{i}", [(0, 0), (0.2, 0.1), (0.05, 0.45), (-0.15, 0.15)], 0.04, K.m["ice"],
                   m=T(tuple(segs[i] + Vector((0, 0, 0.12))), (0, -10, 0))), f"seg{i}")


def b_ribcage(K):
    for i in range(4):
        x = 0.2 - 0.14 * i
        K.P(G.torus(f"cage_rib{i}", 0.18, 0.025, K.m["bone"], m=T((x, 0, K.A["back"].z - 0.05), (0, 0, 90)) @ T((0, 0, 0), (0, 0, 0)), segs=14,
                    arc=0.5), "body")


def b_spine_spikes(K):
    for i in range(5):
        z = 1.25 - 0.13 * i
        K.P(G.spike(f"vert_spike{i}", (-0.18, 0, z), (-0.42, 0, z + 0.12), 0.05, K.m["bone"]), "spine")


def b_brass_wings(K):
    for sy in (-1, 1):
        for i in range(4):
            pts = [(0, 0), (-0.42 + 0.06 * i, 0.32 - 0.1 * i), (-0.5 + 0.06 * i, 0.26 - 0.1 * i), (-0.08, -0.04)]
            K.P(G.slab(f"brass_feather{sy}{i}", pts, 0.025, K.m["brass"] if i % 2 == 0 else K.m["armor"],
                       m=T((-0.12, 0.16 * sy, 1.15 - 0.03 * i), (20 * sy, -10, 0))), "spine")


def b_sash(K):
    K.P(G.tube("sash", [(-0.2, 0.0, 0.8), (-0.38, 0.05, 0.62), (-0.46, 0.05, 0.4)], [0.06, 0.05, 0.02], K.m["glow"], segs=6, flat_y=0.4), "hips")


def b_coil_spine(K):
    pts = [(0.3 - 0.6 * t / 24, 0.08 * math.sin(t * 1.1), K.A["back"].z + 0.06 + 0.06 * math.cos(t * 1.1)) for t in range(25)]
    K.P(G.tube("coil", pts, 0.025, K.m["brass"], segs=6), "body")
    K.P(G.uvsphere("coil_ball", 0.07, K.m["glow"], m=T((0.0, 0, K.A["back"].z + 0.22))), "body")
    K.P(G.tube("coil_rod", [(0.0, 0, K.A["back"].z), (0.0, 0, K.A["back"].z + 0.18)], 0.02, K.m["dark_iron"], segs=5), "body")


def b_gear_array(K):
    for i, (y, z, r) in enumerate(((-0.25, 1.35, 0.26), (0.2, 1.55, 0.2), (0.0, 1.05, 0.18))):
        K.P(G.gear(f"gear{i}", r, r * 0.8, 10, 0.08, K.m["brass"], m=T((-0.35, y, z), (0, 0, 90)) @ T((0, 0, 0), (0, 0, 0))), "spine")
        K.P(G.uvsphere(f"gear_hub{i}", r * 0.3, K.m["dark_iron"], m=T((-0.35, y - 0.05, z))), "spine")
    K.P(G.tube("exhaust", [(-0.3, 0.32, 1.1), (-0.36, 0.32, 1.75)], 0.06, K.m["dark_iron"], segs=8), "spine")
    K.P(G.uvsphere("exhaust_glow", 0.07, K.m["glow"], m=T((-0.36, 0.32, 1.8))), "spine")


# -- shoulders
def _both_shoulders(K, fn):
    for side in ("f", "b"):
        if "shoulder_" + side in K.A:
            fn(K.A["shoulder_" + side], side)


def s_magma_rocks(K):
    def f(p, side):
        K.P(G.ico(f"rock_{side}", 0.18, K.m["body"], m=T(tuple(p + Vector((0, 0, 0.08)))), subdiv=1, jitter=0.18, rng=K.rng), f"arm_{side}")
        K.P(G.uvsphere(f"rock_glow_{side}", 0.06, K.m["glow"], m=T(tuple(p + Vector((0.08, -0.08 if side == "f" else 0.08, 0.15))))), f"arm_{side}")
    _both_shoulders(K, f)


def s_mossy_rocks(K):
    def f(p, side):
        K.P(G.ico(f"rock_{side}", 0.2, K.m["stone"], m=T(tuple(p + Vector((0, 0, 0.06)))), subdiv=1, jitter=0.15, rng=K.rng), f"arm_{side}")
        K.P(G.uvsphere(f"moss_{side}", 0.15, K.m["moss"], m=T(tuple(p + Vector((0, 0, 0.18)))), scale=(1.1, 1.1, 0.5)), f"arm_{side}")
    _both_shoulders(K, f)


def s_plate_spikes(K):
    def f(p, side):
        K.P(G.uvsphere(f"pauldron_{side}", 0.2, K.m["armor"], m=T(tuple(p + Vector((0, 0, 0.05)))), scale=(1.1, 1.0, 0.75)), f"arm_{side}")
        K.P(G.spike(f"pauldron_spike_{side}", tuple(p + Vector((0, 0, 0.12))), tuple(p + Vector((-0.05, 0, 0.42))), 0.06, K.m["dark_iron"]),
            f"arm_{side}")
    _both_shoulders(K, f)


def s_ice_spikes(K):
    def f(p, side):
        for i in range(3):
            K.P(G.lathe(f"sh_ice_{side}{i}", [(0, 0), (0.05, 0.04), (0, 0.26 - 0.05 * i)], K.m["ice"], segs=5, smooth=False,
                        m=T(tuple(p + Vector((-0.06 * i, 0, 0.1))), (0, -20 * i, 0))), f"arm_{side}")
    _both_shoulders(K, f)


def s_skull_pauldrons(K):
    def f(p, side):
        K.P(G.uvsphere(f"sh_skull_{side}", 0.15, K.m["bone"], m=T(tuple(p + Vector((0, 0, 0.08)))), scale=(1.1, 1, 1)), f"arm_{side}")
        K.P(G.uvsphere(f"sh_skull_eye_{side}", 0.04, K.m["eye"], m=T(tuple(p + Vector((0.12, -0.05 if side == "f" else 0.05, 0.1))))), f"arm_{side}")
    _both_shoulders(K, f)


def s_tesla_pylons(K):
    def f(p, side):
        K.P(G.tube(f"pylon_{side}", [tuple(p), tuple(p + Vector((-0.05, 0, 0.42)))], 0.035, K.m["dark_iron"], segs=6), f"arm_{side}")
        for k in range(3):
            K.P(G.torus(f"pylon_ring_{side}{k}", 0.06, 0.015, K.m["brass"], m=T(tuple(p + Vector((-0.012 * k, 0, 0.15 + 0.08 * k)))) @
                        T((0, 0, 0), (90, 0, 0)), segs=10), f"arm_{side}")
        K.P(G.uvsphere(f"pylon_ball_{side}", 0.06, K.m["glow"], m=T(tuple(p + Vector((-0.05, 0, 0.46))))), f"arm_{side}")
    _both_shoulders(K, f)


def s_brass_domes(K):
    def f(p, side):
        K.P(G.uvsphere(f"dome_{side}", 0.24, K.m["brass"], m=T(tuple(p + Vector((0, 0, 0.04)))), scale=(1.0, 1.0, 0.75)), f"arm_{side}")
        K.P(G.uvsphere(f"dome_rivet_{side}", 0.05, K.m["glow"], m=T(tuple(p + Vector((0.1, -0.12 if side == "f" else 0.12, 0.1))))), f"arm_{side}")
    _both_shoulders(K, f)


# -- auras (on the 'orbit' bone, rotated by the idle Action)
def _ring_of(K, n, r, z, fn):
    for i in range(n):
        a = 2 * math.pi * i / n
        fn(i, Vector((math.cos(a) * r, math.sin(a) * r, z)), a)


def a_ember_ring(K):
    _ring_of(K, 6, 0.6, 0.5, lambda i, p, a: K.P(G.slab(f"ember{i}", [(0, 0.1), (0.05, 0.0), (0, -0.06), (-0.05, 0.0)], 0.03, K.m["glow"],
                                                       m=T(tuple(p))), K.bone("orbit")))


def a_spores(K):
    _ring_of(K, 7, 0.55, 1.1, lambda i, p, a: K.P(G.uvsphere(f"spore{i}", 0.04 + 0.015 * (i % 3), K.m["glow"],
                                                            m=T(tuple(p + Vector((0, 0, 0.12 * math.sin(a * 3)))))), K.bone("orbit")))


def a_halo(K):
    c = K.A["head"]
    K.P(G.torus("halo", 0.24, 0.025, K.m["glow"], m=T(tuple(c + Vector((-0.12, 0, 0.12)))) @ T((0, 0, 0), (0, -15, 0)), segs=24), K.bone("head"))


def a_snowflakes(K):
    _ring_of(K, 6, 0.65, 1.3, lambda i, p, a: K.P(G.slab(f"flake{i}", G.star2d(6, 0.07, 0.025), 0.015, K.m["ice"], m=T(tuple(p))),
                                                    K.bone("orbit")))


def a_skull_orbit(K):
    def f(i, p, a):
        K.P(G.uvsphere(f"orb_skull{i}", 0.09, K.m["bone"], m=T(tuple(p))), K.bone("orbit"))
        K.P(G.uvsphere(f"orb_skull_eye{i}", 0.03, K.m["eye"], m=T(tuple(p + Vector((math.cos(a) * 0.07, math.sin(a) * 0.07, 0.0))))), K.bone("orbit"))
    _ring_of(K, 3, 0.55, 1.25, f)


def a_soul_orbs(K):
    _ring_of(K, 4, 0.7, 1.2, lambda i, p, a: K.P(G.uvsphere(f"soul{i}", 0.08, K.m["glow"], m=T(tuple(p))), K.bone("orbit")))


def a_drips(K):
    _ring_of(K, 5, 0.45, 0.45, lambda i, p, a: K.P(G.uvsphere(f"drip{i}", 0.04, K.m["accent"], m=T(tuple(p)), scale=(1, 1, 1.6)), K.bone("orbit")))


def a_sparks(K):
    _ring_of(K, 5, 0.5, 1.45, lambda i, p, a: K.P(G.slab(f"spark{i}", G.star2d(4, 0.07, 0.02), 0.015, K.m["glow"], m=T(tuple(p))),
                                                    K.bone("orbit")))


def a_shield_ring(K):
    K.P(G.torus("shield_ring", 0.62, 0.025, K.m["glow"], m=T((0, 0, 0.45), (90, 0, 0)), segs=40), K.bone("orbit"))
    _ring_of(K, 4, 0.62, 0.45, lambda i, p, a: K.P(G.slab(f"ring_rune{i}", [(0, 0.07), (0.04, 0), (0, -0.07), (-0.04, 0)], 0.02, K.m["glow"],
                                                         m=T(tuple(p))), K.bone("orbit")))


def a_storm_rings(K):
    for i, (r, z, tilt) in enumerate(((0.55, 0.95, 12), (0.62, 1.45, -10))):
        K.P(G.torus(f"storm_ring{i}", r, 0.025, K.m["glow"], m=T((0, 0, z), (90 + tilt, 0, 0)), segs=40), K.bone("orbit"))


def a_rune_circle(K):
    K.P(G.torus("rune_circle", 0.5, 0.02, K.m["glow"], m=T((0, 0, 0.12), (90, 0, 0)), segs=36), K.bone("orbit"))
    _ring_of(K, 6, 0.5, 0.12, lambda i, p, a: K.P(G.slab(f"circle_rune{i}", [(0, 0.06), (0.035, 0), (0, -0.06), (-0.035, 0)], 0.02, K.m["glow"],
                                                         m=T(tuple(p), (90, 0, 0))), K.bone("orbit")))


# -- extras / bodies
def x_magma_belly(K):
    K.P(G.uvsphere("belly_glow", 0.16, K.m["glow"], m=T((0.12, 0, 0.88)), scale=(0.7, 1.0, 1.0)), "spine")


def x_tail(K):
    K.P(G.tube("tail", [(-0.15, 0, 0.72), (-0.35, 0, 0.6), (-0.48, 0, 0.75)], [0.04, 0.03, 0.015], K.m["body"], segs=6), "hips")
    K.P(G.slab("tail_tip", [(0, 0), (0.06, 0.06), (0, 0.14), (-0.06, 0.06)], 0.02, K.m["glow"], m=T((-0.48, 0, 0.75))), "hips")


def x_magma_cracks(K):
    for i in range(5):
        z = 0.75 + 0.1 * i
        bone = K.bone("body")
        y = -0.25 if K.plan == "biped" else -0.2
        if K.plan == "quad":
            K.P(G.box(f"crack{i}", (0.03, 0.02, 0.14), K.m["glow"], m=T((0.25 - 0.13 * i, -0.24, 0.55 + 0.05 * (i % 2)), (0, 20 * (-1) ** i, 0))), bone)
        else:
            K.P(G.box(f"crack{i}", (0.03, 0.02, 0.12), K.m["glow"], m=T((0.15 - 0.05 * (i % 2), y - 0.04 * (i % 2), z), (0, 25 * (-1) ** i, 0))),
                bone)


def x_lava_core(K):
    K.P(G.uvsphere("lava_core", 0.2, K.m["glow"], m=T((0.3, -0.05, 1.0)), scale=(0.6, 1.0, 1.0)), "spine")
    for i in range(4):
        a = math.radians(45 + 90 * i)
        K.P(G.box(f"core_crack{i}", (0.03, 0.02, 0.22), K.m["glow"], m=T((0.3, -0.05 + math.cos(a) * 0.2, 1.0 + math.sin(a) * 0.2),
                                                                        (math.degrees(a), 0, 0))), "spine")


def x_ash_cloud(K):
    for i in range(4):
        a = 2 * math.pi * i / 4
        K.P(G.uvsphere(f"ash{i}", 0.12, K.m["body"], m=T((math.cos(a) * 0.18 - 0.1, math.sin(a) * 0.18, 0.75 + 0.05 * i))), "tail")


def x_ember_sparks(K):
    a_ember_ring(K)


def x_vine_legs(K):
    pass  # handled by the crawler plan using faction leaf material


def x_moss(K):
    K.P(G.uvsphere("moss_patch", 0.25, K.m["moss"], m=T(tuple(K.A["back"] + Vector((0.05, 0, 0.02)))), scale=(1.4, 1.1, 0.45)), K.bone("body"))


def x_flowers(K):
    for i in range(4):
        p = K.A["back"] + Vector((0.1 - 0.12 * i, -0.2 + 0.13 * i, 0.32))
        K.P(G.uvsphere(f"flower{i}", 0.06, K.m["bloom"], m=T(tuple(p)), scale=(1, 1, 0.6)), K.bone("body"))


def x_seeds(K):
    _ring_of(K, 5, 0.5, 0.35, lambda i, p, a: K.P(G.uvsphere(f"seed{i}", 0.07, K.m["accent"], m=T(tuple(p)), scale=(1, 1, 1.4)), K.bone("orbit")))


def x_mire_drip(K):
    for i in range(3):
        K.P(G.uvsphere(f"mire{i}", 0.05, K.m["glow"], m=T((0.55 + 0.02 * i, -0.05 + 0.05 * i, 0.38 - 0.08 * i)), scale=(1, 1, 1.5)), "head")


def x_root_skirt(K):
    for i in range(8):
        a = 2 * math.pi * i / 8
        K.P(G.tube(f"root{i}", [(math.cos(a) * 0.25, math.sin(a) * 0.25, 0.5), (math.cos(a) * 0.45, math.sin(a) * 0.45, 0.2),
                                (math.cos(a) * 0.6, math.sin(a) * 0.55, 0.02)], [0.08, 0.06, 0.03], K.m["wood"], segs=6), "hips")


def x_vine_tentacles(K):
    for i in range(4):
        a = math.radians(-60 + 40 * i)
        base = Vector((0.15, math.sin(a) * 0.4, 0.2))
        K.P(G.tube(f"tentacle{i}", [tuple(base), tuple(base + Vector((0.3, math.sin(a) * 0.2, 0.25))), tuple(base + Vector((0.55, math.sin(a) * 0.25, 0.1)))],
                   [0.07, 0.05, 0.015], K.m["leaf"], segs=6), "body")


def x_ice_chains(K):
    for i in range(5):
        K.P(G.torus(f"ice_chain{i}", 0.05, 0.015, K.m["ice"], m=T((0.15, -0.2, 1.05 - 0.08 * i), (90 * (i % 2), 0, 0)), segs=10, rsegs=5), "body")


def x_ice_fists(K):
    for side in ("f", "b"):
        p = K.A["hand_" + side]
        K.P(G.ico(f"ice_fist_{side}", 0.2, K.m["ice"], m=T(tuple(p)), subdiv=0, scale=(1.1, 1, 1.0)), f"fore_{side}")


def x_fur(K):
    K.P(G.uvsphere("fur_mantle", 0.36, K.m["fur"], m=T((-0.05, 0, 1.18)), scale=(1.0, 1.25, 0.6)), "spine")


def x_ice_eyes(K):
    pass


def x_gown(K):
    K.P(G.lathe("gown_frill", [(0, 0.12), (0.5, 0.12), (0.54, 0.06), (0.52, 0.02), (0, 0.02)], K.m["accent"], segs=24, m=T((0.03, 0, 0))), "hips")


def x_claws(K):
    for side in ("f", "b"):
        p = K.A["hand_" + side]
        for k in range(3):
            K.P(G.spike(f"claw_{side}{k}", tuple(p), tuple(p + Vector((0.12, -0.04 + 0.04 * k, -0.12))), 0.02, K.m["bone"]), f"fore_{side}")


def x_rags(K):
    pts = [(-0.05, 0.85), (-0.25, 0.8), (-0.3, 0.5), (-0.22, 0.55), (-0.18, 0.4), (-0.1, 0.52), (0.0, 0.6)]
    K.P(G.slab("rags", pts, 0.4, K.m["cloth"]), "hips")


def x_ghost_trail(K):
    for i in range(4):
        K.P(G.uvsphere(f"ghost{i}", 0.12 - 0.02 * i, K.m["glow"], m=T((-0.15 - 0.13 * i, 0, 1.0 - 0.06 * i)), scale=(1.3, 0.8, 0.8)), "body")


def x_bone_legs(K):
    pass


def x_bone_club_arm(K):
    p = K.A["hand_f"]
    K.P(G.tube("bone_club", [tuple(p), tuple(p + Vector((0.08, 0, -0.25))), tuple(p + Vector((0.12, 0, -0.45)))], [0.08, 0.1, 0.16], K.m["bone"],
               segs=8), "fore_f")
    K.P(G.uvsphere("bone_club_knob", 0.17, K.m["bone"], m=T(tuple(p + Vector((0.13, 0, -0.5))))), "fore_f")


def x_bell(K):
    K.P(G.lathe("bell", [(0, 0.0), (0.1, 0.0), (0.12, -0.1), (0.2, -0.3), (0, -0.28)], K.m["rust_iron"], segs=14, m=T((0.1, 0, 0.62))), "body")


def x_piston_arms(K):
    for side in ("f", "b"):
        e, h = K.A["elbow_" + side], K.A["hand_" + side]
        K.P(G.tube(f"piston_{side}", [tuple(e + Vector((0.08, 0, 0.05))), tuple(h + Vector((0.08, 0, 0.1)))], 0.035, K.m["dark_iron"], segs=6),
            f"fore_{side}")
        K.P(G.gear(f"wrist_gear_{side}", 0.14, 0.11, 8, 0.06, K.m["brass"], m=T(tuple(h + Vector((0, -0.12 if side == "f" else 0.12, 0.05))))),
            f"fore_{side}")


def x_sparks(K):
    a_sparks(K)


def x_lens_eye(K):
    pass


def x_mini_dummies(K):
    for i, (x, y) in enumerate(((-0.42, 0.18), (0.38, 0.25))):
        K.P(G.tube(f"mini_post{i}", [(x, y, 0.0), (x, y, 0.42)], 0.04, K.m["wood"], segs=6), "root")
        K.P(G.lathe(f"mini_sack{i}", [(0, 0.3), (0.12, 0.32), (0.14, 0.5), (0, 0.6)], K.m["sack"], segs=10, m=T((x, y, 0))), "root")
        K.P(G.uvsphere(f"mini_head{i}", 0.09, K.m["sack"], m=T((x, y, 0.68))), "root")


def x_dummy_plate(K):
    K.P(G.uvsphere("plate", 0.27, K.m["dark_iron"], m=T((0.02, 0, 0.85)), scale=(0.9, 1.0, 1.0)), "body")
    for sy in (-1, 1):
        K.P(G.uvsphere(f"plate_sh{sy}", 0.12, K.m["dark_iron"], m=T((0, 0.3 * sy, 1.0))), "body")


def x_rune_paint(K):
    for i in range(3):
        K.P(G.slab(f"paint{i}", [(0, 0.08), (0.05, 0), (0, -0.08), (-0.05, 0)], 0.02, K.m["glow"], m=T((0.05 - 0.05 * i, -0.24, 1.02 - 0.12 * i))),
            "body")


def x_wyrm_wings(K):
    segs = K.A["segs"]
    for side, sy in (("f", -1), ("b", 1)):
        pts = [(0.0, 0.0), (0.2, 0.3), (0.1, 0.8), (-0.15, 0.95), (-0.2, 0.6), (-0.4, 0.5), (-0.25, 0.2)]
        pts = [(x, y * sy) for x, y in pts]
        if sy > 0:
            pts = pts[::-1]
        K.P(G.slab(f"wyrm_wing{sy}", pts, 0.025, K.m["ice"], m=T(tuple(segs[1] + Vector((0, 0.08 * sy, 0.1))), (-55 * sy, 0, 0)), plane="XY"),
            "seg1")


# -- blob / wisp bodies
def body_ember_core(K):
    K.P(G.uvsphere("core", 0.24, K.m["glow"], m=T((0.05, 0, 1.0))), "body")
    K.P(G.slab("flame", [(0, 0.55), (0.18, 0.2), (0.22, -0.05), (0.0, -0.2), (-0.22, -0.05), (-0.14, 0.25)], 0.26, K.m["cloth"],
               m=T((-0.02, 0, 1.05), (0, -20, 0))), "body")
    K.P(G.cone("tail_flame", 0.14, 0.0, 0.45, K.m["glow"], m=T((-0.05, 0, 0.85), (0, 160, 0)), segs=8), "tail")
    _eyes(K, Vector((0.05, 0, 1.02)), 0.04, sep=0.08, fwd=1.0, z=0.02, mat=K.m["dark"])


def body_pod(K):
    K.P(G.lathe("pod", [(0, 0), (0.36, 0.04), (0.44, 0.25), (0.38, 0.55), (0.2, 0.75), (0, 0.8)], K.m["leaf"], segs=18), "body")
    for i in range(6):
        a = 2 * math.pi * i / 6
        K.P(G.tube(f"pod_seam{i}", [(math.cos(a) * 0.37, math.sin(a) * 0.37, 0.05), (math.cos(a) * 0.45, math.sin(a) * 0.45, 0.3),
                                    (math.cos(a) * 0.2, math.sin(a) * 0.2, 0.75)], 0.02, K.m["accent"], segs=5), "body")
    _eyes(K, Vector((0.25, 0, 0.42)), 0.05, sep=0.1, fwd=0.85, z=0.0)


def body_bloom(K):
    K.P(G.lathe("bulb", [(0, 0), (0.4, 0.05), (0.55, 0.3), (0.48, 0.6), (0.3, 0.75), (0, 0.78)], K.m["leaf"], segs=20), "body")
    K.P(G.uvsphere("maw", 0.28, K.m["dark"], m=T((0.32, 0, 0.55)), scale=(0.6, 1.0, 0.9)), "body")
    for i in range(6):
        K.P(G.cone(f"maw_tooth{i}", 0.03, 0.0, 0.09, K.m["white"], m=T((0.44, -0.18 + 0.07 * i, 0.72), (180, 0, 0)), segs=4), "body")
        K.P(G.cone(f"maw_toothb{i}", 0.03, 0.0, 0.08, K.m["white"], m=T((0.44, -0.18 + 0.07 * i, 0.36)), segs=4), "body")
    _eyes(K, Vector((0.3, 0, 0.9)), 0.05, sep=0.12, fwd=0.5, z=0.0)


def body_crystal_cluster(K):
    for i, (x, y, h, a) in enumerate(((0, 0, 0.75, 0), (0.18, -0.12, 0.5, 25), (-0.16, 0.1, 0.55, -20), (0.05, 0.18, 0.42, -10),
                                      (-0.05, -0.2, 0.45, 15))):
        K.P(G.lathe(f"crystal{i}", [(0, 0), (0.12, 0.08), (0.1, h * 0.75), (0, h)], K.m["ice"] if i % 2 == 0 else K.m["body"], segs=6,
                    smooth=False, m=T((x, y, 0), (a, 0, 0))), "body")
    _eyes(K, Vector((0.12, 0, 0.32)), 0.045, sep=0.07, fwd=1.0, z=0.0)


def body_wraith(K):
    K.P(G.uvsphere("wraith_body", 0.26, K.m["body"], m=T((0.0, 0, 1.0)), scale=(0.9, 0.9, 1.2)), "body")
    K.P(G.uvsphere("wraith_hood", 0.22, K.m["cloth"], m=T((0.06, 0, 1.22))), "body")
    K.P(G.uvsphere("wraith_face", 0.16, K.m["dark"], m=T((0.15, 0, 1.2)), scale=(0.6, 1.0, 1.0)), "body")
    _eyes(K, Vector((0.12, 0, 1.22)), 0.03, sep=0.06, fwd=1.0)
    K.P(G.slab("wraith_tail", [(0.2, 0.0), (0.0, -0.55), (-0.12, -0.35), (-0.2, -0.6), (-0.28, -0.3), (-0.22, 0.05)], 0.32, K.m["body"],
               m=T((0, 0, 0.95))), "tail")
    for sy in (-1, 1):
        K.P(G.tube(f"wraith_arm{sy}", [(0.05, 0.22 * sy, 1.1), (0.25, 0.3 * sy, 0.95), (0.35, 0.28 * sy, 0.82)], [0.06, 0.04, 0.02],
                   K.m["body"], segs=6), "body")


def body_lich(K):
    K.P(G.lathe("lich_robe", [(0, 1.3), (0.24, 1.25), (0.32, 0.9), (0.42, 0.45), (0.0, 0.42)], K.m["cloth"], segs=18), "body")
    K.P(G.slab("lich_tatter", [(0.35, 0.45), (0.2, 0.15), (0.05, 0.4), (-0.1, 0.1), (-0.2, 0.42), (-0.4, 0.45)], 0.5, K.m["cloth"],
               m=T((0, 0, 0))), "tail")
    K.A["head_r"] = 0.17
    c = K.A["head"] = Vector((0.06, 0, 1.45))
    h_skull(K)
    K.P(G.uvsphere("lich_hood", 0.24, K.m["cloth"], m=T((-0.02, 0, 1.48)), scale=(0.9, 1.05, 1.0)), "body")
    for sy in (-1, 1):
        K.P(G.tube(f"lich_arm{sy}", [(0.0, 0.28 * sy, 1.2), (0.2, 0.32 * sy, 1.0), (0.32, 0.28 * sy, 0.95)], [0.07, 0.05, 0.03], K.m["cloth"], segs=6),
            "body")
        K.P(G.uvsphere(f"lich_hand{sy}", 0.05, K.m["bone"], m=T((0.34, 0.28 * sy, 0.94))), "body")
    K.A["chest"] = Vector((0.2, 0, 1.0))
    del c


def body_choir(K):
    for i, (x, y, z, s) in enumerate(((0.1, 0.0, 1.0, 1.0), (-0.25, -0.3, 0.85, 0.8), (-0.25, 0.32, 0.9, 0.85))):
        K.P(G.lathe(f"choir_robe{i}", [(0, 0.5 * s), (0.18 * s, 0.45 * s), (0.25 * s, 0.0), (0.18 * s, -0.4 * s), (0, -0.45 * s)], K.m["accent"],
                    segs=14, m=T((x, y, z))), "body" if i == 0 else "orbit")
        K.P(G.uvsphere(f"choir_hood{i}", 0.17 * s, K.m["cloth"], m=T((x + 0.02, y, z + 0.55 * s))), "body" if i == 0 else "orbit")
        bn = "body" if i == 0 else "orbit"
        K.P(G.uvsphere(f"choir_face{i}", 0.14 * s, K.m["accent"], m=T((x + 0.1 * s, y, z + 0.53 * s)), scale=(0.7, 1, 1.05)), bn)
        K.P(G.uvsphere(f"choir_mouth{i}", 0.05 * s, K.m["dark"], m=T((x + 0.19 * s, y, z + 0.46 * s)), scale=(0.5, 1, 1.5)), bn)
        for sy in (-1, 1):
            K.P(G.uvsphere(f"choir_eye{i}{sy}", 0.035 * s, K.m["eye"], m=T((x + 0.19 * s, y + 0.055 * s * sy, z + 0.58 * s))), bn)
        for k in range(3):
            K.P(G.tube(f"kelp{i}{k}", [(x - 0.05 + 0.08 * k, y - 0.15 * s, z + 0.3 * s), (x + 0.05 * k, y - 0.2 * s, z), (x - 0.04 + 0.06 * k, y - 0.2 * s, z - 0.3 * s)],
                       [0.03, 0.025, 0.01], K.m["moss"], segs=5), bn)
    K.P(G.lathe("choir_water", [(0, 0.35), (0.5, 0.4), (0.55, 0.36), (0, 0.3)], K.m["glow"], segs=20, m=T((-0.1, 0, 0))), "tail")


def body_drone(K):
    K.P(G.uvsphere("drone", 0.24, K.m["armor"], m=T((0, 0, 1.0))), "body")
    K.P(G.torus("drone_band", 0.245, 0.03, K.m["body"], m=T((0, 0, 1.0), (90, 0, 0)), segs=24), "body")
    K.P(G.lathe("drone_eye", [(0, 0), (0.1, 0), (0.11, 0.05), (0, 0.07)], K.m["glow"], segs=14, m=T((0.2, 0, 1.0), (0, 90, 0))), "body")
    for sy in (-1, 1):
        K.P(G.tube(f"drone_arm{sy}", [(0.0, 0.2 * sy, 0.95), (0.1, 0.28 * sy, 0.75), (0.18, 0.26 * sy, 0.68)], 0.025, K.m["dark_iron"], segs=5), "body")
    K.P(G.lathe("drone_stinger", [(0, 0), (0.08, 0.0), (0, -0.22)], K.m["dark_iron"], segs=8, m=T((0, 0, 0.8))), "tail")


def body_static_core(K):
    K.P(G.ico("static", 0.2, K.m["glow"], m=T((0, 0, 1.0)), subdiv=1), "body")
    for i in range(6):
        a = 2 * math.pi * i / 6
        d = Vector((math.cos(a), math.sin(a) * 0.4, math.sin(a)))
        K.P(G.tube(f"arc{i}", [tuple(Vector((0, 0, 1.0)) + d * 0.18), tuple(Vector((0, 0, 1.0)) + d * 0.32 + Vector((0.04, 0, 0.03))),
                               tuple(Vector((0, 0, 1.0)) + d * 0.42)], [0.03, 0.02, 0.005], K.m["glow"], segs=4), "body")
    _eyes(K, Vector((0.0, 0, 1.0)), 0.035, sep=0.07, fwd=0.9, mat=K.m["dark"])


def x_arc_rings(K):
    for i, (r, tilt) in enumerate(((0.36, 20), (0.42, -35))):
        K.P(G.torus(f"arc_ring{i}", r, 0.018, K.m["brass"], m=T((0, 0, 1.0), (90 + tilt, 0, 30 * i)), segs=30), K.bone("orbit"))


def t_sprout(K):
    K.P(G.tube("sprout", [(0, 0, 0.78), (0.05, 0, 0.95), (-0.02, 0, 1.08)], [0.03, 0.025, 0.02], K.m["leaf"], segs=6), "top")
    for sy in (-1, 1):
        K.P(G.slab(f"sprout_leaf{sy}", [(0, 0), (0.1, 0.06), (0.22, 0.0), (0.1, -0.05)], 0.02, K.m["leaf"],
                   m=T((-0.02, 0, 1.06), (0, -20, 90 * sy + 90))), "top")


def t_fanged_petals(K):
    for i in range(7):
        a = 2 * math.pi * i / 7
        K.P(G.slab(f"petal{i}", [(0, 0), (0.15, 0.2), (0.0, 0.55), (-0.15, 0.2)], 0.04, K.m["bloom"],
                   m=T((math.cos(a) * 0.3, math.sin(a) * 0.3, 0.72), (0, 0, math.degrees(a) - 90)) @ T((0, 0, 0), (35, 0, 0))), "top")


def t_rotor(K):
    K.P(G.tube("rotor_mast", [(0, 0, 1.2), (0, 0, 1.36)], 0.025, K.m["dark_iron"], segs=6), "body")
    for i in range(3):
        a = math.radians(120 * i)
        K.P(G.slab(f"rotor_blade{i}", [(0, -0.04), (0.4, -0.03), (0.42, 0.03), (0, 0.04)], 0.015, K.m["brass"],
                   m=T((0, 0, 1.36), (0, 0, math.degrees(a))), plane="XY"), "orbit")


PARTS = {
    "head": {k[2:]: v for k, v in globals().items() if k.startswith("h_")},
    "held": {k[2:]: v for k, v in globals().items() if k.startswith("i_")},
    "offhand": {k[2:]: v for k, v in globals().items() if k.startswith("o_")},
    "back": {k[2:]: v for k, v in globals().items() if k.startswith("b_")},
    "shoulder": {k[2:]: v for k, v in globals().items() if k.startswith("s_")},
    "aura": {k[2:]: v for k, v in globals().items() if k.startswith("a_")},
    "extra": {k[2:]: v for k, v in globals().items() if k.startswith("x_")},
    "body": {k[5:]: v for k, v in globals().items() if k.startswith("body_")},
    "top": {k[2:]: v for k, v in globals().items() if k.startswith("t_")},
}


def build(job):
    K = Kit(job)
    PLANS[K.plan](K)
    order = ["body", "head", "held", "offhand", "back", "shoulder", "aura", "extra", "top"]
    toks = sorted(K.rec.get("parts", []), key=lambda t: order.index(t.split(":")[0]))
    for tok in toks:
        kind, name = tok.split(":")
        PARTS[kind][name](K)
    if K.plan == "biped" and not any(t.startswith("head:") for t in toks):
        _head_ball(K)
    bone_names = {b[0] for b in K.bones}
    alias = {"spine": "body", "hips": "body", "fore_f": "hand_f", "fore_b": "hand_b"}

    def resolve(bname):
        if bname in bone_names:
            return bname
        r = K.B.get(alias.get(bname, bname)) or K.B.get(bname)
        return r if r in bone_names else K.B.get("body", "root")
    arm = C.make_armature(f"rig_{job['asset']}", K.bones)
    for o, bname in K.parts:
        C.bind(arm, o, resolve(bname))
    arm.rotation_euler = (0, 0, math.radians(YAW.get(K.plan, 205.0)))
    s = K.rec.get("scale", 1.0)
    arm.scale = (s, s, s)
    bpy.context.view_layer.update()
    arm["fb_plan"] = K.plan
    arm["fb_hunch"] = float(K.rec.get("hunch", 0.0))
    arm["fb_flying"] = bool(K.rec.get("flying", False))
    arm["fb_held"] = ",".join(t.split(":")[1] for t in toks if t.startswith("held:"))
    return arm


# ================================================================== actions
def _idle_biped(arm, n, f, ph):
    b = math.sin(ph)
    hunch = arm.get("fb_hunch", 0.0)
    heavy = 1.0 + 0.4 * (arm.scale[0] > 1.2)
    held = arm.get("fb_held", "")
    hold_f = (-12, -14) if held else (-6, -10)
    rots = {"spine": [("Y", -hunch + 2.2 * b * heavy)], "head": [("Y", hunch * 0.6 - 2.5 * math.sin(ph - 0.6))],
            "arm_f": [("Y", hold_f[0] + 3 * math.sin(ph - 0.4))], "fore_f": [("Y", hold_f[1] + 2 * math.sin(ph - 0.8))],
            "arm_b": [("Y", 6 - 3 * math.sin(ph - 0.4))], "fore_b": [("Y", -14)],
            "thigh_f": [("Y", -4 - hunch * 0.3)], "shin_f": [("Y", 6 + hunch * 0.3)], "thigh_b": [("Y", 6)], "shin_b": [("Y", 4)],
            "orbit": [("Z", 120.0 * f / n)]}
    return rots, {"spine": (1.0 + 0.015 * b, 1.0 + 0.015 * b, 1.0 + 0.02 * b)}, {}


def _idle_quad(arm, n, f, ph):
    b = math.sin(ph)
    rots = {"head": [("Y", 4 * math.sin(ph - 0.5))], "tail": [("Z", 18 * math.sin(ph)), ("Y", 6 * math.cos(ph))],
            "orbit": [("Z", 120.0 * f / n)]}
    for leg in ("fl", "fr", "ml", "mr", "bl", "br"):
        rots[f"leg_{leg}"] = [("Y", 2.5 * math.sin(ph + (0 if leg[1] == "l" else math.pi)))]
    return rots, {"body": (1.0 + 0.03 * b, 1.0 + 0.03 * b, 1.0 + 0.02 * b)}, {}


def _idle_blob(arm, n, f, ph):
    b = math.sin(ph)
    return ({"body": [("Y", 2.5 * math.cos(ph))], "top": [("Y", 6 * math.sin(ph - 0.7))], "orbit": [("Z", 72.0 * f / n)]},
            {"body": (1 + 0.06 * b, 1 + 0.06 * b, 1 - 0.07 * b)}, {})


def _idle_wing(arm, n, f, ph):
    flap = math.sin(ph)
    # +-28 deg dihedral offset so no frame shows the membranes exactly edge-on
    return ({"wing_f": [("X", -28 - 38 * flap)], "wing_b": [("X", 28 + 38 * flap)], "body": [("Y", 4 * math.cos(ph))], "orbit": [("Z", 60.0 * f / n)]},
            {}, {"root": (0, 0, 0.06 * math.cos(ph))})


def _idle_wisp(arm, n, f, ph):
    return ({"body": [("Y", 4 * math.sin(ph))], "tail": [("Y", 12 * math.sin(ph - 0.8)), ("Z", 8 * math.cos(ph))],
             "orbit": [("Z", 120.0 * f / n)]}, {"body": (1 + 0.02 * math.sin(ph), 1, 1 + 0.02 * math.sin(ph))}, {"root": (0, 0, 0.07 * math.sin(ph))})


def _idle_serpent(arm, n, f, ph):
    rots = {"orbit": [("Z", 0)]}
    for i in range(7):
        rots[f"seg{i}"] = [("Y", 7 * math.sin(ph - i * 0.9))]
    return rots, {}, {"root": (0, 0, 0.06 * math.sin(ph))}


def _idle_dummy(arm, n, f, ph):
    return ({"post": [("Y", 2.5 * math.sin(ph))], "body": [("Y", 3 * math.sin(ph - 0.7)), ("X", 1.5 * math.cos(ph))],
             "orbit": [("Z", 60.0 * f / n)]}, {}, {})


IDLES = {"biped": _idle_biped, "quad": _idle_quad, "blob": _idle_blob, "wing": _idle_wing, "wisp": _idle_wisp, "serpent": _idle_serpent,
         "dummy": _idle_dummy}


def apply_action(arm, job, clip):
    n, fps, loop = spec.CLIPS[clip]
    C.new_action(arm, f"{job['asset']}_{clip}")
    scene = bpy.context.scene
    scene.frame_start, scene.frame_end = 1, n
    fn = IDLES[arm.get("fb_plan", "biped")]
    names = {pb.name for pb in arm.pose.bones}
    for f in range(n):
        ph = 2 * math.pi * f / n
        rots, scales, locs = fn(arm, n, f, ph)
        rots = {k: v for k, v in rots.items() if k in names}
        scales = {k: v for k, v in scales.items() if k in names}
        locs = {k: v for k, v in locs.items() if k in names}
        C._key(arm, f + 1, rots=rots, scales=scales, locs=locs)
    for fc in arm.animation_data.action.fcurves:
        for kp in fc.keyframe_points:
            kp.interpolation = "LINEAR"
    fit_to_canvas(arm, n, grounded=not arm.get("fb_flying", False))


def fit_to_canvas(arm, n, grounded=True):
    """Uniformly shrink the rig (about the ground pivot) until every frame of
    the loop fits the character canvas with a margin."""
    cam = spec.CAMERAS["character"]
    half_w = cam["ortho_scale"] / 2
    cz = cam["center"][1]
    top_lim = cz + half_w - 0.1          # 10 px margin at the top
    side_lim = half_w - 0.08
    bot_lim = cz - half_w + 0.05
    scene = bpy.context.scene
    xs0, xs1, zs0, zs1 = [], [], [], []
    obs = [o for o in scene.objects if o.type == "MESH"]
    for f in range(n):
        scene.frame_set(f + 1)
        for o in obs:
            for c in o.bound_box:
                w = o.matrix_world @ Vector(c)
                xs0.append(w.x)
                zs0.append(w.z)
    scene.frame_set(1)
    x_ext = max(max(xs0), -min(xs0))
    k = min(1.0, side_lim / max(x_ext, 1e-6), top_lim / max(max(zs0), 1e-6))
    if not grounded and min(zs0) < bot_lim:
        k = min(k, 1.0)
    if k < 1.0:
        s = arm.scale[0] * k
        arm.scale = (s, s, s)
        bpy.context.view_layer.update()
    arm["fb_fit"] = round(k, 4)
    del xs1, zs1
