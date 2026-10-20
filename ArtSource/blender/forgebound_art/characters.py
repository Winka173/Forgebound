"""Rigged procedural characters (Forgemaster benchmark + simple enemies).

Rigid-skinned: every mesh part is bone-parented to an Armature; animation is
authored ONLY as Actions on pose bones (rotation quaternions / scale) and the
armature root, then rendered frame-by-frame.  No per-frame geometry edits.

Conventions: modelled facing +X, Z up, feet on Z=0.  The Forgemaster is yawed
-25 deg toward the camera; enemies face -X (toward the player) and are yawed
205 deg.  Rotations are specified around *armature-space* axes and converted to
bone-local quaternions (see _key), so 'Y' swings a limb in the side-view plane;
for a limb hanging down, a negative Y angle swings it forward (+X).
"""
from __future__ import annotations

import math
import random

import bpy
from mathutils import Matrix, Quaternion, Vector

from . import geometry as G
from . import materials as M
from . import palette, spec

T = G.trs


# ------------------------------------------------------------------ rig utils
def make_armature(name, bones):
    """bones: [(name, head, tail, parent)]"""
    arm_data = bpy.data.armatures.new(name)
    arm = bpy.data.objects.new(name, arm_data)
    bpy.context.scene.collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="EDIT")
    eb = arm_data.edit_bones
    for bn, head, tail, parent in bones:
        b = eb.new(bn)
        b.head, b.tail = Vector(head), Vector(tail)
        b.roll = 0.0
        if parent:
            b.parent = eb[parent]
    bpy.ops.object.mode_set(mode="OBJECT")
    for pb in arm.pose.bones:
        pb.rotation_mode = "QUATERNION"
    return arm


def bind(arm, obj, bone):
    """Rigid bind: parent obj to bone keeping its rest world transform."""
    bpy.context.view_layer.update()
    world = obj.matrix_world.copy()
    obj.parent = arm
    obj.parent_type = "BONE"
    obj.parent_bone = bone
    bpy.context.view_layer.update()
    obj.matrix_world = world


def _key(arm, frame, rots=None, scales=None, root_loc=None, locs=None):
    """Key every pose bone.  rots: {bone: [(axis, deg), ...]} around ARMATURE axes;
    locs: {bone: (x, y, z)} offsets in ARMATURE space (converted to bone-local)."""
    rots = rots or {}
    scales = scales or {}
    locs = locs or {}
    for pb in arm.pose.bones:
        if pb.name in locs:
            rest = pb.bone.matrix_local.to_3x3()
            pb.location = rest.inverted() @ Vector(locs[pb.name])
            pb.keyframe_insert("location", frame=frame)
        rest = pb.bone.matrix_local.to_3x3()
        q = Quaternion()
        for axis, deg in rots.get(pb.name, []):
            ax = {"X": Vector((1, 0, 0)), "Y": Vector((0, 1, 0)), "Z": Vector((0, 0, 1))}[axis]
            q = Quaternion(ax, math.radians(deg)) @ q
        local = (rest.inverted() @ q.to_matrix() @ rest).to_quaternion()
        pb.rotation_quaternion = local
        pb.keyframe_insert("rotation_quaternion", frame=frame)
        s = scales.get(pb.name, (1, 1, 1))
        pb.scale = s
        pb.keyframe_insert("scale", frame=frame)
    if root_loc is not None:
        arm.pose.bones["root"].location = root_loc
        arm.pose.bones["root"].keyframe_insert("location", frame=frame)


def new_action(arm, name):
    if arm.animation_data is None:
        arm.animation_data_create()
    act = bpy.data.actions.new(name)
    arm.animation_data.action = act
    return act


# ------------------------------------------------------------- Forgemaster
HUMANOID = [
    ("root", (0, 0, 0), (0, 0, 0.2), None),
    ("hips", (0, 0, 0.58), (0, 0, 0.8), "root"),
    ("spine", (0, 0, 0.8), (0, 0, 1.12), "hips"),
    ("head", (0.02, 0, 1.18), (0.02, 0, 1.55), "spine"),
    ("arm_f", (0.0, -0.36, 1.08), (0.06, -0.38, 0.8), "spine"),
    ("fore_f", (0.06, -0.38, 0.8), (0.2, -0.4, 0.62), "arm_f"),
    ("arm_b", (0.0, 0.36, 1.08), (-0.04, 0.38, 0.8), "spine"),
    ("fore_b", (-0.04, 0.38, 0.8), (0.04, 0.4, 0.6), "arm_b"),
    ("thigh_f", (0.0, -0.15, 0.6), (0.02, -0.15, 0.32), "hips"),
    ("shin_f", (0.02, -0.15, 0.32), (0.0, -0.15, 0.06), "thigh_f"),
    ("thigh_b", (0.0, 0.15, 0.6), (0.02, 0.15, 0.32), "hips"),
    ("shin_b", (0.02, 0.15, 0.32), (0.0, 0.15, 0.06), "thigh_b"),
]


def _lib(key, seed, rim="#FFD9A0"):
    return M.library_material(key, rim=rim, rim_strength=0.7, seed=seed, suffix="_char")


def build_blacksmith(seed):
    arm = make_armature("rig_blacksmith", HUMANOID)
    # batch 2: value-separated outfit (lighter shirt, near-black apron, warm grey trousers, ginger beard)
    skin, shirt, apron, beard = _lib("skin", seed), _lib("bs_shirt", seed), _lib("bs_apron", seed), _lib("bs_beard", seed)
    trousers = _lib("bs_trousers", seed)
    leather, iron, steel, gold = _lib("leather_wrap", seed), _lib("dark_iron", seed), _lib("forged_steel", seed), _lib("gold_trim", seed)
    cloth, wood = _lib("cloth_red", seed), _lib("wood", seed)
    eye = M.toon_material("FB_eye", "#1B1412", "#000000", "#FFFFFF", rim_strength=0.0, spec_amt=1.0, spec_size=0.2)
    ember = M.core_rune_material("fire", 0.9)
    parts = []
    P = lambda o, b: parts.append((o, b))  # noqa: E731
    # body
    P(G.uvsphere("torso", 0.3, shirt, m=T((0.02, 0, 0.96)), scale=(1.15, 1.25, 1.2)), "spine")
    P(G.uvsphere("chest", 0.26, shirt, m=T((0.08, 0, 1.06)), scale=(1.1, 1.3, 0.9)), "spine")
    P(G.uvsphere("belly", 0.3, apron, m=T((0.08, 0, 0.78)), scale=(1.05, 1.1, 0.95)), "hips")
    P(G.lathe("skirt", [(0, 0.8), (0.3, 0.8), (0.34, 0.58), (0.37, 0.4), (0, 0.4)], apron, segs=16, scale_xy=(1.05, 1.1),
              m=T((0.05, 0, 0))), "hips")
    P(G.box("apron_front", (0.06, 0.36, 0.5), leather, m=T((0.39, 0, 0.62), (0, -6, 0)), bevel=0.025, segs=2, smooth=True), "hips")
    P(G.box("bib", (0.05, 0.3, 0.26), apron, m=T((0.33, 0, 1.0), (0, -12, 0)), bevel=0.02, smooth=True), "spine")
    P(G.lathe("belt", [(0, 0.74), (0.33, 0.74), (0.345, 0.8), (0.33, 0.86), (0, 0.86)], leather, segs=16, scale_xy=(1.05, 1.1),
              m=T((0.05, 0, 0))), "hips")
    P(G.box("buckle", (0.06, 0.14, 0.12), gold, m=T((0.41, 0, 0.8)), bevel=0.02), "hips")
    P(G.ico("ember_pendant", 0.045, ember, m=T((0.43, 0, 0.94)), subdiv=1), "spine")
    # head
    P(G.uvsphere("head", 0.23, skin, m=T((0.06, 0, 1.38)), scale=(1.0, 0.95, 1.05)), "head")
    P(G.uvsphere("nose", 0.07, skin, m=T((0.29, 0, 1.37)), scale=(1.1, 0.9, 1.0)), "head")
    for sy in (-1, 1):
        P(G.uvsphere(f"ear{sy}", 0.055, skin, m=T((0.04, 0.22 * sy, 1.38)), scale=(0.8, 0.5, 1.1)), "head")
    P(G.uvsphere("eye", 0.034, eye, m=T((0.25, -0.1, 1.45))), "head")
    P(G.box("brow", (0.13, 0.06, 0.05), beard, m=T((0.24, -0.1, 1.51), (0, -10, 0)), bevel=0.012, smooth=True), "head")
    P(G.lathe("beard", [(0, 0.0), (0.17, 0.02), (0.19, 0.14), (0.15, 0.27), (0, 0.34)], beard, segs=12,
              m=T((0.19, 0, 1.0), (0, 18, 0)), scale_xy=(1.0, 1.25)), "head")
    P(G.uvsphere("moustache", 0.085, beard, m=T((0.28, 0, 1.31)), scale=(0.9, 1.9, 0.6)), "head")
    P(G.torus("headband", 0.215, 0.04, cloth, m=T((0.06, 0, 1.5), (90, 0, 0)), segs=20), "head")
    P(G.tube("band_tail", [(-0.15, 0, 1.5), (-0.27, 0.02, 1.43), (-0.33, 0.02, 1.3)], [0.04, 0.035, 0.02], cloth, segs=6), "head")
    # arms (front arm holds hammer)
    for side, y in (("f", -1), ("b", 1)):
        sh = Vector((0.0, 0.36 * y, 1.08))
        el = Vector((0.06 if side == "f" else -0.04, 0.38 * y, 0.8))
        ha = Vector((0.2 if side == "f" else 0.04, 0.4 * y, 0.62 if side == "f" else 0.6))
        P(G.uvsphere(f"shoulder_{side}", 0.13, shirt, m=T(tuple(sh))), f"arm_{side}")
        P(G.tube(f"upper_{side}", [tuple(sh), tuple(el)], [0.11, 0.095], shirt, segs=10), f"arm_{side}")
        P(G.tube(f"fore_{side}", [tuple(el), tuple(ha)], [0.1, 0.11], skin, segs=10), f"fore_{side}")
        P(G.tube(f"bracer_{side}", [tuple(el.lerp(ha, 0.35)), tuple(el.lerp(ha, 0.8))], 0.122, leather, segs=10), f"fore_{side}")
        P(G.uvsphere(f"hand_{side}", 0.09, skin, m=T(tuple(ha + (ha - el).normalized() * 0.05))), f"fore_{side}")
    # legs
    for side, y in (("f", -1), ("b", 1)):
        P(G.tube(f"thigh_{side}", [(0.0, 0.15 * y, 0.62), (0.02, 0.15 * y, 0.32)], [0.12, 0.1], trousers, segs=10), f"thigh_{side}")
        P(G.tube(f"shin_{side}", [(0.02, 0.15 * y, 0.34), (0.0, 0.15 * y, 0.12)], [0.095, 0.09], trousers, segs=10), f"shin_{side}")
        P(G.box(f"boot_{side}", (0.3, 0.16, 0.14), leather, m=T((0.06, 0.15 * y, 0.07)), bevel=0.05, segs=2, smooth=True), f"shin_{side}")
    # hammer in front hand, continuing the forearm direction
    el = Vector((0.06, -0.38, 0.8))
    ha = Vector((0.2, -0.42, 0.62))
    d = (ha - el).normalized()
    g0, g1 = ha - d * 0.12, ha + d * 0.5
    P(G.tube("hammer_handle", [tuple(g0), tuple(g1)], 0.035, wood, segs=8), "fore_f")
    side = d.cross(Vector((0, 1, 0))).normalized()
    rot = Matrix((side, Vector((0, 1, 0)), d)).transposed().to_4x4()
    head_m = Matrix.Translation(g1 + d * 0.05) @ rot
    P(G.box("hammer_head", (0.36, 0.17, 0.2), iron, m=head_m, bevel=0.03, segs=2), "fore_f")
    P(G.box("hammer_band", (0.07, 0.19, 0.22), gold, m=head_m, bevel=0.01), "fore_f")
    for o, b in parts:
        bind(arm, o, b)
    return arm


# --------------------------------------------------------------- enemies
def build_slime(seed):
    arm = make_armature("rig_slime", [("root", (0, 0, 0), (0, 0, 0.2), None), ("body", (0, 0, 0.0), (0, 0, 0.7), "root")])
    slime = _lib("slime", seed, rim="#E8FFD0")
    eye_w = M.toon_material("FB_eye_white", "#FFFFFF", "#B8C8C0", "#FFFFFF", rim_strength=0.0, spec_amt=0.0)
    eye = M.toon_material("FB_eye", "#1B1412", "#000000", "#FFFFFF", rim_strength=0.0, spec_amt=1.0, spec_size=0.2)
    parts = [
        G.lathe("blob", [(0, 0.0), (0.5, 0.0), (0.58, 0.12), (0.55, 0.3), (0.42, 0.5), (0.22, 0.63), (0, 0.66)], slime, segs=24),
        G.uvsphere("bubble1", 0.06, slime, m=T((0.1, -0.35, 0.18))),
        G.uvsphere("bubble2", 0.04, slime, m=T((-0.2, -0.38, 0.28))),
    ]
    for sy in (-1, 1):
        x = 0.4
        parts.append(G.uvsphere(f"eyew{sy}", 0.11, eye_w, m=T((x, 0.17 * sy, 0.36)), scale=(0.7, 1, 1.3)))
        parts.append(G.uvsphere(f"pupil{sy}", 0.06, eye, m=T((x + 0.07, 0.17 * sy, 0.34)), scale=(0.6, 1, 1.4)))
        parts.append(G.uvsphere(f"glint{sy}", 0.02, eye_w, m=T((x + 0.11, 0.15 * sy, 0.38))))
    parts.append(G.box("mouth", (0.06, 0.18, 0.03), eye, m=T((0.53, 0, 0.17)), bevel=0.012, smooth=True))
    for o in parts:
        bind(arm, o, "body")
    return arm


def build_skeleton(seed):
    arm = make_armature("rig_skeleton", HUMANOID)
    bone = _lib("bone", seed, rim="#C8F0FF")
    rust = _lib("rust_iron", seed, rim="#C8F0FF")
    eye = M.toon_material("FB_eye_ghost", "#7FE8FF", "#1E6A80", "#E8FFFF", emit=0.95, glow="#AEF4FF")
    leather = _lib("leather_wrap", seed)
    parts = []
    P = lambda o, b: parts.append((o, b))  # noqa: E731
    P(G.uvsphere("skull", 0.19, bone, m=T((0.06, 0, 1.38)), scale=(1.05, 0.95, 1.0)), "head")
    P(G.box("jaw", (0.2, 0.2, 0.08), bone, m=T((0.13, 0, 1.2)), bevel=0.03, smooth=True), "head")
    P(G.uvsphere("socket", 0.055, M.toon_material("FB_socket", "#1B1412", "#000000", "#333333", rim_strength=0.0),
                 m=T((0.2, -0.08, 1.4)), scale=(0.6, 1, 1)), "head")
    P(G.uvsphere("eye_glow", 0.03, eye, m=T((0.235, -0.08, 1.4))), "head")
    P(G.tube("spine_col", [(0, 0, 0.62), (0.02, 0, 0.9), (0.0, 0, 1.18)], 0.04, bone, segs=8), "spine")
    for i in range(4):
        P(G.torus(f"rib{i}", 0.2 - 0.02 * i, 0.025, bone, m=T((0.04, 0, 1.05 - 0.08 * i), (90, 0, 0)), segs=16), "spine")
    P(G.uvsphere("pelvis", 0.15, bone, m=T((0, 0, 0.62)), scale=(1, 1.5, 0.6)), "hips")
    P(G.box("loincloth", (0.04, 0.34, 0.26), leather, m=T((0.14, 0, 0.5)), bevel=0.015), "hips")
    for side, y in (("f", -1), ("b", 1)):
        sh = Vector((0.0, 0.26 * y, 1.08))
        el = Vector((0.06 if side == "f" else -0.04, 0.3 * y, 0.8))
        ha = Vector((0.2 if side == "f" else 0.04, 0.32 * y, 0.62 if side == "f" else 0.6))
        P(G.uvsphere(f"shoulder_{side}", 0.07, bone, m=T(tuple(sh))), f"arm_{side}")
        P(G.tube(f"upper_{side}", [tuple(sh), tuple(el)], 0.035, bone, segs=8), f"arm_{side}")
        P(G.tube(f"fore_{side}", [tuple(el), tuple(ha)], 0.03, bone, segs=8), f"fore_{side}")
        P(G.uvsphere(f"hand_{side}", 0.05, bone, m=T(tuple(ha))), f"fore_{side}")
        P(G.tube(f"thigh_{side}", [(0.0, 0.13 * y, 0.6), (0.02, 0.13 * y, 0.32)], 0.04, bone, segs=8), f"thigh_{side}")
        P(G.tube(f"shin_{side}", [(0.02, 0.13 * y, 0.32), (0.0, 0.13 * y, 0.06)], 0.035, bone, segs=8), f"shin_{side}")
        P(G.box(f"foot_{side}", (0.2, 0.09, 0.07), bone, m=T((0.06, 0.13 * y, 0.04)), bevel=0.025, smooth=True), f"shin_{side}")
    el, ha = Vector((0.06, -0.3, 0.8)), Vector((0.2, -0.32, 0.62))
    d = Vector((0.55, 0, 0.84)).normalized()
    P(G.tube("sword_grip", [tuple(ha - d * 0.08), tuple(ha + d * 0.06)], 0.03, leather, segs=6), "fore_f")
    P(G.box("sword_guard", (0.04, 0.06, 0.2), rust, m=Matrix.Translation(ha + d * 0.07) @ Matrix.Rotation(math.atan2(d.x, d.z), 4, "Y")), "fore_f")
    bl = Matrix.Translation(ha + d * 0.08) @ Matrix.Rotation(math.atan2(d.x, d.z), 4, "Y")
    P(G.blade("sword_blade", [(0, 0), (0, 0.4), (0, 0.6)], [0.05, 0.055, 0.035], [0.02, 0.02, 0.016], rust, tip=(0.02, 0.7), m=bl), "fore_f")
    for o, b in parts:
        bind(arm, o, b)
    return arm


def build_golem(seed):
    rng = random.Random(seed)
    bones = [
        ("root", (0, 0, 0), (0, 0, 0.2), None),
        ("hips", (0, 0, 0.5), (0, 0, 0.75), "root"),
        ("spine", (0, 0, 0.75), (0, 0, 1.2), "hips"),
        ("head", (0.15, 0, 1.3), (0.15, 0, 1.55), "spine"),
        ("arm_f", (0.05, -0.55, 1.25), (0.15, -0.6, 0.8), "spine"),
        ("fore_f", (0.15, -0.6, 0.8), (0.25, -0.62, 0.35), "arm_f"),
        ("arm_b", (0.05, 0.55, 1.25), (0.0, 0.6, 0.8), "spine"),
        ("fore_b", (0.0, 0.6, 0.8), (0.05, 0.62, 0.35), "arm_b"),
        ("thigh_f", (0, -0.22, 0.5), (0, -0.22, 0.25), "hips"),
        ("shin_f", (0, -0.22, 0.25), (0, -0.22, 0.02), "thigh_f"),
        ("thigh_b", (0, 0.22, 0.5), (0, 0.22, 0.25), "hips"),
        ("shin_b", (0, 0.22, 0.25), (0, 0.22, 0.02), "thigh_b"),
    ]
    arm = make_armature("rig_golem", bones)
    stone = _lib("stone", seed, rim="#FFC870")
    iron = _lib("dark_iron", seed, rim="#FFC870")
    rune = M.core_rune_material("fire", 0.95)
    moss = M.toon_material("FB_moss", "#6FA35A", "#2A4A22", "#B8E08A", paint=0.4)
    parts = []
    P = lambda o, b: parts.append((o, b))  # noqa: E731
    P(G.box("torso", (0.7, 0.85, 0.62), stone, m=T((0.05, 0, 1.05), (0, 6, 0)), bevel=0.09, segs=1), "spine")
    P(G.box("moss", (0.5, 0.7, 0.08), moss, m=T((0.0, 0, 1.38)), bevel=0.04, smooth=True), "spine")
    P(G.slab("chest_rune", G.star2d(4, 0.14, 0.05, math.pi / 4), 0.04, rune, m=T((0.41, 0, 1.08), (0, 0, 90))), "spine")
    P(G.box("pelvis", (0.5, 0.6, 0.3), stone, m=T((0, 0, 0.6)), bevel=0.06), "hips")
    P(G.box("plate", (0.08, 0.5, 0.36), iron, m=T((0.38, 0, 0.98)), bevel=0.02), "spine")
    P(G.box("head", (0.3, 0.3, 0.26), stone, m=T((0.2, 0, 1.42)), bevel=0.06), "head")
    P(G.box("visor", (0.06, 0.24, 0.05), rune, m=T((0.35, 0, 1.44)), bevel=0.015), "head")
    for side, y in (("f", -1), ("b", 1)):
        P(G.ico(f"shoulder_{side}", 0.24, stone, m=T((0.05, 0.55 * y, 1.25)), subdiv=1, jitter=0.12, rng=rng), f"arm_{side}")
        P(G.ico(f"upper_{side}", 0.17, stone, m=T((0.1 if side == "f" else 0.02, 0.6 * y, 0.95)), subdiv=1, jitter=0.12, rng=rng,
                scale=(1, 1, 1.3)), f"arm_{side}")
        P(G.ico(f"fist_{side}", 0.24, stone, m=T((0.25 if side == "f" else 0.05, 0.62 * y, 0.42)), subdiv=1, jitter=0.12, rng=rng,
                scale=(1.1, 1, 1.0)), f"fore_{side}")
        P(G.ico(f"fore_{side}", 0.15, stone, m=T((0.2 if side == "f" else 0.03, 0.61 * y, 0.65)), subdiv=1, jitter=0.1, rng=rng),
          f"fore_{side}")
        P(G.box(f"leg_{side}", (0.26, 0.24, 0.3), stone, m=T((0, 0.22 * y, 0.32)), bevel=0.05), f"thigh_{side}")
        P(G.box(f"foot_{side}", (0.36, 0.28, 0.18), stone, m=T((0.05, 0.22 * y, 0.09)), bevel=0.05), f"shin_{side}")
    for o, b in parts:
        bind(arm, o, b)
    return arm


def build_bat(seed):
    bones = [
        ("root", (0, 0, 0), (0, 0, 0.2), None),
        ("body", (0, 0, 0.95), (0, 0, 1.2), "root"),
        ("wing_f", (0, -0.12, 1.1), (0, -0.55, 1.15), "body"),
        ("wing_b", (0, 0.12, 1.1), (0, 0.55, 1.15), "body"),
    ]
    arm = make_armature("rig_bat", bones)
    skin = _lib("bat_skin", seed, rim="#FFB0E0")
    membrane = M.toon_material("FB_membrane", "#8A4E7A", "#3A1A36", "#C888B0", rim="#FFB0E0", rim_strength=0.9, paint=0.3)
    eye = M.toon_material("FB_eye_red", "#FF4D4D", "#8A1010", "#FFE0E0", emit=0.9, glow="#FF8080")
    fang = _lib("bone", seed)
    parts = []
    P = lambda o, b: parts.append((o, b))  # noqa: E731
    P(G.uvsphere("body", 0.2, skin, m=T((0, 0, 1.05)), scale=(1.0, 0.9, 1.15)), "body")
    P(G.uvsphere("head", 0.15, skin, m=T((0.12, 0, 1.27))), "body")
    for sy in (-1, 1):
        P(G.cone(f"ear{sy}", 0.07, 0.0, 0.2, skin, m=T((0.08, 0.08 * sy, 1.36), (12 * sy, -10, 0)), segs=6), "body")
        P(G.uvsphere(f"eye{sy}", 0.03, eye, m=T((0.25, 0.06 * sy, 1.3))), "body")
        P(G.cone(f"fang{sy}", 0.018, 0.0, 0.06, fang, m=T((0.25, 0.03 * sy, 1.2), (180, 0, 0)), segs=5), "body")
        side = "f" if sy < 0 else "b"
        # wing membrane in the XY plane spreading along +-Y, scalloped trailing edge
        w = [(0.0, 0.0), (0.12, 0.25), (0.08, 0.6), (-0.12, 0.72), (-0.15, 0.55), (-0.3, 0.5), (-0.25, 0.32), (-0.35, 0.22),
             (-0.2, 0.1)]
        pts = [(x, y * sy) for x, y in w]
        if sy > 0:
            pts = list(reversed(pts))
        P(G.slab(f"wing{sy}", pts, 0.03, membrane, m=T((0, 0.1 * sy, 1.12)), plane="XY"), f"wing_{side}")
        P(G.tube(f"wingbone{sy}", [(0, 0.1 * sy, 1.12), (0.12, 0.35 * sy, 1.12), (0.08, 0.7 * sy, 1.12)], 0.022, skin, segs=6),
          f"wing_{side}")
    for o, b in parts:
        bind(arm, o, b)
    return arm


BUILDERS = {"blacksmith": build_blacksmith, "slime": build_slime, "skeleton": build_skeleton, "golem": build_golem, "bat": build_bat}
YAW = {"blacksmith": -25.0, "slime": 225.0, "skeleton": 205.0, "golem": 210.0, "bat": 245.0}
SCALE = {"blacksmith": 1.1, "golem": 1.1, "bat": 1.35, "slime": 1.15, "skeleton": 1.1}
from .characters_meta import GROUNDED  # noqa: E402,F401


def build(asset, seed):
    if asset not in BUILDERS:
        from . import forgemasters
        return forgemasters.build(asset, seed)
    arm = BUILDERS[asset](seed)
    arm.rotation_euler = (0, 0, math.radians(YAW[asset]))
    s = SCALE.get(asset, 1.0)
    arm.scale = (s, s, s)
    bpy.context.view_layer.update()
    return arm


# --------------------------------------------------------------- actions
def _humanoid_idle(arm, n, heavy=1.0):
    for f in range(n):
        ph = 2 * math.pi * f / n
        b = math.sin(ph)
        _key(arm, f + 1, rots={
            "spine": [("Y", 2.0 * b * heavy)],
            "head": [("Y", -2.5 * math.sin(ph - 0.6) * heavy)],
            "arm_f": [("Y", -8 + 3 * math.sin(ph - 0.4))],
            "fore_f": [("Y", -10 + 2 * math.sin(ph - 0.8))],
            "arm_b": [("Y", 6 - 3 * math.sin(ph - 0.4))],
            "fore_b": [("Y", -12)],
            "thigh_f": [("Y", -4)], "shin_f": [("Y", 6)],
            "thigh_b": [("Y", 6)], "shin_b": [("Y", 4)],
        }, scales={"spine": (1.0 + 0.015 * b, 1.0 + 0.015 * b, 1.0 + 0.02 * b)},
            root_loc=(0, 0, 0))


def _blacksmith_attack(arm, n):
    # keyposes: ready, wind, wind, overhead, slam, impact, recover, recover
    # Angles from straight down, forward positive.  upper = upper-arm swing, the
    # forearm bend follows so the hammer (aligned with the forearm, whose rest
    # pose already sits ~38 deg forward) reaches `hammer`.  Wind-up folds the
    # hammer behind the back (not straight up) so the silhouette stays in frame.
    # (180 = straight up, 270 = horizontal behind, ~320 = hanging behind the back)
    upper = [10, 90, 150, 160, 100, 10, 8, 5]
    hammer = [55, 168, 290, 322, 150, 85, 72, 62]
    spine = [0, -8, -14, -16, 12, 20, 12, 4]
    head = [0, 4, 8, 8, -6, -10, -5, -1]
    arm_b = [8, 25, 35, 40, -20, -30, -10, 4]
    lean = [0.0, -0.02, -0.03, -0.03, 0.04, 0.06, 0.03, 0.01]
    poses = [dict(arm_f=-upper[i], fore_f=-(hammer[i] - 38 - upper[i]), spine=spine[i], head=head[i], arm_b=arm_b[i],
                  lean=lean[i]) for i in range(8)]
    for f, p in enumerate(poses[:n]):
        # arm_f: Y angle of the hammer arm; 0 = hanging, -90 = forward, -180 = up, -205 = up & back
        _key(arm, f + 1, rots={
            "spine": [("Y", p["spine"])],
            "head": [("Y", p["head"])],
            "arm_f": [("Y", p["arm_f"])],
            "fore_f": [("Y", p["fore_f"])],
            "arm_b": [("Y", p["arm_b"])],
            "fore_b": [("Y", -14)],
            "thigh_f": [("Y", -10 - 20 * max(0, p["lean"]) * 10)], "shin_f": [("Y", 10 + 15 * max(0, p["lean"]) * 10)],
            "thigh_b": [("Y", 10)], "shin_b": [("Y", 6)],
        }, root_loc=(p["lean"], 0, 0))


def _slime_idle(arm, n):
    for f in range(n):
        ph = 2 * math.pi * f / n
        b = math.sin(ph)
        # squash/stretch plus a phase-shifted sway so no two frames of the loop are identical
        _key(arm, f + 1, rots={"body": [("Y", 2.0 * math.cos(ph))]}, scales={"body": (1 + 0.07 * b, 1 + 0.07 * b, 1 - 0.09 * b)},
             root_loc=(0, 0, 0))


def _bat_idle(arm, n):
    for f in range(n):
        ph = 2 * math.pi * f / n
        flap = math.sin(ph)
        # root bone points +Z, so its local Y axis is world up
        _key(arm, f + 1, rots={"wing_f": [("X", -42 * flap)], "wing_b": [("X", 42 * flap)], "body": [("Y", 4 * math.cos(ph))]},
             root_loc=(0, -0.3 + 0.05 * math.cos(ph), 0))


def apply_action(arm, asset, clip):
    if asset not in BUILDERS:
        from . import forgemasters
        return forgemasters.apply_action(arm, asset, clip)
    n, fps, loop = spec.CLIPS[clip]
    new_action(arm, f"{asset}_{clip}")
    scene = bpy.context.scene
    scene.frame_start, scene.frame_end = 1, n
    if asset == "slime":
        _slime_idle(arm, n)
    elif asset == "bat":
        _bat_idle(arm, n)
    elif clip == "attack" and asset == "blacksmith":
        _blacksmith_attack(arm, n)
    else:
        # batch 2: the blacksmith idle breathes a little deeper (batch-1 review: motion too subtle)
        _humanoid_idle(arm, n, heavy={"golem": 1.6, "blacksmith": 1.5}.get(asset, 1.0))
    for fc in arm.animation_data.action.fcurves:
        for kp in fc.keyframe_points:
            kp.interpolation = "LINEAR"
