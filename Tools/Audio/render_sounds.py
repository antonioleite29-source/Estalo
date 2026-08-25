#!/usr/bin/env python3
"""Renders every sound Estalo makes.

Each one is a recipe, not a recording -- a noise transient, a tonal body, sometimes a sub, then
saturation and a small room. That means regenerating is always possible, and tuning by ear is
changing a number here rather than hunting for a replacement sample. Nothing is taken from a
library, so no licence is attached to any of it.

    python3 render_sounds.py

    Assets/Resources/Click/<note>/01..05.wav the button, one folder per pentatonic note
    Assets/Resources/Match/Correct.wav       you answered right
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

# Every note in the scale gets its own folder, so which note a button plays is decided in the
# game rather than baked into one shuffled pile. C is the neutral tap; the others are there for
# the buttons that mean something -- Jogar is E, the third.
CLICK_NOTES = {"C": C5, "D": D5, "E": E5, "G": G5, "A": A5}

# Five files per note. With the pitch now fixed per button, the level and length are doing all
# the work of stopping five taps in a row sounding like one recording, so they move further than
# they did when the note itself was changing.
VARIATIONS = 5
LEVEL_SPREAD = 0.18
DECAY_SPREAD = 0.25

# And a hair of pitch on top. Fifteen hundredths of a semitone is far too small to hear as being
# out of tune and just enough to stop the ear recognising a loop -- a real object struck twice is
# never quite the same pitch either.
PITCH_JITTER_SEMITONES = 0.15

# Which octave the button sits in, relative to the written note. 0 is the note itself; -2 was the
# warm bass register where C landed on 128 Hz. One number, so moving the whole set is one edit and
# a re-render rather than three frequencies chased by hand.
CLICK_OCTAVE = 0


def variant(n, freq):
    """One of the five files for a given note."""
    spread = (n - (VARIATIONS - 1) / 2) / ((VARIATIONS - 1) / 2)      # -1 .. 1
    return dict(
        note=freq * 2.0 ** (spread * PITCH_JITTER_SEMITONES / 12.0),
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
    """Warm bass, tuned to the button's own note.

    Three sine partials at 1, 2 and 4.3 times the fundamental, the upper ones dying faster. That
    4.3 is what stops it being an organ: a whole-number ratio would fuse into one tone, while
    something slightly off sits beside the fundamental and reads as an instrument with a body.

    Which octave it sits in is CLICK_OCTAVE. The register matters more than the timbre does:
    there is far less energy up top down low, and that is what lets a click survive being pressed
    forty times. Every version that wore out was a bright one.

    No noise transient anywhere. That was what made every earlier version read as harsh.
    """
    root = v["note"] * (2.0 ** CLICK_OCTAVE)

    # Scaled with the note rather than fixed. A fixed corner would let the top partial through on
    # C and cut it on A, so the scale would get duller as it went up -- backwards.
    lp = root * 6.1

    return (
        body(frames, v, "sine", root,       0, 0, 0.005, 0.170, 0.380, lp=lp)
        + body(frames, v, "sine", root * 2, 0, 0, 0.005, 0.102, 0.099, lp=lp)
        + body(frames, v, "sine", root * 4.3, 0, 0, 0.005, 0.051, 0.034, lp=lp)
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

def correct(frames):
    """Triad: three notes up the chord, settled and clearly finished.

    Climbs, because a wrong answer resolves downward onto the root and the pair only works as
    opposites. Kept to three notes and under a third of a second because this fires many times a
    match and the next question follows almost immediately.
    """
    return run(frames, 0.0, [C5, E5, G5], 0.065,
               decay=0.24, level=0.28, lp=4600, partial=0.3)


def point(frames):
    """Two-note up. A rising fifth: the simplest thing that means yes."""
    return (note(frames, 0.000, C5, decay=0.12, level=0.30, lp=4000)
            + note(frames, 0.070, G5, decay=0.22, level=0.30, lp=4500))


def against(frames):
    """Two-note down: your point played backwards, and nothing else changed.

    It used to be darker and quieter as well as inverted, which muddled the message -- conceding
    sounded like a lesser event rather than the opposite one. Same notes, same levels, same filter
    as point(); only the order of the two pitches is reversed. That is the whole idea.
    """
    return (note(frames, 0.000, G5, decay=0.12, level=0.30, lp=4500)
            + note(frames, 0.070, C5, decay=0.22, level=0.30, lp=4000))


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


def render(n, freq, gain):
    frames = int(RATE * SECONDS)
    signal = room(saturate(click(frames, variant(n, freq)))) * gain

    # Trailing silence trimmed so the AudioSource is not holding samples nobody hears.
    loud = np.abs(signal) > 3e-4
    if loud.any():
        signal = signal[: int(np.flatnonzero(loud)[-1]) + 64]

    # 3ms fade out, so no variation ends on a step that clicks in its own right.
    fade = min(int(RATE * 0.003), len(signal))
    signal[-fade:] *= np.linspace(1, 0, fade)

    return signal


def common_gain():
    """One gain across every note and every variation, set by the loudest of them all.

    Normalising each file separately would undo the level variation, which is most of what stops
    repeated taps sounding identical now that the pitch is fixed. And the reference has to be the
    loudest of the whole set rather than of one note: the same envelope on a higher frequency
    peaks higher once it has been through saturation, so keying off C would ship A far too hot.
    """
    frames = int(RATE * SECONDS)
    peak = max(
        np.max(np.abs(room(saturate(click(frames, variant(n, freq))))))
        for freq in CLICK_NOTES.values()
        for n in range(VARIATIONS)
    )
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

    for name, freq in CLICK_NOTES.items():
        folder = os.path.join(target, name)
        os.makedirs(folder, exist_ok=True)

        for n in range(VARIATIONS):
            signal = render(n, freq, gain)
            write(os.path.join(folder, f"{n + 1:02d}.wav"), signal)

        print(f"  Click/{name}  {VARIATIONS} files  {freq:6.1f} Hz")

    print(f"\n{len(CLICK_NOTES) * VARIATIONS} click files written to {target}")

    # The match sounds. Quieter than their own ceiling on purpose: they land on top of whatever
    # else is happening, and a win that startles is a win nobody hears twice.
    moments = os.path.normpath(os.path.join(here, "..", "..", "Assets", "Resources", "Match"))
    os.makedirs(moments, exist_ok=True)

    frames = int(RATE * 2.2)

    for name, make, level in [("Correct", correct, 0.58), ("Point", point, 0.62),
                              ("Against", against, 0.52),
                              ("Win", win, 0.70), ("Lose", lose, 0.58)]:
        signal = finish(room(saturate(make(frames))), level)
        path = os.path.join(moments, name + ".wav")
        write(path, signal)
        print(f"  {name + '.wav':<14}{len(signal) / RATE * 1000:5.0f} ms  peak {np.max(np.abs(signal)):.2f}")

    print(f"\n5 match sounds written to {moments}")


if __name__ == "__main__":
    main()
