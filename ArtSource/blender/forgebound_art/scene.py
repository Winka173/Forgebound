"""Scene, camera, colour management, outline hulls and render helpers."""
from __future__ import annotations

import math

import bmesh
import bpy
from mathutils import Matrix, Vector

from . import geometry, materials, spec


THREADS = 0


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    col = bpy.data.collections.new("FB_Asset")
    scene.collection.children.link(col)
    geometry.set_collection(col)
    configure_render(scene, THREADS)
    return scene


def configure_render(scene, threads=0):
    scene.render.engine = "CYCLES"
    if threads:
        scene.render.threads_mode = "FIXED"
        scene.render.threads = threads
    c = scene.cycles
    c.device = "CPU"
    c.samples = spec.CYCLES_SAMPLES
    c.use_adaptive_sampling = False
    c.use_denoising = False  # this Blender build has no OIDN; shading is noise-free anyway
    c.seed = spec.CYCLES_SEED
    c.use_animated_seed = False
    c.max_bounces = 0
    c.diffuse_bounces = 0
    c.glossy_bounces = 0
    c.transmission_bounces = 0
    c.transparent_max_bounces = 16
    c.filter_width = 1.2
    c.pixel_filter_type = "BLACKMAN_HARRIS"
    scene.render.film_transparent = True
    scene.render.use_compositing = False
    scene.render.use_sequencer = False
    scene.render.dither_intensity = 0.0  # dithering would break determinism of PNG bytes
    # Locked colour management: Standard view (no Filmic/AgX tonemapping), sRGB display.
    scene.display_settings.display_device = "sRGB"
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.look = "None"
    scene.view_settings.exposure = 0.0
    scene.view_settings.gamma = 1.0
    scene.sequencer_colorspace_settings.name = "sRGB"
    img = scene.render.image_settings
    img.file_format = "PNG"
    img.color_mode = "RGBA"
    img.color_depth = "8"    # straight (un-premultiplied) alpha; post step works in float premultiplied space
    img.compression = 15
    scene.render.use_file_extension = True
    r = scene.render
    for attr in ("use_stamp_date", "use_stamp_time", "use_stamp_render_time", "use_stamp_frame", "use_stamp_camera",
                 "use_stamp_scene", "use_stamp_filename", "use_stamp_memory", "use_stamp_lens", "use_stamp_marker"):
        if hasattr(r, attr):
            setattr(r, attr, False)
    world = bpy.data.worlds.new("FB_World")
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    bg.inputs[0].default_value = (0, 0, 0, 1)
    bg.inputs[1].default_value = 0.0
    scene.world = world


def setup_camera(scene, preset: str):
    p = spec.CAMERAS[preset]
    cam_data = bpy.data.cameras.new("FB_Cam")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = p["ortho_scale"]
    cam_data.clip_start = 0.1
    cam_data.clip_end = 100
    cam = bpy.data.objects.new("FB_Cam", cam_data)
    scene.collection.objects.link(cam)
    pitch = math.radians(p["pitch_deg"])
    cx, cz = p["center"]
    dist = 20.0
    # camera looks along +Y, tilted down by pitch around X
    direction = Vector((0, math.cos(pitch), -math.sin(pitch)))
    target = Vector((cx, 0, cz))
    cam.location = target - direction * dist
    cam.rotation_euler = (math.radians(90) - pitch, 0, 0)
    scene.camera = cam
    w, h = p["final_px"]
    scene.render.resolution_x = w * spec.SUPERSAMPLE
    scene.render.resolution_y = h * spec.SUPERSAMPLE
    scene.render.resolution_percentage = 100
    scene.render.pixel_aspect_x = scene.render.pixel_aspect_y = 1.0
    return cam


def world_to_pixel(scene, cam, co):
    """World coordinate -> final-image pixel (x right, y down)."""
    from bpy_extras.object_utils import world_to_camera_view
    v = world_to_camera_view(scene, cam, Vector(co))
    w = scene.render.resolution_x / spec.SUPERSAMPLE
    h = scene.render.resolution_y / spec.SUPERSAMPLE
    return (v.x * w, (1.0 - v.y) * h)


def mesh_objects(root=None):
    obs = [o for o in bpy.context.scene.objects if o.type == "MESH" and not o.name.endswith("_hull")]
    if root is not None:
        obs = [o for o in obs if _is_child_of(o, root)]
    return obs


def _is_child_of(o, root):
    p = o
    while p is not None:
        if p == root:
            return True
        p = p.parent
    return False


def camera_plane_bbox(objs, cam):
    """Bounding box of objects projected on the camera plane (camera local x/y)."""
    inv = cam.matrix_world.inverted()
    xs, ys = [], []
    dg = bpy.context.evaluated_depsgraph_get()
    for o in objs:
        oe = o.evaluated_get(dg)
        me = oe.to_mesh()
        mw = o.matrix_world
        for v in me.vertices:
            c = inv @ (mw @ v.co)
            xs.append(c.x)
            ys.append(c.y)
        oe.to_mesh_clear()
    return min(xs), max(xs), min(ys), max(ys)


def add_outline(obj, world_thickness):
    """Inverted-hull outline: duplicate mesh pushed along normals, flipped,
    rendered only where its back faces are seen (materials.outline_material)."""
    if not obj.get("fb_outline", True):
        return None
    s = obj.matrix_world.to_scale()
    sc = (s.x + s.y + s.z) / 3.0
    t = world_thickness / max(sc, 1e-6)
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.normal_update()
    for v in bm.verts:
        v.co += v.normal * t
    bmesh.ops.reverse_faces(bm, faces=bm.faces)
    me = bpy.data.meshes.new(obj.name + "_hull")
    bm.to_mesh(me)
    bm.free()
    me.materials.clear()
    me.materials.append(materials.outline_material())
    for p in me.polygons:
        p.use_smooth = True
    hull = bpy.data.objects.new(obj.name + "_hull", me)
    obj.users_collection[0].objects.link(hull)
    hull.parent = obj
    hull.matrix_parent_inverse = Matrix.Identity(4)
    hull.visible_shadow = False
    return hull


def add_outlines(objs, preset):
    p = spec.CAMERAS[preset]
    world_per_px = p["ortho_scale"] / p["final_px"][0]
    t = spec.OUTLINE_PX[preset] * world_per_px
    for o in objs:
        add_outline(o, t)


def render_to(scene, path):
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
