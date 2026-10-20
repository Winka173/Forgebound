#!/usr/bin/env python3
"""Forgebound art quality validator.

    python3 tools/art/art_validator.py                       # validate Generated/ + atlases
    python3 tools/art/art_validator.py --root <dir> --report-dir <dir> [--config <json>] [--no-html]

Exit code: 0 = no FAIL findings, 1 = at least one FAIL, 2 = usage / IO error.

Automated checks catch technical defects (halos, cropping, empty frames,
duplicates, provenance, budgets, legibility heuristics).  They do NOT certify
aesthetic quality; see the qualitative checkpoints in the generated report.
"""
from __future__ import annotations

import argparse
import base64
import csv
import hashlib
import io
import itertools
import json
import os
import re
import sys
from collections import defaultdict
from dataclasses import asdict, dataclass, field

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
DEFAULT_ROOT = os.path.join(REPO, "Assets", "Forgebound", "Resources", "Art", "Generated")
DEFAULT_CONFIG = os.path.join(HERE, "validator_config.json")
DEFAULT_REPORT = os.path.join(REPO, "reports", "art")

LEVELS = {"PASS": 0, "WARN": 1, "FAIL": 2}


@dataclass
class Finding:
    check: str
    level: str
    file: str
    message: str
    value: float | None = None
    threshold: float | None = None


@dataclass
class Report:
    findings: list = field(default_factory=list)
    metrics: dict = field(default_factory=dict)
    files: list = field(default_factory=list)

    def add(self, check, level, file, message, value=None, threshold=None):
        if value is not None:
            value = round(float(value), 5)
        self.findings.append(Finding(check, level, file, message, value, threshold))

    def worst(self, file):
        lv = [LEVELS[f.level] for f in self.findings if f.file == file]
        return ["PASS", "WARN", "FAIL"][max(lv)] if lv else "PASS"

    def has_fail(self):
        return any(f.level == "FAIL" for f in self.findings)


def grade(value, warn, fail, higher_is_bad=True):
    if higher_is_bad:
        return "FAIL" if value > fail else ("WARN" if value > warn else "PASS")
    return "FAIL" if value < fail else ("WARN" if value < warn else "PASS")


# ----------------------------------------------------------------- colour
def hex_rgb(h):
    h = h.lstrip("#")
    return np.array([int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4)])


def srgb_to_lin(c):
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def lstar(rgb):
    """CIE L* (0..100) of sRGB values in 0..1 (last axis = 3)."""
    lin = srgb_to_lin(np.clip(rgb, 0, 1))
    y = lin[..., 0] * 0.2126 + lin[..., 1] * 0.7152 + lin[..., 2] * 0.0722
    return np.where(y > 216 / 24389, 116 * np.cbrt(y) - 16, y * 24389 / 27)


def composite(rgba, bg):
    a = rgba[..., 3:4]
    return rgba[..., :3] * a + np.asarray(bg)[None, None, :] * (1 - a)


def load(path):
    im = Image.open(path)
    fmt, mode = im.format, im.mode
    arr = np.asarray(im.convert("RGBA"), dtype=np.float64) / 255.0
    return im, fmt, mode, arr


def area_downsample(rgba, size):
    """Premultiplied area downsample to size x size (phone-scale preview)."""
    pm = rgba.copy()
    pm[..., :3] *= pm[..., 3:4]
    img = Image.fromarray(np.clip(pm * 255 + 0.5, 0, 255).astype(np.uint8), "RGBA")
    small = np.asarray(img.resize((size, size), Image.BOX), dtype=np.float64) / 255.0
    a = small[..., 3:4]
    small[..., :3] = np.where(a > 1e-6, small[..., :3] / np.maximum(a, 1e-6), 0)
    return np.clip(small, 0, 1)


def phash(rgba, bits=64):
    """DCT perceptual hash of the image composited on mid grey."""
    g = composite(rgba, np.array([0.5, 0.5, 0.5]))
    lum = lstar(g)
    n = 32
    im = Image.fromarray(np.clip(lum * 2.55, 0, 255).astype(np.uint8), "L").resize((n, n), Image.BOX)
    x = np.asarray(im, dtype=np.float64)
    k = np.arange(n)
    dct_m = np.cos(np.pi * (2 * k[None, :] + 1) * k[:, None] / (2 * n))
    d = dct_m @ x @ dct_m.T
    side = int(round(bits ** 0.5))
    low = d[:side, :side].flatten()[1:]
    med = np.median(low)
    return np.concatenate([[0], (low > med).astype(np.uint8)])


# ------------------------------------------------------------ image checks
def check_alpha_present(arr):
    a = arr[..., 3]
    return bool((a < 1.0).any() and (a > 0).any())


def border_opaque_frac(arr, border_px, alpha_thr):
    a = arr[..., 3]
    m = np.zeros(a.shape, bool)
    m[:border_px, :] = m[-border_px:, :] = True
    m[:, :border_px] = m[:, -border_px:] = True
    return float((a[m] > alpha_thr).mean())


def corner_opaque(arr, alpha_thr=0.5):
    a = arr[..., 3]
    return sum(int(a[y, x] > alpha_thr) for y, x in ((0, 0), (0, -1), (-1, 0), (-1, -1)))


def cropped_px(arr, alpha_thr):
    a = arr[..., 3]
    edge = np.concatenate([a[0, :], a[-1, :], a[:, 0], a[:, -1]])
    return int((edge > alpha_thr).sum())


def coverage(arr, alpha_thr):
    return float((arr[..., 3] > alpha_thr).mean())


def halo_fracs(arr, tol, min_sep=0.12):
    """Matte-reconstruction halo test.

    A dark halo is what you get when premultiplied colour is stored as
    straight alpha (or the sprite was matted on black): a semi-transparent
    edge texel then equals alpha * C_interior.  A white fringe equals
    alpha * C_interior + (1 - alpha) * white.  For every edge texel with
    0.25 <= alpha <= 0.85 next to opaque texels, we test whether its colour is
    explained by such a matte (max channel error < tol) while being clearly
    different from the interior colour (> min_sep), which rules out intended
    dark outlines (constant colour, independent of alpha).
    Returns (n_eligible, dark_frac, light_frac)."""
    a = arr[..., 3]
    rgb = arr[..., :3]
    opaque = a >= 0.95
    acc = np.zeros(rgb.shape)
    cnt = np.zeros(a.shape)
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            if dx == 0 and dy == 0:
                continue
            o = np.roll(np.roll(opaque, dy, 0), dx, 1)
            c = np.roll(np.roll(rgb, dy, 0), dx, 1)
            acc += np.where(o[..., None], c, 0)
            cnt += o
    edge = (a >= 0.25) & (a <= 0.85) & (cnt > 0)
    if not edge.any():
        return 0, 0.0, 0.0
    nb = acc[edge] / cnt[edge][:, None]
    e = rgb[edge]
    al = a[edge][:, None]
    sep_dark = np.abs(e - nb).max(-1) > min_sep
    dark_pred = al * nb
    light_pred = al * nb + (1 - al)
    # only texels where the two hypotheses are distinguishable from 'correct'
    eligible = (np.abs(nb - dark_pred).max(-1) > min_sep) | (np.abs(nb - light_pred).max(-1) > min_sep)
    n = int(eligible.sum())
    if n == 0:
        return 0, 0.0, 0.0
    dark = (np.abs(e - dark_pred).max(-1) < tol) & sep_dark & eligible
    light = (np.abs(e - light_pred).max(-1) < tol) & sep_dark & eligible
    return n, float(dark.sum() / n), float(light.sum() / n)


def contrast_frac(arr, bg_hex, delta_l, alpha_thr=0.5):
    bg = hex_rgb(bg_hex)
    m = arr[..., 3] > alpha_thr
    if not m.any():
        return 0.0
    comp = composite(arr, bg)
    Lc = lstar(comp)[m]
    Lb = float(lstar(bg[None, None, :])[0, 0])
    return float((np.abs(Lc - Lb) > delta_l).mean())


def legibility(arr, size):
    small = area_downsample(arr, size)
    m = small[..., 3] > 0.5
    n = int(m.sum())
    lstd = float(lstar(small[..., :3])[m].std()) if n > 1 else 0.0
    return n, lstd


def oversat_frac(arr, s_thr, v_thr, alpha_thr=0.5):
    m = arr[..., 3] > alpha_thr
    if not m.any():
        return 0.0
    rgb = arr[..., :3][m]
    mx, mn = rgb.max(-1), rgb.min(-1)
    s = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-6), 0)
    return float(((s > s_thr) & (mx > v_thr)).mean())


def bbox(arr, alpha_thr=0.1):
    ys, xs = np.where(arr[..., 3] > alpha_thr)
    if len(xs) == 0:
        return None
    return int(xs.min()), int(ys.min()), int(xs.max()), int(ys.max())


def changed_frac(a, b, thumb, delta):
    """Fraction of union-silhouette texels whose composited colour differs."""
    ta = area_downsample(a, thumb)
    tb = area_downsample(b, thumb)
    grey = np.array([0.5, 0.5, 0.5])
    ca, cb = composite(ta, grey), composite(tb, grey)
    union = (ta[..., 3] > 0.05) | (tb[..., 3] > 0.05)
    if not union.any():
        return 0.0
    d = np.sqrt(((ca - cb) ** 2).sum(-1))
    return float((d[union] > delta).mean())


# ------------------------------------------------------------- validator
class Validator:
    def __init__(self, root, config, atlas_dir=None, repo=REPO):
        self.root = root
        self.cfg = config
        self.atlas_dir = atlas_dir
        self.repo = repo
        self.r = Report()
        self.arrays = {}

    # -- manifest / provenance -------------------------------------------
    def load_manifest(self):
        path = os.path.join(self.root, "manifest.json")
        if not os.path.exists(path):
            self.r.add("provenance", "FAIL", "manifest.json", "manifest.json missing")
            return []
        try:
            man = json.load(open(path))
        except Exception as e:  # noqa: BLE001
            self.r.add("provenance", "FAIL", "manifest.json", f"manifest unreadable: {e}")
            return []
        return man.get("files", [])

    def check_provenance(self, e):
        f = e.get("file", "?")
        missing = [k for k in self.cfg["required_fields"] if e.get(k) in (None, "", [])]
        if missing:
            self.r.add("provenance", "FAIL", f, f"missing manifest fields: {', '.join(missing)}")
        if e.get("license") and e["license"] != self.cfg["license"]:
            self.r.add("provenance", "FAIL", f, f"unexpected license: {e['license']!r}")
        src = (e.get("source") or {}).get("script")
        if src and not os.path.exists(os.path.join(self.repo, src)):
            self.r.add("provenance", "FAIL", f, f"source script not found: {src}")
        p = os.path.join(self.root, f)
        if os.path.exists(p) and e.get("sha256"):
            h = hashlib.sha256(open(p, "rb").read()).hexdigest()
            if h != e["sha256"]:
                self.r.add("provenance", "FAIL", f, "sha256 mismatch (file changed after manifest was written)")

    def check_naming(self, e):
        f = e["file"]
        base = os.path.basename(f)
        if not re.match(self.cfg["name_regex"], base):
            self.r.add("naming", "FAIL", f, f"name violates convention fb_<category>_<id>[_<variant>]_<state>_<frame>.png: {base}")
            return
        parts = ["fb", e.get("category", ""), e.get("id", "")]
        if e.get("variant"):
            parts.append(e["variant"])
        parts += [e.get("state", ""), f"{int(e.get('frame', -1)):02d}"]
        expect = "_".join(parts) + ".png"
        if base != expect:
            self.r.add("naming", "FAIL", f, f"filename does not match manifest fields (expected {expect})")
        if not f.startswith(e.get("category", "") + "/"):
            self.r.add("naming", "FAIL", f, f"file not under its category folder '{e.get('category')}/'")

    def check_scale(self, e, im):
        f = e["file"]
        if e.get("ppu") != self.cfg["ppu"]:
            self.r.add("scale_ppu", "FAIL", f, f"ppu {e.get('ppu')} != {self.cfg['ppu']}")
        exp = self.cfg["sizes"].get(e.get("category"), {}).get(e.get("state"))
        if exp is None:
            self.r.add("scale_ppu", "FAIL", f, f"no size spec for {e.get('category')}/{e.get('state')}")
        elif list(im.size) != list(exp):
            self.r.add("scale_ppu", "FAIL", f, f"size {im.size} != spec {tuple(exp)}")
        if list(im.size) != list(e.get("size", [])):
            self.r.add("scale_ppu", "FAIL", f, f"size {im.size} != manifest {e.get('size')}")
        pv = e.get("pivot") or [-1, -1]
        if not (0 <= pv[0] <= 1 and 0 <= pv[1] <= 1):
            self.r.add("scale_ppu", "FAIL", f, f"pivot {pv} outside 0..1")

    # -- per-image pixel checks -------------------------------------------
    def check_image(self, e, im, fmt, mode, arr):
        f = e["file"]
        c = self.cfg
        additive = e.get("blend") == "additive"
        if fmt != "PNG":
            self.r.add("format", "FAIL", f, f"format {fmt} (PNG required)")
        if mode != "RGBA":
            self.r.add("format", "FAIL", f, f"mode {mode} (RGBA required)")
        if not check_alpha_present(arr):
            self.r.add("alpha", "FAIL", f, "no transparency (alpha channel fully opaque or fully empty)")
        ob = c["opaque_background"]
        v = border_opaque_frac(arr, ob["border_px"], ob["alpha"])
        lv = grade(v, ob["warn_frac"], ob["fail_frac"])
        if corner_opaque(arr) >= 3:
            lv = "FAIL"
        if lv != "PASS":
            self.r.add("opaque_background", lv, f, f"border opacity {v:.1%} (unintended background?)", v, ob["fail_frac"])
        cr = c["cropped"]
        n = cropped_px(arr, cr["alpha"])
        lv = "FAIL" if n >= cr["fail_px"] else ("WARN" if n >= cr["warn_px"] else "PASS")
        if lv != "PASS":
            self.r.add("cropped", lv, f, f"{n} visible texels touch the image border (content cropped)", n, cr["fail_px"])
        em = c["empty"]
        # additive glow layers are soft by design: measure their coverage at a lower alpha
        cov = coverage(arr, em["additive_alpha"] if additive else em["alpha"])
        lv = grade(cov, em["warn_coverage"], em["fail_coverage"], higher_is_bad=False)
        if lv != "PASS":
            self.r.add("empty", lv, f, f"opaque coverage {cov:.2%} (empty / nearly empty frame)", cov, em["fail_coverage"])
        metrics = {"coverage": cov}
        if not additive:
            h = c["halo"]
            nedge, dark, light = halo_fracs(arr, h["tol"])
            metrics.update(halo_dark=dark, halo_light=light)
            if nedge >= h["min_edge_px"]:
                for kind, val in (("dark", dark), ("white", light)):
                    lv = grade(val, h["warn_frac"], h["fail_frac"])
                    if lv != "PASS":
                        self.r.add("halo", lv, f, f"{kind} fringe on {val:.1%} of alpha-edge texels", val, h["fail_frac"])
            ct = c["contrast"]
            for name, bg in (("dark", ct["dark_bg"]), ("light", ct["light_bg"])):
                val = contrast_frac(arr, bg, ct["delta_l"])
                metrics[f"contrast_{name}"] = val
                lv = grade(val, ct["warn_frac"], ct["fail_frac"], higher_is_bad=False)
                if lv != "PASS" and cov > em["fail_coverage"]:
                    self.r.add("contrast", lv, f, f"only {val:.0%} of sprite texels contrast with a {name} background", val,
                               ct["fail_frac"])
            lg = c["legibility"]
            npx, lstd = legibility(arr, lg["size"])
            metrics.update(legible_px=npx, legible_lstd=lstd)
            if cov > em["fail_coverage"]:
                lv = grade(npx, lg["warn_min_px"], lg["fail_min_px"], higher_is_bad=False)
                if lv != "PASS":
                    self.r.add("legibility", lv, f, f"only {npx} opaque px at {lg['size']}px phone scale", npx, lg["fail_min_px"])
                lv = grade(lstd, lg["warn_min_lstd"], lg["fail_min_lstd"], higher_is_bad=False)
                if lv != "PASS":
                    self.r.add("legibility", lv, f, f"flat value structure at phone scale (L* std {lstd:.1f})", lstd, lg["fail_min_lstd"])
            os_ = c["oversaturation"]
            val = oversat_frac(arr, os_["s"], os_["v"])
            metrics["oversat"] = val
            lv = grade(val, os_["warn_frac"], os_["fail_frac"])
            if lv != "PASS":
                self.r.add("oversaturation", lv, f, f"{val:.1%} of texels clipped at full saturation/value", val, os_["fail_frac"])
        else:
            os_ = c["oversaturation"]
            m = arr[..., 3] > 0.02
            clip = float((arr[..., 3][m] >= 0.999).mean()) if m.any() else 0.0
            metrics["glow_clip"] = clip
            lv = grade(clip, os_["glow_clip_warn_frac"], os_["glow_clip_fail_frac"])
            if lv != "PASS":
                self.r.add("oversaturation", lv, f, f"additive glow saturates alpha on {clip:.0%} of its area", clip,
                           os_["glow_clip_fail_frac"])
        b = c["budget"]
        kb = os.path.getsize(os.path.join(self.root, f)) / 1024
        metrics["kb"] = kb
        lv = grade(kb, b["file_warn_kb"], b["file_fail_kb"])
        if lv != "PASS":
            self.r.add("budget", lv, f, f"file size {kb:.0f} KB", kb, b["file_fail_kb"])
        self.r.metrics[f] = {k: round(float(v), 4) for k, v in metrics.items()}

    # -- group checks ---------------------------------------------------------
    def check_animations(self, entries):
        c = self.cfg
        an = c["animation"]
        groups = defaultdict(list)
        for e in entries:
            if e.get("category") in c["required_clips"]:
                groups[(e["category"], e["id"], e["state"])].append(e)
        ids = defaultdict(set)
        for (cat, aid, state) in groups:
            ids[(cat, aid)].add(state)
        for (cat, aid), states in ids.items():
            for req in c["required_clips"][cat]:
                if req not in states:
                    self.r.add("animation", "FAIL", f"{cat}/{aid}", f"required clip '{req}' missing")
        for (cat, aid, state), es in groups.items():
            key = f"{cat}/{aid}/{state}"
            spec_ = c["clips"].get(state)
            frames = sorted(int(e["frame"]) for e in es)
            fc = {e.get("frame_count") for e in es}
            fps = {e.get("fps") for e in es}
            if spec_:
                if frames != list(range(spec_["frames"])):
                    self.r.add("animation", "FAIL", key, f"frames {frames} != expected 0..{spec_['frames'] - 1}")
                if fc != {spec_["frames"]}:
                    self.r.add("animation", "FAIL", key, f"manifest frame_count {sorted(fc)} != {spec_['frames']}")
                if fps != {spec_["fps"]}:
                    self.r.add("animation", "FAIL", key, f"manifest fps {sorted(fps)} != {spec_['fps']}")
            elif frames != list(range(len(frames))):
                self.r.add("animation", "FAIL", key, f"frame numbering has gaps: {frames}")
            pivots = {tuple(e.get("pivot") or []) for e in es}
            if len(pivots) > 1:
                self.r.add("animation", "FAIL", key, f"pivot differs across frames: {sorted(pivots)}")
            arrs = [self.arrays.get(e["file"]) for e in sorted(es, key=lambda e: e["frame"])]
            if any(a is None for a in arrs) or not arrs:
                continue
            boxes = [bbox(a) for a in arrs]
            if any(bx is None for bx in boxes):
                continue
            h = arrs[0].shape[0]
            bottoms = [bx[3] for bx in boxes]
            grounded = bool(es[0].get("grounded"))
            if grounded:
                rng = max(bottoms) - min(bottoms)
                lv = grade(rng, an["bottom_warn_px"], an["bottom_fail_px"])
                if lv != "PASS":
                    self.r.add("animation", lv, key, f"feet line (bbox bottom) drifts {rng}px across frames", rng, an["bottom_fail_px"])
                pivot_y = (1 - float(es[0]["pivot"][1])) * h
                off = max(abs(b_ + 1 - pivot_y) for b_ in bottoms)
                lv = grade(off, an["ground_warn_px"], an["ground_fail_px"])
                if lv != "PASS":
                    self.r.add("animation", lv, key, f"feet are {off:.1f}px away from the pivot ground line", off, an["ground_fail_px"])
            areas = [(bx[2] - bx[0] + 1) * (bx[3] - bx[1] + 1) for bx in boxes]
            seq = list(zip(areas, areas[1:] + ([areas[0]] if spec_ and c["clips"].get(state) and state == "idle" else [])))
            jump = max((max(x, y) / max(1, min(x, y)) for x, y in seq), default=1.0)
            lv = grade(jump, an["area_jump_warn"], an["area_jump_fail"])
            if lv != "PASS":
                self.r.add("animation", lv, key, f"bounding box area jumps {jump:.2f}x between frames (popping)", jump,
                           an["area_jump_fail"])
            if len(arrs) > 1:
                ch = max(changed_frac(arrs[0], a, 64, 0.05) for a in arrs[1:])
                if ch <= an["static_max_changed_frac"]:
                    self.r.add("animation", "FAIL", key, "all frames are identical (no animation)")

    def check_duplicates(self, entries):
        d = self.cfg["duplicate"]
        stills = [e for e in entries if e["file"] in self.arrays]
        by_sha = defaultdict(list)
        for e in stills:
            by_sha[e.get("sha256") or hashlib.sha256(open(os.path.join(self.root, e["file"]), "rb").read()).hexdigest()].append(e)
        for sha, es in by_sha.items():
            if len(es) > 1:
                for e in es[1:]:
                    self.r.add("duplicate", "FAIL", e["file"], f"byte-identical to {es[0]['file']}")
        groups = defaultdict(list)
        for e in stills:
            groups[(e["category"], e["state"])].append(e)
        hashes = {e["file"]: phash(self.arrays[e["file"]], d["phash_bits"]) for e in stills}
        self.r.metrics.setdefault("_duplicate_pairs", {})
        for (cat, state), es in groups.items():
            for a, b in itertools.combinations(es, 2):
                # frames of the same animation clip are expected to be similar
                if a["id"] == b["id"] and a.get("variant") == b.get("variant") and a["state"] == b["state"] and a["frame"] != b["frame"]:
                    continue
                dist = int((hashes[a["file"]] != hashes[b["file"]]).sum())
                if dist > d["phash_max_dist"]:
                    continue
                ch = changed_frac(self.arrays[a["file"]], self.arrays[b["file"]], d["thumb"], d["pixel_delta"])
                self.r.metrics["_duplicate_pairs"][f"{a['file']}|{b['file']}"] = round(ch, 4)
                lv = grade(ch, d["warn_changed_frac"], d["fail_changed_frac"], higher_is_bad=False)
                if lv != "PASS":
                    self.r.add("duplicate", lv, b["file"],
                               f"near-duplicate of {os.path.basename(a['file'])}: phash dist {dist}, only {ch:.1%} of texels differ", ch,
                               d["fail_changed_frac"])

    def check_budget_total(self, entries):
        b = self.cfg["budget"]
        total = 0
        for dp, _, fs in os.walk(self.root):
            for fn in fs:
                if fn.endswith(".png"):
                    total += os.path.getsize(os.path.join(dp, fn))
        mb = total / (1024 * 1024)
        self.r.metrics["_total_mb"] = round(mb, 2)
        lv = grade(mb, b["total_warn_mb"], b["total_fail_mb"])
        if lv != "PASS":
            self.r.add("budget", lv, "(total)", f"generated PNGs total {mb:.1f} MB", mb, b["total_fail_mb"])

    def check_atlases(self):
        if not self.atlas_dir or not os.path.isdir(self.atlas_dir):
            return
        a = self.cfg["atlas"]
        for fn in sorted(os.listdir(self.atlas_dir)):
            if not fn.endswith(".json"):
                continue
            rel = os.path.relpath(os.path.join(self.atlas_dir, fn), self.root).replace(os.sep, "/")
            meta = json.load(open(os.path.join(self.atlas_dir, fn)))
            for page in meta.get("pages", []):
                ppath = os.path.join(self.atlas_dir, page["image"])
                if not os.path.exists(ppath):
                    self.r.add("atlas", "FAIL", rel, f"atlas page missing: {page['image']}")
                    continue
                W, H = Image.open(ppath).size
                if [W, H] != page["size"]:
                    self.r.add("atlas", "FAIL", rel, f"page size {W}x{H} != metadata {page['size']}")
                if max(W, H) > a["max_size"]:
                    self.r.add("atlas", "FAIL", rel, f"page {W}x{H} exceeds {a['max_size']}")
                pad = meta.get("padding", 0)
                if pad < a["min_padding"]:
                    self.r.add("atlas", "FAIL", rel, f"padding {pad}px < {a['min_padding']}px (bleeding risk)")
                elif pad > a["max_padding"]:
                    self.r.add("atlas", "WARN", rel, f"excess padding {pad}px > {a['max_padding']}px")
                rects = [s for s in meta["sprites"] if s["page"] == page["index"]]
                used = 0
                for s in rects:
                    x, y, w, h = s["rect"]
                    used += w * h
                    if x < 0 or y < 0 or x + w > W or y + h > H:
                        self.r.add("atlas", "FAIL", rel, f"sprite {s['name']} rect {s['rect']} outside page")
                for s1, s2 in itertools.combinations(rects, 2):
                    x1, y1, w1, h1 = s1["rect"]
                    x2, y2, w2, h2 = s2["rect"]
                    # rects must be separated by at least `pad` texels
                    if x1 < x2 + w2 + pad and x2 < x1 + w1 + pad and y1 < y2 + h2 + pad and y2 < y1 + h1 + pad:
                        self.r.add("atlas", "FAIL", rel, f"sprites overlap / violate padding: {s1['name']} vs {s2['name']}")
                eff = used / float(W * H)
                lv = grade(eff, a["warn_efficiency"], a["fail_efficiency"], higher_is_bad=False)
                self.r.metrics[f"{rel}#page{page['index']}"] = {"efficiency": round(eff, 3)}
                if lv != "PASS":
                    self.r.add("atlas", lv, rel, f"page {page['index']} packing efficiency {eff:.0%}", eff, a["fail_efficiency"])

    # -- driver -------------------------------------------------------------
    def run(self):
        entries = self.load_manifest()
        seen = set()
        for e in entries:
            f = e.get("file")
            if not f:
                self.r.add("provenance", "FAIL", "manifest.json", "entry without 'file'")
                continue
            seen.add(f)
            self.r.files.append(e)
            self.check_provenance(e)
            self.check_naming(e)
            p = os.path.join(self.root, f)
            if not os.path.exists(p):
                self.r.add("provenance", "FAIL", f, "listed in manifest but file missing")
                continue
            try:
                im, fmt, mode, arr = load(p)
            except Exception as ex:  # noqa: BLE001
                self.r.add("format", "FAIL", f, f"unreadable image: {ex}")
                continue
            self.arrays[f] = arr
            self.check_scale(e, im)
            self.check_image(e, im, fmt, mode, arr)
        atlas_rel = os.path.relpath(self.atlas_dir, self.root).replace(os.sep, "/") if self.atlas_dir else None
        for dp, _, fs in os.walk(self.root):
            for fn in fs:
                if not fn.endswith(".png"):
                    continue
                rel = os.path.relpath(os.path.join(dp, fn), self.root).replace(os.sep, "/")
                if atlas_rel and rel.startswith(atlas_rel + "/"):
                    continue
                if rel not in seen:
                    self.r.add("provenance", "FAIL", rel, "PNG not listed in manifest (no provenance)")
        self.check_animations([e for e in entries if e.get("file") in self.arrays])
        self.check_duplicates([e for e in entries if e.get("file") in self.arrays])
        self.check_budget_total(entries)
        self.check_atlases()
        return self.r


# ---------------------------------------------------------------- reports
def _b64png(img):
    buf = io.BytesIO()
    img.save(buf, format="PNG")
    return base64.b64encode(buf.getvalue()).decode()


def checker(w, h, s=8, c1=(70, 74, 84), c2=(52, 55, 63)):
    yy, xx = np.mgrid[0:h, 0:w]
    m = ((xx // s + yy // s) % 2).astype(bool)
    arr = np.where(m[..., None], np.array(c1), np.array(c2)).astype(np.uint8)
    return Image.fromarray(arr, "RGB").convert("RGBA")


def contact_sheet(paths, root, cell, cols, bg=None, labels=False):
    rows = max(1, (len(paths) + cols - 1) // cols)
    sheet = checker(cols * cell, rows * cell) if bg is None else Image.new("RGBA", (cols * cell, rows * cell), bg)
    for i, rel in enumerate(paths):
        im = Image.open(os.path.join(root, rel)).convert("RGBA")
        arr = area_downsample(np.asarray(im, dtype=np.float64) / 255.0, cell) if im.size[0] != cell else np.asarray(im) / 255.0
        tile = Image.fromarray(np.clip(arr * 255 + 0.5, 0, 255).astype(np.uint8), "RGBA")
        sheet.alpha_composite(tile, ((i % cols) * cell, (i // cols) * cell))
    return sheet


def write_reports(rep: Report, root, outdir, cfg, make_html=True):
    os.makedirs(outdir, exist_ok=True)
    files = [e["file"] for e in rep.files]
    counts = {"PASS": 0, "WARN": 0, "FAIL": 0}
    for f in files:
        counts[rep.worst(f)] += 1
    by_check = defaultdict(lambda: {"WARN": 0, "FAIL": 0})
    for fd in rep.findings:
        by_check[fd.check][fd.level] += 1
    summary = {"files": len(files), "file_levels": counts, "findings": len(rep.findings),
               "fail_findings": sum(1 for f in rep.findings if f.level == "FAIL"),
               "warn_findings": sum(1 for f in rep.findings if f.level == "WARN"),
               "by_check": dict(by_check), "total_mb": rep.metrics.get("_total_mb"),
               "verdict": "FAIL" if rep.has_fail() else "PASS"}
    disclaimer = ("Automated checks verify technical correctness (format, alpha, halos, cropping, provenance, budgets, "
                  "duplicates, legibility heuristics). They do NOT certify aesthetic quality; a human art review against "
                  "the qualitative checkpoints is required before assets are considered final.")
    with open(os.path.join(outdir, "art_validation.json"), "w") as fh:
        json.dump({"summary": summary, "disclaimer": disclaimer, "qualitative_checkpoints": cfg["qualitative_checkpoints"],
                   "findings": [asdict(f) for f in rep.findings],
                   "files": [{"file": f, "level": rep.worst(f), "metrics": rep.metrics.get(f, {})} for f in files]}, fh, indent=1)
    with open(os.path.join(outdir, "art_validation.csv"), "w", newline="") as fh:
        w = csv.writer(fh)
        w.writerow(["file", "check", "level", "message", "value", "threshold"])
        for f in rep.findings:
            w.writerow([f.file, f.check, f.level, f.message, f.value, f.threshold])
        for f in files:
            if rep.worst(f) == "PASS":
                w.writerow([f, "all", "PASS", "", "", ""])
    # markdown
    md = ["# Forgebound art validation", "", f"**Verdict: {summary['verdict']}** - {len(files)} files: "
          f"{counts['PASS']} PASS / {counts['WARN']} WARN / {counts['FAIL']} FAIL "
          f"({summary['fail_findings']} fail findings, {summary['warn_findings']} warn findings). "
          f"Generated PNG total: {summary['total_mb']} MB.", "", f"> {disclaimer}", "", "## Findings by check", "",
          "| check | WARN | FAIL |", "|---|---|---|"]
    for k, v in sorted(by_check.items()):
        md.append(f"| {k} | {v['WARN']} | {v['FAIL']} |")
    md += ["", "## FAIL / WARN details (first 80)", ""]
    for f in sorted(rep.findings, key=lambda f: -LEVELS[f.level])[:80]:
        md.append(f"- **{f.level}** `{f.check}` {f.file}: {f.message}")
    md += ["", "## Qualitative review checkpoints (human)", ""] + [f"- [ ] {q}" for q in cfg["qualitative_checkpoints"]]
    md += ["", "Contact sheets and phone-scale previews: `art_validation.html` (and `contact_*.png`)."]
    with open(os.path.join(outdir, "art_validation.md"), "w") as fh:
        fh.write("\n".join(md) + "\n")
    if not make_html:
        return summary
    # html with contact sheets per category/state
    groups = defaultdict(list)
    for e in rep.files:
        if os.path.exists(os.path.join(root, e["file"])):
            groups[(e["category"], e["state"])].append(e["file"])
    sections = []
    for (cat, state), paths in sorted(groups.items()):
        paths = sorted(paths)
        cols = 12 if len(paths) > 24 else 8
        cell = 96 if len(paths) > 24 else 128
        full = contact_sheet(paths, root, cell, cols)
        full.save(os.path.join(outdir, f"contact_{cat}_{state}.png"))
        dark = contact_sheet(paths, root, 48, cols * 2, bg=(26, 28, 34, 255))
        light = contact_sheet(paths, root, 48, cols * 2, bg=(228, 228, 228, 255))
        sections.append(f"<h2>{cat} / {state} ({len(paths)})</h2>"
                        f"<p>Contact sheet ({cell}px cells, checker = transparency):</p><img class=sheet src='data:image/png;base64,{_b64png(full)}'>"
                        f"<p>Actual phone scale (48px, 1:1 pixels) on dark and light UI backgrounds:</p>"
                        f"<img class=px src='data:image/png;base64,{_b64png(dark)}'><br><img class=px src='data:image/png;base64,{_b64png(light)}'>")
    rows = []
    for f in sorted(rep.findings, key=lambda f: (-LEVELS[f.level], f.check, f.file)):
        thumb = ""
        p = os.path.join(root, f.file)
        if os.path.isfile(p) and p.endswith(".png"):
            im = Image.open(p).convert("RGBA")
            im.thumbnail((64, 64))
            bgim = checker(im.size[0], im.size[1], 4)
            bgim.alpha_composite(im)
            thumb = f"<img src='data:image/png;base64,{_b64png(bgim)}'>"
        rows.append(f"<tr class={f.level}><td>{thumb}</td><td>{f.level}</td><td>{f.check}</td><td>{f.file}</td><td>{f.message}</td></tr>")
    checks = "".join(f"<li>{q}</li>" for q in cfg["qualitative_checkpoints"])
    html = f"""<!doctype html><html><head><meta charset=utf-8><title>Forgebound Art Validation</title>
<style>body{{font-family:system-ui,sans-serif;background:#16181d;color:#e4e4e4;margin:24px;max-width:1700px}}
table{{border-collapse:collapse}}td,th{{border-bottom:1px solid #333;padding:4px 8px;text-align:left;vertical-align:middle}}
.FAIL td:nth-child(2){{color:#ff6b6b;font-weight:bold}}.WARN td:nth-child(2){{color:#ffc857}}
img.sheet{{max-width:100%}}img.px{{image-rendering:pixelated}}.note{{background:#2a2420;border-left:4px solid #ffb347;padding:8px 12px}}</style></head><body>
<h1>Forgebound art validation - {summary['verdict']}</h1>
<p>{len(files)} files: {counts['PASS']} PASS / {counts['WARN']} WARN / {counts['FAIL']} FAIL. Total PNG size {summary['total_mb']} MB.</p>
<p class=note>{disclaimer}</p><h2>Qualitative review checkpoints</h2><ol>{checks}</ol>
<h2>Findings ({len(rep.findings)})</h2><table><tr><th></th><th>level</th><th>check</th><th>file</th><th>message</th></tr>{''.join(rows)}</table>
{''.join(sections)}</body></html>"""
    with open(os.path.join(outdir, "art_validation.html"), "w") as fh:
        fh.write(html)
    return summary


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--root", default=DEFAULT_ROOT)
    ap.add_argument("--config", default=DEFAULT_CONFIG)
    ap.add_argument("--report-dir", default=DEFAULT_REPORT)
    ap.add_argument("--atlas-dir", default=None, help="default: <root>/atlases")
    ap.add_argument("--no-html", action="store_true")
    args = ap.parse_args(argv)
    if not os.path.isdir(args.root):
        print(f"root not found: {args.root}", file=sys.stderr)
        return 2
    cfg = json.load(open(args.config))
    atlas_dir = args.atlas_dir or os.path.join(args.root, "atlases")
    rep = Validator(args.root, cfg, atlas_dir).run()
    s = write_reports(rep, args.root, args.report_dir, cfg, make_html=not args.no_html)
    print(f"[art-validator] {s['verdict']}: {s['files']} files -> {s['file_levels']} ; "
          f"{s['fail_findings']} FAIL / {s['warn_findings']} WARN findings ; total {s['total_mb']} MB")
    for k, v in sorted(s["by_check"].items()):
        print(f"   {k:18s} WARN={v['WARN']:4d} FAIL={v['FAIL']:4d}")
    print(f"   reports: {args.report_dir}/art_validation.(json|csv|html|md)")
    return 1 if rep.has_fail() else 0


if __name__ == "__main__":
    sys.exit(main())
