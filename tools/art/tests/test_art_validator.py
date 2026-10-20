"""Synthetic good/bad sprites proving each validator check catches its defect.

Run: python3 -m pytest tools/art/tests -q
"""
from __future__ import annotations

import hashlib
import json
import os
import sys

import numpy as np
import pytest
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))

import art_validator as V  # noqa: E402
import atlas_packer as AP  # noqa: E402

CFG = json.load(open(os.path.join(os.path.dirname(HERE), "validator_config.json")))
LICENSE = CFG["license"]


# ------------------------------------------------------------------ helpers
def disc_sprite(size=256, radius=80, center=None, outline=3.0, hue=(0.85, 0.45, 0.25), seed=0, aa=True):
    """Shaded disc with a dark outline on a transparent background (straight alpha)."""
    cx, cy = center or (size / 2, size / 2)
    yy, xx = np.mgrid[0:size, 0:size].astype(np.float64) + 0.5
    d = np.sqrt((xx - cx) ** 2 + (yy - cy) ** 2)
    alpha = np.clip(radius + 0.5 - d, 0, 1) if aa else (d <= radius).astype(float)
    shade = np.clip(0.35 + 0.65 * ((cx - xx) + (cy - yy)) / (2 * radius) + 0.5, 0.25, 1.0)
    rgb = np.stack([shade * h for h in hue], -1)
    rng = np.random.default_rng(seed)
    rgb = np.clip(rgb + rng.normal(0, 0.02, rgb.shape), 0, 1)
    if outline:
        ring = d > radius - outline
        rgb[ring] = [0.1, 0.08, 0.07]
    return np.concatenate([rgb, alpha[..., None]], -1)


def save(arr, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    Image.fromarray(np.clip(np.round(arr * 255), 0, 255).astype(np.uint8), "RGBA").save(path)


def entry(root, rel, cat="weapon", aid="sword", variant="fire-t1", state="icon", frame=0, frame_count=1, fps=0,
          pivot=(0.5, 0.5), size=(256, 256), **kw):
    p = os.path.join(root, rel)
    e = {"file": rel, "id": aid, "category": cat, "variant": variant, "state": state, "frame": frame,
         "frame_count": frame_count, "fps": fps, "size": list(size), "pivot": list(pivot), "ppu": 100, "blend": "alpha",
         "source": {"script": "tools/art/art_validator.py", "function": "test"}, "parameters": {"t": 1}, "seed": 1,
         "blender_version": "Blender 4.0.2", "pipeline_version": "1.0.0", "license": LICENSE,
         "sha256": hashlib.sha256(open(p, "rb").read()).hexdigest() if os.path.exists(p) else None}
    e.update(kw)
    return e


def name(cat, aid, variant, state, frame):
    parts = ["fb", cat, aid] + ([variant] if variant else []) + [state, f"{frame:02d}"]
    return "_".join(parts) + ".png"


def write_manifest(root, entries):
    with open(os.path.join(root, "manifest.json"), "w") as fh:
        json.dump({"files": entries}, fh)


def add_still(root, arr, aid="sword", variant="fire-t1", cat="weapon", state="icon", **kw):
    rel = f"{cat}/{state}/" + name(cat, aid, variant, state, 0)
    save(arr, os.path.join(root, rel))
    return entry(root, rel, cat=cat, aid=aid, variant=variant, state=state, **kw)


def run(root, atlas_dir=None):
    return V.Validator(str(root), CFG, atlas_dir).run()


def checks(rep, level="FAIL", file=None):
    return {f.check for f in rep.findings if f.level == level and (file is None or f.file == file)}


# --------------------------------------------------------------------- tests
def test_good_sprite_passes(tmp_path):
    e = add_still(tmp_path, disc_sprite())
    write_manifest(tmp_path, [e])
    rep = run(tmp_path)
    assert not rep.has_fail(), [vars(f) for f in rep.findings]


def test_opaque_background_detected(tmp_path):
    arr = disc_sprite()
    bg = arr.copy()
    bg[..., :3] = np.where(arr[..., 3:4] > 0, arr[..., :3], 0.9)  # sprite flattened onto a light grey card
    bg[..., 3] = 1.0
    e = add_still(tmp_path, bg)
    write_manifest(tmp_path, [e])
    rep = run(tmp_path)
    assert "opaque_background" in checks(rep)
    assert "alpha" in checks(rep)


def test_white_halo_detected(tmp_path):
    good = disc_sprite(outline=0)
    bad = good.copy()
    a = bad[..., 3:4]
    bad[..., :3] = bad[..., :3] * a + (1 - a)  # matted on white, saved as straight
    # widen the soft edge so there are enough semi-transparent texels
    soft = disc_sprite(outline=0, radius=80)
    yy, xx = np.mgrid[0:256, 0:256] + 0.5
    d = np.sqrt((xx - 128) ** 2 + (yy - 128) ** 2)
    al = np.clip((84 - d) / 6.0, 0, 1)[..., None]
    soft[..., 3:4] = al
    soft[..., :3] = soft[..., :3] * al + (1 - al)
    e = add_still(tmp_path, soft)
    write_manifest(tmp_path, [e])
    assert "halo" in checks(run(tmp_path))


def test_dark_halo_detected(tmp_path):
    soft = disc_sprite(outline=0, radius=80, hue=(0.95, 0.85, 0.6))
    yy, xx = np.mgrid[0:256, 0:256] + 0.5
    d = np.sqrt((xx - 128) ** 2 + (yy - 128) ** 2)
    al = np.clip((84 - d) / 6.0, 0, 1)[..., None]
    soft[..., 3:4] = al
    soft[..., :3] = soft[..., :3] * al  # premultiplied colour stored as straight alpha
    e = add_still(tmp_path, soft)
    write_manifest(tmp_path, [e])
    assert "halo" in checks(run(tmp_path))


def test_clean_soft_edge_not_flagged_as_halo(tmp_path):
    soft = disc_sprite(outline=0, radius=80, hue=(0.95, 0.85, 0.6))
    yy, xx = np.mgrid[0:256, 0:256] + 0.5
    d = np.sqrt((xx - 128) ** 2 + (yy - 128) ** 2)
    soft[..., 3] = np.clip((84 - d) / 6.0, 0, 1)  # straight alpha, colour untouched
    e = add_still(tmp_path, soft)
    write_manifest(tmp_path, [e])
    rep = run(tmp_path)
    assert "halo" not in checks(rep) and "halo" not in checks(rep, "WARN")


def test_empty_frame_detected(tmp_path):
    arr = disc_sprite(radius=4)
    e = add_still(tmp_path, arr)
    write_manifest(tmp_path, [e])
    assert "empty" in checks(run(tmp_path))


def test_cropped_detected(tmp_path):
    e = add_still(tmp_path, disc_sprite(center=(240, 128), radius=60))
    write_manifest(tmp_path, [e])
    assert "cropped" in checks(run(tmp_path))


def test_exact_and_near_duplicates_detected(tmp_path):
    a = disc_sprite(seed=1)
    near = a.copy()
    near[100:102, 100:102, 0] = 0.0  # 4 texels changed
    other = disc_sprite(seed=2, hue=(0.2, 0.5, 0.9), radius=70)
    es = [add_still(tmp_path, a, variant="fire-t1"), add_still(tmp_path, a, variant="frost-t1"),
          add_still(tmp_path, near, variant="life-t1"), add_still(tmp_path, other, variant="void-t1")]
    write_manifest(tmp_path, es)
    rep = run(tmp_path)
    dup_files = {f.file for f in rep.findings if f.check == "duplicate" and f.level == "FAIL"}
    assert any("frost" in f for f in dup_files)
    assert any("life" in f for f in dup_files)
    assert not any("void" in f for f in dup_files)


def test_missing_provenance_detected(tmp_path):
    e = add_still(tmp_path, disc_sprite())
    del e["license"]
    e["seed"] = None
    write_manifest(tmp_path, [e])
    assert "provenance" in checks(run(tmp_path))


def test_sha_mismatch_and_orphan_detected(tmp_path):
    e = add_still(tmp_path, disc_sprite())
    e["sha256"] = "0" * 64
    save(disc_sprite(seed=5), os.path.join(tmp_path, "weapon/icon/fb_weapon_dagger_fire-t1_icon_00.png"))  # not in manifest
    write_manifest(tmp_path, [e])
    rep = run(tmp_path)
    msgs = " ".join(f.message for f in rep.findings if f.check == "provenance")
    assert "sha256 mismatch" in msgs and "not listed in manifest" in msgs


def test_bad_name_detected(tmp_path):
    rel = "weapon/icon/Sword Fire.png"
    save(disc_sprite(), os.path.join(tmp_path, rel))
    write_manifest(tmp_path, [entry(tmp_path, rel)])
    assert "naming" in checks(run(tmp_path))


def test_name_not_matching_manifest_fields(tmp_path):
    rel = "weapon/icon/fb_weapon_sword_frost-t1_icon_00.png"  # manifest says variant fire-t1
    save(disc_sprite(), os.path.join(tmp_path, rel))
    write_manifest(tmp_path, [entry(tmp_path, rel)])
    assert "naming" in checks(run(tmp_path))


def test_wrong_size_and_ppu_detected(tmp_path):
    rel = "weapon/icon/" + name("weapon", "sword", "fire-t1", "icon", 0)
    save(disc_sprite(size=200, radius=60), os.path.join(tmp_path, rel))
    write_manifest(tmp_path, [entry(tmp_path, rel, size=(200, 200), ppu=64)])
    rep = run(tmp_path)
    msgs = " ".join(f.message for f in rep.findings if f.check == "scale_ppu")
    assert "ppu 64" in msgs and "spec" in msgs


def _anim(root, frames, fps=8, aid="blob", pivot=(0.5, 0.1), static=False, drop=None, grounded=True):
    es = []
    for f in range(frames):
        if drop is not None and f == drop:
            continue
        r = 60 if static else 60 + 4 * np.sin(2 * np.pi * f / frames)
        arr = disc_sprite(radius=r, center=(128, 230 - r))  # sits on the ground line y=230
        rel = f"enemy/{aid}/" + name("enemy", aid, None, "idle", f)
        save(arr, os.path.join(root, rel))
        es.append(entry(root, rel, cat="enemy", aid=aid, variant=None, state="idle", frame=f, frame_count=8, fps=fps,
                        pivot=pivot, grounded=grounded))
    return es


def test_good_animation_passes(tmp_path):
    write_manifest(tmp_path, _anim(tmp_path, 8, pivot=(0.5, 1 - 230 / 256)))
    rep = run(tmp_path)
    assert "animation" not in checks(rep), [vars(f) for f in rep.findings if f.check == "animation"]


def test_missing_frame_and_wrong_fps_detected(tmp_path):
    write_manifest(tmp_path, _anim(tmp_path, 8, fps=10, drop=3, pivot=(0.5, 1 - 230 / 256)))
    msgs = " ".join(f.message for f in run(tmp_path).findings if f.check == "animation")
    assert "frames" in msgs and "fps" in msgs


def test_static_animation_and_bad_pivot_detected(tmp_path):
    es = _anim(tmp_path, 8, static=True, pivot=(0.5, 0.5))
    es[2]["pivot"] = [0.4, 0.5]
    write_manifest(tmp_path, es)
    msgs = " ".join(f.message for f in run(tmp_path).findings if f.check == "animation")
    assert "identical" in msgs and "pivot differs" in msgs and "ground line" in msgs


def test_missing_required_clip_detected(tmp_path):
    es = []
    for f in range(8):
        rel = "char/hero/" + name("char", "hero", None, "idle", f)
        save(disc_sprite(radius=60 + f, center=(128, 170 - f)), os.path.join(tmp_path, rel))
        es.append(entry(tmp_path, rel, cat="char", aid="hero", variant=None, state="idle", frame=f, frame_count=8, fps=8,
                        pivot=(0.5, 1 - 230 / 256), grounded=True))
    write_manifest(tmp_path, es)
    msgs = " ".join(f.message for f in run(tmp_path).findings if f.check == "animation")
    assert "required clip 'attack' missing" in msgs


def test_low_contrast_and_illegible_detected(tmp_path):
    arr = disc_sprite(outline=0, hue=(1, 1, 1))
    arr[..., :3] = np.array([0.11, 0.115, 0.14])  # same as dark UI background, flat
    e = add_still(tmp_path, arr)
    write_manifest(tmp_path, [e])
    rep = run(tmp_path)
    assert "contrast" in checks(rep)
    assert "legibility" in checks(rep)


def test_oversaturation_detected(tmp_path):
    arr = disc_sprite(outline=0)
    arr[..., :3] = np.array([1.0, 0.0, 1.0])
    e = add_still(tmp_path, arr)
    write_manifest(tmp_path, [e])
    assert "oversaturation" in checks(run(tmp_path))


def test_budget_detected(tmp_path):
    rng = np.random.default_rng(0)
    arr = disc_sprite(radius=120)
    arr[..., :3] = rng.random(arr[..., :3].shape)  # incompressible noise -> large PNG
    e = add_still(tmp_path, arr)
    write_manifest(tmp_path, [e])
    rep = run(tmp_path)
    assert "budget" in checks(rep, "WARN") | checks(rep, "FAIL")


def test_atlas_packer_output_valid_and_overlap_detected(tmp_path):
    es = []
    for i, v in enumerate(["fire-t1", "frost-t1", "life-t1", "void-t1", "wind-t1"]):
        e = add_still(tmp_path, disc_sprite(radius=30 + 8 * i, seed=i, hue=(0.2 + 0.15 * i, 0.5, 0.6)), variant=v,
                      state="ingame", size=(256, 256), group="g")
        es.append(e)
    write_manifest(tmp_path, es)
    out = os.path.join(tmp_path, "atlases")
    os.makedirs(out)
    meta = AP.build_group(str(tmp_path), es, "g", out, 4, 1024, 100)
    v = V.Validator(str(tmp_path), CFG, out)
    v.check_atlases()
    assert not [f for f in v.r.findings if f.level == "FAIL"]
    # pivots are preserved: trimmed-rect pivot maps back to the source centre
    for s in meta["sprites"]:
        sw, sh = s["source_size"]
        tx, ty = s["trim_offset"]
        _, _, w, h = s["rect"]
        px = (s["pivot"][0] * w + tx) / sw
        py = (s["pivot"][1] * h + (sh - ty - h)) / sh
        assert abs(px - 0.5) < 1e-3 and abs(py - 0.5) < 1e-3
    # now corrupt: make two rects overlap
    jp = os.path.join(out, "fb_atlas_g.json")
    m = json.load(open(jp))
    m["sprites"][1]["rect"][:2] = m["sprites"][0]["rect"][:2]
    json.dump(m, open(jp, "w"))
    v2 = V.Validator(str(tmp_path), CFG, out)
    v2.check_atlases()
    assert any(f.check == "atlas" and f.level == "FAIL" for f in v2.r.findings)


def test_cli_exit_codes_and_reports(tmp_path):
    root = tmp_path / "gen"
    root.mkdir()
    e = add_still(str(root), disc_sprite())
    write_manifest(str(root), [e])
    rep_dir = tmp_path / "rep"
    assert V.main(["--root", str(root), "--report-dir", str(rep_dir)]) == 0
    for ext in ("json", "csv", "html", "md"):
        assert (rep_dir / f"art_validation.{ext}").exists()
    assert "do NOT certify aesthetic" in (rep_dir / "art_validation.md").read_text()
    # introduce a defect -> non-zero exit
    arr = disc_sprite()
    arr[..., 3] = 1.0
    save(arr, os.path.join(root, e["file"]))
    assert V.main(["--root", str(root), "--report-dir", str(rep_dir), "--no-html"]) == 1
    assert V.main(["--root", str(tmp_path / "missing"), "--report-dir", str(rep_dir)]) == 2


@pytest.mark.skipif(not os.path.exists(os.path.join(V.DEFAULT_ROOT, "manifest.json")), reason="no generated art")
def test_generated_art_has_no_failures():
    rep = V.Validator(V.DEFAULT_ROOT, CFG, os.path.join(V.DEFAULT_ROOT, "atlases")).run()
    assert not rep.has_fail(), [vars(f) for f in rep.findings if f.level == "FAIL"][:10]
