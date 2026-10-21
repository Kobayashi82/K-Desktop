# Avisos de terceros

K-Desktop incluye estas bibliotecas nativas de terceros dentro de su ejecutable.

## SpeexDSP (cancelación de eco)

`speexdsp.dll`, del paquete [SpeexDSPSharp.Natives](https://github.com/AvionBlock/SpeexDSPSharp) (compilado de
[xiph/speexdsp](https://github.com/xiph/speexdsp)). Se usa su cancelador de eco (`Voice/EchoCanceller.cs`).

```
Copyright 2002-2008 	Xiph.org Foundation
Copyright 2002-2008 	Jean-Marc Valin
Copyright 2005-2007	Analog Devices Inc.
Copyright 2005-2008	Commonwealth Scientific and Industrial Research
                        Organisation (CSIRO)
Copyright 1993, 2002, 2006 David Rowe
Copyright 2003 		EpicGames
Copyright 1992-1994	Jutta Degener, Carsten Bormann

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions
are met:

- Redistributions of source code must retain the above copyright
notice, this list of conditions and the following disclaimer.

- Redistributions in binary form must reproduce the above copyright
notice, this list of conditions and the following disclaimer in the
documentation and/or other materials provided with the distribution.

- Neither the name of the Xiph.org Foundation nor the names of its
contributors may be used to endorse or promote products derived from
this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
``AS IS'' AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
A PARTICULAR PURPOSE ARE DISCLAIMED.  IN NO EVENT SHALL THE FOUNDATION OR
CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL,
EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO,
PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR
PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF
LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING
NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

## Modelos que se descargan aparte

No van dentro del ejecutable: se descargan desde Opciones › Voz a la carpeta `Models` de los datos.

- **EmbeddingGemma 300M** (Google; versión ONNX int8 de
  [onnx-community/embeddinggemma-300m-ONNX](https://huggingface.co/onnx-community/embeddinggemma-300m-ONNX)): la capa
  de significado de las órdenes de voz (`Voice/TextEmbedder.cs`, `Voice/SemanticMatcher.cs`). Se distribuye bajo los
  [Gemma Terms of Use](https://ai.google.dev/gemma/terms) y su
  [política de uso prohibido](https://ai.google.dev/gemma/prohibited_use_policy).
- Parakeet TDT 0.6B v3 (NVIDIA, CC BY 4.0), Silero VAD (MIT) y los modelos de openWakeWord (Apache 2.0).
- **Pocket TTS** ([Kyutai](https://kyutai.org/), [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/)), en la
  exportación ONNX de [OpenVoiceOS/phoonnx-pocket-tts](https://huggingface.co/OpenVoiceOS/phoonnx-pocket-tts): la voz
  natural de los asistentes, que se descarga desde Opciones › Módulo Asistente › Voz a `Models\pocket-tts`. El código
  que lo ejecuta (`KDesktop.Assistant.Voice/Pocket`) sigue la implementación de Kyutai (Apache 2.0) y la de phoonnx, y
  viene de Kobii.
