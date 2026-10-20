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
