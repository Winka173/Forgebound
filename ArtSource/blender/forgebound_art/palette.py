"""Controlled palette: Core colours and base material swatches (sRGB hex).
Pure python.  Blender materials convert these to linear via srgb_to_linear.
"""
from __future__ import annotations

# Each Core: primary (gem / inlay body), secondary (accent), dark (shadow side),
# glow (emissive core of runes / VFX), plus shape language used for gems and
# legendary crowns so cores are distinguishable without colour (accessibility).
CORE_PALETTES = {
    "fire":      {"primary": "#F05A28", "secondary": "#FFC53D", "dark": "#6E1A0E", "glow": "#FFB347", "gem": "flame",    "crown": "flames"},
    "frost":     {"primary": "#8FD8FF", "secondary": "#FFFFFF", "dark": "#2A5C9E", "glow": "#D8F7FF", "gem": "crystal",  "crown": "ice_shards"},
    "lightning": {"primary": "#FFE83A", "secondary": "#7B61FF", "dark": "#3B2A8A", "glow": "#FFF7A0", "gem": "bolt",     "crown": "bolts"},
    "earth":     {"primary": "#B5844E", "secondary": "#6FA35A", "dark": "#4A3220", "glow": "#E8C27A", "gem": "boulder",  "crown": "rocks"},
    "life":      {"primary": "#3BD66A", "secondary": "#F5F0A0", "dark": "#14603A", "glow": "#A8FFC0", "gem": "leaf",     "crown": "leaves"},
    "shadow":    {"primary": "#6B5C9A", "secondary": "#1A1028", "dark": "#22183A", "glow": "#B4A0FF", "gem": "crescent", "crown": "thorns"},
    "venom":     {"primary": "#A6E22E", "secondary": "#4E7A1A", "dark": "#2B3A10", "glow": "#D4FF5A", "gem": "drop",     "crown": "fangs"},
    "arcane":    {"primary": "#4D6BFF", "secondary": "#4DE8FF", "dark": "#1A2470", "glow": "#A8C4FF", "gem": "star",     "crown": "rune_rings"},
    "wind":      {"primary": "#9FF0DC", "secondary": "#FFFFFF", "dark": "#2E7A72", "glow": "#E6FFF8", "gem": "swirl",    "crown": "gusts"},
    "steel":     {"primary": "#A8B8CC", "secondary": "#DDE6F0", "dark": "#3A4658", "glow": "#E8F0FF", "gem": "ingot",    "crown": "blades"},
    "solar":     {"primary": "#FFC93D", "secondary": "#FFF4D0", "dark": "#9A5A10", "glow": "#FFFBE0", "gem": "sun",      "crown": "halo"},
    "void":      {"primary": "#3A1450", "secondary": "#FF3DCB", "dark": "#0E0414", "glow": "#FF5AD8", "gem": "void_orb", "crown": "rifts"},
}

# Base material swatches: base, shadow (hue-shifted, painted), highlight.
MATERIALS = {
    "forged_steel": {"base": "#B4BECA", "shadow": "#434C62", "highlight": "#F2F6FF", "metal": 1.0, "spec": 0.9, "spec_size": 0.06, "paint": 0.18, "flat": True},
    "dark_iron":    {"base": "#5A5C66", "shadow": "#24242E", "highlight": "#A9AEBA", "metal": 0.8, "spec": 0.6, "spec_size": 0.05, "paint": 0.22, "flat": False},
    "gold_trim":    {"base": "#E6B04A", "shadow": "#8A4E1C", "highlight": "#FFF2B8", "metal": 1.0, "spec": 1.0, "spec_size": 0.08, "paint": 0.15, "flat": False},
    "leather_wrap": {"base": "#7E4A2C", "shadow": "#3A1C16", "highlight": "#B47A50", "metal": 0.0, "spec": 0.15, "spec_size": 0.04, "paint": 0.30, "flat": False},
    "wood":         {"base": "#8E5C34", "shadow": "#45261A", "highlight": "#C69060", "metal": 0.0, "spec": 0.1, "spec_size": 0.03, "paint": 0.35, "flat": False},
    "parchment":    {"base": "#EADBB4", "shadow": "#A88A60", "highlight": "#FFF6DC", "metal": 0.0, "spec": 0.0, "spec_size": 0.02, "paint": 0.25, "flat": False},
    "cloth_red":    {"base": "#A8322A", "shadow": "#4E1420", "highlight": "#D86048", "metal": 0.0, "spec": 0.0, "spec_size": 0.02, "paint": 0.3, "flat": False},
    "stone":        {"base": "#8E8B84", "shadow": "#45434A", "highlight": "#C8C4B8", "metal": 0.0, "spec": 0.1, "spec_size": 0.03, "paint": 0.4, "flat": True},
    "bone":         {"base": "#E4D8B8", "shadow": "#8A7660", "highlight": "#FFF8E4", "metal": 0.0, "spec": 0.2, "spec_size": 0.04, "paint": 0.3, "flat": False},
    "skin":         {"base": "#E0A47C", "shadow": "#9A5448", "highlight": "#F8CDA8", "metal": 0.0, "spec": 0.05, "spec_size": 0.03, "paint": 0.2, "flat": False},
    "beard":        {"base": "#8A4A26", "shadow": "#3E1C14", "highlight": "#C47A44", "metal": 0.0, "spec": 0.0, "spec_size": 0.02, "paint": 0.4, "flat": False},
    "apron":        {"base": "#5E3A26", "shadow": "#2A1814", "highlight": "#8E6040", "metal": 0.0, "spec": 0.1, "spec_size": 0.03, "paint": 0.35, "flat": False},
    "shirt":        {"base": "#4E6A8A", "shadow": "#232E4A", "highlight": "#7E9ABA", "metal": 0.0, "spec": 0.0, "spec_size": 0.02, "paint": 0.3, "flat": False},
    "slime":        {"base": "#4CBF5C", "shadow": "#1A5E44", "highlight": "#D8FFC0", "metal": 0.0, "spec": 1.0, "spec_size": 0.07, "paint": 0.12, "flat": False},
    "bat_skin":     {"base": "#5A4470", "shadow": "#22162E", "highlight": "#8E74A8", "metal": 0.0, "spec": 0.1, "spec_size": 0.03, "paint": 0.3, "flat": False},
    "rust_iron":    {"base": "#6E5A4C", "shadow": "#2E2220", "highlight": "#A88A6E", "metal": 0.5, "spec": 0.4, "spec_size": 0.04, "paint": 0.4, "flat": True},
}


def _m(base, shadow, highlight, metal=0.0, spec=0.1, spec_size=0.03, paint=0.3):
    return {"base": base, "shadow": shadow, "highlight": highlight, "metal": metal, "spec": spec, "spec_size": spec_size,
            "paint": paint, "flat": False}


# --- Regeneration batch 2 -----------------------------------------------------
# Forgemaster outfits.  Each Forgemaster owns a dominant hue (cloth), a value-
# separated secondary (leather/plate) and a glow accent, so the 8 heroes are
# distinct in colour as well as silhouette.  The blacksmith rows replace the
# muddy brown/blue mass of batch 1: lighter cornflower shirt, near-black apron,
# warm grey trousers, so skin / shirt / apron / boots step down in value.
MATERIALS.update({
    "bs_shirt":     _m("#5F8FCC", "#24386A", "#A6C8F0"),
    "bs_apron":     _m("#4A2E22", "#1C100E", "#7E5440", spec=0.2),
    "bs_trousers":  _m("#7A6E66", "#33292A", "#ABA096"),
    "bs_beard":     _m("#C2622A", "#5A2216", "#F0A050", paint=0.4),
    "rc_robe":      _m("#3A3E8C", "#161634", "#6E78D0"),
    "rc_trim":      _m("#E8D9A8", "#8A7454", "#FFF8DC"),
    "al_coat":      _m("#2F7A58", "#10301F", "#6CC08E"),
    "al_leather":   _m("#9A6A3C", "#41241A", "#D0A070"),
    "ar_shirt":     _m("#2E8C96", "#103842", "#6CD0D8"),
    "ar_brass":     _m("#D69A3A", "#7A3E12", "#FFE0A0", metal=0.9, spec=0.9, spec_size=0.07, paint=0.18),
    "hx_cloak":     _m("#3A2A4A", "#120A1A", "#6E5490", paint=0.35),
    "hx_mask":      _m("#ECE6DA", "#8A8098", "#FFFFFF", spec=0.4, spec_size=0.05, paint=0.15),
    "sm_robe":      _m("#E4E8F4", "#6E7AA8", "#FFFFFF", paint=0.25),
    "sm_sash":      _m("#2F55C8", "#141E5A", "#7C9CFF"),
    "du_cape":      _m("#C02838", "#4E0C1C", "#F06070"),
    "du_vest":      _m("#2A2A36", "#0E0E16", "#5A5A70"),
    "du_shirt":     _m("#F2EADC", "#9A8C80", "#FFFFFF", paint=0.2),
    "wd_plate":     _m("#9AA6B4", "#3A4456", "#E2EAF4", metal=1.0, spec=0.9, spec_size=0.06, paint=0.18),
    "wd_tabard":    _m("#4E7E3A", "#1C3416", "#8EC070"),
    "hair_dark":    _m("#3A2A26", "#140C0C", "#6E5248", paint=0.4),
    "hair_white":   _m("#E8E4EC", "#8E88A0", "#FFFFFF", paint=0.4),
    "skin_tan":     _m("#C88A60", "#7A4238", "#EDB890", spec=0.05),
    "skin_pale":    _m("#EAC4A8", "#A07070", "#FFE6D0", spec=0.05),
    "skin_dark":    _m("#8E5A3C", "#46241E", "#C08860", spec=0.05),
})

# Enemy faction kits: each faction = body material, armour/secondary, cloth,
# accent and an emissive glow.  Faction identity is carried by these materials;
# role is carried by the silhouette (see enemykit.py).
FACTION_KITS = {
    "slagborn":      {"body": _m("#4A3C38", "#1A1216", "#7E6A60", paint=0.4), "armor": _m("#5C5C66", "#22222C", "#A0A4B0", metal=0.8, spec=0.6, spec_size=0.05, paint=0.22),
                      "cloth": _m("#8E2E1C", "#3E0E10", "#D0603A"), "accent": _m("#C47A2A", "#5A2A10", "#F0B060", metal=0.6, spec=0.6, spec_size=0.06),
                      "glow": ("#FF6A2A", "#7A1A08", "#FFC060"), "eye": "#FFD24A"},
    "overgrowth":    {"body": _m("#6E4C2E", "#2C1A12", "#A88058", paint=0.45), "armor": _m("#4E8E3C", "#1A3A1A", "#9AD070", paint=0.35),
                      "cloth": _m("#8E6AB0", "#3A2450", "#C8A8E8"), "accent": _m("#E8709A", "#7A2040", "#FFC0D4"),
                      "glow": ("#B8F060", "#2E6A10", "#EFFFB0"), "eye": "#E8FF6A"},
    "rime_court":    {"body": _m("#A8DCF4", "#3A6AA8", "#F0FCFF", spec=0.8, spec_size=0.08, paint=0.18), "armor": _m("#C4CEE0", "#4A5878", "#F4F8FF", metal=1.0, spec=0.9, spec_size=0.06, paint=0.15),
                      "cloth": _m("#2E4A96", "#101C46", "#6C8CE0"), "accent": _m("#E8F4FF", "#7A9AC0", "#FFFFFF"),
                      "glow": ("#7FD8FF", "#1E4A8A", "#E8FAFF"), "eye": "#BFF4FF"},
    "hollow_dead":   {"body": _m("#E0D4B4", "#7E6E5C", "#FFF6E0", spec=0.2, spec_size=0.04), "armor": _m("#6E5A4C", "#2E2220", "#A88A6E", metal=0.5, spec=0.4, spec_size=0.04, paint=0.4),
                      "cloth": _m("#4E4462", "#1C1626", "#857AA0", paint=0.4), "accent": _m("#7AA898", "#2A443E", "#B8E0D0"),
                      "glow": ("#B48CFF", "#3A1A7A", "#EEE0FF"), "eye": "#C8A8FF"},
    "tempest_order": {"body": _m("#8E9CBC", "#323A5A", "#D4DEF4", metal=0.9, spec=0.8, spec_size=0.06, paint=0.2), "armor": _m("#D4A040", "#7A4414", "#FFE6A0", metal=1.0, spec=1.0, spec_size=0.07, paint=0.16),
                      "cloth": _m("#2A4AB0", "#0E1A50", "#6A8AF0"), "accent": _m("#F4F0E4", "#8A8678", "#FFFFFF"),
                      "glow": ("#FFE24A", "#7A5A08", "#FFFBC8"), "eye": "#FFF27A"},
    "training":      {"body": _m("#C8A060", "#6A4422", "#F0D090", paint=0.45), "armor": _m("#8A8C96", "#363844", "#C8CCD6", metal=0.8, spec=0.6, spec_size=0.05),
                      "cloth": _m("#B83A2E", "#4E1212", "#E87060"), "accent": _m("#8E5C34", "#45261A", "#C69060"),
                      "glow": ("#FFB347", "#8A3A10", "#FFF0C0"), "eye": "#1B1412"},
}

# Biome backdrops: (sky top, sky horizon, far, mid, near ground, ground top, accent glow)
# derived from Content/biomes.json palettes (dark, accent, light, mid).
BIOME_SCENES = {
    "ember_foundry": {"sky": ("#1E0E0C", "#7A2E14"), "far": "#3A1A14", "mid": "#4E2A20", "near": "#2A1610", "ground": "#5A3A2A",
                      "accent": "#FF6A2A", "light": "#FFB347"},
    "verdant_ruins": {"sky": ("#0E1E16", "#4E7A4A"), "far": "#22402C", "mid": "#2E4E34", "near": "#14261A", "ground": "#3E5A3A",
                      "accent": "#7DFF8A", "light": "#C6A15B"},
    "frostspire":    {"sky": ("#0E1A2A", "#5A86AE"), "far": "#2A4A6A", "mid": "#3A5A7A", "near": "#1A2C44", "ground": "#C8E0F0",
                      "accent": "#7FD8FF", "light": "#E8F6FF"},
    "sunken_crypt":  {"sky": ("#0A0810", "#2E2648"), "far": "#1E1A2C", "mid": "#2A2A3A", "near": "#120F1A", "ground": "#3A3A4A",
                      "accent": "#7A4BD6", "light": "#9AB0A0"},
    "storm_citadel": {"sky": ("#0A0C1E", "#3A4A8A"), "far": "#1E2448", "mid": "#2E3460", "near": "#10142A", "ground": "#5A5A7A",
                      "accent": "#FFE24A", "light": "#4FA3FF"},
}


def hex_to_srgb(h: str):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4))


def srgb_to_linear_c(c: float) -> float:
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def hex_to_linear(h: str, alpha: float = 1.0):
    r, g, b = hex_to_srgb(h)
    return (srgb_to_linear_c(r), srgb_to_linear_c(g), srgb_to_linear_c(b), alpha)


def mix_hex(a: str, b: str, t: float) -> str:
    ca, cb = hex_to_srgb(a), hex_to_srgb(b)
    c = [ca[i] * (1 - t) + cb[i] * t for i in range(3)]
    return "#" + "".join(f"{int(round(max(0, min(1, v)) * 255)):02X}" for v in c)
