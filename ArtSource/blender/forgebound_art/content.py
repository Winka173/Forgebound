"""Read the game content JSON (Assets/Forgebound/Resources/Content/*.json).

Pure python (no bpy).  The content files allow whole-line `//` comments, which
are stripped before parsing.  Only ids and the few fields that drive visual
choices (silhouette, stage, trigger, stats, faction tags, palette) are used.
"""
from __future__ import annotations

import json
import os

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
CONTENT_DIR = os.path.join(REPO, "Assets", "Forgebound", "Resources", "Content")

_CACHE = {}


def load(name: str):
    if name not in _CACHE:
        with open(os.path.join(CONTENT_DIR, name + ".json"), encoding="utf-8") as fh:
            text = "\n".join(line for line in fh.read().splitlines() if not line.strip().startswith("//"))
        _CACHE[name] = json.loads(text)["items"]
    return _CACHE[name]


def ids(name: str):
    return [x["id"] for x in load(name)]


def by_id(name: str):
    return {x["id"]: x for x in load(name)}


FACTIONS = ["slagborn", "overgrowth", "rime_court", "hollow_dead", "tempest_order"]


def enemy_faction(e):
    for t in e.get("tags", []):
        if t in FACTIONS:
            return t
    return "training" if "training" in e.get("tags", []) else None
