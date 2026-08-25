#!/usr/bin/env python3
"""Renders every sound Estalo makes.

Each one is a recipe, not a recording -- a noise transient, a tonal body, sometimes a sub, then
saturation and a small room. That means regenerating is always possible, and tuning by ear is
changing a number here rather than hunting for a replacement sample. Nothing is taken from a
library, so no licence is attached to any of it.

    python3 render_sounds.py

    Assets/Resources/Click/Click01..10.wav   the button, one pentatonic note per tap
    Assets/Resources/Match/Point.wav         you scored
    Assets/Resources/Match/Against.wav       they scored
    Assets/Resources/Match/Win.wav           you won
    Assets/Resources/Match/Lose.wav          you lost

Everything is drawn from C major pentatonic. It has no semitone steps in it, which is why the
button can play a different note on every tap without ever clashing with the match sounds.
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
# C major pentatonic, fifth octave. Five notes, no semitone steps -- which is the whole reason
# any of these can be played in any order without sounding wrong.
C5, D5, E5, G5, A5 = 523.25, 587.33, 659.25, 783.99, 880.00
C6, D6, E6, G6 = 1046.50, 1174.66, 1318.51, 1567.98
C4, E4, G4, A4 = 261.63, 329.63, 392.00, 440.00

# The button plays a note now rather than a pitch-shifted thud, and every note is from the same
# octave, so tapping around the interface is a melody in one register instead of a slide whistle.
CLICK_NOTES = [C5, D5, E5, G5, A5]

# Ten files from five notes: each note twice, with the level and length nudged so the two copies
# are not identical. The shuffle bag in ButtonClickSound then hands out all ten before repeating,
# which means all five notes are heard before any of them comes round again.
LEVEL_SPREAD = 0.14
DECAY_SPREAD = 0.20


def variant(n):
    """Which note this file is, and how it sits against its twin."""
    spread = 1.0 if n >= len(CLICK_NOTES) else -1.0
    return dict(
        note=CLICK_NOTES[n % len(CLICK_NOTES)],
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
    burst = bandpass(rng.uniform(-1, 1, n), centre, q)
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
        freq = start * ratio
    else:
        freq = np.full(n, start)

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


def click(frames, v):
    """The stone, tuned.

    The noise transient is left alone -- it has no pitch to tune, and it is what makes this read
    as something being struck rather than a note being played. Underneath it the body now holds a
    steady pentatonic note instead of bending, which is the difference between a thud and a pitch.
    """
    note = v["note"]

    return (
        transient(frames, v, centre=6000, q=0.8, decay=0.012, level=0.30)
        + body(frames, v, "triangle", note, 0, 0, 0.002, 0.075, 0.40, lp=note * 3.2)
        + body(frames, v, "sine", note * 2, 0, 0, 0.002, 0.045, 0.14)
        + body(frames, v, "sine", note / 4, 0, 0, 0.004, 0.07, 0.16)
    )


def note(frames, t0, freq, decay=0.25, level=0.3, kind="sine",
         attack=0.004, lp=0, partial=0.35):
    """One struck note: the fundamental plus a partial an octave and a fifth above it.

    A single sine is a test tone. Two, with the upper one decaying faster, is the shape almost
    every struck instrument actually has, and is the whole difference between "beep" and "bell".
    """
    out = shifted(frames, t0, body(frames, dict(level=1, decay=1), kind, freq, 0, 0,
                                   attack, decay, level, lp=lp))

    if partial > 0:
        out = out + shifted(frames, t0,
                            body(frames, dict(level=1, decay=1), kind, freq * 3, 0, 0,
                                 attack, decay * 0.45, level * partial,
                                 lp=lp * 2 if lp else 0))

    return out


def run(frames, t0, freqs, gap, **kw):
    """A sequence of notes, evenly spaced."""
    out = np.zeros(frames)
    for i, freq in enumerate(freqs):
        out = out + note(frames, t0 + i * gap, freq, **kw)
    return out


def shifted(frames, t0, signal):
    """Places a rendered layer at t0 inside a buffer of the full length."""
    start = int(RATE * t0)
    out = np.zeros(frames)
    take = min(len(signal), frames - start)
    if take > 0:
        out[start:start + take] = signal[:take]
    return out


def sweep_burst(frames, t0, start_hz, end_hz, decay, level, q=0.7):
    """Filtered noise whose band moves. Needs per-sample coefficients, so it is its own function."""
    n = int(RATE * (decay + 0.02))
    rng = np.random.default_rng(9001)
    x = rng.uniform(-1, 1, n)
    y = np.zeros(n)

    t = np.arange(n) / n
    freqs = start_hz * (end_hz / start_hz) ** t

    x1 = x2 = y1 = y2 = 0.0
    for i in range(n):
        w = 2 * math.pi * min(freqs[i], RATE * 0.45) / RATE
        alpha = math.sin(w) / (2 * q)
        a0 = 1 + alpha
        b0, b2 = alpha / a0, -alpha / a0
        a1, a2 = -2 * math.cos(w) / a0, (1 - alpha) / a0

        out = b0 * x[i] + b2 * x2 - a1 * y1 - a2 * y2
        x2, x1 = x1, x[i]
        y2, y1 = y1, out
        y[i] = out

    y = y * envelope(n, 0.0012, level, decay)
    return shifted(frames, t0, y)


# --- the four match moments, exactly as auditioned ---------------------------

def point(frames):
    """Two-note up. A rising fifth: the simplest thing that means yes."""
    return (note(frames, 0.000, C5, decay=0.12, level=0.30, lp=4000)
            + note(frames, 0.070, G5, decay=0.22, level=0.30, lp=4500))


def against(frames):
    """Two-note down. Your point backwards, so conceding is the opposite of scoring."""
    return (note(frames, 0.000, G5, decay=0.12, level=0.26, lp=3000)
            + note(frames, 0.070, C5, decay=0.24, level=0.26, lp=2600))


def win(frames):
    """Rise. A run up the scale with a sparkle on the end."""
    return (run(frames, 0.0, [C5, D5, E5, G5, A5, C6], 0.055,
                decay=0.2, level=0.20, lp=5000, partial=0.25)
            + sweep_burst(frames, 0.30, 2000, 9000, 0.12, 0.12, q=0.6)
            + note(frames, 0.34, E6, decay=0.55, level=0.20, lp=8000))


def lose(frames):
    """Descend. The exact mirror of a fanfare, landing somewhere settled."""
    return (run(frames, 0.0, [C6, G5, E5, C5], 0.085,
                decay=0.3, level=0.22, lp=3000, partial=0.2)
            + note(frames, 0.34, C4, decay=0.8, level=0.20, lp=1600, partial=0.15))


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
    signal = room(saturate(click(frames, variant(n)))) * gain

    # Trailing silence trimmed so the AudioSource is not holding samples nobody hears.
    loud = np.abs(signal) > 3e-4
    if loud.any():
        signal = signal[: int(np.flatnonzero(loud)[-1]) + 64]

    # 3ms fade out, so no variation ends on a step that clicks in its own right.
    fade = min(int(RATE * 0.003), len(signal))
    signal[-fade:] *= np.linspace(1, 0, fade)

    return signal


def common_gain():
    """One gain for all ten, set by the LOUDEST of them.

    Normalising each file separately would undo a third of the work -- the level difference
    between variations is deliberate, and per-file normalisation is exactly the operation that
    removes it. But the reference has to be the loudest note rather than the first, or the higher
    notes come out hotter than everything else in the game: the same envelope on a higher
    frequency simply peaks higher once it has been through saturation.
    """
    frames = int(RATE * SECONDS)
    peak = max(np.max(np.abs(room(saturate(click(frames, variant(n))))))
               for n in range(COUNT))
    return 0.5 / peak if peak > 0 else 1.0


def finish(signal, peak_target):
    """Trim the tail, set the level, and fade the last few milliseconds."""
    loud = np.abs(signal) > 3e-4
    if loud.any():
        signal = signal[: int(np.flatnonzero(loud)[-1]) + 64]

    peak = np.max(np.abs(signal))
    if peak > 0:
        signal = signal / peak * peak_target

    fade = min(int(RATE * 0.006), len(signal))
    signal[-fade:] *= np.linspace(1, 0, fade)
    return signal


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

    print(f"\n{COUNT} click variations written to {target}")

    # The match sounds. Quieter than their own ceiling on purpose: they land on top of whatever
    # else is happening, and a win that startles is a win nobody hears twice.
    moments = os.path.normpath(os.path.join(here, "..", "..", "Assets", "Resources", "Match"))
    os.makedirs(moments, exist_ok=True)

    frames = int(RATE * 2.2)

    for name, make, level in [("Point", point, 0.62), ("Against", against, 0.52),
                              ("Win", win, 0.70), ("Lose", lose, 0.58)]:
        signal = finish(room(saturate(make(frames))), level)
        path = os.path.join(moments, name + ".wav")
        write(path, signal)
        print(f"  {name + '.wav':<14}{len(signal) / RATE * 1000:5.0f} ms  peak {np.max(np.abs(signal)):.2f}")

    print(f"\n4 match sounds written to {moments}")


if __name__ == "__main__":
    main()
