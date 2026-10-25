# DecisionBench

Experimento de la rama `decision_nvidia`: qué decidiría un modelo de decisión (o un LLM con un token) en la zona dudosa
de los comandos, frente a lo que deciden hoy `IntentMatcher` y `SemanticMatcher`. Resultados en
`docs/assistant/decision-model-pruebas.md`. No forma parte de K-Desktop.

```powershell
dotnet build tools\decision\DecisionBench\DecisionBench.csproj
$exe = "tools\decision\DecisionBench\bin\Debug\net10.0-windows10.0.19041.0\win-x64\DecisionBench.exe"

# 1. Lo de hoy y los candidatos de EmbeddingGemma (solo lee config.json; usa su propia carpeta de datos, con
#    Models\embeddinggemma copiado o por enlaces duros, para no tocar anchors.bin).
& $exe baseline --config "$env:APPDATA\K-Desktop 5\config.json" --cases artifacts\decision\cases.json --data <carpeta> --out artifacts\decision\baseline.json

# 2. Un modelo en un llama-server ya arrancado: «systemone» para los modelos de decisión (/v1/systemone), «letter» para
#    un LLM normal (una letra, top_logprobs). --top 0 = todos los comandos. BENCH_VARIANT: «nodesc», «noul».
& $exe decide --baseline artifacts\decision\baseline.json --method systemone --url http://127.0.0.1:8091 --name d1 --out artifacts\decision\r-d1.json --top 8

# 3. Tablas (BENCH_FAILS=1 lista los fallos).
& $exe report --baseline artifacts\decision\baseline.json --results artifacts\decision\r-d1.json
```

Cada caso de `cases.json`: `{ "say": "…", "lang": "es", "cat": "paraphrase|slip|power|power-no|none|ai|tie|compound|named",
"expect": ["run:Comando", "run:A+B", "ask:A|B|C", "none"] }`. Las órdenes multimedia van como `#Mute`, `#MusicOn`…
