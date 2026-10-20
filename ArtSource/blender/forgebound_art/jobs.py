"""Enumerates every render job of a full build (pure python).

A job is a dict consumed by worker.py; `outputs` describes the final files the
post-processor will write for it (category / id / variant / state / frames).
"""
from __future__ import annotations

from . import characters_meta as CM
from . import spec


def _weapon(frame, core, tier, legendary, preset, glow=False):
    variant = f"{core}-{'leg' if legendary else 't%d' % tier}"
    state = "icon" if preset == "weapon_icon" else "ingame"
    name = f"weapon_{frame}_{variant}_{state}"
    return {
        "category": "weapon", "name": name, "frame": frame, "core": core, "tier": tier, "legendary": legendary,
        "preset": preset, "state": state, "glow": glow, "seed": spec.stable_seed("weapon", frame, core, tier, legendary),
        "asset_id": frame, "variant": variant, "function": "forgebound_art.weapons.build_weapon",
        "group": f"weapon_{state}",
    }


def all_jobs(scope="full"):
    jobs = []
    frames = spec.FRAMES
    cores = spec.CORES
    if scope == "smoke":
        frames, cores = ["sword", "staff"], ["fire", "frost"]
    # tier-1 matrix: every Frame x every Core, icon + in-game sprite
    for f in frames:
        for c in cores:
            jobs.append(_weapon(f, c, 1, False, "weapon_icon"))
            jobs.append(_weapon(f, c, 1, False, "weapon_ingame"))
    # benchmark subset: tier 3 + legendary (with additive glow layer)
    for f in [x for x in spec.BENCHMARK_FRAMES if x in frames]:
        for c in cores:
            jobs.append(_weapon(f, c, 3, False, "weapon_icon"))
            jobs.append(_weapon(f, c, 3, True, "weapon_icon", glow=True))
    pieces = spec.BOARD_PIECES if scope != "smoke" else spec.BOARD_PIECES[:2]
    for i, p in enumerate(pieces):
        jobs.append({"category": "board", "name": f"board_{p}", "piece": p, "yaw": 20, "seed": spec.stable_seed("board", p),
                     "asset_id": p, "variant": None, "function": "forgebound_art.board.build_piece", "group": "board_icon"})
    for a in spec.CHARACTERS:
        for clip in spec.REQUIRED_CLIPS["char"]:
            jobs.append({"category": "char", "name": f"char_{a}_{clip}", "asset": a, "clip": clip, "seed": spec.stable_seed("char", a),
                         "asset_id": a, "variant": None, "function": "forgebound_art.characters.build+apply_action",
                         "grounded": CM.GROUNDED[a], "group": f"char_{a}"})
    enemies = spec.ENEMIES if scope != "smoke" else spec.ENEMIES[:1]
    for e in enemies:
        for clip in spec.REQUIRED_CLIPS["enemy"]:
            jobs.append({"category": "enemy", "name": f"enemy_{e}_{clip}", "asset": e, "clip": clip, "seed": spec.stable_seed("enemy", e),
                         "asset_id": e, "variant": None, "function": "forgebound_art.characters.build+apply_action",
                         "grounded": CM.GROUNDED[e], "group": "enemy"})
    return jobs
