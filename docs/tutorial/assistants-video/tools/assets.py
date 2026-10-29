#!/usr/bin/env python3
"""
The sound effects and the music bed, made with the synthesis of the tutorial video (../video/tools/assets.py), so
nothing has a licence of its own: public/sfx/*.wav and public/sfx/music.wav (kept if it is already there).

    python tools/assets.py
"""

import importlib.util
from pathlib import Path

import soundfile as sf

ROOT = Path(__file__).resolve().parent.parent
spec = importlib.util.spec_from_file_location("tutorial_assets", ROOT.parent / "video" / "tools" / "assets.py")
synth = importlib.util.module_from_spec(spec)
spec.loader.exec_module(synth)


def main() -> None:
    sfx = ROOT / "public" / "sfx"
    sfx.mkdir(parents=True, exist_ok=True)
    effects = {
        "whoosh": synth.whoosh(), "whoosh-short": synth.whoosh(0.5), "whoosh-down": synth.whoosh(0.8, up=False),
        "click": synth.click(), "key": synth.key(), "pop": synth.pop(), "pop-low": synth.pop(560), "pop-high": synth.pop(1320),
        "chime": synth.chime(), "rise": synth.rise(), "impact": synth.impact(), "blip": synth.blip(), "blip-low": synth.blip(880),
    }
    for name, x in effects.items():
        sf.write(sfx / f"{name}.wav", x.astype("float32"), synth.SR)
    if not (sfx / "music.wav").exists():
        sf.write(sfx / "music.wav", synth.music().astype("float32"), synth.SR)
    print(f"{len(effects)} effects and the music")


if __name__ == "__main__":
    main()
