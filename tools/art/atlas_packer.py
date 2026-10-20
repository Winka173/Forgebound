#!/usr/bin/env python3
"""Pack generated sprites into SpriteAtlas-like pages + JSON metadata.

    python3 tools/art/atlas_packer.py                     # default groups
    python3 tools/art/atlas_packer.py --groups weapon_ingame enemy --padding 4 --max-size 2048

Groups come from the manifest's "group" field (weapon_icon, weapon_ingame,
board_icon, char_<id>, enemy).  Sprites are alpha-trimmed; pivots are
re-expressed relative to the trimmed rect (Unity convention: normalised,
origin bottom-left).  Rects are separated by >= padding texels.  Output:
Assets/Forgebound/Resources/Art/Generated/atlases/fb_atlas_<group>[_<page>].png + .json
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
DEFAULT_ROOT = os.path.join(REPO, "Assets", "Forgebound", "Resources", "Art", "Generated")
DEFAULT_GROUPS = ["weapon_ingame", "board_icon", "char_blacksmith", "enemy"]


def bleed_page(page: Image.Image, iterations=4) -> Image.Image:
    """Dilate colour into transparent texels (alpha stays 0) so bilinear
    sampling / mipmaps never blend sprite edges with black."""
    a = np.asarray(page, dtype=np.float64)
    known = a[..., 3] > 0
    rgb = a[..., :3] * known[..., None]
    for _ in range(iterations):
        acc = np.zeros_like(rgb)
        cnt = np.zeros(known.shape)
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                if dx or dy:
                    acc += np.roll(np.roll(rgb * known[..., None], dy, 0), dx, 1)
                    cnt += np.roll(np.roll(known, dy, 0), dx, 1)
        new = (~known) & (cnt > 0)
        rgb[new] = acc[new] / cnt[new][:, None]
        known |= new
    out = a.copy()
    out[..., :3] = np.where(a[..., 3:4] > 0, a[..., :3], rgb)
    return Image.fromarray(np.clip(np.round(out), 0, 255).astype(np.uint8), "RGBA")


def trim_box(img: Image.Image):
    a = np.asarray(img)[..., 3]
    ys, xs = np.where(a > 0)
    if len(xs) == 0:
        return 0, 0, 1, 1
    return int(xs.min()), int(ys.min()), int(xs.max() - xs.min() + 1), int(ys.max() - ys.min() + 1)


def skyline_pack(sizes, W, H, pad):
    """Skyline bottom-left packer.  Each rect occupies (w+pad, h+pad) so any two
    rects are >= pad texels apart; the page keeps a pad border too."""
    sky = [[pad, pad, W - pad]]  # segments: x, y, width
    pos = [None] * len(sizes)
    order = sorted(range(len(sizes)), key=lambda i: (-sizes[i][1], -sizes[i][0], i))
    for i in order:
        w, h = sizes[i][0] + pad, sizes[i][1] + pad
        best = None
        for si in range(len(sky)):
            x = sky[si][0]
            if x + w > W:
                break
            y, need, j = 0, w, si
            while need > 0 and j < len(sky):
                y = max(y, sky[j][1])
                need -= sky[j][2]
                j += 1
            if need > 0 or y + h > H:
                continue
            if best is None or (y + h, x) < (best[1] + h, best[0]):
                best = (x, y)
        if best is None:
            return None
        x, y = best
        pos[i] = (x, y)
        # update skyline
        new = [[x, y + h, w]]
        out = []
        for seg in sky:
            sx, sy, sw = seg
            if sx + sw <= x or sx >= x + w:
                out.append(seg)
                continue
            if sx < x:
                out.append([sx, sy, x - sx])
            if sx + sw > x + w:
                out.append([x + w, sy, sx + sw - (x + w)])
        out += new
        out.sort()
        merged = []
        for seg in out:
            if merged and merged[-1][1] == seg[1] and merged[-1][0] + merged[-1][2] == seg[0]:
                merged[-1][2] += seg[2]
            else:
                merged.append(seg)
        sky = merged
    return pos


def candidate_sizes(max_size, step=128):
    """Multiple-of-128 page sizes (block-compression friendly), aspect <= 2."""
    s = list(range(step, max_size + 1, step))
    pairs = [(w, h) for w in s for h in s if max(w, h) <= 2 * min(w, h)]
    return sorted(pairs, key=lambda t: (t[0] * t[1], -t[0]))


def pack_pages(sizes, max_size, pad):
    """Greedy: smallest page fitting everything; otherwise fill max pages."""
    remaining = list(range(len(sizes)))
    pages = []
    while remaining:
        sub = [sizes[i] for i in remaining]
        done = False
        for W, H in candidate_sizes(max_size):
            pos = skyline_pack(sub, W, H, pad)
            if pos is not None:
                pages.append(((W, H), {remaining[k]: pos[k] for k in range(len(sub))}))
                remaining = []
                done = True
                break
        if done:
            break
        # take the largest prefix that fits in a max page
        lo, hi = 1, len(sub)
        while lo < hi:
            mid = (lo + hi + 1) // 2
            if skyline_pack(sub[:mid], max_size, max_size, pad) is not None:
                lo = mid
            else:
                hi = mid - 1
        pos = skyline_pack(sub[:lo], max_size, max_size, pad)
        if pos is None:
            raise RuntimeError("sprite larger than max atlas size")
        pages.append(((max_size, max_size), {remaining[k]: pos[k] for k in range(lo)}))
        remaining = remaining[lo:]
    return pages


def build_group(root, entries, group, out_dir, pad, max_size, ppu):
    es = sorted([e for e in entries if e.get("group") == group and e.get("blend") != "additive"], key=lambda e: e["file"])
    if not es:
        return None
    imgs, trims = [], []
    for e in es:
        im = Image.open(os.path.join(root, e["file"])).convert("RGBA")
        imgs.append(im)
        trims.append(trim_box(im))
    sizes = [(t[2], t[3]) for t in trims]
    pages = pack_pages(sizes, max_size, pad)
    meta = {"schema": "forgebound.art.atlas/1", "group": group, "ppu": ppu, "padding": pad, "pages": [], "sprites": [], "clips": {}}
    for pi, ((W, H), placement) in enumerate(pages):
        page = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        name = f"fb_atlas_{group}" + (f"_{pi}" if len(pages) > 1 else "") + ".png"
        for idx, (x, y) in sorted(placement.items()):
            e, im, (tx, ty, tw, th) = es[idx], imgs[idx], trims[idx]
            page.paste(im.crop((tx, ty, tx + tw, ty + th)), (x, y))
            sw, sh = im.size
            px, py = e["pivot"]
            piv_x = (px * sw - tx) / tw
            piv_y = (py * sh - (sh - ty - th)) / th
            sname = os.path.splitext(os.path.basename(e["file"]))[0]
            meta["sprites"].append({
                "name": sname, "file": e["file"], "page": pi,
                "rect": [x, y, tw, th], "rect_unity": [x, H - y - th, tw, th],
                "pivot": [round(piv_x, 5), round(piv_y, 5)], "source_size": [sw, sh], "trim_offset": [tx, ty],
                "id": e["id"], "variant": e.get("variant"), "state": e["state"], "frame": e["frame"],
                "source_sha256": e.get("sha256"),
            })
            if e.get("frame_count", 1) > 1:
                key = f"{e['id']}/{e['state']}"
                clip = meta["clips"].setdefault(key, {"fps": e["fps"], "loop": e.get("loop", False), "frame_count": e["frame_count"],
                                                      "frames": [None] * e["frame_count"]})
                clip["frames"][e["frame"]] = sname
        page = bleed_page(page)
        page.save(os.path.join(out_dir, name), optimize=True)
        meta["pages"].append({"index": pi, "image": name, "size": [W, H],
                              "sha256": hashlib.sha256(open(os.path.join(out_dir, name), "rb").read()).hexdigest()})
    for clip in meta["clips"].values():
        clip["duration_s"] = round(clip["frame_count"] / clip["fps"], 4)
    with open(os.path.join(out_dir, f"fb_atlas_{group}.json"), "w") as fh:
        json.dump(meta, fh, indent=1)
    return meta


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--root", default=DEFAULT_ROOT)
    ap.add_argument("--groups", nargs="*", default=DEFAULT_GROUPS)
    ap.add_argument("--padding", type=int, default=4)
    ap.add_argument("--max-size", type=int, default=2048)
    args = ap.parse_args(argv)
    man = json.load(open(os.path.join(args.root, "manifest.json")))
    out_dir = os.path.join(args.root, "atlases")
    os.makedirs(out_dir, exist_ok=True)
    for g in args.groups:
        for fn in os.listdir(out_dir):
            if fn.startswith(f"fb_atlas_{g}.") or fn.startswith(f"fb_atlas_{g}_"):
                os.remove(os.path.join(out_dir, fn))
        m = build_group(args.root, man["files"], g, out_dir, args.padding, args.max_size, man.get("ppu", 100))
        if m is None:
            print(f"group {g}: no sprites", file=sys.stderr)
            continue
        used = sum(s["rect"][2] * s["rect"][3] for s in m["sprites"])
        area = sum(p["size"][0] * p["size"][1] for p in m["pages"])
        print(f"group {g:16s} sprites={len(m['sprites']):4d} pages={[tuple(p['size']) for p in m['pages']]} efficiency={used / area:.0%}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
