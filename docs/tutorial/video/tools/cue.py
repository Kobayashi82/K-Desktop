#!/usr/bin/env python3
"""Sets the text of beats in script/<lang>.json (used while adding {cue} markers): python tools/cue.py en chapter.beat "text" …"""
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
lang, pairs = sys.argv[1], sys.argv[2:]
path = ROOT / "script" / f"{lang}.json"
script = json.loads(path.read_text(encoding="utf-8"))
for key, text in zip(pairs[::2], pairs[1::2]):
    chapter, beat = key.split(".", 1)
    target = next(b for c in script["chapters"] if c["id"] == chapter for b in c["beats"] if b["id"] == beat)
    clean = lambda s: __import__("re").sub(r"\{[\w-]+\}", "", s)
    if clean(target["text"]) != clean(text):
        print(f"! {key}: the words changed", file=sys.stderr)
    target["text"] = text
path.write_text(json.dumps(script, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
