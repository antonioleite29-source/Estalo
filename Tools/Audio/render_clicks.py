#!/usr/bin/env python3
"""Renders the button-click variations Estalo ships.

The sound is a recipe, not a recording. This script is the same construction that was auditioned
in the browser -- a noise transient, a tonal body, a sub, then saturation and a small room -- so
regenerating is always possible and tweaking by ear means changing a number here rather than
hunting for a replacement sample.

    python3 render_clicks.py            # writes Assets/Resources/Click/Click01..10.wav

Nothing is sampled from anywhere, so there is no licence attached to any of it.
"""

import math
import os
import struct
import wave

import numpy as np

RATE = 44100
SECONDS = 0.34
COUNT = 10

# The spread across the ten. Doubled from the first pass: a third of a semitone was too subtle to
# do its job, which is to stop ten taps in a row sounding like one recording on a loop.
PITCH_SEMITONES = 0.66
LEVEL_SPREAD = 0.20
DECAY_SPREAD = 0.28


def variant(n):
    """Variation 0 is the sound as designed; the rest sit either side of it."""
    if n == 0:
        return dict(pitch=1.0, level=1.0, decay=1.0)

    spread = ((n % COUNT) - 4.5) / 4.5          # -1 .. 1
    return dict(
        pitch=2.0 ** (spread * PITCH_SEMITONES / 12.0),
        level=1.0 + spread * LEVEL_SPREAD,
        decay=1.0 + spread * DECAY_SPREAD,
    )


def envelope(frames, attack, peak, decay):
    """Matches the browser's exponentialRampToValueAtTime pair: up fast, then down."""
    t = np.arange(frames) / RATE
    floor = 1e-4
    out = np.full(frames, floor)

    rise = t < attack
    if attack > 0:
        out[rise] = floor * (peak / floor) ** (t[rise] / attack)

    fall = ~rise
    if decay > 0:
        out[fall] = peak * (floor / peak) ** ((t[fall] - attack) / decay)

    return np.clip(out, 0.0, peak)


def biquad(x, b, a):
    y = np.zeros_like(x)
    x1 = x2 = y1 = y2 = 0.0

    for i, sample in enumerate(x):
        out = b[0] * sample + b[1] * x1 + b[2] * x2 - a[1] * y1 - a[2] * y2
        x2, x1 = x1, sample
        y2, y1 = y1, out
        y[i] = out

    return y


def bandpass(x, freq, q):
    w = 2 * math.pi * freq / RATE
    alpha = math.sin(w) / (2 * q)
    b = [alpha, 0.0, -alpha]
    a = [1 + alpha, -2 * math.cos(w), 1 - alpha]
    return biquad(x, [v / a[0] for v in b], [1.0, a[1] / a[0], a[2] / a[0]])


def lowpass(x, freq):
    w = 2 * math.pi * freq / RATE
    alpha = math.sin(w) / (2 * 0.707)
    cos_w = math.cos(w)
    b = [(1 - cos_w) / 2, 1 - cos_w, (1 - cos_w) / 2]
    a = [1 + alpha, -2 * cos_w, 1 - alpha]
    return biquad(x, [v / a[0] for v in b], [1.0, a[1] / a[0], a[2] / a[0]])


def transient(frames, v, centre, q, decay, level):
    """The finger arriving: short filtered noise with no pitch to it."""
    rng = np.random.default_rng(1917)
    n = int(RATE * (decay * v["decay"] + 0.03))
    burst = bandpass(rng.uniform(-1, 1, n), centre * v["pitch"], q)
    burst *= envelope(n, 0.0012, level * v["level"], decay * v["decay"])

    out = np.zeros(frames)
    out[:n] = burst
    return out


def body(frames, v, wave_type, start, end, glide, attack, decay, level, lp=0):
    """What the object is made of. A falling pitch is a struck thing."""
    n = int(RATE * (attack + decay * v["decay"] + 0.25))
    n = min(n, frames)
    t = np.arange(n) / RATE

    if end:
        # Exponential glide, the same curve as exponentialRampToValueAtTime.
        ratio = (end / start) ** np.clip(t / glide, 0, 1)
        freq = start * v["pitch"] * ratio
    else:
        freq = np.full(n, start * v["pitch"])

    phase = 2 * np.pi * np.cumsum(freq) / RATE

    if wave_type == "triangle":
        tone = 2 / np.pi * np.arcsin(np.sin(phase))
    else:
        tone = np.sin(phase)

    if lp:
        tone = lowpass(tone, lp)

    tone *= envelope(n, attack, level * v["level"], decay * v["decay"])

    out = np.zeros(frames)
    out[:n] = tone
    return out


def stone(frames, v):
    """Two hard surfaces meeting: bright transient, dense body, a little weight underneath."""
    return (
        transient(frames, v, centre=5200, q=0.8, decay=0.013, level=0.32)
        + body(frames, v, "triangle", 620, 260, 0.025, 0.003, 0.06, 0.44, lp=2200)
        + body(frames, v, "sine", 110, 0, 0, 0.004, 0.09, 0.22)
    )


def saturate(x, amount=2.4):
    """Soft clipping. Adds harmonics, so a quiet sound reads as loud without being louder."""
    return np.tanh(x * amount) / math.tanh(amount)


def room(x):
    """90ms of decaying noise. Not meant to be heard; without it the sound stops dead."""
    n = int(RATE * 0.09)
    rng = np.random.default_rng(4242)
    impulse = rng.uniform(-1, 1, n) * (1 - np.arange(n) / n) ** 3.2
    wet = np.convolve(x, impulse)[: len(x)]

    peak = np.max(np.abs(wet))
    if peak > 0:
        wet /= peak

    return 0.92 * x + 0.16 * wet


def render(n, gain):
    frames = int(RATE * SECONDS)
    signal = room(saturate(stone(frames, variant(n)))) * gain

    # Trailing silence trimmed so the AudioSource is not holding samples nobody hears.
    loud = np.abs(signal) > 3e-4
    if loud.any():
        signal = signal[: int(np.flatnonzero(loud)[-1]) + 64]

    # 3ms fade out, so no variation ends on a step that clicks in its own right.
    fade = min(int(RATE * 0.003), len(signal))
    signal[-fade:] *= np.linspace(1, 0, fade)

    return signal


def common_gain():
    """One gain for all ten, taken from variation 0.

    Normalising each file to the same peak would undo a third of the work: the level difference
    between variations is deliberate, and per-file normalisation is exactly the operation that
    removes it. Headroom rather than the ceiling, because this plays under every tap.
    """
    frames = int(RATE * SECONDS)
    reference = room(saturate(stone(frames, variant(0))))
    peak = np.max(np.abs(reference))
    return 0.55 / peak if peak > 0 else 1.0


def write(path, signal):
    data = np.clip(signal, -1, 1)
    pcm = (data * 32767).astype("<i2")

    with wave.open(path, "wb") as out:
        out.setnchannels(1)
        out.setsampwidth(2)
        out.setframerate(RATE)
        out.writeframes(pcm.tobytes())


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    # Under Resources so the game can load all ten by name with no inspector wiring, the same way
    # LoadingScreenController finds its artwork. Ten short mono clips is about 90 KB.
    target = os.path.join(here, "..", "..", "Assets", "Resources", "Click")
    target = os.path.normpath(target)
    os.makedirs(target, exist_ok=True)

    gain = common_gain()

    for n in range(COUNT):
        signal = render(n, gain)
        path = os.path.join(target, f"Click{n + 1:02d}.wav")
        write(path, signal)
        print(f"  {os.path.basename(path)}  {len(signal) / RATE * 1000:5.0f} ms  peak {np.max(np.abs(signal)):.2f}")

    print(f"\n{COUNT} variations written to {target}")


if __name__ == "__main__":
    main()
