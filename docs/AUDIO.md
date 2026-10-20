# Audio

* Generator: `tools/audio/synth.py` (numpy only) — inharmonic metal partials (anvil/clank), filtered noise (whoosh, hiss, fire),
  sweeps, FM-like bells, simple modal music loops per biome + workshop + boss. Deterministic seeds per clip id.
* Manifest: `Resources/Audio/audio_manifest.json` (id, category, seconds, peak, RMS dBFS, params, sha256, licence).
* Runtime: `AudioManager` — 16 voices, same clip ≤ 3 voices and ≥ 50 ms apart, voice stealing only for high priority,
  music cross-fade, ducking under stingers; volumes from settings.
* Validator: `tools/audio/audio_validator.py` — every clip referenced by code/content exists; no clipping/silence; SFX ≤ 2.5 s;
  loop seams; writes `reports/audio/`.
* Review checkpoints (manual): listen at low volume on phone speakers; 2x speed swarm fight for fatigue; boss stinger vs music;
  distinctness of the 12 frame attacks. Status: **pending human review** (KI-06).
