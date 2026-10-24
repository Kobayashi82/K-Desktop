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

CUE = re.compile(r"\{([\w-]+)\}")

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
        parts += re.split(r"(?<=[.!?])(?<!\.\.\.)\s+(?=[{A-Z0-9\"'])", chunk.strip())
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


# Words a subtitle line should not end on: they belong with what follows.
CLINGY = {"a", "an", "the", "of", "to", "in", "on", "at", "by", "for", "with", "from", "into", "and", "or", "but", "nor",
          "your", "its", "their", "my", "our", "his", "her", "this", "these", "that", "as", "than", "if", "un", "una",
          "el", "la", "los", "las", "de", "del", "al", "en", "con", "por", "para", "y", "o", "que", "tu", "tus", "su", "sus"}


NUMBERS = {"one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "fifteen", "twenty", "thirty",
           "eighty", "hundred", "thousand", "dos", "tres", "cuatro", "cinco", "siete", "ocho", "quince", "veinte", "cien",
           "ochenta", "cientos", "mil"}
# Words that start a phrase: cutting just before them reads naturally.
STARTERS = {"and", "but", "with", "to", "in", "on", "for", "from", "that", "which", "where", "while", "when", "if",
            "unless", "so", "during", "into", "without", "even", "as", "like", "whether", "y", "pero", "con", "para",
            "que", "donde", "cuando", "si", "sin", "mientras", "aunque", "como"}
NBSP = "\u00a0"


def cut_cost(words: list[str], i: int) -> float:
    """How bad it is to cut after words[i]: free where the voice pauses, dear inside a phrase."""
    word = words[i]
    if word[-1] in ".!?…":
        return 0
    if word[-1] in ",;:":
        return 2
    return (6 if words[i + 1].lower() in STARTERS else 10) + (14 if word.lower() in CLINGY else 0) + (10 if word.lower() in NUMBERS else 0)


def two_lines(words: list[str], width: int) -> tuple[str, float]:
    """A cue on one line, or on two balanced ones cut at the best place."""
    text = " ".join(words)
    if len(text) <= width or len(words) < 2:
        return text, 0.0
    best, best_cost = text, float("inf")
    for i in range(len(words) - 1):
        top, bottom = " ".join(words[: i + 1]), " ".join(words[i + 1:])
        cost = cut_cost(words, i) + 0.25 * abs(len(top) - len(bottom)) + 4 * max(0, len(top) - width, len(bottom) - width)
        if cost < best_cost:
            best, best_cost = f"{top}\n{bottom}", cost
    return best, best_cost


def subtitle_lines(text: str, width: int = 42, keep: list[str] = ()) -> list[str]:
    """The sentence as subtitles: the fewest cues of up to two lines, cut where the voice pauses when it can.
    Phrases in keep (names like Home Assistant) are never cut."""
    for phrase in keep:
        text = text.replace(phrase, phrase.replace(" ", NBSP))
    text = re.sub(r"(\S+) (plus|más) (\S+)", rf"\1{NBSP}\2{NBSP}\3", text)
    words = text.split(" ")
    # best[j]: the cheapest way to show words[:j]; each cue costs a little, short ones and bad cuts cost more.
    best: list[tuple[float, int, str]] = [(0.0, 0, "")] + [(float("inf"), 0, "")] * len(words)
    for j in range(1, len(words) + 1):
        for i in range(j - 1, -1, -1):
            size = len(" ".join(words[i:j]))
            if size > 2 * width and j - i > 1:
                break
            lines, line_cost = two_lines(words[i:j], width)
            cost = best[i][0] + 8 + 0.6 * max(0, 24 - size) + line_cost + (2.2 * cut_cost(words, j - 1) if j < len(words) else 0)
            if cost < best[j][0]:
                best[j] = (cost, i, lines)
    cues, j = [], len(words)
    while j > 0:
        cues.insert(0, best[j][2].replace(NBSP, " "))
        j = best[j][1]
    return cues


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--lang", default="en")
    args = parser.parse_args()

    script = json.loads((ROOT / "script" / f"{args.lang}.json").read_text(encoding="utf-8"))
    rules_file = ROOT / "script" / f"pronunciation.{args.lang}.json"
    rules = json.loads(rules_file.read_text(encoding="utf-8"))["rules"] if rules_file.exists() else []
    voice = Voice(script["tts"], ROOT / ".cache" / "tts" / args.lang)
    subtitle_options = script.get("subtitles", {})  # width (characters per line) and keep (phrases never cut)

    audio_dir = ROOT / "public" / "audio" / args.lang
    audio_dir.mkdir(parents=True, exist_ok=True)
    for old in audio_dir.glob("*.wav"):
        old.unlink()

    timeline = {"lang": args.lang, "fps": FPS, "chapters": []}
    subtitles = []
    t = 0.0
    for index, chapter in enumerate(script["chapters"]):
        start = t
        cursor = start + (LEAD_IN_FIRST if index == 0 else LEAD_IN)
        beats = []
        for beat in chapter["beats"]:
            parts, segments, cues, offset = [], [], {}, 0.0
            for marked in sentences(beat["text"]):
                text = CUE.sub("", marked)
                audio = voice.say(spoken(text, rules))
                if parts:
                    parts.append(np.zeros(int(SENTENCE_GAP * SR), dtype="float32"))
                    offset += SENTENCE_GAP
                duration = len(audio) / SR
                segments.append({"text": text, "from": frames(cursor - start + offset), "to": frames(cursor - start + offset + duration)})
                # {name} before a word: the moment it is said, estimated from where it sits in the sentence.
                for m in CUE.finditer(marked):
                    at = len(CUE.sub("", marked[: m.start()]))
                    cues[m.group(1)] = frames(cursor - start + offset + duration * at / max(1, len(text)))
                # Each subtitle stays up for its share of the sentence, by length.
                lines = subtitle_lines(text, **subtitle_options)
                weights = [len(x) + 6 for x in lines]
                at = cursor + offset
                for cue, weight in zip(lines, weights):
                    share = duration * weight / sum(weights)
                    subtitles.append((at, at + share, cue))
                    at += share
                parts.append(audio)
                offset += duration
            name = f"{chapter['id']}-{beat['id']}.wav"
            sf.write(audio_dir / name, np.concatenate(parts), SR)
            beats.append({
                "id": beat["id"],
                "text": CUE.sub("", beat["text"]),
                "audio": f"audio/{args.lang}/{name}",
                "from": frames(cursor - start),
                "durationInFrames": frames(offset),
                "segments": segments,
                "cues": cues,
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
    timeline["subtitles"] = [{"from": frames(a), "to": frames(b), "text": text} for a, b, text in subtitles]

    out = ROOT / "public" / "generated"
    out.mkdir(parents=True, exist_ok=True)
    (out / f"timeline.{args.lang}.json").write_text(json.dumps(timeline, ensure_ascii=False, indent=1), encoding="utf-8")

    subs = ROOT / "out"
    subs.mkdir(exist_ok=True)
    srt = "\n".join(f"{i}\n{srt_time(a)} --> {srt_time(b)}\n{text}\n" for i, (a, b, text) in enumerate(subtitles, 1))
    (subs / f"subtitles.{args.lang}.srt").write_text(srt, encoding="utf-8")
    print(f"Total: {timeline['durationInFrames'] / FPS / 60:.2f} min", file=sys.stderr)


if __name__ == "__main__":
    main()
