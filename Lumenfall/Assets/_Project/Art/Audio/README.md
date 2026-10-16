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

## Supplied forest track

`lumenForest.wav` is the user-supplied forest audio, preserved byte-for-byte from the
provided file. It is a 74.76925-second, stereo, 24-bit PCM WAV at 48,000 Hz. Its existing
Unity import settings use streaming Vorbis (quality 0.7), background loading, and no
preloading. The approved `music.forest` cue already uses the same audio, so the Area Audio
Forest profile uses the distinct `ForestAmbience.wav` starter loop to avoid playing the
same track through both the music and ambience buses. The generator only regenerates the
three starter loops above.
