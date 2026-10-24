#!/usr/bin/env python3
"""
Everything the video needs besides the narration, made here so nothing has a licence of its own:

- public/brand/noise.png: the film grain tile;
- public/sfx/*.wav: whooshes, clicks, key taps, pops and chimes, synthesised;
- public/sfx/music.wav: a calm electronic bed, synthesised (replace it with your own track if you like);
- public/captures/: a copy of ../captures (the real screenshots) and public/generated/captures.json, their list.

    python tools/assets.py
"""

import json
import shutil
from pathlib import Path

import numpy as np
import soundfile as sf

ROOT = Path(__file__).resolve().parent.parent
SR = 48000
rng = np.random.default_rng(82)


def t(seconds: float) -> np.ndarray:
    return np.arange(int(seconds * SR)) / SR


def env(n: int, attack: float, release: float, curve: float = 4.0) -> np.ndarray:
    a = max(1, int(attack * SR))
    e = np.ones(n)
    e[:a] = np.linspace(0, 1, a) ** 2
    rest = n - a
    if rest > 0:
        e[a:] = np.exp(-curve * np.linspace(0, 1, rest) * (len(e) / SR) / max(release, 1e-3))
    return e


def lowpass(x: np.ndarray, cutoff) -> np.ndarray:
    """One-pole low-pass; `cutoff` may change over time (array)."""
    cutoff = np.broadcast_to(np.asarray(cutoff, dtype=float), x.shape)
    alpha = 1 - np.exp(-2 * np.pi * cutoff / SR)
    y = np.empty_like(x)
    acc = 0.0
    for i in range(len(x)):
        acc += alpha[i] * (x[i] - acc)
        y[i] = acc
    return y


def highpass(x: np.ndarray, cutoff: float) -> np.ndarray:
    return x - lowpass(x, cutoff)


def stereo(x: np.ndarray, width: float = 0.0) -> np.ndarray:
    if width <= 0:
        return np.stack([x, x], axis=1)
    d = int(0.011 * SR)
    r = np.concatenate([np.zeros(d), x[:-d]])
    return np.stack([x * (1 - width * 0.3) + r * width * 0.3, r * (1 - width * 0.3) + x * width * 0.3], axis=1)


def normalize(x: np.ndarray, peak: float) -> np.ndarray:
    m = np.max(np.abs(x))
    return x if m == 0 else x / m * peak


def reverb(x: np.ndarray, seconds: float = 2.4, mix: float = 0.3, circular: bool = False) -> np.ndarray:
    """Convolution with decaying noise: a soft, wide room. Circular: the tail wraps round to the start (for loops)."""
    n = int(seconds * SR)
    ir = rng.standard_normal((n, 2)) * np.exp(-6 * np.linspace(0, 1, n))[:, None]
    ir[:, 0] = lowpass(ir[:, 0], 5000)
    ir[:, 1] = lowpass(ir[:, 1], 5000)
    ir /= np.sqrt((ir ** 2).sum(axis=0))
    if x.ndim == 1:
        x = stereo(x)
    size = len(x) if circular else 1 << int(np.ceil(np.log2(len(x) + n)))
    wet = np.stack([np.fft.irfft(np.fft.rfft(x[:, c], size) * np.fft.rfft(ir[:, c], size), size)[:len(x)] for c in range(2)], axis=1)
    return x * (1 - mix) + wet * mix * 3


# ── sound effects ────────────────────────────────────────────────────

def whoosh(seconds=0.9, up=True) -> np.ndarray:
    n = int(seconds * SR)
    noise = rng.standard_normal(n)
    s = np.linspace(0, 1, n)
    sweep = 300 + (6000 if up else 2500) * (np.sin(np.pi * s) ** 1.5)
    x = lowpass(noise, sweep) - lowpass(noise, sweep * 0.25)
    shape = np.sin(np.pi * s) ** 2
    return reverb(stereo(normalize(x * shape, 0.5), 0.8), 1.2, 0.25)


def click() -> np.ndarray:
    n = int(0.06 * SR)
    x = rng.standard_normal(n) * env(n, 0.0005, 0.012, 6)
    x = highpass(x, 1500) + np.sin(2 * np.pi * 2400 * t(0.06)) * env(n, 0.0005, 0.01, 8) * 0.4
    return stereo(normalize(x, 0.35))


def key() -> np.ndarray:
    n = int(0.09 * SR)
    body = np.sin(2 * np.pi * 180 * t(0.09)) * env(n, 0.001, 0.03, 6)
    tick = highpass(rng.standard_normal(n), 2500) * env(n, 0.0005, 0.008, 8)
    return stereo(normalize(body * 0.6 + tick, 0.4))


def pop(freq=880.0) -> np.ndarray:
    n = int(0.18 * SR)
    f = freq * (1 + 0.6 * np.exp(-30 * t(0.18)))
    x = np.sin(2 * np.pi * np.cumsum(f) / SR) * env(n, 0.002, 0.06, 5)
    return reverb(stereo(normalize(x, 0.35)), 0.8, 0.2)


def chime(notes=(1046.5, 1318.5, 1568.0), gap=0.07) -> np.ndarray:
    seconds = 1.6
    out = np.zeros(int(seconds * SR))
    for i, f in enumerate(notes):
        start = int(i * gap * SR)
        n = len(out) - start
        tt = np.arange(n) / SR
        tone = (np.sin(2 * np.pi * f * tt) + 0.3 * np.sin(2 * np.pi * 2 * f * tt) + 0.1 * np.sin(2 * np.pi * 3.01 * f * tt)) * np.exp(-3.2 * tt)
        tone[: int(0.003 * SR)] *= np.linspace(0, 1, int(0.003 * SR))
        out[start:] += tone
    return reverb(stereo(normalize(out, 0.3), 0.5), 1.8, 0.3)


def rise(seconds=1.6) -> np.ndarray:
    """A swelling shimmer for big reveals."""
    n = int(seconds * SR)
    s = np.linspace(0, 1, n)
    x = np.zeros(n)
    for k, f in enumerate([220, 330, 440, 660, 880]):
        x += np.sin(2 * np.pi * np.cumsum(f * (1 + 0.5 * s ** 2)) / SR) / (k + 1)
    noise = lowpass(rng.standard_normal(n), 2000 + 8000 * s) * 0.4
    x = (x * 0.3 + noise) * s ** 2.5 * (1 - np.clip((s - 0.94) / 0.06, 0, 1))
    return reverb(stereo(normalize(x, 0.45), 0.9), 2.0, 0.3)


def impact() -> np.ndarray:
    n = int(1.4 * SR)
    tt = t(1.4)
    boom = np.sin(2 * np.pi * np.cumsum(60 * (1 + 2 * np.exp(-25 * tt))) / SR) * np.exp(-4 * tt)
    hit = lowpass(rng.standard_normal(n), 3000) * np.exp(-18 * tt)
    return reverb(stereo(normalize(boom + hit * 0.5, 0.6), 0.6), 2.2, 0.3)


def blip(freq=1320.0) -> np.ndarray:
    n = int(0.12 * SR)
    x = np.sin(2 * np.pi * freq * t(0.12)) * env(n, 0.002, 0.04, 5) + 0.5 * np.sin(2 * np.pi * freq * 1.5 * t(0.12)) * env(n, 0.002, 0.03, 6)
    return stereo(normalize(x, 0.25))


# ── music ────────────────────────────────────────────────────────────

def midi(note: float) -> float:
    return 440.0 * 2 ** ((note - 69) / 12)


def music(bars: int = 92, bpm: float = 92.0) -> np.ndarray:
    """A calm, warm loop: pads, a soft pluck arpeggio, a sub bass and a light pulse.

    It loops seamlessly: 92 bars at 92 bpm are exactly 4 minutes (7200 frames, what src/Video.tsx expects), the
    notes and the reverb that ring past the end come back in at the start, and the pulse plays from the first bar
    (an intro without it would restart with every copy)."""
    beat = 60 / bpm
    bar = beat * 4
    chords = [  # Fmaj9, Am7, Dm9, Bbmaj7(#11)
        [53, 57, 60, 64, 67], [57, 60, 64, 67, 71], [50, 57, 60, 64, 65], [46, 53, 57, 62, 64],
    ]
    bass = [41, 45, 38, 46]
    n = int(round(bars * bar * SR))
    L = np.zeros(n)
    R = np.zeros(n)
    tt_bar = np.arange(int(bar * SR)) / SR

    def add(buf, start, x):
        """Mixes x in at start; what runs past the end wraps round to the beginning."""
        start %= n
        end = min(n, start + len(x))
        buf[start:end] += x[: end - start]
        rest = x[end - start:]
        if len(rest):
            buf[: len(rest)] += rest

    for b in range(bars):
        chord = chords[b % 4]
        start = int(b * bar * SR)
        # Pad: detuned saw-ish (few harmonics), slow swell.
        pad = np.zeros(len(tt_bar))
        for note in chord:
            f = midi(note)
            for det in (-0.08, 0.08):
                ff = f * 2 ** (det / 12)
                pad += sum(np.sin(2 * np.pi * ff * k * tt_bar + k) / k ** 1.4 for k in range(1, 5))
        swell = np.minimum(1, tt_bar / 0.9) * np.minimum(1, (bar - tt_bar) / 0.6 + 0.35)
        pad = lowpass(pad * swell, 900) * 0.022
        add(L, start, pad)
        add(R, start, np.roll(pad, 220))
        # Sub bass on beats 1 and 3.
        for k in (0, 2):
            bs = int((b * bar + k * beat) * SR)
            nb = int(beat * 1.8 * SR)
            tb = np.arange(nb) / SR
            x = np.sin(2 * np.pi * midi(bass[b % 4] - 12) * tb) * np.exp(-1.6 * tb) * np.minimum(1, tb / 0.01) * 0.11
            add(L, bs, x)
            add(R, bs, x)
        # Pluck arpeggio in eighths.
        arp = [chord[1], chord[2], chord[3], chord[4], chord[3] + 12, chord[4], chord[2] + 12, chord[3]]
        for k, note in enumerate(arp):
            ps = int((b * bar + k * beat / 2) * SR)
            npk = int(0.5 * SR)
            tp = np.arange(npk) / SR
            f = midi(note + 12)
            x = (np.sin(2 * np.pi * f * tp) + 0.25 * np.sin(4 * np.pi * f * tp)) * np.exp(-7 * tp) * np.minimum(1, tp / 0.003)
            gain = 0.028 * (1.0 if k % 2 == 0 else 0.7)
            pan = 0.5 + 0.35 * np.sin(k * 1.3 + b)
            add(L, ps, x * gain * (1 - pan) * 2)
            add(R, ps, x * gain * pan * 2)
        # Soft kick on every beat, airy shaker on the off-beats.
        for k in range(4):
            ks = int((b * bar + k * beat) * SR)
            nk = int(0.3 * SR)
            tk = np.arange(nk) / SR
            kick = np.sin(2 * np.pi * np.cumsum(50 * (1 + 2.5 * np.exp(-40 * tk))) / SR) * np.exp(-10 * tk) * 0.07
            add(L, ks, kick)
            add(R, ks, kick)
            hs = int((b * bar + k * beat + beat / 2) * SR)
            nh = int(0.08 * SR)
            hat = highpass(rng.standard_normal(nh), 7000) * np.exp(-60 * np.arange(nh) / SR) * 0.018
            add(L, hs, hat * 0.8)
            add(R, hs, hat)
    mix = np.stack([L, R], axis=1)
    mix = reverb(mix, 3.0, 0.22, circular=True)
    return normalize(mix, 0.5)


def main() -> None:
    from PIL import Image

    brand = ROOT / "public" / "brand"
    brand.mkdir(parents=True, exist_ok=True)
    noise = (rng.random((256, 256)) * 255).astype("uint8")
    Image.fromarray(noise, "L").save(brand / "noise.png")

    sfx = ROOT / "public" / "sfx"
    sfx.mkdir(parents=True, exist_ok=True)
    effects = {
        "whoosh": whoosh(), "whoosh-short": whoosh(0.5), "whoosh-down": whoosh(0.8, up=False),
        "click": click(), "key": key(), "pop": pop(), "pop-low": pop(560), "pop-high": pop(1320),
        "chime": chime(), "rise": rise(), "impact": impact(), "blip": blip(), "blip-low": blip(880),
    }
    for name, x in effects.items():
        sf.write(sfx / f"{name}.wav", x.astype("float32"), SR)
    if not (sfx / "music.wav").exists():
        sf.write(sfx / "music.wav", music().astype("float32"), SR)
    shutil.copy(ROOT.parent.parent.parent / "src" / "KDesktop.Core" / "Assets" / "Sounds" / "Logro.wav", sfx / "achievement.wav")

    # The real screenshots (../captures/<lang>/: en, es…), with their sizes, keyed «<lang>/<name>».
    captures = ROOT.parent / "captures"
    target = ROOT / "public" / "captures"
    if target.exists():
        shutil.rmtree(target)
    found = {}
    for f in sorted(captures.rglob("*")) if captures.exists() else []:
        if f.suffix.lower() not in (".png", ".jpg", ".jpeg", ".webp", ".mp4", ".webm"):
            continue
        rel = f.relative_to(captures)
        (target / rel.parent).mkdir(parents=True, exist_ok=True)
        shutil.copy(f, target / rel)
        info = {"file": f"captures/{rel.as_posix()}"}
        if f.suffix.lower() in (".png", ".jpg", ".jpeg", ".webp"):
            with Image.open(f) as im:
                info.update(width=im.width, height=im.height)
        found[rel.with_suffix("").as_posix()] = info
    generated = ROOT / "public" / "generated"
    generated.mkdir(parents=True, exist_ok=True)
    (generated / "captures.json").write_text(json.dumps(found, indent=1), encoding="utf-8")
    print(f"{len(effects)} effects, music, {len(found)} captures")


if __name__ == "__main__":
    main()
