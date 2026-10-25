# Prueba de voces (rama `decision_nvidia`)

Compara Ming-3.0-tts-0.5b con el Kokoro de K-Desktop. Resultados: `docs/assistant/tts-ming-pruebas.md`. Nada de esto es
parte de K-Desktop.

1. Kokoro como lo dice K-Desktop (escribe `ref.wav`/`ref.txt`, la voz que copia Ming, y `kokoro_<n>.wav`):

   ```powershell
   dotnet run --project tools\tts\KokoroBench -c Release -- tools\tts\frases.txt <salida> [ef_dora]
   ```

2. Ming (entorno aparte, Python 3.11):

   ```powershell
   git clone --depth 1 https://github.com/inclusionAI/Ming-3.0-tts.git <repo>
   py -3.11 -m venv <venv>
   <venv>\Scripts\python -m pip install torch==2.6.0 torchaudio==2.6.0 --index-url https://download.pytorch.org/whl/cu124
   <venv>\Scripts\python -m pip install transformers==4.46.3 tokenizers accelerate==1.3.0 "huggingface_hub[cli]" numpy soundfile==0.12.1 tqdm loguru x_transformers==2.9.2 diffusers==0.33.0 onnxruntime inflect einops packaging sherpa-onnx
   <venv>\Scripts\hf download inclusionAI/Ming-3.0-tts-0.5b --local-dir <modelo>
   <venv>\Scripts\python tools\tts\ming_bench.py <repo> <modelo> tools\tts\frases.txt <salida> [--runs 2] [--flow_steps 5]
   ```

3. Leerlo todo con el Parakeet de K-Desktop y contar palabras mal:

   ```powershell
   <venv>\Scripts\python tools\tts\transcribe.py "$env:APPDATA\K-Desktop 5\Models\parakeet-tdt-0.6b-v3" tools\tts\frases.txt <salida> kokoro <salida> ming
   ```
