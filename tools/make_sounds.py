#!/usr/bin/env python3
"""Synthesises AVAParadroid's sound effects and tunes into ../src/AVAParadroid/Sounds as 16-bit mono WAV.

Nothing is sampled from any game: every effect is built here from square, triangle and noise voices, and the
tunes are new compositions. The ship's computer speaks through the formant voice in speech.py, the way the
talking arcade games of the day spoke, so no recording or speech engine is needed. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

from speech import say



RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVAParadroid", "Sounds")
RNG = random.Random(1984)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out


def echoed(dry, taps=((0.03, 0.15),)):
    """A little ring of the loudspeaker."""
    out = dry + [0.0] * int(RATE * 0.3)
    for delay, gain in taps:
        k = int(RATE * delay)
        for i, v in enumerate(dry):
            out[i + k] += v * gain
    return out


def computer(words):
    """The ship's computer: a clear, level voice with a fifth humming under it, a little metallic."""
    voice = say(words, rate=RATE, pitch=142, speed=1.05, size=0.9)
    hum = say(words, rate=RATE, pitch=213, speed=1.05, size=0.9, seed=11)
    return echoed([a + 0.15 * b for a, b in zip(voice, hum)])


# The computer's lines, written out as phonemes.
LINES = {
    "SayTransfer": "T R AE1 N S F ER !",
    "SayDeckClear": "D EH1 K , K L IY1 R D !",
    "SayShipClear": "V EH1 S AH L , K L IY1 R !",
}


def arp(chord, beats, step=0.25):
    notes = []
    for i in range(int(beats / step)):
        notes.append((chord[i % len(chord)], step))
    return notes


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # Shots and hits.
    write("Fire.wav", tone(0.1, 1800, 700, "square", attack=0.001, release=1.3, gain=0.5), 0.16)
    write("DroidFire.wav", tone(0.12, 900, 1400, "saw", attack=0.001, vibrato=0.05, vib_rate=50, release=1.0, gain=0.5), 0.14)
    write("Hit.wav", mix(tone(0.08, 900, 300, "square", attack=0.001, release=1.2, gain=0.5), tone(0.06, 2500, 800, "noise", release=1.5, gain=0.4)), 0.24)
    write("Boom.wav", mix(tone(0.5, 2200, 120, "noise", attack=0.001, release=0.7), tone(0.45, 240, 50, "saw", vibrato=0.2, vib_rate=14, release=0.7, gain=0.6)), 0.4)
    write("Die.wav", mix(tone(1.4, 2600, 60, "noise", attack=0.001, release=0.4), tone(1.2, 220, 30, "saw", vibrato=0.25, vib_rate=10, release=0.5, gain=0.6)), 0.5)
    write("HostLost.wav", cat(mix(tone(0.4, 1800, 200, "noise", attack=0.001, release=0.8), tone(0.35, 500, 80, "square", release=0.8, gain=0.5)),
                             *[tone(0.06, hz(n), kind="pulse", release=0.4) for n in ["E5", "C5", "A4"]]), 0.4)
    write("Charge.wav", tone(0.6, 300, 900, "sine", attack=0.05, vibrato=0.05, vib_rate=12, release=0.6), 0.2)
    write("TransferOn.wav", tone(0.25, 400, 1600, "square", attack=0.005, vibrato=0.08, vib_rate=30, release=0.6, gain=0.5), 0.2)
    write("TransferOff.wav", tone(0.2, 1200, 300, "square", attack=0.005, release=0.8, gain=0.5), 0.16)
    write("DeckClear.wav", cat(*[tone(0.08, hz(n), kind="pulse", release=0.3) for n in ["A4", "C5", "E5", "A5", "E5", "A5"]]), 0.3)
    write("ExtraLife.wav", cat(*[tone(0.07, hz(n), kind="pulse", release=0.3) for n in ["C6", "E6", "G6", "C7"]]), 0.3)
    # The duel.
    write("DuelStart.wav", cat(*[tone(0.05, 200 + i * 120, kind="square", release=0.4, gain=0.5) for i in range(10)]), 0.24)
    write("Pulse.wav", tone(0.3, 600, 2400, "saw", attack=0.002, release=0.7, gain=0.5), 0.2)
    write("Land.wav", tone(0.1, 1600, 1200, "square", attack=0.001, release=1.0, gain=0.5), 0.18)
    write("DuelWin.wav", cat(*[tone(0.07, hz(n), kind="square", release=0.3) for n in ["C5", "E5", "G5", "C6", "G5", "C6", "E6"]]), 0.3)
    write("DuelLose.wav", cat(*[tone(0.12, hz(n), kind="saw", release=0.4) for n in ["E4", "D#4", "D4", "C#4"]], tone(0.5, hz("C4"), kind="saw", vibrato=0.04, release=0.6)), 0.34)
    # The lift.
    write("LiftMove.wav", tone(0.12, 500, 700, "tri", attack=0.005, release=0.8, gain=0.6), 0.16)
    write("LiftGo.wav", mix(tone(0.6, 120, 240, "saw", attack=0.05, release=0.5, gain=0.5), tone(0.6, 600, 300, "noise", attack=0.05, release=0.6, gain=0.3)), 0.24)
    # The computer.
    for name, words in LINES.items():
        write(f"{name}.wav", computer(words), 0.75)

    # The ship's hum (an original composition): a slow, watchful arpeggio in A minor over a drone, looping.
    lead = [("-", 4), ("E5", 2), ("D5", 1), ("C5", 1), ("B4", 4), ("-", 4), ("C5", 2), ("D5", 1), ("E5", 1), ("A4", 4),
            ("-", 4), ("E5", 2), ("F5", 1), ("E5", 1), ("D5", 4), ("-", 4), ("B4", 2), ("C5", 1), ("B4", 1), ("A4", 4)]
    harmony = arp(["A2", "E3", "A3", "C4"], 8, 0.5) + arp(["F2", "C3", "F3", "A3"], 8, 0.5) + arp(["D2", "A2", "D3", "F3"], 8, 0.5) + arp(["E2", "B2", "E3", "G#3"], 8, 0.5)
    write("Music.wav", tune(100, lead, harmony, kind="tri"), 0.22)
    title_lead = [("A4", 1), ("C5", 1), ("E5", 1), ("A5", 1), ("G5", 1.5), ("E5", 0.5), ("D5", 1), ("E5", 1),
                  ("F5", 1), ("E5", 1), ("D5", 1), ("C5", 1), ("B4", 2), ("E5", 2),
                  ("A4", 1), ("C5", 1), ("E5", 1), ("A5", 1), ("B5", 1.5), ("G#5", 0.5), ("E5", 2), ("A5", 4)]
    title_bass = arp(["A2", "E3"], 4, 0.5) + arp(["C3", "G3"], 4, 0.5) + arp(["D3", "A3"], 4, 0.5) + arp(["E2", "B2"], 4, 0.5) + arp(["A2", "E3"], 4, 0.5) + arp(["E2", "B2"], 2, 0.5) + arp(["A2", "E3"], 4, 0.5)
    write("TitleTune.wav", cat(tune(112, title_lead, title_bass, kind="pulse"), silence(0.8)), 0.36)
    write("Victory.wav", tune(120, [("A4", 0.5), ("C#5", 0.5), ("E5", 0.5), ("A5", 1.5), ("G#5", 0.5), ("A5", 0.5), ("B5", 0.5), ("C#6", 3)],
                              [("A2", 1), ("E2", 1), ("F#2", 1), ("D2", 1), ("E2", 1), ("A2", 2.5)], kind="pulse"), 0.4)
    write("GameOver.wav", tune(66, [("E5", 1), ("C5", 1), ("B4", 1), ("G#4", 1), ("A4", 4)], [("A2", 2), ("F2", 2), ("E2", 2), ("A1", 2)], kind="tri"), 0.42)


if __name__ == "__main__":
    main()
