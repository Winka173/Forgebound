"""Procedural mesh helpers (bmesh).  Every builder bakes its transform into
vertex positions and returns an object with identity scale so outline hulls can
be computed in world units.  Convention: weapons are modelled along +Z (grip
near origin, business end up), width on X, thickness on Y (camera axis).
"""
from __future__ import annotations

import math

import bmesh
import bpy
from mathutils import Matrix, Vector

_COLLECTION = None
TAGS = {"extra": False, "glow": False}


class tagged:
    """Context manager: objects created inside get the given tags
    (extra=True -> not used for scale normalisation; glow=True -> emissive,
    kept visible in the legendary glow pass)."""

    def __init__(self, **kw):
        self.kw = kw
        self.prev = {}

    def __enter__(self):
        self.prev = dict(TAGS)
        TAGS.update(self.kw)

    def __exit__(self, *a):
        TAGS.clear()
        TAGS.update(self.prev)


def set_collection(col):
    global _COLLECTION
    _COLLECTION = col


def _finish(bm, name, mat, smooth, parent=None, outline=True):
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for p in me.polygons:
        p.use_smooth = smooth
    if mat is not None:
        me.materials.append(mat)
    ob = bpy.data.objects.new(name, me)
    (_COLLECTION or bpy.context.scene.collection).objects.link(ob)
    if parent is not None:
        ob.parent = parent
    ob["fb_outline"] = bool(outline)
    ob["fb_extra"] = bool(TAGS["extra"])
    ob["fb_glow"] = bool(TAGS["glow"])
    return ob


def _xf(bm, m):
    bmesh.ops.transform(bm, matrix=m, verts=bm.verts)


def trs(loc=(0, 0, 0), rot=(0, 0, 0), scale=(1, 1, 1)):
    """Location / XYZ-euler(degrees) / scale -> Matrix."""
    from mathutils import Euler
    r = Euler([math.radians(a) for a in rot], "XYZ").to_matrix().to_4x4()
    s = Matrix.Diagonal((*scale, 1.0))
    return Matrix.Translation(loc) @ r @ s


def box(name, size, mat, m=None, bevel=0.0, segs=1, smooth=False, **kw):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    _xf(bm, Matrix.Diagonal((*size, 1.0)))
    if bevel > 0:
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bevel, segments=segs, profile=0.5, affect="EDGES", clamp_overlap=True)
    if m is not None:
        _xf(bm, m)
    return _finish(bm, name, mat, smooth, **kw)


def ico(name, radius, mat, m=None, subdiv=1, scale=(1, 1, 1), smooth=False, jitter=0.0, rng=None, **kw):
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=radius)
    if jitter and rng is not None:
        for v in bm.verts:
            v.co *= 1.0 + rng.uniform(-jitter, jitter)
    _xf(bm, Matrix.Diagonal((*scale, 1.0)))
    if m is not None:
        _xf(bm, m)
    return _finish(bm, name, mat, smooth, **kw)


def uvsphere(name, radius, mat, m=None, segs=16, rings=10, scale=(1, 1, 1), smooth=True, **kw):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=segs, v_segments=rings, radius=radius)
    _xf(bm, Matrix.Diagonal((*scale, 1.0)))
    if m is not None:
        _xf(bm, m)
    return _finish(bm, name, mat, smooth, **kw)


def lathe(name, profile, mat, m=None, segs=16, smooth=True, scale_xy=(1.0, 1.0), **kw):
    """Revolve [(r, z), ...] around Z.  r==0 endpoints are collapsed to poles."""
    bm = bmesh.new()
    rings = []
    for r, z in profile:
        ring = []
        if r <= 1e-6:
            ring = [bm.verts.new((0, 0, z))]
        else:
            for i in range(segs):
                a = 2 * math.pi * i / segs
                ring.append(bm.verts.new((math.cos(a) * r * scale_xy[0], math.sin(a) * r * scale_xy[1], z)))
        rings.append(ring)
    for ra, rb in zip(rings, rings[1:]):
        if len(ra) == 1 and len(rb) == 1:
            continue
        if len(ra) == 1:
            for i in range(segs):
                bm.faces.new((ra[0], rb[i], rb[(i + 1) % segs]))
        elif len(rb) == 1:
            for i in range(segs):
                bm.faces.new((ra[i], rb[0], ra[(i + 1) % segs]))
        else:
            for i in range(segs):
                bm.faces.new((ra[i], rb[i], rb[(i + 1) % segs], ra[(i + 1) % segs]))
    if len(rings[0]) > 1:
        bm.faces.new(list(reversed(rings[0])))
    if len(rings[-1]) > 1:
        bm.faces.new(rings[-1])
    if m is not None:
        _xf(bm, m)
    return _finish(bm, name, mat, smooth, **kw)


def cylinder(name, r, z0, z1, mat, m=None, segs=12, **kw):
    return lathe(name, [(0, z0), (r, z0), (r, z1), (0, z1)], mat, m=m, segs=segs, **kw)


def _frames_along(points):
    """Parallel-transport frames for a polyline."""
    pts = [Vector(p) for p in points]
    tans = []
    for i in range(len(pts)):
        a = pts[max(0, i - 1)]
        b = pts[min(len(pts) - 1, i + 1)]
        tans.append((b - a).normalized())
    ref = Vector((0, 1, 0)) if abs(tans[0].dot(Vector((0, 1, 0)))) < 0.9 else Vector((1, 0, 0))
    n = tans[0].cross(ref).normalized()
    frames = []
    for i, t in enumerate(tans):
        if i > 0:
            n = (n - t * n.dot(t)).normalized()
        b = t.cross(n).normalized()
        frames.append((pts[i], t, n, b))
    return frames


def tube(name, points, radii, mat, segs=10, smooth=True, cap=True, m=None, flat_y=1.0, **kw):
    """Sweep a circle along a polyline; radii per point (or float)."""
    if not isinstance(radii, (list, tuple)):
        radii = [radii] * len(points)
    bm = bmesh.new()
    rings = []
    for (p, t, n, b), r in zip(_frames_along(points), radii):
        ring = []
        for i in range(segs):
            a = 2 * math.pi * i / segs
            off = n * math.cos(a) * r + b * math.sin(a) * r
            off.y *= flat_y
            ring.append(bm.verts.new(p + off))
        rings.append(ring)
    for ra, rb in zip(rings, rings[1:]):
        for i in range(segs):
            bm.faces.new((ra[i], ra[(i + 1) % segs], rb[(i + 1) % segs], rb[i]))
    if cap:
        bm.faces.new(list(reversed(rings[0])))
        bm.faces.new(rings[-1])
    if m is not None:
        _xf(bm, m)
    return _finish(bm, name, mat, smooth, **kw)


def blade(name, center, half_w, half_t, mat, section="diamond", tip=None, m=None, smooth=False, **kw):
    """Blade swept along a 2D centre-line in the XZ plane.

    center: [(x, z)] ; half_w / half_t: per-station lists.  Cross-section
    is either 'diamond' (double edged, central ridge) or 'single' (edge on the
    -normal side, flat spine on the +normal side).  tip: optional (x, z) apex.
    """
    n_st = len(center)
    pts = [Vector((x, 0, z)) for x, z in center]
    bm = bmesh.new()
    rings = []
    for i in range(n_st):
        a = pts[max(0, i - 1)]
        b = pts[min(n_st - 1, i + 1)]
        t = (b - a).normalized()
        nrm = Vector((t.z, 0, -t.x))  # perpendicular in XZ
        w, th = half_w[i], half_t[i]
        if section == "diamond":
            prof = [(-1, 0.0), (-0.15, 1.0), (0.15, 1.0), (1, 0.0), (0.15, -1.0), (-0.15, -1.0)]
        else:  # single edged: sharp at u=-1, spine at u=+1
            prof = [(-1, 0.0), (0.1, 1.0), (1, 0.75), (1, -0.75), (0.1, -1.0)]
        ring = [bm.verts.new(pts[i] + nrm * (u * w) + Vector((0, y * th, 0))) for u, y in prof]
        rings.append(ring)
    k = len(rings[0])
    for ra, rb in zip(rings, rings[1:]):
        for j in range(k):
            bm.faces.new((ra[j], ra[(j + 1) % k], rb[(j + 1) % k], rb[j]))
    bm.faces.new(list(reversed(rings[0])))
    if tip is not None:
        apex = bm.verts.new((tip[0], 0, tip[1]))
        last = rings[-1]
        for j in range(k):
            bm.faces.new((last[j], last[(j + 1) % k], apex))
    else:
        bm.faces.new(rings[-1])
    if m is not None:
        _xf(bm, m)
    return _finish(bm, name, mat, smooth, **kw)


def slab(name, outline2d, thickness, mat, m=None, bevel=0.0, smooth=False, plane="XZ", **kw):
    """Extrude a 2D polygon (in XZ by default) symmetrically along Y."""
    bm = bmesh.new()
    vs = []
    for x, z in outline2d:
        vs.append(bm.verts.new((x, -thickness / 2, z)))
    f = bm.faces.new(vs)
    ext = bmesh.ops.extrude_face_region(bm, geom=[f])
    nv = [e for e in ext["geom"] if isinstance(e, bmesh.types.BMVert)]
    bmesh.ops.translate(bm, verts=nv, vec=(0, thickness, 0))
    if bevel > 0:
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bevel, segments=1, profile=0.5, affect="EDGES", clamp_overlap=True)
    if plane == "XY":
        _xf(bm, Matrix.Rotation(math.radians(-90), 4, "X"))
    if m is not None:
        _xf(bm, m)
    return _finish(bm, name, mat, smooth, **kw)


def torus(name, R, r, mat, m=None, segs=24, rsegs=8, arc=1.0, smooth=True, **kw):
    pts = []
    n = segs if arc >= 1.0 else segs + 1
    for i in range(n):
        a = 2 * math.pi * arc * i / segs
        pts.append((math.cos(a) * R, 0.0, math.sin(a) * R))
    if arc >= 1.0:
        # closed: build manually
        bm = bmesh.new()
        rings = []
        for i in range(segs):
            a = 2 * math.pi * i / segs
            c = Vector((math.cos(a) * R, 0, math.sin(a) * R))
            radial = Vector((math.cos(a), 0, math.sin(a)))
            ring = []
            for j in range(rsegs):
                b = 2 * math.pi * j / rsegs
                ring.append(bm.verts.new(c + radial * math.cos(b) * r + Vector((0, 1, 0)) * math.sin(b) * r))
            rings.append(ring)
        for i in range(segs):
            ra, rb = rings[i], rings[(i + 1) % segs]
            for j in range(rsegs):
                bm.faces.new((ra[j], rb[j], rb[(j + 1) % rsegs], ra[(j + 1) % rsegs]))
        if m is not None:
            _xf(bm, m)
        return _finish(bm, name, mat, smooth, **kw)
    return tube(name, pts, r, mat, segs=rsegs, m=m, smooth=smooth, **kw)


def gear(name, r_out, r_in, teeth, thickness, mat, m=None, hole=0.0, **kw):
    pts = []
    for i in range(teeth * 4):
        a = 2 * math.pi * i / (teeth * 4)
        r = r_out if (i % 4) in (1, 2) else r_in
        pts.append((math.cos(a) * r, math.sin(a) * r))
    ob = slab(name, pts, thickness, mat, m=m, bevel=thickness * 0.15, **kw)
    return ob


def star2d(points, r_out, r_in, rot=0.0):
    res = []
    for i in range(points * 2):
        a = rot + math.pi * i / points
        r = r_out if i % 2 == 0 else r_in
        res.append((math.cos(a) * r, math.sin(a) * r))
    return res


def cone(name, r0, r1, h, mat, m=None, segs=10, smooth=False, **kw):
    return lathe(name, [(0, 0), (r0, 0), (r1, h)] + ([(0, h)] if r1 > 0 else []), mat, m=m, segs=segs, smooth=smooth, **kw)


def spike(name, base, tip, r, mat, segs=6, **kw):
    """Cone from base point to tip point."""
    b, t = Vector(base), Vector(tip)
    d = t - b
    h = d.length
    rot = d.to_track_quat("Z", "Y").to_matrix().to_4x4()
    return cone(name, r, 0.0, h, mat, m=Matrix.Translation(b) @ rot, segs=segs, **kw)
