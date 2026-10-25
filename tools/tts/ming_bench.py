"""Experiment (branch decision_nvidia): Ming-3.0-tts-0.5b saying the same Spanish sentences as KokoroBench, cloning
the reference voice that KokoroBench wrote (ref.wav + ref.txt). See tools/tts/README.md.

    python ming_bench.py <Ming-3.0-tts repo> <model folder> <frases.txt> <out folder> [--runs 2]

Writes ming_<n>.wav per sentence and ming.csv (first fragment, total, audio length, real-time factor, VRAM)."""
import argparse
import os
import sys
import time

ap = argparse.ArgumentParser()
ap.add_argument("repo")
ap.add_argument("model")
ap.add_argument("sentences")
ap.add_argument("out")
ap.add_argument("--runs", type=int, default=2)
ap.add_argument("--flow_steps", type=int, default=0, help="0 = the model's default")
a = ap.parse_args()
a.model, a.sentences, a.out = (os.path.abspath(p) for p in (a.model, a.sentences, a.out))

sys.path.insert(0, os.path.abspath(a.repo))
os.chdir(a.repo)
import torch  # noqa: E402
import torchaudio  # noqa: E402
from ming_tts import MingTTS  # noqa: E402

out = os.path.abspath(a.out)
sentences = [s.strip() for s in open(a.sentences, encoding="utf-8") if s.strip()]
ref_wav = os.path.join(out, "ref.wav")
ref_text = open(os.path.join(out, "ref.txt"), encoding="utf-8").read().strip()

t = time.perf_counter()
tts = MingTTS(os.path.abspath(a.model), text_normalization=False)
torch.cuda.synchronize()
print(f"load: {time.perf_counter() - t:.1f} s, VRAM {torch.cuda.memory_allocated() / 2**20:.0f} MiB", flush=True)
if a.flow_steps:
    # Fewer flow-matching steps per audio patch (10 by default; 5, 6, 7 have their own schedules).
    flow_sample = tts.model.flowloss.sample
    tts.model.flowloss.sample = lambda *args, **kwargs: flow_sample(*args, **kwargs, steps=a.flow_steps)
    print("flow_steps override:", a.flow_steps)

# The time of the first fragment is when K-Desktop could start playing.
first = {}
generate = tts.model.generate


def timed_generate(*args, **kwargs):
    result = generate(*args, **kwargs)
    torch.cuda.synchronize()
    first.setdefault("t", time.perf_counter())
    return result


tts.model.generate = timed_generate


def say(text):
    first.clear()
    torch.cuda.synchronize()
    start = time.perf_counter()
    wave = tts.synthesize(task="MULTILINGUAL", text=text, language_id="es", prompt_wav_path=ref_wav,
                          prompt_text=ref_text, use_spk_emb=True)
    torch.cuda.synchronize()
    end = time.perf_counter()
    return wave, (first["t"] - start) * 1000, (end - start) * 1000


say("Hola.")  # warm up (CUDA kernels, the prompt cache)
torch.cuda.reset_peak_memory_stats()
rows = ["run,n,first_ms,total_ms,audio_s,rtf"]
for run in range(1, a.runs + 1):
    for i, text in enumerate(sentences, 1):
        wave, first_ms, total_ms = say(text)
        seconds = wave.shape[-1] / tts.sample_rate
        if run == 1:
            torchaudio.save(os.path.join(out, f"ming_{i}.wav"), wave.float().cpu(), sample_rate=tts.sample_rate)
        rows.append(f"{run},{i},{first_ms:.0f},{total_ms:.0f},{seconds:.2f},{total_ms / 1000 / seconds:.3f}")
        print(rows[-1], flush=True)
peak = torch.cuda.max_memory_allocated() / 2**20
print(f"sample rate {tts.sample_rate}, peak VRAM (torch) {peak:.0f} MiB")
with open(os.path.join(out, "ming.csv"), "w", encoding="utf-8") as f:
    f.write("\n".join(rows) + f"\n# peak_vram_mib={peak:.0f} sample_rate={tts.sample_rate}\n")
