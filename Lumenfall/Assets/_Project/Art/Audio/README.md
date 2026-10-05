# Starter ambience provenance

`ForestAmbience.wav`, `CampusAmbience.wav`, and `InteriorAmbience.wav` are original
procedural starter loops generated for this repository by `scripts/generate_area_ambience.py`.
They use mathematical partials and synthetic chirps; they contain no external recordings,
samples, or licensed third-party content. They are covered by the repository LICENSE.

Each file is a 12-second, mono, 16-bit PCM WAV at 22,050 Hz. Background oscillators use
whole cycles across the loop and chirps finish before the seam. Forest has soft wind and
frequent synthetic birds, campus has quieter outdoor wind/birds, and interiors use quiet
room tone. These are editable starter ambience, not final environmental recordings.

Regenerate deterministically from the repository root with:

```sh
python scripts/generate_area_ambience.py
```

Keep the existing `.meta` files when replacing loops so profile references remain valid.
Unity imports these short mono clips as preloaded PCM; the audio controller sets looping
and 2D playback at runtime. Per-area gains live in the three AreaAudioProfile assets.
