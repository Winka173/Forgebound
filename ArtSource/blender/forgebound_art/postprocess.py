"""Post-processing of raw Blender renders (system python: Pillow + numpy).

* raw renders are straight-alpha RGBA at SUPERSAMPLE x final size
* downsample = box filter in PREMULTIPLIED space (no dark/white fringes)
* un-premultiply, zero out alpha < 1/255
* colour-bleed RGB into fully transparent texels so bilinear filtering /
  mipmaps in Unity never pull in black (prevents halos)
* glow layers: blurred emissive pass stored as an additive sprite
"""
from __future__ import annotations

import hashlib

import numpy as np
from PIL import Image, ImageFilter


def load_rgba(path) -> np.ndarray:
    return np.asarray(Image.open(path).convert("RGBA"), dtype=np.float64) / 255.0


def premultiply(a):
    out = a.copy()
    out[..., :3] *= out[..., 3:4]
    return out


def unpremultiply(p):
    out = p.copy()
    al = out[..., 3:4]
    safe = np.where(al > 1e-6, al, 1.0)
    out[..., :3] = np.where(al > 1e-6, out[..., :3] / safe, 0.0)
    return np.clip(out, 0.0, 1.0)


def box_down(p, factor):
    if factor == 1:
        return p
    h, w, c = p.shape
    return p.reshape(h // factor, factor, w // factor, factor, c).mean(axis=(1, 3))


def bleed(rgba, iterations=12):
    """Fill RGB of transparent texels with the average of known neighbours."""
    out = rgba.copy()
    known = out[..., 3] > 0
    rgb = out[..., :3] * known[..., None]
    for _ in range(iterations):
        if known.all():
            break
        acc = np.zeros_like(rgb)
        cnt = np.zeros(known.shape)
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                if dx == 0 and dy == 0:
                    continue
                acc += np.roll(np.roll(rgb, dy, 0), dx, 1)
                cnt += np.roll(np.roll(known, dy, 0), dx, 1)
        new = (~known) & (cnt > 0)
        rgb[new] = acc[new] / cnt[new][:, None]
        known = known | new
    if not known.all():
        mean = rgb[known].mean(axis=0) if known.any() else np.zeros(3)
        rgb[~known] = mean
    out[..., :3] = np.where(out[..., 3:4] > 0, out[..., :3], rgb)
    return out


def to_image(rgba) -> Image.Image:
    arr = np.clip(np.round(rgba * 255.0), 0, 255).astype(np.uint8)
    arr[..., 3] = np.where(arr[..., 3] < 1, 0, arr[..., 3])
    return Image.fromarray(arr, "RGBA")


def process_sprite(raw_path, factor):
    p = box_down(premultiply(load_rgba(raw_path)), factor)
    s = unpremultiply(p)
    s[..., 3] = np.where(s[..., 3] < 1.0 / 255.0, 0.0, s[..., 3])
    return to_image(bleed(s))


def process_opaque(raw_path, factor):
    """Full-frame backgrounds: plain box downsample, alpha forced to 1, RGB only."""
    p = box_down(load_rgba(raw_path), factor)
    return Image.fromarray(np.clip(np.round(p[..., :3] * 255.0), 0, 255).astype(np.uint8), "RGB")


def process_glow(raw_path, factor):
    p = box_down(premultiply(load_rgba(raw_path)), factor)
    img = Image.fromarray(np.clip(p * 255, 0, 255).astype(np.uint8), "RGBA")
    wide = np.asarray(img.filter(ImageFilter.GaussianBlur(6)), dtype=np.float64) / 255.0
    tight = np.asarray(img.filter(ImageFilter.GaussianBlur(3)), dtype=np.float64) / 255.0
    g = wide * 1.3 + tight * 0.5
    rgb = g[..., :3]
    al = np.clip(rgb.max(axis=-1, keepdims=True), 0, 1)
    out = np.concatenate([np.where(al > 1e-4, np.clip(rgb / np.maximum(al, 1e-4), 0, 1), 0), al], axis=-1)
    out[..., 3] = np.where(out[..., 3] < 2.0 / 255.0, 0.0, out[..., 3])
    return to_image(bleed(out))


def save_png(img: Image.Image, path):
    # no metadata chunks -> byte-identical output for identical pixels
    img.save(path, format="PNG", optimize=True)


def sha256_file(path) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as fh:
        for chunk in iter(lambda: fh.read(1 << 16), b""):
            h.update(chunk)
    return h.hexdigest()
