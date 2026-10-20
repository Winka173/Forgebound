"""Blender-side entry point.

    blender -b --factory-startup -P worker.py -- --jobs jobs.json --out results.json

Each job renders one or more raw (supersampled, 16-bit, straight-alpha) PNGs
into ArtSource/renders_cache/ and reports per-image metadata (pivot etc.).
Post-processing (downsample, colour bleed, naming, manifest) happens outside
Blender in postprocess.py.
"""
from __future__ import annotations

import json
import math
import os
import sys
import time
import traceback

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))

import bpy  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

from forgebound_art import scene as S  # noqa: E402
from forgebound_art import spec, weapons  # noqa: E402


def _root(name="FB_Root"):
    root = bpy.data.objects.new(name, None)
    bpy.context.scene.collection.objects.link(root)
    return root


def _normalise(scene, cam, root, R, fit_px_frac, preset, extra_ok=False):
    """Uniformly scale + centre `root` so its *base* geometry (not tier/core
    extras) fits fit_px_frac of the frame.  Deterministic per Frame."""
    root.matrix_world = R
    bpy.context.view_layer.update()
    base = [o for o in S.mesh_objects(root) if extra_ok or not o.get("fb_extra")]
    x0, x1, y0, y1 = S.camera_plane_bbox(base, cam)
    p = spec.CAMERAS[preset]
    s = fit_px_frac * p["ortho_scale"] / max(x1 - x0, y1 - y0)
    cx, cy = (x0 + x1) / 2 * s, (y0 + y1) / 2 * s
    cam_rot = cam.matrix_world.to_3x3()
    off = cam_rot @ Vector((cx, cy, 0))
    root.matrix_world = Matrix.Translation(-off) @ R @ Matrix.Diagonal((s, s, s, 1))
    bpy.context.view_layer.update()
    return s


def _rot(icon_rot=0.0, spin=0.0, tilt=0.0):
    return (Matrix.Rotation(math.radians(icon_rot), 4, "Y") @ Matrix.Rotation(math.radians(tilt), 4, "X")
            @ Matrix.Rotation(math.radians(spin), 4, "Z"))


def _glow_pass(scene, path):
    for o in scene.objects:
        if o.type == "MESH":
            parent_glow = o.parent.get("fb_glow") if (o.name.endswith("_hull") and o.parent) else False
            o.is_holdout = not (o.get("fb_glow") or False) or parent_glow
    S.render_to(scene, path)


def job_weapon(job, cache):
    scene = S.reset_scene()
    preset = job["preset"]
    cam = S.setup_camera(scene, preset)
    root = _root()
    sockets = weapons.build_weapon(root, job["frame"], job.get("core"), job["tier"], job.get("legendary", False), job["seed"])
    if preset == "weapon_icon":
        R = _rot(sockets["icon_rot"], sockets["spin"], sockets.get("tilt", 0))
        frac = spec.ICON_FIT
    else:
        R = _rot(sockets.get("ingame_rot", 90), sockets["spin"], 0)
        frac = spec.INGAME_LONG_AXIS_PX / spec.CAMERAS[preset]["final_px"][0]
    s = _normalise(scene, cam, root, R, frac, preset)
    S.add_outlines(S.mesh_objects(), preset)
    out = []
    path = os.path.join(cache, job["name"] + ".png")
    S.render_to(scene, path)
    w, h = spec.CAMERAS[preset]["final_px"]
    if preset == "weapon_ingame":
        gx, gy = S.world_to_pixel(scene, cam, root.matrix_world @ Vector(sockets["grip"]))
        pivot = (round(gx / w, 4), round(1 - gy / h, 4))
    else:
        pivot = (0.5, 0.5)
    out.append({"raw": path, "state": job["state"], "frame": 0, "pivot": pivot, "scale": s})
    if job.get("glow"):
        gpath = os.path.join(cache, job["name"] + "_glow.png")
        _glow_pass(scene, gpath)
        out.append({"raw": gpath, "state": "glow", "frame": 0, "pivot": pivot, "scale": s, "post": "glow"})
    return out


def job_board(job, cache):
    from forgebound_art import board
    scene = S.reset_scene()
    cam = S.setup_camera(scene, "board_icon")
    root = _root()
    board.build_piece(root, job["piece"], job["seed"])
    s = _normalise(scene, cam, root, Matrix.Rotation(math.radians(job.get("yaw", 0)), 4, "Z"), 0.78, "board_icon",
                   extra_ok=True)
    S.add_outlines(S.mesh_objects(), "board_icon")
    path = os.path.join(cache, job["name"] + ".png")
    S.render_to(scene, path)
    return [{"raw": path, "state": "icon", "frame": 0, "pivot": (0.5, 0.5), "scale": s}]


def job_character(job, cache):
    from forgebound_art import characters
    scene = S.reset_scene()
    cam = S.setup_camera(scene, "character")
    rig = characters.build(job["asset"], job["seed"])
    S.add_outlines(S.mesh_objects(), "character")
    clip = job["clip"]
    n, fps, loop = spec.CLIPS[clip]
    characters.apply_action(rig, job["asset"], clip)
    scene.render.fps = fps
    out = []
    w, h = spec.CAMERAS["character"]["final_px"]
    gx, gy = S.world_to_pixel(scene, cam, (0, 0, 0))
    pivot = (round(gx / w, 4), round(1 - gy / h, 4))
    for f in range(n):
        scene.frame_set(f + 1)
        path = os.path.join(cache, f"{job['name']}_{f:02d}.png")
        S.render_to(scene, path)
        out.append({"raw": path, "state": clip, "frame": f, "pivot": pivot, "scale": 1.0})
    return out


HANDLERS = {"weapon": job_weapon, "board": job_board, "char": job_character, "enemy": job_character}


def main():
    argv = sys.argv[sys.argv.index("--") + 1:]
    jobs_path = argv[argv.index("--jobs") + 1]
    out_path = argv[argv.index("--out") + 1]
    cache = argv[argv.index("--cache") + 1]
    os.makedirs(cache, exist_ok=True)
    if "--threads" in argv:
        S.THREADS = int(argv[argv.index("--threads") + 1])
    jobs = json.load(open(jobs_path))
    results = []
    for job in jobs:
        t0 = time.time()
        try:
            imgs = HANDLERS[job["category"]](job, cache)
            results.append({"job": job, "images": imgs, "seconds": round(time.time() - t0, 3), "ok": True})
        except Exception:
            traceback.print_exc()
            results.append({"job": job, "images": [], "seconds": round(time.time() - t0, 3), "ok": False,
                            "error": traceback.format_exc()})
        print(f"[fb-worker] {job['name']} {results[-1]['ok']} {results[-1]['seconds']}s", flush=True)
    with open(out_path, "w") as fh:
        json.dump(results, fh, indent=1)


if __name__ == "__main__":
    main()
