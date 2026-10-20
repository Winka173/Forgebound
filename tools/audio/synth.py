#!/usr/bin/env python3
"""Forgebound procedural audio: original SFX + music loops synthesised from code (no samples, no third-party audio).

Every clip id referenced by the game (content sfx fields + Presentation code) is generated deterministically from a
seed so `python3 tools/audio/synth.py` reproduces the same WAVs. Output: Assets/Forgebound/Resources/Audio/<id>.wav
+ audio_manifest.json (provenance, parameters, sha256, loudness). Then run tools/audio/audio_validator.py.

Quality note: this is a legitimate, provenance-safe baseline ("forge-y" clanks, whooshes, magical tones, simple
modal loops). It is NOT a substitute for a sound designer's final mix; see docs/AUDIO.md review checkpoints.
"""
import hashlib, json, math, os, struct, sys, wave
import numpy as np

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Assets", "Forgebound", "Resources", "Audio")
SR = 44100
MUSIC_SR = 22050


def rng(seed):
    return np.random.default_rng(seed)


def env(n, a=0.005, d=0.2, sustain=0.0, sr=SR):
    """Attack / exponential decay envelope of n samples."""
    t = np.arange(n) / sr
    att = np.clip(t / max(a, 1e-4), 0, 1)
    dec = np.exp(-t / max(d, 1e-4))
    return att * (sustain + (1 - sustain) * dec)


def tone(freq, dur, sr=SR, kind="sine", phase=0.0):
    t = np.arange(int(dur * sr)) / sr
    f = np.asarray(freq) if np.ndim(freq) else np.full_like(t, freq)
    ph = 2 * np.pi * np.cumsum(f) / sr + phase
    if kind == "sine":
        return np.sin(ph)
    if kind == "tri":
        return 2 / np.pi * np.arcsin(np.sin(ph))
    if kind == "saw":
        return 2 * ((ph / (2 * np.pi)) % 1.0) - 1
    if kind == "square":
        return np.sign(np.sin(ph))
    raise ValueError(kind)


def noise(dur, seed, sr=SR, color="white"):
    r = rng(seed)
    x = r.standard_normal(int(dur * sr))
    if color == "pink":
        # Voss-ish: cumulative filtered white
        b = np.array([0.049922035, -0.095993537, 0.050612699, -0.004408786])
        a = np.array([1, -2.494956002, 2.017265875, -0.522189400])
        x = lfilter(b, a, x)
    elif color == "brown":
        x = np.cumsum(x)
        x -= np.convolve(x, np.ones(2000) / 2000, mode="same")
    return x / (np.max(np.abs(x)) + 1e-9)


def lfilter(b, a, x):
    """Direct-form I IIR filter (scipy-free)."""
    y = np.zeros_like(x)
    nb, na = len(b), len(a)
    for i in range(len(x)):
        acc = 0.0
        for j in range(nb):
            if i - j >= 0:
                acc += b[j] * x[i - j]
        for j in range(1, na):
            if i - j >= 0:
                acc -= a[j] * y[i - j]
        y[i] = acc / a[0]
    return y


def onepole(x, cutoff, sr=SR, high=False):
    """Fast one-pole low/high-pass (vectorised via recursion in blocks is overkill; loop is fine for short clips)."""
    k = math.exp(-2 * math.pi * cutoff / sr)
    y = np.empty_like(x)
    s = 0.0
    for i, v in enumerate(x):
        s = (1 - k) * v + k * s
        y[i] = s
    return x - y if high else y


def bandpass(x, lo, hi, sr=SR):
    return onepole(onepole(x, hi, sr), lo, sr, high=True)


def metal(freq, dur, seed, inharm=(1.0, 2.76, 5.40, 8.93, 13.34), decay=0.6, sr=SR):
    """Inharmonic partial stack — struck metal / anvil."""
    r = rng(seed)
    n = int(dur * sr)
    out = np.zeros(n)
    for i, ratio in enumerate(inharm):
        f = freq * ratio * (1 + r.uniform(-0.01, 0.01))
        amp = 1.0 / (1 + i * 0.8)
        out += amp * tone(f, dur, sr) * env(n, 0.001, decay / (1 + i * 0.6), sr=sr)
    return out


def sweep(f0, f1, dur, sr=SR, kind="sine"):
    n = int(dur * sr)
    f = np.geomspace(max(f0, 1), max(f1, 1), n)
    return tone(f, dur, sr, kind)


def mix(*parts):
    n = max(len(p) for p in parts)
    out = np.zeros(n)
    for p in parts:
        out[: len(p)] += p
    return out


def pad(x, dur, sr=SR):
    n = int(dur * sr)
    return np.pad(x, (0, max(0, n - len(x))))[:n]


def normalize(x, peak=0.89):
    m = np.max(np.abs(x)) + 1e-9
    return x / m * peak


def fade(x, inn=0.002, out=0.03, sr=SR):
    n = len(x)
    a, b = int(inn * sr), int(out * sr)
    if a > 0:
        x[:a] *= np.linspace(0, 1, a)
    if b > 0:
        x[-b:] *= np.linspace(1, 0, b)
    return x


# ----------------------------------------------------------------------------- SFX recipes
def whoosh(seed, dur=0.18, lo=500, hi=4000, weight=1.0):
    n = noise(dur, seed)
    n = bandpass(n, lo, hi)
    e = np.sin(np.linspace(0, np.pi, len(n))) ** 2
    return n * e * weight


def impact(seed, low=80, dur=0.25, crunch=0.5):
    body = tone(sweep(low * 2.2, low, dur), dur) if False else sweep(low * 2.2, low, dur) * env(int(dur * SR), 0.001, dur / 3)
    click = noise(0.03, seed) * env(int(0.03 * SR), 0.0005, 0.008)
    grit = bandpass(noise(dur, seed + 1), 800, 5000) * env(int(dur * SR), 0.001, 0.05) * crunch
    return mix(body, click * 0.6, grit)


def sfx_frame(fid, seed):
    """Six+ distinct frame attack sounds (spec: Hammer heavy, Dagger crisp, projectiles readable)."""
    if fid == "sword":
        return mix(whoosh(seed, 0.16, 900, 5000), metal(1400, 0.3, seed, decay=0.25) * 0.25)
    if fid == "greatsword":
        return mix(whoosh(seed, 0.32, 300, 2500, 1.3), impact(seed, 70, 0.35, 0.6) * 0.8)
    if fid == "bow":
        twang = tone(sweep(420, 300, 0.25), 0.25) if False else sweep(420, 300, 0.25) * env(int(0.25 * SR), 0.001, 0.08)
        return mix(twang * 0.7, whoosh(seed, 0.2, 2000, 7000, 0.6))
    if fid == "staff":
        return mix(sweep(300, 900, 0.3, kind="tri") * env(int(0.3 * SR), 0.02, 0.15) * 0.6, bandpass(noise(0.3, seed), 1500, 6000) * env(int(0.3 * SR), 0.05, 0.1) * 0.4)
    if fid == "hammer":
        return mix(impact(seed, 55, 0.45, 0.9), metal(220, 0.6, seed, decay=0.5) * 0.45)
    if fid == "dagger":
        return mix(whoosh(seed, 0.08, 2500, 9000, 0.8), metal(2600, 0.12, seed, decay=0.06) * 0.3)
    if fid == "spear":
        return mix(whoosh(seed, 0.14, 1200, 6000), impact(seed, 120, 0.15, 0.3) * 0.5)
    if fid == "crossbow":
        clack = metal(900, 0.15, seed, decay=0.05)
        return mix(clack * 0.6, pad(np.zeros(1), 0.04), np.concatenate([np.zeros(int(0.03 * SR)), whoosh(seed, 0.18, 1500, 8000, 0.8)]))
    if fid == "scythe":
        return mix(whoosh(seed, 0.38, 400, 3000, 1.1), sweep(900, 400, 0.38) * env(int(0.38 * SR), 0.05, 0.15) * 0.2)
    if fid == "bulwark":
        return mix(impact(seed, 90, 0.3, 0.4), metal(600, 0.35, seed, decay=0.2) * 0.5)
    if fid == "whip":
        crack = bandpass(noise(0.05, seed), 2000, 10000) * env(int(0.05 * SR), 0.0005, 0.01)
        return mix(whoosh(seed, 0.2, 800, 5000, 0.7), np.concatenate([np.zeros(int(0.15 * SR)), crack * 1.4]))
    if fid == "tome":
        chord = sum(tone(f, 0.6, kind="sine") for f in (330, 415, 494)) / 3
        return mix(chord * env(int(0.6 * SR), 0.05, 0.25) * 0.7, bandpass(noise(0.6, seed), 3000, 9000) * env(int(0.6 * SR), 0.1, 0.2) * 0.25)
    return whoosh(seed)


CORE_SOUNDS = {
    "fire": lambda s: mix(bandpass(noise(0.4, s, color="brown"), 100, 1800) * env(int(0.4 * SR), 0.02, 0.2), bandpass(noise(0.4, s + 1), 3000, 8000) * env(int(0.4 * SR), 0.01, 0.08) * 0.3),
    "frost": lambda s: mix(metal(2200, 0.5, s, inharm=(1, 1.5, 2.3, 3.1), decay=0.3) * 0.6, bandpass(noise(0.5, s), 5000, 11000) * env(int(0.5 * SR), 0.01, 0.15) * 0.3),
    "lightning": lambda s: mix(bandpass(noise(0.25, s), 1000, 9000) * (np.abs(tone(37, 0.25, kind="square")) * env(int(0.25 * SR), 0.001, 0.08)), sweep(2000, 200, 0.25, kind="saw") * env(int(0.25 * SR), 0.001, 0.05) * 0.2),
    "earth": lambda s: impact(s, 45, 0.45, 1.0),
    "life": lambda s: sum(tone(f, 0.5) * env(int(0.5 * SR), 0.03 + i * 0.03, 0.25) for i, f in enumerate((523, 659, 784))) / 3,
    "shadow": lambda s: mix(sweep(220, 110, 0.5, kind="saw") * env(int(0.5 * SR), 0.05, 0.2) * 0.35, bandpass(noise(0.5, s), 200, 1200) * env(int(0.5 * SR), 0.1, 0.2) * 0.5),
    "venom": lambda s: bandpass(noise(0.35, s), 400, 2500) * (0.6 + 0.4 * tone(18, 0.35)) * env(int(0.35 * SR), 0.01, 0.12),
    "arcane": lambda s: mix(sweep(600, 1200, 0.4, kind="tri") * env(int(0.4 * SR), 0.01, 0.15) * 0.5, tone(1800, 0.4) * env(int(0.4 * SR), 0.05, 0.1) * 0.2),
    "wind": lambda s: whoosh(s, 0.45, 300, 3000, 1.0),
    "steel": lambda s: metal(1800, 0.4, s, decay=0.25),
    "solar": lambda s: sum(tone(f, 0.6, kind="tri") * env(int(0.6 * SR), 0.02, 0.3) for f in (440, 660, 880, 1320)) / 4,
    "void": lambda s: mix(sweep(90, 40, 0.6) * env(int(0.6 * SR), 0.05, 0.3), bandpass(noise(0.6, s, color="brown"), 40, 400) * env(int(0.6 * SR), 0.05, 0.25) * 0.7),
}


GENERIC = {
    "ui_click": lambda s: metal(2400, 0.06, s, decay=0.02) * 0.5,
    "ui_error": lambda s: (tone(180, 0.15, kind="square") * env(int(0.15 * SR), 0.002, 0.06)) * 0.3,
    "ui_confirm": lambda s: mix(tone(660, 0.12) * env(int(0.12 * SR), 0.002, 0.05), np.concatenate([np.zeros(int(0.06 * SR)), tone(990, 0.12) * env(int(0.12 * SR), 0.002, 0.06)])) * 0.5,
    "ui_node": lambda s: mix(metal(700, 0.3, s, decay=0.12) * 0.4, whoosh(s, 0.2, 800, 4000, 0.4)),
    "sfx_hit_generic": lambda s: impact(s, 140, 0.12, 0.5) * 0.7,
    "sfx_enemy_attack": lambda s: whoosh(s, 0.15, 400, 2500, 0.7),
    "sfx_crit": lambda s: mix(impact(s, 100, 0.2, 0.8), metal(3000, 0.25, s, decay=0.1) * 0.5),
    "sfx_hero_hurt": lambda s: mix(impact(s, 70, 0.2, 0.4), tone(sweep(300, 150, 0.2), 0.2) if False else sweep(300, 150, 0.2) * env(int(0.2 * SR), 0.005, 0.08) * 0.3),
    "sfx_shield_break": lambda s: mix(metal(1500, 0.45, s, inharm=(1, 1.7, 2.9, 4.6), decay=0.2), bandpass(noise(0.45, s), 3000, 10000) * env(int(0.45 * SR), 0.001, 0.1) * 0.6),
    "sfx_enemy_death": lambda s: mix(bandpass(noise(0.4, s), 200, 2000) * env(int(0.4 * SR), 0.005, 0.15), sweep(400, 80, 0.4) * env(int(0.4 * SR), 0.005, 0.15) * 0.4),
    "sfx_rune_proc": lambda s: mix(tone(1320, 0.25) * env(int(0.25 * SR), 0.002, 0.08), tone(1980, 0.25) * env(int(0.25 * SR), 0.01, 0.06) * 0.5) * 0.6,
    "sfx_relay_fire": lambda s: mix(sweep(500, 2500, 0.18, kind="tri") * env(int(0.18 * SR), 0.002, 0.08), bandpass(noise(0.18, s), 4000, 9000) * env(int(0.18 * SR), 0.001, 0.04) * 0.4),
    "sfx_telegraph": lambda s: (tone(110, 0.6, kind="saw") * (0.5 + 0.5 * tone(6, 0.6))) * env(int(0.6 * SR), 0.05, 0.4) * 0.5,
    "sfx_slam": lambda s: impact(s, 40, 0.7, 1.2),
    "sfx_explosion": lambda s: mix(bandpass(noise(0.6, s, color="brown"), 30, 1500) * env(int(0.6 * SR), 0.002, 0.25), impact(s, 50, 0.4, 0.8) * 0.6),
    "sfx_legendary_signature": lambda s: mix(sum(tone(f, 0.7, kind="tri") * env(int(0.7 * SR), 0.01, 0.35) for f in (392, 587, 784)) / 3, whoosh(s, 0.5, 600, 6000, 0.5)),
    "sfx_slot_click": lambda s: mix(metal(1600, 0.1, s, decay=0.03), impact(s, 300, 0.05, 0.2) * 0.4),
    "sfx_rotate": lambda s: metal(900, 0.12, s, decay=0.04) * 0.6,
    "sfx_forge_assemble": lambda s: mix(metal(500, 0.5, s, decay=0.3), np.concatenate([np.zeros(int(0.12 * SR)), metal(760, 0.4, s + 1, decay=0.25) * 0.7])),
    "sfx_unsocket": lambda s: sweep(1200, 600, 0.15, kind="tri") * env(int(0.15 * SR), 0.002, 0.06) * 0.5,
    "sfx_core_attach": lambda s: mix(metal(1100, 0.35, s, decay=0.15), sum(tone(f, 0.35) * env(int(0.35 * SR), 0.03, 0.15) for f in (660, 880)) * 0.3),
    "sfx_modifier_attach": lambda s: mix(metal(700, 0.25, s, decay=0.1), impact(s, 200, 0.1, 0.3) * 0.5),
    "sfx_rune_etch": lambda s: bandpass(noise(0.45, s), 2500, 8000) * (0.5 + 0.5 * np.abs(tone(22, 0.45))) * env(int(0.45 * SR), 0.01, 0.2) * 0.7,
    "sfx_buy": lambda s: mix(metal(2000, 0.2, s, inharm=(1, 2.0, 3.0), decay=0.08), np.concatenate([np.zeros(int(0.07 * SR)), metal(2600, 0.2, s + 2, inharm=(1, 2.0, 3.0), decay=0.08)])) * 0.6,
    "sfx_sell": lambda s: metal(1500, 0.25, s, inharm=(1, 2.0, 3.0), decay=0.1) * 0.6,
    "sfx_temper": lambda s: mix(bandpass(noise(0.7, s), 2000, 9000) * env(int(0.7 * SR), 0.01, 0.35) * 0.5, metal(400, 0.7, s, decay=0.4) * 0.6),
    "sfx_reroll": lambda s: mix(*[np.concatenate([np.zeros(int(i * 0.04 * SR)), metal(1800 + i * 200, 0.08, s + i, decay=0.03) * 0.4]) for i in range(4)]),
    "sfx_board_expand": lambda s: mix(impact(s, 90, 0.4, 0.5), sweep(200, 600, 0.4, kind="tri") * env(int(0.4 * SR), 0.05, 0.2) * 0.3),
    "sfx_rest": lambda s: bandpass(noise(1.2, s, color="pink"), 200, 3000) * env(int(1.2 * SR), 0.2, 0.6) * 0.6,
    "sfx_reward": lambda s: mix(*[np.concatenate([np.zeros(int(i * 0.08 * SR)), tone(f, 0.3) * env(int(0.3 * SR), 0.003, 0.12)]) for i, f in enumerate((523, 659, 784))]) / 2,
    "sting_boss_phase": lambda s: mix(impact(s, 35, 1.2, 1.0), (tone(55, 1.2, kind="saw") + tone(82.4, 1.2, kind="saw")) * env(int(1.2 * SR), 0.02, 0.6) * 0.3),
    "sting_revive": lambda s: sum(tone(f, 1.0, kind="tri") * env(int(1.0 * SR), 0.05, 0.5) for f in (440, 554, 659, 880)) / 4,
    "sting_victory": lambda s: mix(*[np.concatenate([np.zeros(int(i * 0.15 * SR)), tone(f, 0.8, kind="tri") * env(int(0.8 * SR), 0.01, 0.4)]) for i, f in enumerate((392, 494, 587, 784))]) / 3,
    "sting_defeat": lambda s: mix(*[np.concatenate([np.zeros(int(i * 0.25 * SR)), tone(f, 1.0, kind="saw") * env(int(1.0 * SR), 0.02, 0.5)]) for i, f in enumerate((220, 207.6, 196, 146.8))]) / 5,
    "sting_run_start": lambda s: mix(metal(300, 1.0, s, decay=0.6), sum(tone(f, 1.0, kind="tri") * env(int(1.0 * SR), 0.1, 0.5) for f in (196, 294)) * 0.3),
    "sting_legendary": lambda s: mix(mix(*[np.concatenate([np.zeros(int(i * 0.1 * SR)), tone(f, 1.4, kind="tri") * env(int(1.4 * SR), 0.01, 0.7)]) for i, f in enumerate((392, 523, 659, 784, 1047))]) / 4, metal(260, 1.4, s, decay=0.8) * 0.5),
}


# ----------------------------------------------------------------------------- music
SCALES = {"aeolian": [0, 2, 3, 5, 7, 8, 10], "dorian": [0, 2, 3, 5, 7, 9, 10], "phrygian": [0, 1, 3, 5, 7, 8, 10], "lydian": [0, 2, 4, 6, 7, 9, 11]}
MUSIC = {
    "mus_workshop": dict(root=50, scale="dorian", bpm=84, bars=8, prog=[0, 3, 4, 3], drums=False, timbre="tri", seed=11),
    "mus_ember": dict(root=45, scale="phrygian", bpm=104, bars=8, prog=[0, 1, 0, 5], drums=True, timbre="saw", seed=21),
    "mus_verdant": dict(root=52, scale="dorian", bpm=92, bars=8, prog=[0, 4, 5, 3], drums=True, timbre="tri", seed=31),
    "mus_frost": dict(root=57, scale="aeolian", bpm=80, bars=8, prog=[0, 5, 3, 6], drums=False, timbre="sine", seed=41),
    "mus_crypt": dict(root=43, scale="phrygian", bpm=76, bars=8, prog=[0, 1, 5, 0], drums=True, timbre="tri", seed=51),
    "mus_storm": dict(root=48, scale="aeolian", bpm=118, bars=8, prog=[0, 6, 5, 4], drums=True, timbre="saw", seed=61),
    "mus_boss": dict(root=40, scale="phrygian", bpm=132, bars=8, prog=[0, 1, 0, 6], drums=True, timbre="saw", seed=71),
}


def midi(n):
    return 440.0 * 2 ** ((n - 69) / 12)


def music(params):
    sr = MUSIC_SR
    r = rng(params["seed"])
    beat = 60.0 / params["bpm"]
    bar = 4 * beat
    total = params["bars"] * bar
    n = int(total * sr)
    out = np.zeros(n)
    scale = SCALES[params["scale"]]

    def deg(d, octv=0):
        return params["root"] + 12 * (octv + d // 7) + scale[d % 7]

    for b in range(params["bars"]):
        chord_root = params["prog"][b % len(params["prog"])]
        t0 = int(b * bar * sr)
        # pad
        for d in (0, 2, 4):
            f = midi(deg(chord_root + d, 1))
            x = tone(f, bar, sr, params["timbre"]) * 0.5 + tone(f * 1.003, bar, sr, "sine") * 0.5
            x *= np.minimum(1, np.minimum(np.arange(len(x)) / (0.25 * sr), (len(x) - np.arange(len(x))) / (0.25 * sr)))
            out[t0:t0 + len(x)] += x * 0.08
        # bass on beats
        for q in range(4):
            f = midi(deg(chord_root, -1))
            x = tone(f, beat * 0.9, sr, "tri") * env(int(beat * 0.9 * sr), 0.005, beat * 0.6, sr=sr)
            s = t0 + int(q * beat * sr)
            out[s:s + len(x)] += x[: max(0, n - s)] * 0.22
        # melody: seeded walk on 8ths
        cur = r.integers(0, 5)
        for e8 in range(8):
            if r.random() < 0.35:
                continue
            cur = int(np.clip(cur + r.integers(-2, 3), 0, 9))
            f = midi(deg(chord_root + cur, 2))
            d = beat * 0.5
            x = tone(f, d, sr, "tri") * env(int(d * sr), 0.01, d * 0.5, sr=sr)
            s = t0 + int(e8 * d * sr)
            out[s:s + len(x)] += x[: max(0, n - s)] * 0.1
        if params["drums"]:
            for q in range(4):
                s = t0 + int(q * beat * sr)
                if q % 2 == 0:
                    k = sweep(120, 45, 0.25, sr) * env(int(0.25 * sr), 0.001, 0.08, sr=sr)
                else:
                    k = bandpass(noise(0.2, params["seed"] + b * 4 + q, sr), 1500, 6000, sr) * env(int(0.2 * sr), 0.001, 0.05, sr=sr) * 0.5
                out[s:s + len(k)] += k[: max(0, n - s)] * 0.35
                hs = s + int(beat * 0.5 * sr)
                h = bandpass(noise(0.05, params["seed"] + 999 + b * 4 + q, sr), 6000, 10000, sr) * env(int(0.05 * sr), 0.0005, 0.01, sr=sr) * 0.15
                out[hs:hs + len(h)] += h[: max(0, n - hs)]
    # loop seam: crossfade tail into head so the loop is click-free
    xf = int(0.05 * sr)
    out[:xf] = out[:xf] * np.linspace(0, 1, xf) + out[-xf:] * np.linspace(1, 0, xf)
    out = out[:-xf]
    return normalize(out, 0.8), sr


# ----------------------------------------------------------------------------- driver
def content_sfx_ids():
    ids = {}
    def load(name):
        txt = open(os.path.join(ROOT, "Assets", "Forgebound", "Resources", "Content", name + ".json"), encoding="utf-8").read()
        txt = "\n".join(l for l in txt.splitlines() if not l.strip().startswith("//"))
        return json.loads(txt)["items"]
    for f in load("frames"):
        if f.get("sfx"):
            ids[f["sfx"]] = ("frame", f["id"])
    for c in load("cores"):
        if c.get("sfx"):
            ids[c["sfx"]] = ("core", c["id"])
    return ids


def write_wav(path, x, sr):
    x = np.clip(x, -1, 1)
    data = (x * 32767).astype("<i2").tobytes()
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(sr)
        w.writeframes(data)
    return hashlib.sha256(open(path, "rb").read()).hexdigest()


def seed_of(clip_id):
    return int(hashlib.sha1(clip_id.encode()).hexdigest()[:8], 16)


def main():
    os.makedirs(OUT, exist_ok=True)
    manifest = {"schema": "forgebound.audio.manifest/1", "generator": "tools/audio/synth.py", "license": "Original procedural audio generated by the Forgebound pipeline (project-owned, CC0-like)", "clips": []}
    jobs = []
    for cid, (kind, ref) in sorted(content_sfx_ids().items()):
        jobs.append((cid, "sfx_" + kind, (lambda c=cid, k=kind, r=ref: (sfx_frame(r, seed_of(c)) if k == "frame" else CORE_SOUNDS[r](seed_of(c))))))
    for cid, fn in sorted(GENERIC.items()):
        jobs.append((cid, "sfx_ui" if cid.startswith("ui_") else ("stinger" if cid.startswith("sting_") else "sfx"), (lambda f=fn, c=cid: f(seed_of(c)))))
    for cid in sorted(MUSIC):
        jobs.append((cid, "music", None))
    for cid, cat, fn in jobs:
        if cat == "music":
            x, sr = music(MUSIC[cid])
            params = MUSIC[cid]
        else:
            x = fade(normalize(np.asarray(fn(), dtype=float), 0.89 if cat != "sfx_ui" else 0.6))
            sr, params = SR, {"seed": seed_of(cid)}
        path = os.path.join(OUT, cid + ".wav")
        sha = write_wav(path, x, sr)
        rms = float(np.sqrt(np.mean(x ** 2)))
        manifest["clips"].append({"id": cid, "category": cat, "file": "Audio/" + cid + ".wav", "sample_rate": sr, "seconds": round(len(x) / sr, 3),
                                  "peak": round(float(np.max(np.abs(x))), 4), "rms_dbfs": round(20 * math.log10(rms + 1e-9), 2), "params": params, "sha256": sha})
        print(f"{cid:28s} {cat:8s} {len(x)/sr:6.2f}s")
    json.dump(manifest, open(os.path.join(OUT, "audio_manifest.json"), "w"), indent=1)
    print(len(manifest["clips"]), "clips")


if __name__ == "__main__":
    main()
