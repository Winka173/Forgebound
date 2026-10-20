"""Enumerates every render job of a full build (pure python).

A job is a dict consumed by worker.py; `outputs` describes the final files the
post-processor will write for it (category / id / variant / state / frames).
Content-driven ids (pieces, forgemasters, items, enemies, biomes) come from
Assets/Forgebound/Resources/Content via content.py.
"""
from __future__ import annotations

from collections import defaultdict

from . import characters_meta as CM
from . import content, spec, symbols


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


def weapon_jobs(scope):
    jobs = []
    frames, cores = spec.FRAMES, spec.CORES
    if scope == "smoke":
        frames, cores = ["sword", "staff"], ["fire", "frost"]
    for f in frames:
        for c in cores:
            jobs.append(_weapon(f, c, 1, False, "weapon_icon"))
            jobs.append(_weapon(f, c, 1, False, "weapon_ingame"))
    for f in [x for x in spec.BENCHMARK_FRAMES if x in frames]:
        for c in cores:
            jobs.append(_weapon(f, c, 3, False, "weapon_icon"))
            jobs.append(_weapon(f, c, 3, True, "weapon_icon", glow=True))
    return jobs


def board_jobs(scope):
    pieces = content.load("pieces")
    if scope == "smoke":
        pieces = pieces[:2]
    return [{"category": "board", "name": f"board_{p['id']}", "piece": p["id"], "data": p, "seed": spec.stable_seed("board", p["id"]),
             "asset_id": p["id"], "variant": None, "function": "forgebound_art.board.build_piece", "group": "board_icon"} for p in pieces]


def _icon(cat, iid, **kw):
    j = {"category": cat, "name": f"{cat}_{iid}_icon", "state": "icon", "seed": spec.stable_seed(cat, iid), "asset_id": iid,
         "variant": None, "function": f"forgebound_art.icons.build_{cat}", "group": f"{cat}_icon"}
    j.update(kw)
    return j


def item_jobs(scope):
    jobs = []
    cores = content.load("cores")
    mods = content.load("modifiers")
    runes = content.load("runes")
    bless = content.load("blessings")
    if scope == "smoke":
        cores, mods, runes, bless = cores[:1], mods[:1], runes[:1], bless[:1]
    for c in cores:
        jobs.append(_icon("core", c["id"], core=c["id"]))
    for m in mods:
        jobs.append(_icon("modifier", m["id"], stage=m["stage"], tags=m.get("tags", []), rarity=m.get("rarity")))
    glyphs = symbols.all_rune_glyphs(content.ids("runes"))
    for r in runes:
        jobs.append(_icon("rune", r["id"], trigger=r["trigger"], rarity=r.get("rarity"), glyph=glyphs[r["id"]]))
    # blessings: siblings that would share symbol+secondary+family+frame get a hue step (variant_index)
    designs = {}
    groups = defaultdict(list)
    for b in content.load("blessings"):
        sym, sec, fam = symbols.blessing_design(b)
        curse = bool(b.get("curse"))
        key = (sym, sec, fam, curse)
        designs[b["id"]] = (sym, sec, fam, curse, len(groups[key]))
        groups[key].append(b["id"])
    for b in bless:
        sym, sec, fam, curse, vi = designs[b["id"]]
        jobs.append(_icon("blessing", b["id"], symbol=sym, secondary=sec, family=fam, curse=curse, variant_index=vi,
                          trial="challenge_only" in (b.get("tags") or []), rarity=b.get("rarity")))
    return jobs


def char_jobs(scope):
    ids = content.ids("forgemasters") if scope != "smoke" else spec.CHARACTERS
    jobs = []
    for a in ids:
        for clip in spec.REQUIRED_CLIPS["char"]:
            jobs.append({"category": "char", "name": f"char_{a}_{clip}", "asset": a, "clip": clip, "seed": spec.stable_seed("char", a),
                         "asset_id": a, "variant": None, "function": "forgebound_art.characters.build+apply_action",
                         "grounded": True, "group": f"char_{a}"})
    return jobs


def enemy_jobs(scope):
    jobs = []
    legacy = spec.ENEMIES if scope != "smoke" else spec.ENEMIES[:1]
    for e in legacy:
        jobs.append({"category": "enemy", "name": f"enemy_{e}_idle", "asset": e, "clip": "idle", "seed": spec.stable_seed("enemy", e),
                     "asset_id": e, "variant": None, "function": "forgebound_art.characters.build+apply_action",
                     "grounded": CM.GROUNDED[e], "group": "enemy"})
    if scope == "smoke":
        return jobs
    from . import enemy_recipes as ER
    for e in content.load("enemies"):
        if e["id"] in legacy:
            continue  # 'skeleton' is both a legacy archetype and a content enemy: one sprite set
        rec = ER.recipe(e)
        fac = content.enemy_faction(e)
        jobs.append({"category": "enemy", "name": f"enemy_{e['id']}_idle", "asset": e["id"], "clip": "idle",
                     "seed": spec.stable_seed("enemy", e["id"]), "asset_id": e["id"], "variant": None,
                     "function": "forgebound_art.enemykit.build+animate", "kit": True, "faction": fac, "role": e.get("role"),
                     "rank": e.get("rank"), "recipe": rec, "grounded": not rec.get("flying", False),
                     "group": "enemy_boss" if e.get("rank") == "boss" else f"enemy_{fac}"})
    return jobs


def env_jobs(scope):
    biomes = content.load("biomes")
    if scope == "smoke":
        biomes = biomes[:1]
    return [{"category": "env", "name": f"env_{b['id']}_bg", "biome": b["id"], "palette": b.get("palette"), "state": "bg",
             "seed": spec.stable_seed("env", b["id"]), "asset_id": b["id"], "variant": None,
             "function": "forgebound_art.environments.build_env", "group": None} for b in biomes]


CATEGORY_JOBS = {"weapon": weapon_jobs, "board": board_jobs, "item": item_jobs, "char": char_jobs, "enemy": enemy_jobs, "env": env_jobs}


def all_jobs(scope="full", categories=None):
    """categories: optional set of job categories (core/modifier/rune/blessing map to the 'item' builder)."""
    jobs = []
    for key, fn in CATEGORY_JOBS.items():
        keys = set(spec.ITEM_CATEGORIES) if key == "item" else {key}
        if categories is None or keys & set(categories):
            jobs += fn(scope)
    return jobs
