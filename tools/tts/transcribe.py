"""Experiment (branch decision_nvidia): reads back every <prefix>_<n>.wav with K-Desktop's Parakeet and counts the words
that do not match frases.txt (a rough check that the voice says what it should). See tools/tts/README.md.

    python transcribe.py <parakeet folder> <frases.txt> <folder> <prefix> [<folder> <prefix> ...]"""
import re
import sys
import unicodedata

import numpy as np
import sherpa_onnx
import soundfile as sf

model, sentences_file, pairs = sys.argv[1], sys.argv[2], sys.argv[3:]
sentences = [s.strip() for s in open(sentences_file, encoding="utf-8") if s.strip()]
recognizer = sherpa_onnx.OfflineRecognizer.from_transducer(
    encoder=f"{model}/encoder.int8.onnx", decoder=f"{model}/decoder.int8.onnx", joiner=f"{model}/joiner.int8.onnx",
    tokens=f"{model}/tokens.txt", model_type="nemo_transducer", num_threads=4)


def words(text):
    text = unicodedata.normalize("NFKD", text.lower())
    text = "".join(c for c in text if not unicodedata.combining(c))
    return re.findall(r"[a-z0-9ñ]+", text)


def distance(a, b):
    row = list(range(len(b) + 1))
    for i, x in enumerate(a, 1):
        prev, row[0] = row[0], i
        for j, y in enumerate(b, 1):
            prev, row[j] = row[j], min(row[j] + 1, row[j - 1] + 1, prev + (x != y))
    return row[-1]


for folder, prefix in zip(pairs[::2], pairs[1::2]):
    errors = total = 0
    print(f"== {prefix} ({folder})")
    for i, sentence in enumerate(sentences, 1):
        audio, rate = sf.read(f"{folder}/{prefix}_{i}.wav", dtype="float32")
        if audio.ndim > 1:
            audio = audio.mean(axis=1)
        if rate != 16000:
            audio = np.interp(np.arange(0, len(audio), rate / 16000), np.arange(len(audio)), audio).astype(np.float32)
        stream = recognizer.create_stream()
        stream.accept_waveform(16000, audio)
        recognizer.decode_stream(stream)
        heard = stream.result.text.strip()
        e = distance(words(sentence), words(heard))
        errors += e
        total += len(words(sentence))
        print(f"{i}: {e} | {heard}")
    print(f"WER {prefix}: {errors}/{total} = {100 * errors / total:.1f} %")
