"""Generate the project's original, seamless starter ambience (no external samples)."""
from pathlib import Path
import math
import random
import struct
import wave

RATE = 22050
SECONDS = 12
OUTPUT = Path(__file__).resolve().parents[1] / "Lumenfall/Assets/_Project/Art/Audio"


def generate(kind):
    rng = random.Random({"Forest": 31, "Campus": 47, "Interior": 59}[kind])
    size = RATE * SECONDS
    # Integer-cycle partials make the background continuous at the loop boundary.
    partials = [(rng.randint(60, 2400), rng.random() * math.tau,
                 0.006 / (1 + i * 0.08)) for i in range(64)]
    samples = []
    for index in range(size):
        t = index / RATE
        wind = sum(math.sin(math.tau * cycles * t / SECONDS + phase) * gain
                   for cycles, phase, gain in partials)
        wind *= 0.65 + 0.2 * math.sin(math.tau * t / SECONDS)
        value = wind * (0.8 if kind == "Forest" else 0.5 if kind == "Campus" else 0.15)
        if kind == "Interior":
            value += 0.018 * math.sin(math.tau * 120 * t)
            value += 0.009 * math.sin(math.tau * 180 * t)
        else:
            # Short synthetic bird calls with smooth envelopes; all finish before the seam.
            starts = (1.1, 3.7, 7.2, 9.4) if kind == "Forest" else (2.5, 8.3)
            for start in starts:
                local = t - start
                if 0 <= local < 0.45:
                    envelope = math.sin(math.pi * local / 0.45) ** 2
                    phase = math.tau * (2100 * local + 450 * local * local)
                    value += (0.08 if kind == "Forest" else 0.035) * envelope * math.sin(phase)
        samples.append(struct.pack("<h", int(max(-1, min(1, value)) * 32767)))
    with wave.open(str(OUTPUT / f"{kind}Ambience.wav"), "wb") as audio:
        audio.setnchannels(1)
        audio.setsampwidth(2)
        audio.setframerate(RATE)
        audio.writeframes(b"".join(samples))


if __name__ == "__main__":
    OUTPUT.mkdir(parents=True, exist_ok=True)
    for ambience in ("Forest", "Campus", "Interior"):
        generate(ambience)
        print(f"Generated {ambience}Ambience.wav")
