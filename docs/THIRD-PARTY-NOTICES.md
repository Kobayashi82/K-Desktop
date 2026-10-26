# Avisos de terceros

K-Desktop incluye estas bibliotecas de terceros dentro de su ejecutable.

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

## Concentus (grabaciones en Opus)

Las grabaciones de audio en Opus se codifican con [Concentus](https://github.com/lostromb/concentus) (paquete NuGet
`Concentus`, una versión en C# del códec Opus de Xiph.org; `Platform/AudioEncoders.cs`). Las de MP3 usan el codificador de
Windows.

```
Copyright (c) by various holding parties, including (but not limited to):
Skype Limited, Xiph.Org Foundation, CSIRO, Microsoft Corporation,
Jean-Marc Valin, Gregory Maxwell, Mark Borgerding, Timothy B. Terriberry,
Logan Stromberg. All rights are reserved by their respective holders.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

* Redistributions of source code must retain the above copyright notice, this
  list of conditions and the following disclaimer.

* Redistributions in binary form must reproduce the above copyright notice,
  this list of conditions and the following disclaimer in the documentation
  and/or other materials provided with the distribution.

* Neither the name of Internet Society, IETF or IETF Trust, nor the
   names of specific contributors, may be used to endorse or promote
   products derived from this software without specific prior written
   permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

## PdfPig (texto de los PDF)

El proceso de los asistentes lee el texto de los PDF que se les dan con [PdfPig](https://github.com/UglyToad/PdfPig)
(paquete NuGet `PdfPig`, licencia Apache 2.0).

## Modelos que se descargan aparte

No van dentro del ejecutable: se descargan desde Opciones › Voz a la carpeta `Models` de los datos.

- **EmbeddingGemma 300M** (Google; versión ONNX int8 de
  [onnx-community/embeddinggemma-300m-ONNX](https://huggingface.co/onnx-community/embeddinggemma-300m-ONNX)): la capa
  de significado de las órdenes de voz (`Voice/TextEmbedder.cs`, `Voice/SemanticMatcher.cs`). Se distribuye bajo los
  [Gemma Terms of Use](https://ai.google.dev/gemma/terms) y su
  [política de uso prohibido](https://ai.google.dev/gemma/prohibited_use_policy).
- Parakeet TDT 0.6B v3 (NVIDIA, CC BY 4.0), Silero VAD (MIT) y los modelos de openWakeWord (Apache 2.0).
- **Kokoro 82M** ([hexgrad](https://huggingface.co/hexgrad/Kokoro-82M), Apache 2.0), en la exportación ONNX de
  [onnx-community/Kokoro-82M-v1.0-ONNX](https://huggingface.co/onnx-community/Kokoro-82M-v1.0-ONNX): la voz natural de
  los asistentes, que se descarga desde Opciones › Módulo Asistente › Voz a `Models\kokoro`. Los fonemas del español
  los escribe K-Desktop con reglas propias (`KDesktop.Assistant.Voice/Kokoro/SpanishPhonemes`), comprobadas contra lo
  que escribe espeak-ng; el vocabulario de fonemas y el tratamiento de cada idioma siguen a misaki (hexgrad, Apache 2.0).
- **Piper** ([rhasspy/piper](https://github.com/rhasspy/piper), MIT) y sus voces
  ([rhasspy/piper-voices](https://huggingface.co/rhasspy/piper-voices), en un commit fijo): la segunda voz natural de los
  asistentes. K-Desktop no lleva código de Piper: lee sus `.onnx` con ONNX Runtime y escribe los números de los fonemas
  como piper-phonemize. Cada voz se descarga al elegirla a `Models\piper` y **tiene la licencia de sus grabaciones**
  (su `MODEL_CARD` en el repositorio de voces).
- **espeak-ng** ([espeak-ng](https://github.com/espeak-ng/espeak-ng), **GPL 3.0**): **no va dentro de K-Desktop**. Para
  inglés, francés, italiano y portugués, Kokoro necesita sus fonemas, y Piper los necesita siempre: el usuario lo descarga
  aparte, como un modelo más (el paquete para Windows de [espeakng-loader](https://pypi.org/project/espeakng-loader/), MIT,
  con la DLL y los datos de espeak-ng), a `Models\espeak-ng`, y el proceso del asistente lo carga desde ahí solo si está
  y solo cuando hace falta. Kokoro en español no lo usa.
