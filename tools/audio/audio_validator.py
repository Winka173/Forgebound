#!/usr/bin/env python3
"""Audio validator: every clip id referenced by code/content exists; levels, durations, silence and loop seams are sane.
Exit code 1 on FAIL. Writes reports/audio/audio_validation.json/.md. Automated checks don't certify mix quality."""
import json, os, re, sys, wave
import numpy as np
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
AUD = os.path.join(ROOT, "Assets", "Forgebound", "Resources", "Audio")

def referenced_ids():
    ids = set()
    pres = os.path.join(ROOT, "Assets", "Forgebound", "Presentation")
    for dp, _, fs in os.walk(pres):
        for f in fs:
            if f.endswith(".cs"):
                src = open(os.path.join(dp, f), encoding="utf-8").read()
                ids.update(re.findall(r'(?:Play|Ui|Stinger|Music)\(\s*"([a-z0-9_]+)"', src))
    cdir = os.path.join(ROOT, "Assets", "Forgebound", "Resources", "Content")
    for name, field in (("frames", "sfx"), ("cores", "sfx"), ("biomes", "music")):
        txt = "\n".join(l for l in open(os.path.join(cdir, name + ".json"), encoding="utf-8").read().splitlines() if not l.strip().startswith("//"))
        for it in json.loads(txt)["items"]:
            if it.get(field): ids.add(it[field])
    return ids

def load(path):
    with wave.open(path) as w:
        sr = w.getframerate()
        x = np.frombuffer(w.readframes(w.getnframes()), dtype="<i2").astype(float) / 32768
    return x, sr

def main():
    issues, rows = [], []
    for cid in sorted(referenced_ids()):
        p = os.path.join(AUD, cid + ".wav")
        if not os.path.exists(p):
            issues.append(("FAIL", cid, "missing clip referenced by code/content")); continue
        x, sr = load(p)
        dur, peak = len(x) / sr, float(np.max(np.abs(x)))
        rms = float(np.sqrt(np.mean(x ** 2)))
        music = cid.startswith("mus_")
        rows.append({"id": cid, "seconds": round(dur, 3), "peak": round(peak, 3), "rms": round(rms, 4)})
        if peak >= 0.99: issues.append(("FAIL", cid, "clipping (peak %.3f)" % peak))
        if peak < 0.05 or rms < 0.003: issues.append(("FAIL", cid, "silent or near-silent"))
        if not music and dur > 2.5: issues.append(("WARN", cid, "SFX longer than 2.5 s (%.2f)" % dur))
        if music and dur < 8: issues.append(("FAIL", cid, "music loop shorter than 8 s"))
        if music and abs(x[0] - x[-1]) > 0.08: issues.append(("WARN", cid, "loop seam jump %.3f" % abs(x[0] - x[-1])))
        if not music and abs(x[-1]) > 0.02: issues.append(("WARN", cid, "clip does not end near zero (click risk)"))
    fails = sum(1 for i in issues if i[0] == "FAIL")
    os.makedirs(os.path.join(ROOT, "reports", "audio"), exist_ok=True)
    json.dump({"clips": rows, "issues": [{"level": a, "id": b, "msg": c} for a, b, c in issues]}, open(os.path.join(ROOT, "reports", "audio", "audio_validation.json"), "w"), indent=1)
    with open(os.path.join(ROOT, "reports", "audio", "audio_validation.md"), "w") as f:
        f.write("# Audio validation\n\n%d referenced clips checked, %d FAIL, %d WARN.\n\nAutomated checks don't certify mix quality; listen-through review required (docs/AUDIO.md).\n\n" % (len(rows), fails, len(issues) - fails))
        for i in issues: f.write("- %s `%s`: %s\n" % i)
    print("%d clips, %d FAIL, %d WARN" % (len(rows), fails, len(issues) - fails))
    for i in issues: print(" ", *i)
    return 1 if fails else 0

if __name__ == "__main__":
    sys.exit(main())
