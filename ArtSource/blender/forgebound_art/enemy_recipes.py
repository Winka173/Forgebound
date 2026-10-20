"""Per-enemy kit recipes (pure python, no bpy).

Every non-legacy enemy of Content/enemies.json is assembled by enemykit.py
from:

* a FACTION KIT  (palette.FACTION_KITS: body / armour / cloth / accent / glow
  materials + faction motif details),
* a ROLE ARCHETYPE that fixes the silhouette class (body plan, size, stance),
* a short list of PARTS that make the enemy distinct inside its faction.

ROLE_DEFAULTS gives the archetype; RECIPES overrides it per enemy id.  Bosses
are unique part combinations at boss scale (1.35-1.6) with boss-only parts.
The validator's near-duplicate check (pHash + changed-texel fraction) runs over
all enemy frames, so two enemies may share a plan but never plan + parts.

Body plans: biped (humanoid rig), quad (4 legs + tail), blob (squash rig),
wing (bat-like flyer), wisp (floating ghost / orb), serpent (segment chain),
dummy (training post).
"""
from __future__ import annotations

from . import content

# role -> archetype (plan, scale, build, stance)
ROLE_DEFAULTS = {
    "swarm":    {"plan": "quad", "scale": 0.72, "build": "normal"},
    "tank":     {"plan": "biped", "scale": 1.18, "build": "bulky"},
    "melee":    {"plan": "biped", "scale": 1.0, "build": "normal"},
    "ranged":   {"plan": "biped", "scale": 0.98, "build": "slim"},
    "support":  {"plan": "biped", "scale": 1.0, "build": "robe"},
    "healer":   {"plan": "biped", "scale": 0.98, "build": "robe"},
    "flyer":    {"plan": "wisp", "scale": 0.9, "flying": True},
    "assassin": {"plan": "biped", "scale": 0.98, "build": "slim", "hunch": 18},
    "bruiser":  {"plan": "biped", "scale": 1.15, "build": "bulky", "hunch": 14},
    "summoner": {"plan": "biped", "scale": 1.0, "build": "robe"},
    "splitter": {"plan": "blob", "scale": 0.9},
    "elite":    {"plan": "biped", "scale": 1.3, "build": "bulky"},
    "boss":     {"plan": "biped", "scale": 1.5, "build": "bulky"},
    "training": {"plan": "dummy", "scale": 1.0},
}

# Parts vocabulary (see enemykit.PARTS): head:<kind>, held:<kind>, offhand:<kind>, back:<kind>,
# shoulder:<kind>, top:<kind>, aura:<kind>, extra:<kind>.
RECIPES = {
    # ---------------------------------------------------------------- slagborn
    "cinder_imp":          {"plan": "biped", "scale": 0.84, "build": "imp", "parts": ["head:imp", "back:ember_wings", "extra:magma_belly", "extra:tail"]},
    "slag_brute":          {"parts": ["head:brute_horns", "held:slag_slab", "shoulder:magma_rocks", "extra:magma_cracks"]},
    "forge_bandit":        {"parts": ["head:bandit_mask", "held:cleaver", "offhand:buckler", "back:scarf"]},
    "ember_slinger":       {"parts": ["head:hood", "held:sling", "back:ember_pouch", "extra:magma_cracks"]},
    "bellows_tender":      {"build": "robe", "parts": ["head:goggle_cap", "held:brazier_staff", "back:bellows_pack", "aura:ember_ring"]},
    "ash_wisp":            {"plan": "wisp", "scale": 1.1, "parts": ["body:ember_core", "extra:ash_cloud", "extra:ember_sparks"]},
    "slag_hound":          {"plan": "quad", "scale": 1.0, "parts": ["head:hound", "back:magma_spine", "extra:magma_cracks"]},
    "molten_foreman":      {"parts": ["head:foreman_helm", "held:great_hammer", "back:chimneys", "extra:magma_cracks"]},
    "slag_titan":          {"plan": "biped", "scale": 1.55, "build": "golem", "parts": ["head:titan_maw", "back:chimneys_big", "extra:lava_core",
                                                                                           "shoulder:magma_rocks", "extra:magma_cracks"]},
    "kaldur_forge_tyrant": {"plan": "biped", "scale": 1.45, "build": "bulky", "parts": ["head:anvil_crown", "held:forge_maul", "back:royal_cape",
                                                                                       "shoulder:plate_spikes", "aura:ember_ring"]},
    # -------------------------------------------------------------- overgrowth
    "vine_crawler":        {"plan": "quad", "scale": 1.15, "build": "crawler", "parts": ["head:bud", "back:leaf_frill", "extra:vine_legs"]},
    "spore_shaman":        {"parts": ["head:mushroom", "held:spore_staff", "aura:spores"]},
    "thorn_beast":         {"plan": "quad", "scale": 1.12, "build": "beast", "parts": ["head:beast_horns", "back:thorns", "extra:moss"]},
    "moss_golem":          {"build": "golem", "scale": 1.18, "parts": ["head:stone_block", "back:moss_mound", "extra:flowers", "shoulder:mossy_rocks"]},
    "seed_pod":            {"scale": 1.1, "parts": ["body:pod", "top:sprout", "extra:seeds"]},
    "briar_stalker":       {"parts": ["head:thorn_hood", "held:thorn_blade", "offhand:thorn_blade", "back:leaf_cape"]},
    "mire_toad":           {"plan": "quad", "scale": 1.15, "build": "toad", "parts": ["head:toad", "back:lily", "extra:mire_drip"]},
    "elder_treant":        {"plan": "biped", "build": "tree", "scale": 1.32, "parts": ["head:treant", "back:branches", "extra:moss", "held:root_club"]},
    "rootmother":          {"plan": "biped", "build": "robe", "scale": 1.5, "parts": ["head:bloom_crown", "back:great_branches", "extra:root_skirt",
                                                                                     "held:life_staff", "aura:spores"]},
    "hollow_bloom":        {"plan": "blob", "scale": 1.45, "build": "flower", "parts": ["body:bloom", "top:fanged_petals", "extra:vine_tentacles"]},
    # -------------------------------------------------------------- rime_court
    "frost_wraith":        {"plan": "wisp", "scale": 1.15, "parts": ["body:wraith", "head:ice_crown_small", "extra:ice_chains"]},
    "rime_archer":         {"parts": ["head:winged_helm", "held:ice_bow", "back:quiver"]},
    "glacier_knight":      {"parts": ["head:great_helm", "held:ice_sword", "offhand:ice_tower_shield", "shoulder:ice_spikes"]},
    "ice_mauler":          {"plan": "biped", "build": "ape", "parts": ["head:beast", "extra:ice_fists", "back:ice_spikes", "extra:fur"]},
    "frost_priest":        {"parts": ["head:mitre", "held:crystal_staff", "aura:halo"]},
    "shardling":           {"plan": "blob", "scale": 1.05, "build": "crystal", "parts": ["body:crystal_cluster", "extra:ice_eyes"]},
    "snow_sprite":         {"plan": "wing", "scale": 1.75, "build": "sprite", "parts": ["head:sprite", "back:ice_wings", "aura:snowflakes"]},
    "rime_countess":       {"build": "robe", "scale": 1.24, "parts": ["head:tiara", "held:ice_fan", "back:frost_collar", "extra:gown"]},
    "frost_sovereign":     {"plan": "biped", "build": "bulky", "scale": 1.5, "parts": ["head:ice_crown", "held:frost_scepter", "back:royal_cape_ice",
                                                                                      "shoulder:ice_spikes", "aura:snowflakes"]},
    "glass_wyrm":          {"plan": "serpent", "scale": 1.35, "flying": True, "parts": ["head:wyrm", "back:crystal_fins", "extra:wyrm_wings"]},
    # ------------------------------------------------------------- hollow_dead
    "bone_archer":         {"build": "skeleton", "parts": ["head:skull_hood", "held:bone_bow", "back:quiver"]},
    "ghoul":               {"build": "ghoul", "parts": ["head:ghoul", "extra:claws", "extra:rags"]},
    "necromancer":         {"parts": ["head:skull_cowl", "held:skull_staff", "aura:skull_orbit"]},
    "crypt_bat":           {"plan": "wing", "scale": 1.2, "parts": ["head:bat_skull", "extra:ghost_trail"]},
    "grave_warden":        {"build": "skeleton_bulky", "parts": ["head:skull_helm", "held:rusty_halberd", "offhand:tombstone_shield"]},
    "grave_crawler":       {"plan": "quad", "scale": 1.1, "build": "crawler", "parts": ["head:skull", "back:ribcage", "extra:bone_legs"]},
    "bone_colossus":       {"build": "skeleton_bulky", "scale": 1.38, "parts": ["head:horned_skull", "extra:bone_club_arm", "back:spine_spikes",
                                                                                "shoulder:skull_pauldrons"]},
    "lich_regent":         {"plan": "wisp", "scale": 1.45, "build": "lich", "flying": True, "parts": ["body:lich", "head:lich_crown", "held:phylactery",
                                                                                                    "aura:soul_orbs"]},
    "drowned_choir":       {"plan": "wisp", "scale": 1.4, "build": "choir", "flying": True, "parts": ["body:choir", "extra:bell", "aura:drips"]},
    # ----------------------------------------------------------- tempest_order
    "storm_sentinel":      {"build": "construct", "parts": ["head:lens_dome", "offhand:mirror_shield", "shoulder:tesla_pylons"]},
    "arc_mage":            {"parts": ["head:tall_hood", "held:bolt_staff", "aura:sparks"]},
    "clockwork_drone":     {"plan": "wisp", "scale": 1.2, "build": "drone", "parts": ["body:drone", "top:rotor", "extra:lens_eye"]},
    "sky_lancer":          {"parts": ["head:plumed_helm", "held:lance", "back:brass_wings"]},
    "aegis_construct":     {"build": "construct", "scale": 1.05, "parts": ["head:dome_eye", "held:projector", "aura:shield_ring"]},
    "thunder_hound":       {"plan": "quad", "scale": 1.0, "build": "mech", "parts": ["head:mech_hound", "back:coil_spine", "extra:sparks"]},
    "tempest_adept":       {"parts": ["head:storm_hood", "held:dagger", "offhand:dagger", "back:sash"]},
    "static_wisp":         {"plan": "wisp", "scale": 1.15, "parts": ["body:static_core", "extra:arc_rings"]},
    "iron_conscript":      {"build": "normal", "scale": 1.05, "parts": ["head:kettle_helm", "held:pike", "offhand:kite_shield"]},
    "storm_paragon":       {"parts": ["head:halo_helm", "held:storm_spear", "back:brass_wings", "aura:halo"]},
    "storm_tyrant":        {"plan": "biped", "build": "bulky", "scale": 1.5, "parts": ["head:storm_crown", "held:thunder_glaive", "back:storm_cape",
                                                                                      "shoulder:tesla_pylons", "aura:storm_rings"]},
    "clockwork_colossus":  {"plan": "biped", "build": "construct", "scale": 1.58, "parts": ["head:colossus_head", "back:gear_array", "extra:piston_arms",
                                                                                           "shoulder:brass_domes"]},
    # ---------------------------------------------------------------- training
    "dummy_single":        {"plan": "dummy", "parts": []},
    "dummy_swarm":         {"plan": "dummy", "build": "swarm", "parts": ["extra:mini_dummies"]},
    "dummy_armored":       {"plan": "dummy", "parts": ["extra:dummy_plate", "head:dummy_bucket"]},
    "dummy_shielded":      {"plan": "dummy", "parts": ["offhand:dummy_shield", "aura:shield_ring"]},
    "dummy_resistant":     {"plan": "dummy", "parts": ["extra:rune_paint", "aura:rune_circle"]},
}


def recipe(e):
    """Merge role defaults with the per-enemy recipe."""
    role = e.get("role") or "melee"
    if e.get("rank") == "boss":
        role = "boss"
    base = dict(ROLE_DEFAULTS.get(role, ROLE_DEFAULTS["melee"]))
    base.update(RECIPES.get(e["id"], {}))
    if e.get("flying"):
        base["flying"] = True
    base.setdefault("parts", [])
    base["role"] = role
    base["kit_parts"] = list(base["parts"])
    return base


def all_recipes():
    return {e["id"]: recipe(e) for e in content.load("enemies")}
