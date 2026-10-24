# Prepares everything for the tutorial screenshots: the demo data folder ($Work\kddemo, made from scratch), the demo
# documents for «What it knows» (Documents\Manuals), the pretend MCP servers (Documents\MCP), a folder of hard links to a
# few models with clean names (E:\AI Models) and the synthetic voices of the simulated microphone. cleanup.ps1 undoes it.
. "$PSScriptRoot\_paths.ps1"
if (Test-Path $Data) { "Ya existe ${Data}: bórralo (o ejecuta cleanup.ps1) antes de prepararlo otra vez."; exit 1 }
New-Item -ItemType Directory -Force $Work, $Raw, "$Work\fakevoice" | Out-Null

# Documents and MCP servers in neat paths (they show up in Settings).
New-Item -ItemType Directory -Force $Knowledge, (Split-Path $McpScript) | Out-Null
Copy-Item "$PSScriptRoot\demo-docs\*" $Knowledge -Recurse -Force
Copy-Item "$PSScriptRoot\demo-mcp.js" $McpScript -Force

# Models: hard links (nothing is copied) to the ones whose names are fine for a video.
$src = 'E:\ProjectAI\Open WebUI'
$models = [ordered]@{
  'Qwen 3.5 4B' = @("$src\Llama-CPP\Models\Qwen 3.5 4B\Qwen3.5-4B-Q4_K_M.gguf", "$src\Llama-CPP\Models\Qwen 3.5 4B\Qwen3.5-4B-mmproj-F16.gguf")
  'Qwen 3.5 9B' = @("$src\Llama-CPP\Models\Qwen 3.5 9B\Qwen3.5-9B-Q4_K_M.gguf", "$src\Llama-CPP\Models\Qwen 3.5 9B\Qwen3.5-9B-mmproj-BF16.gguf")
  'Qwen 3.6 35B A3B' = @("$src\Llama-CPP\Models\Qwen 3.6 35B A3B\Qwen3.6-35B-A3B-UD-Q4_K_XL.gguf", "$src\Llama-CPP\Models\Qwen 3.6 35B A3B\Qwen3.6-35B-A3B-mmproj-BF16.gguf")
  'Bonsai 2 27B' = @("$src\Bonsai\Bonsai-demo\models\bonsai2-gguf\27B\Ternary-Bonsai-2-27B-PQ2_0.gguf", "$src\Bonsai\Bonsai-demo\models\bonsai2-gguf\27B\Ternary-Bonsai-2-27B-mmproj-Q8_0.gguf")
}
foreach ($name in $models.Keys) {
  New-Item -ItemType Directory -Force "$DemoModels\$name" | Out-Null
  foreach ($f in $models[$name]) {
    $d = "$DemoModels\$name\$(Split-Path $f -Leaf)"
    if ((Test-Path $f) -and -not (Test-Path $d)) { cmd /c mklink /H "$d" "$f" | Out-Null }
  }
}

# Simulated microphone: what an order says (session.wav) and six takes of «Computer» for the wake word wizard.
Add-Type -AssemblyName System.Speech
$syn = New-Object System.Speech.Synthesis.SpeechSynthesizer
$syn.SelectVoice('Microsoft Zira Desktop')
$fmt = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(16000, [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen, [System.Speech.AudioFormat.AudioChannel]::Mono)
$syn.Rate = -2; $syn.SetOutputToWaveFile("$Work\session.wav", $fmt); $syn.Speak('Open the calculator.'); $syn.SetOutputToNull()
$i = 0
foreach ($rate in -2, 0, -1, 1, -3, 0) { $i++; $syn.Rate = $rate; $syn.SetOutputToWaveFile("$Work\fakevoice\word_$i.wav", $fmt); $syn.Speak('Computer'); $syn.SetOutputToNull() }
$syn.Dispose()

node "$PSScriptRoot\mkdemo.js" $Data $Knowledge $McpScript $DemoModels
"Listo: $Data"
