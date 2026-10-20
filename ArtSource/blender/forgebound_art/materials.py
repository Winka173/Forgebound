"""Controlled material library (Blender side).

All surface shading is a deterministic NPR "painted toon" shader evaluated in
Cycles as an Emission closure.  Lighting (key / fill / rim, see spec.LIGHT_RIG)
is computed in-shader from world-space normals, so renders are noise-free at
low sample counts (samples only anti-alias) and identical between runs.

Look recipe (ART_BIBLE "Shading"):
  * 2-band soft ramp (shadow -> base) driven by N.L(key), broken up by object-
    space painted noise so terminators look brushed rather than CG-smooth.
  * cool hue-shifted shadow colours + warm key, faint cool fill.
  * metals get a stylised "horizon" environment band and a hard specular blob.
  * rim light from behind-right, tinted by the weapon's Core colour.
  * an inverted-hull outline (see scene.add_outline) in OUTLINE_COLOR.
"""
from __future__ import annotations

import bpy

from . import palette, spec

GROUP_NAME = "FB_PaintedToon"


def _mix_rgb(nodes, links, fac, a, b, blend="MIX"):
    m = nodes.new("ShaderNodeMix")
    m.data_type = "RGBA"
    m.blend_type = blend
    m.clamp_result = True
    _connect_or_set(links, fac, m.inputs[0])
    _connect_or_set(links, a, m.inputs[6])
    _connect_or_set(links, b, m.inputs[7])
    return m.outputs[2]


def _connect_or_set(links, src, dst):
    if isinstance(src, bpy.types.NodeSocket):
        links.new(src, dst)
    else:
        dst.default_value = src


def _math(nodes, links, op, a, b=None, clamp=False):
    n = nodes.new("ShaderNodeMath")
    n.operation = op
    n.use_clamp = clamp
    _connect_or_set(links, a, n.inputs[0])
    if b is not None:
        _connect_or_set(links, b, n.inputs[1])
    return n.outputs[0]


def _dot(nodes, links, vec_socket, const_vec):
    n = nodes.new("ShaderNodeVectorMath")
    n.operation = "DOT_PRODUCT"
    links.new(vec_socket, n.inputs[0])
    n.inputs[1].default_value = const_vec
    return n.outputs["Value"]


def _maprange(nodes, links, v, fmin, fmax, tmin=0.0, tmax=1.0, interp="SMOOTHSTEP"):
    n = nodes.new("ShaderNodeMapRange")
    n.interpolation_type = interp
    n.clamp = True
    _connect_or_set(links, v, n.inputs[0])
    _connect_or_set(links, fmin, n.inputs[1])
    _connect_or_set(links, fmax, n.inputs[2])
    n.inputs[3].default_value = tmin
    n.inputs[4].default_value = tmax
    return n.outputs[0]


def build_group():
    if GROUP_NAME in bpy.data.node_groups:
        return bpy.data.node_groups[GROUP_NAME]
    g = bpy.data.node_groups.new(GROUP_NAME, "ShaderNodeTree")
    iface = g.interface
    def inp(name, kind, default):
        s = iface.new_socket(name, in_out="INPUT", socket_type=kind)
        s.default_value = default
        return s
    inp("Base", "NodeSocketColor", (0.5, 0.5, 0.5, 1))
    inp("Shadow", "NodeSocketColor", (0.1, 0.1, 0.15, 1))
    inp("Highlight", "NodeSocketColor", (1, 1, 1, 1))
    inp("Rim", "NodeSocketColor", (1, 0.8, 0.5, 1))
    inp("RimStrength", "NodeSocketFloat", 0.8)
    inp("Metal", "NodeSocketFloat", 0.0)
    inp("Spec", "NodeSocketFloat", 0.5)
    inp("SpecSize", "NodeSocketFloat", 0.05)
    inp("Paint", "NodeSocketFloat", 0.2)
    inp("Emit", "NodeSocketFloat", 0.0)
    inp("Glow", "NodeSocketColor", (1, 1, 1, 1))
    inp("Seed", "NodeSocketFloat", 0.0)
    iface.new_socket("Shader", in_out="OUTPUT", socket_type="NodeSocketShader")

    nodes, links = g.nodes, g.links
    gi = nodes.new("NodeGroupInput")
    go = nodes.new("NodeGroupOutput")
    geo = nodes.new("ShaderNodeNewGeometry")
    tc = nodes.new("ShaderNodeTexCoord")
    N = geo.outputs["Normal"]

    # painted noise (object space -> stable under animation / per-asset scale)
    noise = nodes.new("ShaderNodeTexNoise")
    noise.noise_dimensions = "4D"
    noise.inputs["Scale"].default_value = 3.5
    noise.inputs["Detail"].default_value = 2.0
    noise.inputs["Roughness"].default_value = 0.55
    links.new(tc.outputs["Object"], noise.inputs["Vector"])
    links.new(gi.outputs["Seed"], noise.inputs["W"])
    paint = _math(nodes, links, "MULTIPLY", _math(nodes, links, "SUBTRACT", noise.outputs["Fac"], 0.5), gi.outputs["Paint"])

    key = spec.LIGHT_RIG["key"]
    ndl = _math(nodes, links, "ADD", _dot(nodes, links, N, key["dir"]), paint)
    lit = _maprange(nodes, links, ndl, -0.02, 0.16)          # shadow terminator
    hi = _maprange(nodes, links, ndl, 0.62, 0.70)            # top light band
    col = _mix_rgb(nodes, links, lit, gi.outputs["Shadow"], gi.outputs["Base"])
    hi_col = _mix_rgb(nodes, links, 0.45, gi.outputs["Base"], gi.outputs["Highlight"])
    col = _mix_rgb(nodes, links, hi, col, hi_col)

    # cool fill into the shadow side
    fill = spec.LIGHT_RIG["fill"]
    ndf = _maprange(nodes, links, _dot(nodes, links, N, fill["dir"]), 0.1, 0.6)
    fill_amt = _math(nodes, links, "MULTIPLY", ndf, fill["intensity"] * 0.5)
    fill_col = palette.hex_to_linear(fill["color"])
    col = _mix_rgb(nodes, links, fill_amt, col, fill_col, blend="SCREEN")

    # stylised metal horizon band (fake environment): sky above, ground below
    sep = nodes.new("ShaderNodeSeparateXYZ")
    links.new(N, sep.inputs[0])
    nz = _math(nodes, links, "ADD", sep.outputs["Z"], _math(nodes, links, "MULTIPLY", paint, 0.6))
    sky = _maprange(nodes, links, nz, -0.08, 0.02)
    env_col = _mix_rgb(nodes, links, sky, gi.outputs["Shadow"], hi_col)
    env_amt = _math(nodes, links, "MULTIPLY", gi.outputs["Metal"], 0.6)
    col = _mix_rgb(nodes, links, env_amt, col, env_col)

    # hard specular blob (Blinn half vector, fixed because camera is ortho+fixed)
    ndh = _math(nodes, links, "ADD", _dot(nodes, links, N, key["half"]), _math(nodes, links, "MULTIPLY", paint, 0.15))
    thr = _math(nodes, links, "SUBTRACT", 1.0, gi.outputs["SpecSize"])
    thr2 = _math(nodes, links, "ADD", thr, 0.012)
    specm = _math(nodes, links, "MULTIPLY", _maprange(nodes, links, ndh, thr, thr2), gi.outputs["Spec"])
    col = _mix_rgb(nodes, links, specm, col, gi.outputs["Highlight"])

    # rim light (fresnel x rim direction), tinted by Core colour
    rim = spec.LIGHT_RIG["rim"]
    ndv = _dot(nodes, links, N, spec.VIEW_DIR)
    fres = _maprange(nodes, links, _math(nodes, links, "SUBTRACT", 1.0, ndv), 0.55, 0.72)
    ndr = _maprange(nodes, links, _dot(nodes, links, N, rim["dir"]), 0.15, 0.35)
    rimm = _math(nodes, links, "MULTIPLY", _math(nodes, links, "MULTIPLY", fres, ndr), gi.outputs["RimStrength"])
    col = _mix_rgb(nodes, links, rimm, col, gi.outputs["Rim"])

    # emissive blend (runes / glow materials are mostly unlit)
    glow = _mix_rgb(nodes, links, _math(nodes, links, "MULTIPLY", noise.outputs["Fac"], 0.6), gi.outputs["Glow"], gi.outputs["Highlight"])
    col = _mix_rgb(nodes, links, gi.outputs["Emit"], col, glow)

    em = nodes.new("ShaderNodeEmission")
    em.inputs["Strength"].default_value = 1.0
    links.new(col, em.inputs["Color"])
    links.new(em.outputs[0], go.inputs["Shader"])
    return g


def toon_material(name, base, shadow, highlight, rim="#FFE8C8", rim_strength=0.8, metal=0.0,
                  spec_amt=0.5, spec_size=0.05, paint=0.2, emit=0.0, glow="#FFFFFF", seed=0.0):
    if name in bpy.data.materials:
        return bpy.data.materials[name]
    grp = build_group()
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    gn = nt.nodes.new("ShaderNodeGroup")
    gn.node_tree = grp
    vals = {
        "Base": palette.hex_to_linear(base), "Shadow": palette.hex_to_linear(shadow),
        "Highlight": palette.hex_to_linear(highlight), "Rim": palette.hex_to_linear(rim),
        "RimStrength": rim_strength, "Metal": metal, "Spec": spec_amt, "SpecSize": spec_size,
        "Paint": paint, "Emit": emit, "Glow": palette.hex_to_linear(glow), "Seed": float(seed % 1000) / 10.0,
    }
    for k, v in vals.items():
        gn.inputs[k].default_value = v
    nt.links.new(gn.outputs[0], out.inputs["Surface"])
    return m


def library_material(key: str, rim="#FFE8C8", rim_strength=0.7, seed=0.0, suffix=""):
    """Materials from the controlled library (palette.MATERIALS)."""
    p = palette.MATERIALS[key]
    return toon_material(f"FB_{key}{suffix}", p["base"], p["shadow"], p["highlight"], rim=rim,
                         rim_strength=rim_strength, metal=p["metal"], spec_amt=p["spec"],
                         spec_size=p["spec_size"], paint=p["paint"], seed=seed)


def core_gem_material(core: str, rim=None):
    c = palette.CORE_PALETTES[core]
    return toon_material(f"FB_gem_{core}", c["primary"], c["dark"], c["glow"], rim=rim or c["glow"],
                         rim_strength=1.0, metal=0.3, spec_amt=1.0, spec_size=0.10, paint=0.12,
                         emit=0.18, glow=c["glow"])


def core_rune_material(core: str, strength=0.85):
    """Emissive rune glow (inlays, legendary crowns)."""
    c = palette.CORE_PALETTES[core]
    return toon_material(f"FB_rune_{core}_{int(strength*100)}", c["primary"], c["dark"], c["glow"], rim=c["glow"],
                         rim_strength=0.5, metal=0.0, spec_amt=0.0, paint=0.25, emit=strength, glow=c["glow"])


def core_accent_material(core: str):
    """Solid (non-emissive) material in the core's secondary colour."""
    c = palette.CORE_PALETTES[core]
    return toon_material(f"FB_accent_{core}", c["secondary"], palette.mix_hex(c["secondary"], c["dark"], 0.7),
                         palette.mix_hex(c["secondary"], "#FFFFFF", 0.5), rim=c["glow"], rim_strength=0.8,
                         metal=0.2, spec_amt=0.6, spec_size=0.06, paint=0.2)


def outline_material():
    name = "FB_outline"
    if name in bpy.data.materials:
        return bpy.data.materials[name]
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    geo = nt.nodes.new("ShaderNodeNewGeometry")
    em = nt.nodes.new("ShaderNodeEmission")
    em.inputs["Color"].default_value = palette.hex_to_linear(spec.OUTLINE_COLOR)
    tr = nt.nodes.new("ShaderNodeBsdfTransparent")
    mix = nt.nodes.new("ShaderNodeMixShader")
    nt.links.new(geo.outputs["Backfacing"], mix.inputs[0])
    nt.links.new(em.outputs[0], mix.inputs[1])
    nt.links.new(tr.outputs[0], mix.inputs[2])
    nt.links.new(mix.outputs[0], out.inputs["Surface"])
    return m
