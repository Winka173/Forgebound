"""2D symbol vocabulary for item icons (pure python, no bpy).

A symbol is a list of primitives in a [-1, 1] box (x right, y up):
    ("poly", [(x, y), ...])                 filled polygon (simple, may be concave)
    ("line", [(x, y), ...], w)              stroked polyline, half-width w
    ("disc", (cx, cy), r)                   filled circle
    ("ring", (cx, cy), r, w)                circle outline, half-width w
    ("hole", (cx, cy), r)                   dark recessed dot (eye sockets, holes)
icons.py turns them into extruded, outlined, toon-shaded relief so symbols
share the light rig and outline weight of every other asset.

Symbols are designed to read at 48 px: one bold idea, few parts, stroke
half-widths >= 0.08.  Every modifier, blessing stat and rune trigger maps to a
symbol name in MODIFIER_MOTIFS / STAT_SYMBOLS / TRIGGER_SYMBOLS.
"""
from __future__ import annotations

import hashlib
import math
import random


# ------------------------------------------------------------------ helpers
def arc(cx, cy, r, a0, a1, n=16):
    return [(cx + math.cos(math.radians(a0 + (a1 - a0) * i / (n - 1))) * r,
             cy + math.sin(math.radians(a0 + (a1 - a0) * i / (n - 1))) * r) for i in range(n)]


def circle(cx, cy, r, n=24):
    return [(cx + math.cos(2 * math.pi * i / n) * r, cy + math.sin(2 * math.pi * i / n) * r) for i in range(n)]


def star(cx, cy, n, ro, ri, rot=90.0):
    pts = []
    for i in range(2 * n):
        a = math.radians(rot + 180.0 * i / n)
        r = ro if i % 2 == 0 else ri
        pts.append((cx + math.cos(a) * r, cy + math.sin(a) * r))
    return pts


def xf(prims, dx=0.0, dy=0.0, s=1.0, rot=0.0, flip=False):
    """Transform a primitive list (scale, rotate degrees, translate, mirror x)."""
    c, si = math.cos(math.radians(rot)), math.sin(math.radians(rot))

    def p(pt):
        x, y = pt
        if flip:
            x = -x
        x, y = x * s, y * s
        return (x * c - y * si + dx, x * si + y * c + dy)
    out = []
    for pr in prims:
        k = pr[0]
        if k == "poly":
            pts = [p(q) for q in pr[1]]
            out.append(("poly", pts[::-1] if flip else pts))
        elif k == "line":
            out.append(("line", [p(q) for q in pr[1]], pr[2] * s))
        elif k in ("disc", "hole"):
            out.append((k, p(pr[1]), pr[2] * s))
        elif k == "ring":
            out.append(("ring", p(pr[1]), pr[2] * s, pr[3] * s))
    return out


def arrow(x0, y0, x1, y1, w=0.09, head=0.3):
    a = math.atan2(y1 - y0, x1 - x0)
    bx, by = x1 - math.cos(a) * head * 0.8, y1 - math.sin(a) * head * 0.8
    l, r = a + math.radians(150), a - math.radians(150)
    tip = [(x1, y1), (x1 + math.cos(l) * head, y1 + math.sin(l) * head), (bx, by), (x1 + math.cos(r) * head, y1 + math.sin(r) * head)]
    return [("line", [(x0, y0), (bx, by)], w), ("poly", tip)]


def chevron(cx, cy, s=0.4, w=0.11):
    return [("line", [(cx - s * 0.5, cy + s), (cx + s * 0.5, cy), (cx - s * 0.5, cy - s)], w)]


# ------------------------------------------------------------------ symbols
def s_heart():
    pts = arc(-0.36, 0.25, 0.4, 0, 200, 12)[::-1][::-1]
    left = arc(-0.36, 0.25, 0.4, 20, 215, 12)
    right = arc(0.36, 0.25, 0.4, -35, 160, 12)
    return [("poly", right + left + [(0.0, -0.75)])]


def s_cracked_heart():
    return s_heart() + [("line", [(0.05, 0.55), (-0.12, 0.2), (0.12, 0.0), (-0.05, -0.35)], 0.07)]


def s_sword():
    return [("poly", [(-0.11, -0.3), (-0.11, 0.62), (0.0, 0.9), (0.11, 0.62), (0.11, -0.3)]),
            ("line", [(-0.4, -0.3), (0.4, -0.3)], 0.1), ("line", [(0, -0.35), (0, -0.72)], 0.09), ("disc", (0, -0.82), 0.11)]


def s_crossed_swords():
    a = xf(s_sword(), rot=-40, s=0.9)
    b = xf(s_sword(), rot=40, s=0.9)
    return a + b


def s_hammer():
    return [("line", [(-0.05, -0.85), (0.05, 0.3)], 0.1),
            ("poly", [(-0.6, 0.25), (0.55, 0.25), (0.65, 0.4), (0.55, 0.8), (-0.6, 0.8), (-0.68, 0.55)])]


def s_axe():
    return [("line", [(0.0, -0.9), (0.0, 0.85)], 0.09),
            ("poly", [(0.05, 0.6), (0.45, 0.85), (0.75, 0.55), (0.8, 0.0), (0.45, 0.1), (0.05, 0.25)])]


def s_crosshair():
    return [("ring", (0, 0), 0.6, 0.09), ("line", [(0, 0.95), (0, 0.4)], 0.09), ("line", [(0, -0.95), (0, -0.4)], 0.09),
            ("line", [(0.95, 0), (0.4, 0)], 0.09), ("line", [(-0.95, 0), (-0.4, 0)], 0.09), ("disc", (0, 0), 0.14)]


def s_burst():
    return [("poly", star(0, 0, 8, 0.95, 0.42, 90))]


def s_starburst_crack():
    return [("poly", star(0, 0, 4, 0.98, 0.3, 90)), ("poly", star(0, 0, 4, 0.6, 0.22, 45))]


def s_chevrons():
    return chevron(-0.25, 0) + chevron(0.25, 0)


def s_wing():
    return [("poly", [(-0.7, -0.5), (-0.2, 0.0), (0.2, 0.55), (0.75, 0.85), (0.55, 0.45), (0.8, 0.35), (0.5, 0.05), (0.7, -0.1),
                      (0.3, -0.3), (0.35, -0.5), (-0.1, -0.5)])]


def s_flask():
    body = arc(0, -0.35, 0.55, 110, 430, 18)
    return [("poly", [(-0.17, 0.5), (-0.17, 0.15)] + body[1:-1] + [(0.17, 0.15), (0.17, 0.5)]),
            ("line", [(-0.28, 0.6), (0.28, 0.6)], 0.09), ("disc", (0.15, -0.35), 0.1)]


def s_hourglass():
    return [("poly", [(-0.5, 0.8), (0.5, 0.8), (0.08, 0.0), (0.5, -0.8), (-0.5, -0.8), (-0.08, 0.0)]),
            ("line", [(-0.6, 0.85), (0.6, 0.85)], 0.08), ("line", [(-0.6, -0.85), (0.6, -0.85)], 0.08)]


def s_flame():
    return [("poly", [(0.0, 0.95), (0.25, 0.5), (0.5, 0.15), (0.55, -0.3), (0.35, -0.7), (0.0, -0.85), (-0.35, -0.7), (-0.55, -0.3),
                      (-0.45, 0.2), (-0.2, 0.45), (-0.15, 0.1), (0.05, 0.4)])]


def s_drop():
    pts = arc(0, -0.25, 0.55, 125, 415, 18)
    return [("poly", pts + [(0.0, 0.9)])]


def s_skull():
    head = arc(0, 0.15, 0.62, -20, 200, 18)
    return [("poly", head + [(-0.35, -0.3), (-0.3, -0.65), (0.3, -0.65), (0.35, -0.3)]),
            ("hole", (-0.24, 0.1), 0.17), ("hole", (0.24, 0.1), 0.17)]


def s_chain():
    return [("ring", (-0.38, 0.0), 0.32, 0.09), ("ring", (0.38, 0.0), 0.32, 0.09), ("line", [(-0.1, 0), (0.1, 0)], 0.09)]


def s_arrows_fan(n=3):
    out = []
    for i in range(n):
        a = math.radians(90 + (i - (n - 1) / 2) * 28)
        out += arrow(0, -0.8, math.cos(a) * 0.9, -0.8 + math.sin(a) * 1.7, w=0.07, head=0.28)
    return out


def s_arrow_fast():
    return arrow(-0.6, -0.1, 0.85, -0.1, 0.09, 0.35) + [("line", [(-0.9, 0.25), (-0.3, 0.25)], 0.06),
                                                         ("line", [(-0.85, -0.45), (-0.4, -0.45)], 0.06)]


def s_spark():
    return [("poly", star(0, 0, 4, 0.95, 0.24, 90))]


def s_circuit():
    return [("line", [(-0.6, -0.5), (0.0, 0.55), (0.6, -0.5), (-0.6, -0.5)], 0.08),
            ("disc", (-0.6, -0.5), 0.22), ("disc", (0.0, 0.55), 0.22), ("disc", (0.6, -0.5), 0.22)]


def s_hexes():
    def hexa(cx, cy, r):
        return ("poly", [(cx + math.cos(math.radians(60 * i + 30)) * r, cy + math.sin(math.radians(60 * i + 30)) * r) for i in range(6)])
    return [hexa(-0.4, 0.3, 0.36), hexa(0.4, 0.3, 0.36), hexa(0.0, -0.4, 0.36)]


def s_shield():
    return [("poly", [(-0.65, 0.75), (0.0, 0.9), (0.65, 0.75), (0.6, 0.0), (0.35, -0.5), (0.0, -0.9), (-0.35, -0.5), (-0.6, 0.0)])]


def s_shield_bubble():
    return xf(s_shield(), s=0.7) + [("ring", (0, 0), 0.88, 0.07)]


def s_broken_shield():
    l = [(-0.65, 0.75), (0.0, 0.9), (0.08, 0.5), (-0.1, 0.1), (0.05, -0.3), (0.0, -0.9), (-0.35, -0.5), (-0.6, 0.0)]
    r = [(0.15, 0.88), (0.65, 0.75), (0.6, 0.0), (0.35, -0.5), (0.12, -0.78), (0.17, -0.3), (0.02, 0.1), (0.2, 0.5)]
    return [("poly", l), ("poly", xf([("poly", r)], dx=0.12)[0][1])]


def s_plus():
    return [("poly", [(-0.22, 0.8), (0.22, 0.8), (0.22, 0.22), (0.8, 0.22), (0.8, -0.22), (0.22, -0.22), (0.22, -0.8), (-0.22, -0.8),
                      (-0.22, -0.22), (-0.8, -0.22), (-0.8, 0.22), (-0.22, 0.22)])]


def s_thorns():
    out = [("line", [(-0.85, -0.7), (-0.3, -0.2), (0.25, 0.25), (0.85, 0.75)], 0.1)]
    for t, sgn in ((0.18, 1), (0.38, -1), (0.6, 1), (0.8, -1)):
        x, y = -0.85 + 1.7 * t, -0.7 + 1.45 * t
        nx, ny = -0.65 * sgn, 0.76 * sgn
        out.append(("poly", [(x - 0.14, y - 0.1), (x + 0.14, y + 0.1), (x + nx * 0.45, y + ny * 0.45)][::sgn]))
    return out


def s_feather():
    return [("poly", [(0.75, 0.85), (0.3, 0.75), (-0.2, 0.3), (-0.45, -0.25), (-0.3, -0.35), (0.2, -0.05), (0.55, 0.35)]),
            ("line", [(0.75, 0.85), (-0.75, -0.85)], 0.06)]


def s_fang():
    one = [(-0.3, 0.7), (0.3, 0.7), (0.22, 0.1), (0.0, -0.85), (-0.12, -0.2)]
    return [("poly", [(-0.85, 0.95), (0.85, 0.95), (0.85, 0.6), (-0.85, 0.6)]), ("poly", [(x - 0.42, y) for x, y in one]),
            ("poly", [(-x + 0.42, y) for x, y in one][::-1])]


def s_crown():
    return [("poly", [(-0.75, -0.55), (0.75, -0.55), (0.85, 0.55), (0.42, 0.1), (0.0, 0.75), (-0.42, 0.1), (-0.85, 0.55)]),
            ("disc", (0.0, 0.82), 0.13), ("disc", (-0.85, 0.62), 0.11), ("disc", (0.85, 0.62), 0.11)]


def s_horned_skull():
    return xf(s_skull(), s=0.8, dy=-0.12) + [("poly", [(-0.45, 0.45), (-0.95, 0.95), (-0.75, 0.3)]),
                                               ("poly", [(0.45, 0.45), (0.95, 0.95), (0.75, 0.3)])]


def s_eye():
    up = arc(0, -0.55, 1.0, 35, 145, 12)
    dn = arc(0, 0.55, 1.0, 215, 325, 12)
    return [("line", up + dn + [up[0]], 0.08), ("disc", (0, 0), 0.26)]


def s_coin():
    return [("ring", (0, 0), 0.68, 0.12), ("poly", star(0, 0, 4, 0.38, 0.14, 90))]


def s_coins():
    return [("ring", (-0.3, -0.25), 0.45, 0.1), ("ring", (0.32, 0.3), 0.45, 0.1), ("disc", (0.32, 0.3), 0.18)]


def s_crystal():
    return [("poly", [(0.0, 0.95), (0.4, 0.35), (0.3, -0.6), (0.0, -0.9), (-0.3, -0.6), (-0.4, 0.35)]),
            ("poly", [(0.55, 0.3), (0.75, 0.0), (0.65, -0.55), (0.45, -0.65), (0.42, -0.1)])]


def s_campfire():
    return xf(s_flame(), s=0.6, dy=0.25) + [("line", [(-0.75, -0.75), (0.75, -0.45)], 0.1), ("line", [(-0.75, -0.45), (0.75, -0.75)], 0.1)]


def s_scroll():
    return [("poly", [(-0.55, 0.65), (0.55, 0.65), (0.55, -0.65), (-0.55, -0.65)]), ("disc", (-0.55, 0.65), 0.18), ("disc", (0.55, -0.65), 0.18),
            ("line", [(-0.3, 0.25), (0.3, 0.25)], 0.06), ("line", [(-0.3, -0.05), (0.3, -0.05)], 0.06)]


def s_cards():
    def card(dx, rot):
        return xf([("poly", [(-0.32, -0.5), (0.32, -0.5), (0.32, 0.5), (-0.32, 0.5)])], dx=dx, rot=rot)
    return card(-0.42, 18) + card(0.42, -18) + card(0.0, 0)


def s_cycle():
    return [("line", arc(0, 0, 0.6, 20, 160, 12), 0.09), ("poly", [(-0.82, 0.15), (-0.35, 0.15), (-0.6, -0.15)]),
            ("line", arc(0, 0, 0.6, 200, 340, 12), 0.09), ("poly", [(0.82, -0.15), (0.35, -0.15), (0.6, 0.15)])]


def s_chalice():
    cup = arc(0, 0.35, 0.55, 180, 360, 12)
    return [("poly", cup), ("line", [(0, -0.2), (0, -0.6)], 0.1), ("poly", [(-0.4, -0.85), (0.4, -0.85), (0.2, -0.6), (-0.2, -0.6)]),
            ("disc", (0, 0.7), 0.13)]


def s_grid():
    out = []
    for x in (-0.55, 0.0, 0.55):
        for y in (-0.55, 0.0, 0.55):
            out.append(("poly", [(x - 0.2, y - 0.2), (x + 0.2, y - 0.2), (x + 0.2, y + 0.2), (x - 0.2, y + 0.2)]))
    return out


def s_snowflake():
    out = []
    for i in range(3):
        a = math.radians(90 + 60 * i)
        out.append(("line", [(math.cos(a) * 0.9, math.sin(a) * 0.9), (-math.cos(a) * 0.9, -math.sin(a) * 0.9)], 0.08))
    for i in range(6):
        a = math.radians(90 + 60 * i)
        cx, cy = math.cos(a) * 0.55, math.sin(a) * 0.55
        out.append(("line", [(cx + math.cos(a + 0.9) * 0.25, cy + math.sin(a + 0.9) * 0.25), (cx, cy),
                             (cx + math.cos(a - 0.9) * 0.25, cy + math.sin(a - 0.9) * 0.25)], 0.06))
    return out


def s_bolt():
    return [("poly", [(0.1, 0.95), (0.55, 0.95), (0.2, 0.15), (0.55, 0.15), (-0.3, -0.95), (-0.05, -0.1), (-0.45, -0.1)])]


def s_crescent():
    return [("poly", arc(0, 0, 0.85, 60, 300, 20) + arc(0.38, 0, 0.62, 250, 110, 16))]


def s_sun():
    return [("poly", star(0, 0, 10, 0.95, 0.62, 90)), ("ring", (0, 0), 0.4, 0.0), ("disc", (0, 0), 0.42)]


def s_arcane_star():
    return [("ring", (0, 0), 0.82, 0.07), ("poly", star(0, 0, 5, 0.66, 0.27, 90))]


def s_ghost():
    top = arc(0, 0.2, 0.55, 0, 180, 12)
    return [("poly", top + [(-0.55, -0.7), (-0.3, -0.5), (-0.1, -0.75), (0.1, -0.5), (0.3, -0.75), (0.55, -0.55)]),
            ("hole", (-0.2, 0.25), 0.12), ("hole", (0.2, 0.25), 0.12)]


def s_bow():
    return [("line", arc(-0.55, 0, 1.0, -58, 58, 14), 0.1), ("line", [(-0.02, -0.85), (-0.02, 0.85)], 0.04)] + \
        arrow(-0.7, 0.0, 0.85, 0.0, 0.06, 0.28)


def s_claw():
    return [("line", [(-0.6, 0.8), (-0.15, -0.8)], 0.09), ("line", [(-0.1, 0.9), (0.3, -0.75)], 0.09), ("line", [(0.4, 0.8), (0.75, -0.6)], 0.09)]


def s_socket():
    return [("ring", (0, 0), 0.62, 0.1)] + [("poly", star(0, 0, 8, 0.95, 0.72, 90 + 22.5 * k)) for k in (0,)][:0] + \
        [("disc", (math.cos(math.radians(45 * i)) * 0.82, math.sin(math.radians(45 * i)) * 0.82), 0.13) for i in range(8)] + \
        [("disc", (0, 0), 0.28)]


def s_banner():
    return [("line", [(-0.55, -0.95), (-0.55, 0.9)], 0.08), ("poly", [(-0.5, 0.85), (0.75, 0.85), (0.45, 0.4), (0.75, -0.05), (-0.5, -0.05)])]


def s_clock():
    return [("ring", (0, 0), 0.75, 0.1), ("line", [(0, 0.5), (0, 0), (0.35, -0.2)], 0.08)]


def s_reflect():
    return [("line", [(-0.7, 0.8), (-0.7, -0.8)], 0.1)] + arrow(0.8, 0.5, -0.45, 0.0, 0.07, 0.25) + arrow(-0.45, -0.05, 0.8, -0.55, 0.07, 0.25)


def s_spores():
    return [("disc", (-0.4, 0.35), 0.24), ("disc", (0.35, 0.45), 0.18), ("disc", (0.0, -0.2), 0.3), ("disc", (0.5, -0.45), 0.15),
            ("disc", (-0.55, -0.45), 0.13)]


def s_fist():
    # "on hit": a striking arrow meeting an impact star
    return xf(s_burst(), s=0.55, dx=0.38, dy=0.38) + arrow(-0.9, -0.9, 0.05, 0.05, 0.1, 0.36)


def s_slash():
    return [("poly", arc(0.1, -0.2, 0.95, 70, 200, 14) + arc(0.35, -0.05, 0.75, 195, 75, 14))]


def s_boot():
    return [("poly", [(-0.45, 0.85), (0.1, 0.85), (0.1, -0.2), (0.75, -0.4), (0.8, -0.8), (-0.45, -0.8)])] + \
        [("poly", [(0.1, 0.75), (0.75, 0.95), (0.4, 0.55), (0.75, 0.45), (0.1, 0.35)])]


def s_heart_horns():
    return xf(s_heart(), s=0.8, dy=-0.1) + [("poly", [(-0.45, 0.4), (-0.85, 0.95), (-0.7, 0.3)]), ("poly", [(0.45, 0.4), (0.85, 0.95), (0.7, 0.3)])]


def s_shield_ring():
    return xf(s_shield(), s=0.62) + [("ring", (0, 0), 0.82, 0.08)] + [("disc", (math.cos(math.radians(a)) * 0.82, math.sin(math.radians(a)) * 0.82), 0.12)
                                                                      for a in (90, 210, 330)]


def s_bars_up():
    return [("poly", [(x - 0.17, -0.8), (x + 0.17, -0.8), (x + 0.17, -0.8 + h), (x - 0.17, -0.8 + h)]) for x, h in ((-0.55, 0.6), (0.0, 1.05), (0.55, 1.6))]


# ---- modifier-specific motifs
def m_fork():
    return arrow(-0.8, 0, -0.05, 0, 0.09, 0.0)[:1] + arrow(-0.1, 0, 0.75, 0.6, 0.08, 0.3) + arrow(-0.1, 0, 0.75, -0.6, 0.08, 0.3)


def m_pierce():
    return [("poly", [(-0.1, -0.85), (0.1, -0.85), (0.1, 0.85), (-0.1, 0.85)])] + arrow(-0.9, 0.0, 0.9, 0.0, 0.08, 0.32)


def m_homing():
    return [("ring", (0.55, 0.45), 0.3, 0.08), ("disc", (0.55, 0.45), 0.1)] + \
        [("line", arc(-0.05, -0.15, 0.62, 200, 330, 10), 0.08)] + arrow(0.48, -0.42, 0.55, 0.05, 0.08, 0.28)


def m_ricochet():
    return [("line", [(-0.85, 0.6), (-0.35, -0.5), (0.15, 0.4)], 0.08)] + arrow(0.15, 0.4, 0.8, -0.55, 0.08, 0.3) + \
        [("poly", [(-0.9, -0.7), (0.9, -0.7), (0.9, -0.85), (-0.9, -0.85)])]


def m_charged():
    return [("ring", (0, 0), 0.85, 0.07)] + xf(s_bolt(), s=0.7)


def m_bomb():
    return [("disc", (-0.1, -0.2), 0.6), ("line", [(0.25, 0.25), (0.45, 0.55), (0.65, 0.6)], 0.07)] + xf(s_spark(), s=0.28, dx=0.72, dy=0.72)


def m_bomb_clock():
    return m_bomb() + [("line", [(-0.1, 0.1), (-0.1, -0.2), (0.1, -0.35)], 0.06)]


def m_shockwave():
    return [("disc", (-0.7, 0), 0.18)] + [("line", arc(-0.7, 0, r, -50, 50, 10), 0.08) for r in (0.55, 1.0, 1.45)]


def m_magnet():
    return [("line", arc(0, 0.1, 0.55, 180, 360, 14), 0.2), ("line", [(-0.55, 0.1), (-0.55, 0.7)], 0.2), ("line", [(0.55, 0.1), (0.55, 0.7)], 0.2),
            ("poly", [(-0.78, 0.6), (-0.32, 0.6), (-0.32, 0.9), (-0.78, 0.9)]), ("poly", [(0.32, 0.6), (0.78, 0.6), (0.78, 0.9), (0.32, 0.9)])]


def m_shatter():
    return [("poly", [(-0.15, 0.9), (0.2, 0.2), (-0.05, 0.05)]), ("poly", [(0.3, 0.75), (0.85, 0.25), (0.35, 0.05)]),
            ("poly", [(-0.85, 0.2), (-0.25, -0.1), (-0.5, -0.65)]), ("poly", [(0.05, -0.2), (0.7, -0.4), (0.15, -0.9)])]


def m_knockback():
    return [("poly", [(0.55, -0.8), (0.75, -0.8), (0.75, 0.8), (0.55, 0.8)])] + arrow(0.4, 0.3, -0.85, 0.3, 0.08, 0.3) + arrow(0.4, -0.3, -0.85, -0.3, 0.08, 0.3)


def m_sweep():
    return s_slash() + [("line", arc(0.1, -0.2, 1.12, 90, 150, 8), 0.05)]


def m_reach():
    return arrow(-0.2, 0.0, 0.92, 0.0, 0.08, 0.32) + arrow(0.2, 0.0, -0.92, 0.0, 0.08, 0.32) + [("poly", [(-0.06, -0.5), (0.06, -0.5), (0.06, 0.5), (-0.06, 0.5)])]


def m_mace():
    return [("line", [(-0.7, -0.85), (0.0, 0.0)], 0.09), ("poly", star(0.25, 0.3, 7, 0.65, 0.38, 0))]


def m_saw():
    pts = [(-0.85, -0.3)]
    for i in range(7):
        x = -0.85 + 1.7 * i / 7
        pts += [(x + 0.12, 0.45), (x + 1.7 / 7, 0.05)]
    pts += [(0.85, -0.3)]
    return [("poly", pts)]


def m_weight():
    return [("poly", [(-0.6, -0.8), (0.6, -0.8), (0.4, 0.3), (-0.4, 0.3)]), ("ring", (0, 0.55), 0.28, 0.1)]


def m_volatile():
    return [("ring", (0, -0.1), 0.55, 0.12), ("line", [(-0.2, 0.3), (0.05, 0.0), (-0.1, -0.25), (0.15, -0.5)], 0.06)] + \
        xf(s_spark(), s=0.3, dx=0.65, dy=0.65) + xf(s_spark(), s=0.22, dx=-0.7, dy=0.55)


def m_forked_chain():
    return [("line", [(-0.85, 0.0), (-0.3, 0.2), (0.0, -0.1)], 0.07), ("line", [(0.0, -0.1), (0.4, 0.5), (0.85, 0.65)], 0.07),
            ("line", [(0.0, -0.1), (0.45, -0.35), (0.85, -0.7)], 0.07), ("disc", (-0.85, 0.0), 0.15), ("disc", (0.85, 0.65), 0.15),
            ("disc", (0.85, -0.7), 0.15)]


def m_boomerang():
    return [("poly", [(-0.8, 0.55), (-0.5, 0.75), (0.1, 0.0), (0.75, -0.2), (0.8, -0.55), (0.0, -0.4), (-0.3, -0.15)])]


def m_focus():
    return [("disc", (0, 0), 0.2)] + [l for a in (45, 135, 225, 315) for l in arrow(math.cos(math.radians(a)) * 0.95, math.sin(math.radians(a)) * 0.95,
                                                                                    math.cos(math.radians(a)) * 0.35, math.sin(math.radians(a)) * 0.35, 0.06, 0.22)]


def m_puddle():
    return [("poly", [(x, y * 0.4 - 0.45) for x, y in circle(0, 0, 0.85, 20)]), ("disc", (-0.25, 0.25), 0.16), ("disc", (0.2, 0.55), 0.12),
            ("disc", (0.35, 0.15), 0.09)]


def m_battery():
    return [("poly", [(-0.42, -0.85), (0.42, -0.85), (0.42, 0.7), (-0.42, 0.7)]), ("poly", [(-0.18, 0.7), (0.18, 0.7), (0.18, 0.88), (-0.18, 0.88)])]


def m_siphon():
    return xf(s_drop(), s=0.55, dx=-0.45, dy=0.35) + [("line", arc(0.0, 0.0, 0.6, 150, 10, 10), 0.07)] + \
        xf(s_heart(), s=0.45, dx=0.5, dy=-0.45) + arrow(0.55, 0.15, 0.55, -0.05, 0.07, 0.22)


def m_tempo():
    return [("poly", [(-0.5, -0.85), (0.5, -0.85), (0.22, 0.75), (-0.22, 0.75)]), ("line", [(0.0, -0.45), (0.5, 0.85)], 0.07),
            ("disc", (0.33, 0.45), 0.1)]


def m_ambush():
    return [("poly", arc(0, 0, 0.88, 70, 290, 18) + arc(0.32, 0, 0.66, 260, 100, 14))] + xf(s_sword(), s=0.6, dx=0.35, rot=-30)


def m_sunder():
    return xf(s_shield(), s=0.85) + [("line", [(0.05, 0.85), (-0.15, 0.3), (0.15, -0.05), (-0.05, -0.7)], 0.09)]


def m_barrage():
    return [l for x in (-0.6, -0.2, 0.2, 0.6) for l in arrow(x + 0.25, 0.9, x - 0.15, -0.8, 0.055, 0.22)]


def m_tuning_fork():
    return [("line", [(0, -0.9), (0, -0.2)], 0.09), ("line", arc(0, 0.05, 0.3, 180, 360, 10), 0.09), ("line", [(-0.3, 0.05), (-0.3, 0.75)], 0.09),
            ("line", [(0.3, 0.05), (0.3, 0.75)], 0.09)] + [("line", arc(0, 0.5, r, -40, 40, 6), 0.05) for r in (0.62, 0.85)] + \
        [("line", arc(0, 0.5, r, 140, 220, 6), 0.05) for r in (0.62, 0.85)]


def m_scatter():
    return [("disc", (-0.7, -0.6), 0.2)] + [("disc", (math.cos(math.radians(a)) * r - 0.2, math.sin(math.radians(a)) * r - 0.1), 0.13)
                                            for a, r in ((20, 0.8), (45, 0.95), (70, 0.85), (35, 0.5), (60, 0.6), (5, 0.55))]


def m_quickdraw():
    return xf(s_sword(), s=0.7, rot=-90, dx=0.2) + [("line", [(-0.95, 0.5), (-0.45, 0.5)], 0.05), ("line", [(-0.95, -0.5), (-0.45, -0.5)], 0.05)]


def m_spike_plate():
    return xf(s_shield(), s=0.7, dx=0.15) + [("poly", [(-0.95, 0.12), (0.95, 0.0), (-0.95, -0.12)]), ("hole", (0.15, 0.0), 0.14)]


def m_coil():
    return [("line", [(-0.6 + 0.6 * math.cos(t * 0.6) * 0, -0.85 + 1.7 * t / 18) if False else
                      (0.45 * math.sin(t * math.pi * 2 / 6), -0.85 + 1.7 * t / 18) for t in range(19)], 0.07)] + xf(s_bolt(), s=0.4, dx=0.6, dy=0.5)


def m_bullseye():
    return [("ring", (0, 0), 0.8, 0.08), ("ring", (0, 0), 0.48, 0.08), ("disc", (0, 0), 0.18), ("line", [(-0.95, 0.95), (-0.2, 0.2)], 0.07)]


def m_cauterize():
    return xf(s_flame(), s=0.65, dx=-0.25, dy=0.15) + xf(s_plus(), s=0.38, dx=0.55, dy=-0.5)


def m_spectral():
    return xf(s_sword(), s=0.65, rot=25, dx=-0.3) + xf(s_sword(), s=0.65, rot=-25, dx=0.3) + [("ring", (0, 0.1), 0.95, 0.04)]


def m_frostbite():
    return xf(s_sword(), s=0.75, rot=-40, dx=-0.15, dy=-0.1) + xf(s_snowflake(), s=0.38, dx=0.55, dy=0.55)


def m_twin():
    return s_crossed_swords()


def m_repeat():
    return s_cycle() + [("disc", (0, 0), 0.18)]


SYMBOLS = {
    "heart": s_heart, "cracked_heart": s_cracked_heart, "sword": s_sword, "crossed_swords": s_crossed_swords, "hammer": s_hammer,
    "axe": s_axe, "crosshair": s_crosshair, "burst": s_burst, "star_crack": s_starburst_crack, "chevrons": s_chevrons, "wing": s_wing,
    "flask": s_flask, "hourglass": s_hourglass, "flame": s_flame, "drop": s_drop, "skull": s_skull, "chain": s_chain,
    "arrows_fan": s_arrows_fan, "arrow_fast": s_arrow_fast, "spark": s_spark, "circuit": s_circuit, "hexes": s_hexes, "shield": s_shield,
    "shield_bubble": s_shield_bubble, "broken_shield": s_broken_shield, "plus": s_plus, "thorns": s_thorns, "feather": s_feather,
    "fang": s_fang, "crown": s_crown, "horned_skull": s_horned_skull, "eye": s_eye, "coin": s_coin, "coins": s_coins, "crystal": s_crystal,
    "campfire": s_campfire, "scroll": s_scroll, "cards": s_cards, "cycle": s_cycle, "chalice": s_chalice, "grid": s_grid,
    "snowflake": s_snowflake, "bolt": s_bolt, "crescent": s_crescent, "sun": s_sun, "arcane_star": s_arcane_star, "ghost": s_ghost,
    "bow": s_bow, "claw": s_claw, "socket": s_socket, "banner": s_banner, "clock": s_clock, "reflect": s_reflect, "spores": s_spores,
    "fist": s_fist, "slash": s_slash, "boot": s_boot, "heart_horns": s_heart_horns, "shield_ring": s_shield_ring, "bars_up": s_bars_up,
    # modifier motifs
    "fork": m_fork, "pierce": m_pierce, "homing": m_homing, "ricochet": m_ricochet, "charged": m_charged, "bomb": m_bomb,
    "bomb_clock": m_bomb_clock, "shockwave": m_shockwave, "magnet": m_magnet, "shatter": m_shatter, "knockback": m_knockback,
    "sweep": m_sweep, "reach": m_reach, "mace": m_mace, "saw": m_saw, "weight": m_weight, "volatile": m_volatile,
    "forked_chain": m_forked_chain, "boomerang": m_boomerang, "focus": m_focus, "puddle": m_puddle, "battery": m_battery,
    "siphon": m_siphon, "tempo": m_tempo, "ambush": m_ambush, "sunder": m_sunder, "barrage": m_barrage, "tuning_fork": m_tuning_fork,
    "scatter": m_scatter, "quickdraw": m_quickdraw, "spike_plate": m_spike_plate, "coil": m_coil, "bullseye": m_bullseye,
    "cauterize": m_cauterize, "spectral": m_spectral, "frostbite": m_frostbite, "twin": m_twin, "repeat": m_repeat,
}


def get(name):
    return SYMBOLS[name]()


# ------------------------------------------------------------ modifiers
# One distinct motif per modifier; the badge plate shape comes from `stage`
# and the enamel colour from the first tag family (see icons.py).
MODIFIER_MOTIFS = {
    "split": "fork", "pierce": "pierce", "homing": "homing", "ricochet": "ricochet", "rapid": "chevrons", "charged": "charged",
    "repeat": "repeat", "delayed": "bomb_clock", "explosion": "burst", "shockwave": "shockwave", "pull": "magnet", "shatter": "shatter",
    "knockback": "knockback", "sweep": "sweep", "long_reach": "reach", "precision": "crosshair", "brutal": "mace", "serrated": "saw",
    "weighted": "weight", "volatile": "volatile", "forked": "forked_chain", "boomerang": "boomerang", "focus": "focus",
    "lingering": "puddle", "overcharge": "battery", "siphon": "siphon", "guarding": "shield", "executioner": "axe", "tempo": "tempo",
    "ambush": "ambush", "sundering": "sunder", "twin_strike": "twin", "barrage": "barrage", "infused": "flask",
    "resonant": "tuning_fork", "frenzied": "claw", "scatter": "scatter", "heavy_head": "hammer", "quickdraw": "quickdraw",
    "armor_piercer": "spike_plate", "overdrive_coil": "coil", "hunters_mark": "bullseye", "cauterize": "cauterize",
    "spectral_bond": "spectral", "frostbite_edge": "frostbite",
}

# ------------------------------------------------------------ blessings
STAT_SYMBOLS = {
    "maxHp": "heart", "damage": "crossed_swords", "critChance": "crosshair", "critDamage": "star_crack", "attackSpeed": "chevrons",
    "moveSpeed": "boot", "statusChance": "flask", "statusDuration": "hourglass", "burnDamage": "flame", "poisonDamage": "drop",
    "chainCount": "chain", "projectileCount": "arrows_fan", "projectileSpeed": "arrow_fast", "procDamage": "spark",
    "relayCapacity": "circuit", "relayThreshold": "circuit", "adjacencyPower": "hexes", "armor": "shield", "shieldOnStart": "shield_bubble",
    "regen": "plus", "thorns": "thorns", "evasion": "feather", "leech": "fang", "damageVsElite": "crown", "damageVsBoss": "horned_skull",
    "damageVsStatus": "eye", "lowHpDamage": "cracked_heart", "shopDiscount": "coin", "rerollDiscount": "cycle", "goldGain": "coins",
    "essenceGain": "crystal", "restHeal": "campfire", "shopOffers": "scroll", "rewardChoices": "cards", "healingPower": "chalice",
    "firstStrike": "bolt", "boardWidth": "grid", "boardHeight": "bars_up", "summonPower": "ghost", "meleeDamage": "sword",
    "rangedDamage": "bow", "resistAll": "shield_ring", "range": "reach", "incomingDamage": "broken_shield", "enemyHp": "heart_horns",
    "enemyDamage": "claw", "enemySpeed": "chevrons", "modifierSlots": "socket",
}
TAG_SYMBOLS = {"blade": "sword", "heavy": "hammer", "Fire": "flame", "Frost": "snowflake", "Lightning": "bolt", "Arcane": "arcane_star",
               "Shadow": "crescent", "Radiant": "sun", "fire": "flame", "frost": "snowflake", "lightning": "bolt", "arcane": "arcane_star",
               "shadow": "crescent", "radiant": "sun"}
TRIGGER_SYMBOLS = {
    "onHit": "fist", "onCrit": "crosshair", "onKill": "skull", "onAttack": "slash", "onStatusApplied": "flask", "onShieldBreak": "broken_shield",
    "onDamageTaken": "reflect", "onBlock": "shield", "onLowHp": "cracked_heart", "onTimer": "clock", "onBattleStart": "banner",
    "onWaveStart": "banner", "onProjectileFired": "arrow_fast", "onSummon": "ghost",
}
EFFECT_SYMBOLS = {"heal": "plus", "shield": "shield", "haste": "chevrons", "nova": "burst", "chain": "bolt", "projectile": "arrows_fan",
                  "mark": "bullseye", "gold": "coin", "summon": "ghost", "spreadStatus": "spores", "fortify": "shield_ring",
                  "damage": "spark", "applyStatus": "flask", "extraAttack": "twin"}

# Stat families -> enamel hue (shared by blessings and the curse frame tint)
STAT_FAMILY = {
    "maxHp": "life", "regen": "life", "healingPower": "life", "restHeal": "life", "leech": "blood", "lowHpDamage": "blood",
    "damage": "might", "meleeDamage": "might", "rangedDamage": "might", "critChance": "precision", "critDamage": "precision",
    "attackSpeed": "speed", "moveSpeed": "speed", "evasion": "speed", "firstStrike": "speed", "enemySpeed": "speed",
    "statusChance": "status", "statusDuration": "status", "burnDamage": "status", "poisonDamage": "status", "damageVsStatus": "status",
    "chainCount": "arcane", "projectileCount": "arcane", "projectileSpeed": "arcane", "procDamage": "arcane", "relayCapacity": "arcane",
    "relayThreshold": "arcane", "adjacencyPower": "arcane", "summonPower": "arcane", "modifierSlots": "arcane",
    "armor": "guard", "shieldOnStart": "guard", "thorns": "guard", "resistAll": "guard", "incomingDamage": "guard",
    "damageVsElite": "hunt", "damageVsBoss": "hunt", "enemyHp": "hunt", "enemyDamage": "hunt",
    "shopDiscount": "wealth", "rerollDiscount": "wealth", "goldGain": "wealth", "essenceGain": "wealth", "shopOffers": "wealth",
    "rewardChoices": "wealth", "boardWidth": "wealth", "boardHeight": "wealth", "damageTypeBonus": "element", "range": "precision",
}
FAMILY_COLORS = {
    "life": "#3FBF5A", "blood": "#C42A3A", "might": "#D2552A", "precision": "#E8B030", "speed": "#36B8C8", "status": "#8CC83A",
    "arcane": "#4F6CF0", "guard": "#7E8EA8", "hunt": "#9A3AC0", "wealth": "#E8C040", "element": "#E07A30", "rule": "#B07ADA",
}


def blessing_design(b):
    """Return (symbol, secondary_symbol_or_None, family) for a blessing dict."""
    stats = b.get("stats") or []
    runes = b.get("runes") or []
    sym, sec, fam = None, None, "rule"
    if stats:
        st = stats[0]
        tag = st.get("tag")
        if st["stat"] == "damageTypeBonus" and tag:
            sym = TAG_SYMBOLS.get(tag, "spark")
        elif st["stat"] == "damage" and tag in TAG_SYMBOLS:
            sym = TAG_SYMBOLS[tag]
        else:
            sym = STAT_SYMBOLS.get(st["stat"], "spark")
        fam = STAT_FAMILY.get(st["stat"], "rule")
        if len(stats) > 1:
            sec = STAT_SYMBOLS.get(stats[1]["stat"])
    if runes:
        r = runes[0]
        tsym = TRIGGER_SYMBOLS.get(r.get("trigger"), "spark")
        eff = (r.get("effects") or [{}])[0]
        esym = EFFECT_SYMBOLS.get(eff.get("type"))
        if eff.get("type") == "applyStatus":
            esym = {"burn": "flame", "poison": "drop", "bleed": "drop", "chill": "snowflake", "stun": "star_crack", "weaken": "broken_shield",
                    "slow": "hourglass", "curse": "crescent"}.get(eff.get("status"), "flask")
        if sym is None:
            # rule blessings: the EFFECT is the main symbol, the trigger the badge, and the
            # enamel follows the trigger family, so "on wave start: X" blessings differ at a glance
            sym, sec, fam = (esym or tsym), tsym, "trig_" + TRIGGER_FAMILY.get(r.get("trigger"), "strike")
        elif sec is None:
            sec = tsym
    return sym or "spark", (sec if sec != sym else None), fam


# ---------------------------------------------------------------- runes
# Trigger families -> glow colour (rune tablets).  Offensive triggers are warm,
# defensive ones cool, temporal ones teal/gold, so the board reads at a glance.
TRIGGER_FAMILY = {
    "onHit": "strike", "onAttack": "strike", "onProjectileFired": "strike",
    "onCrit": "crit", "onKill": "reap", "onSummon": "reap",
    "onStatusApplied": "affliction", "onShieldBreak": "breaker",
    "onDamageTaken": "guard", "onBlock": "guard", "onLowHp": "guard",
    "onTimer": "time", "onBattleStart": "herald", "onWaveStart": "herald",
}
TRIGGER_COLORS = {  # (glow, dark) — glow is the emissive inlay, dark the recess
    "strike": ("#FF8A3A", "#6A2208"), "crit": ("#FF4A5A", "#6A0E1A"), "reap": ("#C47AFF", "#3A1466"),
    "affliction": ("#A6E22E", "#2E4A0E"), "breaker": ("#5CE1E6", "#0E4A5A"), "guard": ("#5C9AFF", "#10285A"),
    "time": ("#3AE0B0", "#0A4A3A"), "herald": ("#FFD24A", "#6A4A08"),
}


def rune_glyph(rune_id, grid=(3, 4), strokes=None):
    """Deterministic angular glyph: 3-5 strokes on a lattice, mirrored per seed.
    Returns (prims, signature) — the signature (set of lattice edges) is used
    to guarantee every rune gets a different glyph."""
    seed = int(hashlib.sha256(("rune|" + rune_id).encode()).hexdigest()[:8], 16)
    rng = random.Random(seed)
    gx, gy = grid
    pts = [(x, y) for x in range(gx) for y in range(gy)]
    def to_xy(p):
        return (-0.55 + 1.1 * p[0] / (gx - 1), -0.75 + 1.5 * p[1] / (gy - 1))
    edges = set()
    prims = []
    # a spine (vertical stave) is common to runic alphabets: keep it most of the time
    spine = None
    if rng.random() < 0.8:
        x = rng.choice([0, 1, 1, 2])
        spine = x
        y0, y1 = 0, gy - 1
        prims.append(("line", [to_xy((x, y0)), to_xy((x, y1))], 0.1))
        edges.add(("v", x))
    n = strokes or rng.randint(2, 4)
    cur = rng.choice(pts)
    for _ in range(n):
        path = [cur]
        for _k in range(rng.randint(1, 2)):
            nxt = rng.choice([p for p in pts if p != path[-1] and abs(p[0] - path[-1][0]) <= 2 and abs(p[1] - path[-1][1]) <= 2])
            path.append(nxt)
            if not (spine is not None and path[-2][0] == spine and path[-1][0] == spine):  # hidden under the stave
                edges.add(tuple(sorted([path[-2], path[-1]])))
        prims.append(("line", [to_xy(p) for p in path], 0.09))
        cur = rng.choice(path) if rng.random() < 0.6 else rng.choice(pts)
    if rng.random() < 0.45:
        p = rng.choice(pts)
        prims.append(("disc", to_xy(p), 0.13))
        edges.add(("d", p))
    if rng.random() < 0.3:
        p = to_xy(rng.choice(pts))
        prims.append(("ring", p, 0.2, 0.06))
        edges.add(("r", p))
    return prims, frozenset(edges)


def all_rune_glyphs(rune_ids):
    """Glyphs for every rune, re-rolling (with a salted id) until unique."""
    seen, out = set(), {}
    for rid in rune_ids:
        salt = 0
        while True:
            prims, sig = rune_glyph(rid + ("" if salt == 0 else f"#{salt}"))
            if sig not in seen and sum(1 for e in sig if e[0] not in ("d", "r")) >= 4:
                break
            salt += 1
        seen.add(sig)
        out[rid] = prims
    return out
