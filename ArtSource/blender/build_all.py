#!/usr/bin/env python3
"""Forgebound art build CLI (run with system python 3 + Pillow + numpy).

    python3 ArtSource/blender/build_all.py                 # full build
    python3 ArtSource/blender/build_all.py --scope smoke   # tiny subset
    python3 ArtSource/blender/build_all.py --only weapon board --workers 4
    python3 ArtSource/blender/build_all.py --post-only     # re-run post step from cache
    python3 ArtSource/blender/build_all.py --only enemy --match 'slag|cinder'   # iterate on a few jobs

Spawns headless Blender workers (Cycles CPU) that render raw supersampled PNGs
into ArtSource/renders_cache/, then post-processes them into
Assets/Forgebound/Resources/Art/Generated/<category>/... and writes manifest.json.
"""
from __future__ import annotations

import argparse
import datetime as _dt
import json
import os
import shutil
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, HERE)

from forgebound_art import jobs as J  # noqa: E402
from forgebound_art import postprocess as P  # noqa: E402
from forgebound_art import spec  # noqa: E402

OUT_DEFAULT = os.path.join(REPO, "Assets", "Forgebound", "Resources", "Art", "Generated")
CACHE_DEFAULT = os.path.join(REPO, "ArtSource", "renders_cache")
WORKER = os.path.join(HERE, "forgebound_art", "worker.py")


def blender_version(blender):
    try:
        out = subprocess.run([blender, "--version"], capture_output=True, text=True, timeout=60).stdout
        return out.splitlines()[0].strip()
    except Exception:
        return "unknown"


def git_commit():
    try:
        r = subprocess.run(["git", "-C", REPO, "rev-parse", "HEAD"], capture_output=True, text=True, timeout=20)
        if r.returncode == 0:
            return r.stdout.strip()
    except Exception:
        pass
    return None


JOB_COST = {"env": 40.0, "char": 14.0, "enemy": 12.0, "weapon": 1.0}


def run_workers(jobs, workers, blender, cache, log_dir):
    chunks = [[] for _ in range(workers)]
    load = [0.0] * workers
    # longest-processing-time-first: heavy (animated / background) jobs spread evenly over workers
    for j in sorted(jobs, key=lambda j: (-JOB_COST.get(j["category"], 1.0), j["name"])):
        k = min(range(workers), key=lambda i: load[i])
        chunks[k].append(j)
        load[k] += JOB_COST.get(j["category"], 1.0)
    threads = max(1, (os.cpu_count() or 4) // workers)
    procs = []
    for i, chunk in enumerate(chunks):
        if not chunk:
            continue
        jf = os.path.join(cache, f"_jobs_{i}.json")
        rf = os.path.join(cache, f"_results_{i}.json")
        with open(jf, "w") as fh:
            json.dump(chunk, fh)
        log = open(os.path.join(log_dir, f"worker_{i}.log"), "w")
        cmd = [blender, "-b", "--factory-startup", "-noaudio", "-P", WORKER, "--", "--jobs", jf, "--out", rf,
               "--cache", os.path.join(cache, "raw"), "--threads", str(threads)]
        procs.append((subprocess.Popen(cmd, stdout=log, stderr=subprocess.STDOUT, cwd=REPO), rf, log))
    results = []
    failed = False
    for p, rf, log in procs:
        rc = p.wait()
        log.close()
        if rc != 0 or not os.path.exists(rf):
            print(f"worker failed rc={rc}; see {log.name}", file=sys.stderr)
            failed = True
            continue
        results.extend(json.load(open(rf)))
    return results, failed


def final_relpath(job, img):
    cat = job["category"]
    state = img["state"]
    fname = spec.asset_filename(cat, job["asset_id"], job.get("variant"), state, img["frame"])
    if cat == "weapon":
        sub = os.path.join("weapon", state)
    elif cat == "board":
        sub = os.path.join("board", "icon")
    elif cat in spec.ITEM_CATEGORIES:
        sub = os.path.join(cat, "icon")
    elif cat == "env":
        sub = "env"
    else:
        sub = os.path.join(cat, job["asset_id"])
    return os.path.join(sub, fname).replace(os.sep, "/")


def postprocess(results, out_dir, meta):
    entries = []
    for res in results:
        if not res.get("ok"):
            continue
        job = res["job"]
        n_frames = len([i for i in res["images"] if i.get("post") != "glow"])
        for img in res["images"]:
            rel = final_relpath(job, img)
            dst = os.path.join(out_dir, rel)
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            if img.get("post") == "glow":
                im = P.process_glow(img["raw"], spec.SUPERSAMPLE)
            elif img.get("post") == "opaque":
                im = P.process_opaque(img["raw"], spec.SUPERSAMPLE)
            else:
                im = P.process_sprite(img["raw"], spec.SUPERSAMPLE)
            P.save_png(im, dst)
            state = img["state"]
            clip = spec.CLIPS.get(state)
            params = {k: v for k, v in job.items() if k not in ("name", "function", "group", "glyph", "data", "recipe")}
            if job.get("recipe"):
                params["recipe"] = {k: v for k, v in job["recipe"].items() if k in ("plan", "scale", "kit_parts")}
            entries.append({
                "file": rel,
                "id": job["asset_id"],
                "category": job["category"],
                "variant": job.get("variant"),
                "state": state,
                "frame": img["frame"],
                "frame_count": clip[0] if clip else 1,
                "fps": clip[1] if clip else 0,
                "loop": clip[2] if clip else False,
                "size": list(im.size),
                "pivot": [round(float(v), 4) for v in img["pivot"]],
                "ppu": spec.PPU,
                "blend": {"glow": "additive", "opaque": "opaque"}.get(img.get("post"), "alpha"),
                "grounded": job.get("grounded"),
                "group": job.get("group"),
                "core": job.get("core"),
                "tier": job.get("tier"),
                "legendary": job.get("legendary"),
                "source": {"script": "ArtSource/blender/forgebound_art/worker.py", "function": job["function"],
                           "postprocess": "ArtSource/blender/forgebound_art/postprocess.py:" +
                                          ("process_glow" if img.get("post") == "glow" else "process_sprite")},
                "parameters": params,
                "seed": job["seed"],
                "render_seconds": round(res["seconds"] / max(1, n_frames), 3),
                "blender_version": meta["blender_version"],
                "pipeline_version": spec.PIPELINE_VERSION,
                "git_commit": meta["git_commit"],
                "license": spec.LICENSE,
                "sha256": P.sha256_file(dst),
            })
    return entries


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--scope", default="full", choices=["full", "smoke"])
    ap.add_argument("--only", nargs="*", default=None,
                    help="categories: weapon board item (=core modifier rune blessing) char enemy env")
    ap.add_argument("--match", default=None, help="regex on job names; renders only matching jobs and keeps other outputs")
    ap.add_argument("--workers", type=int, default=4)
    ap.add_argument("--blender", default=os.environ.get("BLENDER", "blender"))
    ap.add_argument("--out", default=OUT_DEFAULT)
    ap.add_argument("--cache", default=CACHE_DEFAULT)
    ap.add_argument("--post-only", action="store_true", help="skip rendering, reuse cached worker results")
    args = ap.parse_args()

    only = None
    if args.only:
        only = set(args.only)
        if "item" in only:
            only |= set(spec.ITEM_CATEGORIES)
    jobs = J.all_jobs(args.scope, only)
    if only:
        jobs = [j for j in jobs if j["category"] in only]
    if args.match:
        import re
        rx = re.compile(args.match)
        jobs = [j for j in jobs if rx.search(j["name"])]
    os.makedirs(args.cache, exist_ok=True)
    os.makedirs(os.path.join(args.cache, "raw"), exist_ok=True)
    log_dir = os.path.join(args.cache, "logs")
    os.makedirs(log_dir, exist_ok=True)
    meta = {"blender_version": blender_version(args.blender), "git_commit": git_commit()}

    t0 = time.time()
    if args.post_only:
        results = []
        for f in sorted(os.listdir(args.cache)):
            if f.startswith("_results_"):
                results.extend(json.load(open(os.path.join(args.cache, f))))
        names = {j["name"] for j in jobs}
        results = [r for r in results if r["job"]["name"] in names]
        failed = False
    else:
        for f in os.listdir(args.cache):
            if f.startswith("_results_") or f.startswith("_jobs_"):
                os.remove(os.path.join(args.cache, f))
        results, failed = run_workers(jobs, args.workers, args.blender, args.cache, log_dir)
    t_render = time.time() - t0

    cats = sorted({j["category"] for j in jobs})
    if not args.match:
        for c in cats:  # remove stale outputs of the categories we rebuild
            d = os.path.join(args.out, c)
            if os.path.isdir(d):
                shutil.rmtree(d)
    t1 = time.time()
    entries = postprocess(results, args.out, meta)
    t_post = time.time() - t1

    man_path = os.path.join(args.out, "manifest.json")
    old = []
    if os.path.exists(man_path) and (args.only or args.match):
        new_files = {e["file"] for e in entries}
        old = [e for e in json.load(open(man_path)).get("files", [])
               if (e["category"] not in cats or args.match) and e["file"] not in new_files]
    all_entries = sorted(old + entries, key=lambda e: e["file"])
    manifest = {
        "schema": "forgebound.art.manifest/1",
        "pipeline_version": spec.PIPELINE_VERSION,
        "generated_utc": _dt.datetime.now(_dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "blender_version": meta["blender_version"],
        "git_commit": meta["git_commit"],
        "ppu": spec.PPU,
        "build": {"scope": args.scope, "only": args.only, "match": args.match, "workers": args.workers,
                  "render_seconds": round(t_render, 1), "post_seconds": round(t_post, 1),
                  "jobs": len(jobs), "jobs_failed": sum(1 for r in results if not r.get("ok")) + (len(jobs) - len(results))},
        "license": spec.LICENSE,
        "files": all_entries,
    }
    with open(man_path, "w") as fh:
        json.dump(manifest, fh, indent=1)
    per_cat = {}
    for r in results:
        c = r["job"]["category"]
        per_cat.setdefault(c, [0, 0.0])
        per_cat[c][0] += len(r["images"])
        per_cat[c][1] += r["seconds"]
    print(f"render wall {t_render:.1f}s  post {t_post:.1f}s  files {len(entries)}  (manifest {len(all_entries)})")
    for c, (n, s) in sorted(per_cat.items()):
        print(f"  {c:7s} images={n:4d} worker-seconds={s:7.1f}")
    bad = [r["job"]["name"] for r in results if not r.get("ok")]
    if bad or failed:
        print("FAILED jobs:", bad, file=sys.stderr)
        sys.exit(1)


if __name__ == "__main__":
    main()
