#!/usr/bin/env python3
"""
Narration of the K-Desktop video.

Reads script/<lang>.json, says every sentence with Kokoro (the same natural voice K-Desktop's assistants use), joins
the sentences of each beat into public/audio/<lang>/<chapter>-<beat>.wav and lays the whole video out in time:
public/generated/timeline.<lang>.json (what the video follows, in frames) and out/subtitles.<lang>.srt.

Sentences already said are kept in .cache/tts, so changing one sentence only says that one again.

    python tools/tts.py --lang en
"""

import argparse
import hashlib
import json
import os
import re
import sys
import urllib.request
from pathlib import Path

import numpy as np
import soundfile as sf

ROOT = Path(__file__).resolve().parent.parent
FPS = 30
SR = 24000

# Layout of the video, in seconds.
LEAD_IN = 1.5          # a chapter's title plays before its first beat
LEAD_IN_FIRST = 0.8    # the intro starts talking sooner
TAIL = 1.0             # after the last beat of a chapter
OVERLAP = 0.6          # chapters cross-fade into each other
DEFAULT_PAUSE = 0.35   # after a beat
SENTENCE_GAP = 0.26    # between the sentences of a beat

MODEL_URL = "https://github.com/thewh1teagle/kokoro-onnx/releases/download/model-files-v1.0/"
MODEL_FILES = ["kokoro-v1.0.onnx", "voices-v1.0.bin"]


def model_dir() -> Path:
    folder = Path(os.environ.get("KOKORO_DIR", Path.home() / ".cache" / "kokoro"))
    folder.mkdir(parents=True, exist_ok=True)
    for name in MODEL_FILES:
        target = folder / name
        if not target.exists():
            print(f"Downloading {name}…", file=sys.stderr)
            urllib.request.urlretrieve(MODEL_URL + name, target)
    return folder


def sentences(text: str) -> list[str]:
    """A beat split into sentences ("|" forces a split; an ellipsis does not end a sentence)."""
    parts = []
    for chunk in text.split("|"):
        parts += re.split(r"(?<=[.!?])(?<!\.\.\.)\s+(?=[A-Z0-9\"'])", chunk.strip())
    return [p.strip() for p in parts if p.strip()]


def spoken(text: str, rules: list[list[str]]) -> str:
    for pattern, replacement in rules:
        text = re.sub(pattern, replacement, text)
    return text


def trim(audio: np.ndarray, threshold: float = 0.006, margin: float = 0.03) -> np.ndarray:
    loud = np.flatnonzero(np.abs(audio) > threshold)
    if loud.size == 0:
        return audio[:0]
    pad = int(margin * SR)
    return audio[max(0, loud[0] - pad): loud[-1] + pad]


class Voice:
    def __init__(self, tts: dict, cache: Path):
        self.tts = tts
        self.cache = cache
        self.cache.mkdir(parents=True, exist_ok=True)
        self._kokoro = None

    def say(self, text: str) -> np.ndarray:
        key = json.dumps([text, self.tts["voice"], self.tts["kokoroLang"], self.tts.get("speed", 1.0)], ensure_ascii=False)
        file = self.cache / (hashlib.sha1(key.encode("utf-8")).hexdigest()[:16] + ".wav")
        if file.exists():
            audio, _ = sf.read(file, dtype="float32")
            return audio
        if self._kokoro is None:
            from kokoro_onnx import Kokoro
            folder = model_dir()
            self._kokoro = Kokoro(str(folder / MODEL_FILES[0]), str(folder / MODEL_FILES[1]))
        audio, rate = self._kokoro.create(text, voice=self.tts["voice"], speed=self.tts.get("speed", 1.0), lang=self.tts["kokoroLang"])
        assert rate == SR, rate
        audio = trim(np.asarray(audio, dtype="float32"))
        sf.write(file, audio, SR)
        print(f"  said: {text[:70]}", file=sys.stderr)
        return audio


def frames(seconds: float) -> int:
    return int(round(seconds * FPS))


def srt_time(seconds: float) -> str:
    ms = int(round(seconds * 1000))
    return f"{ms // 3600000:02}:{ms // 60000 % 60:02}:{ms // 1000 % 60:02},{ms % 1000:03}"


def subtitle_lines(text: str, width: int = 46) -> list[str]:
    """Up to two lines per cue; longer sentences become several cues."""
    words, lines, line = text.split(), [], ""
    for w in words:
        if line and len(line) + 1 + len(w) > width:
            lines.append(line)
            line = w
        else:
            line = f"{line} {w}".strip()
    if line:
        lines.append(line)
    return ["\n".join(lines[i:i + 2]) for i in range(0, len(lines), 2)]


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--lang", default="en")
    args = parser.parse_args()

    script = json.loads((ROOT / "script" / f"{args.lang}.json").read_text(encoding="utf-8"))
    rules_file = ROOT / "script" / f"pronunciation.{args.lang}.json"
    rules = json.loads(rules_file.read_text(encoding="utf-8"))["rules"] if rules_file.exists() else []
    voice = Voice(script["tts"], ROOT / ".cache" / "tts" / args.lang)

    audio_dir = ROOT / "public" / "audio" / args.lang
    audio_dir.mkdir(parents=True, exist_ok=True)
    for old in audio_dir.glob("*.wav"):
        old.unlink()

    timeline = {"lang": args.lang, "fps": FPS, "chapters": []}
    cues = []
    t = 0.0
    for index, chapter in enumerate(script["chapters"]):
        start = t
        cursor = start + (LEAD_IN_FIRST if index == 0 else LEAD_IN)
        beats = []
        for beat in chapter["beats"]:
            parts, segments, offset = [], [], 0.0
            for text in sentences(beat["text"]):
                audio = voice.say(spoken(text, rules))
                if parts:
                    parts.append(np.zeros(int(SENTENCE_GAP * SR), dtype="float32"))
                    offset += SENTENCE_GAP
                duration = len(audio) / SR
                segments.append({"text": text, "from": frames(cursor - start + offset), "to": frames(cursor - start + offset + duration)})
                for i, cue in enumerate(subtitle_lines(text)):
                    share = duration / len(subtitle_lines(text))
                    cues.append((cursor + offset + i * share, cursor + offset + (i + 1) * share, cue))
                parts.append(audio)
                offset += duration
            name = f"{chapter['id']}-{beat['id']}.wav"
            sf.write(audio_dir / name, np.concatenate(parts), SR)
            beats.append({
                "id": beat["id"],
                "text": beat["text"],
                "audio": f"audio/{args.lang}/{name}",
                "from": frames(cursor - start),
                "durationInFrames": frames(offset),
                "segments": segments,
            })
            cursor += offset + beat.get("pause", DEFAULT_PAUSE)
        end = cursor + TAIL
        timeline["chapters"].append({
            "id": chapter["id"],
            "title": chapter["title"],
            "label": chapter["label"],
            "from": frames(start),
            "durationInFrames": frames(end) - frames(start),
            "beats": beats,
        })
        print(f"{chapter['id']:12} {start / 60:5.2f} → {end / 60:5.2f} min", file=sys.stderr)
        t = end - OVERLAP
    last = timeline["chapters"][-1]
    timeline["durationInFrames"] = last["from"] + last["durationInFrames"]

    out = ROOT / "public" / "generated"
    out.mkdir(parents=True, exist_ok=True)
    (out / f"timeline.{args.lang}.json").write_text(json.dumps(timeline, ensure_ascii=False, indent=1), encoding="utf-8")

    subs = ROOT / "out"
    subs.mkdir(exist_ok=True)
    srt = "\n".join(f"{i}\n{srt_time(a)} --> {srt_time(b)}\n{text}\n" for i, (a, b, text) in enumerate(cues, 1))
    (subs / f"subtitles.{args.lang}.srt").write_text(srt, encoding="utf-8")
    print(f"Total: {timeline['durationInFrames'] / FPS / 60:.2f} min", file=sys.stderr)


if __name__ == "__main__":
    main()
