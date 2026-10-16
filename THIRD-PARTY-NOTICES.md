# Third-party notices

Voice Keyer itself is released under the GNU General Public License v3.0 (`License.txt`).
The installer and this repository also contain the following third-party components, each under its own license.

## Piper text-to-speech engine

`src/VoiceKeyer/Bundled/piper/` is the Windows build of [Piper](https://github.com/rhasspy/piper)
(release 2023.11.14-2), unmodified. It is used to turn message text into speech, and it brings these components:

| Component | License | Source |
| --- | --- | --- |
| Piper (`piper.exe`) | MIT | https://github.com/rhasspy/piper |
| piper-phonemize (`piper_phonemize.dll`) | MIT | https://github.com/rhasspy/piper-phonemize |
| espeak-ng (`espeak-ng.dll`, `espeak-ng-data`) | GPL-3.0-or-later | https://github.com/espeak-ng/espeak-ng |
| ONNX Runtime (`onnxruntime.dll`, `onnxruntime_providers_shared.dll`) | MIT | https://github.com/microsoft/onnxruntime |
| libtashkeel model (`libtashkeel_model.ort`) | MIT | https://github.com/mush42/libtashkeel |

The complete corresponding source for espeak-ng is available at the link above.

## Stock voices

`src/VoiceKeyer/Bundled/voices/` holds two voices from the [Piper voices](https://huggingface.co/rhasspy/piper-voices) collection, chosen because they may be freely redistributed:

| Voice | Speaker | Dataset license |
| --- | --- | --- |
| `en_US-joe-medium` | male | CC0 (public domain dedication) |
| `en_US-ljspeech-medium` | female | public domain (LJ Speech dataset) |

A voice you train yourself with the voice trainer is yours, is never part of this repository or the installer,
and is not covered by these notices.

## Libraries used by the program

- [NAudio](https://github.com/naudio/NAudio): MIT, Copyright 2020 Mark Heath
- System.IO.Ports (part of .NET): MIT

## The MIT license (Piper)

MIT License

Copyright (c) 2022 Michael Hansen

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
