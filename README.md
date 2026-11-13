# Voice Keyer

A small floating button bar that sends voice messages over the air: a function-key voice keyer as its own
window you can drag anywhere on screen. It plays recordings (`.wav`) or speaks text in **your own voice**
(Piper text-to-speech) to your radio's sound card and keys the transmitter.

Voice Keyer is a **standalone program**. It does not talk to, read from or depend on any logging software. Piper
(the speech engine) and two stock voices (one male, one female) ship inside the Voice Keyer folder, so there is nothing else to install.

Messages live in a plain-text function-key file (`.mc` format, one `F<n> label,message` line per key). There is one set of 12 messages; to use a different set, load another `.mc` file.

## What it does

- **F1 to F12 buttons** in a borderless, always-on-top bar. Drag the grip (the dots on the left) to move it; it remembers its position.
- **Docking.** Drop the bar near the bottom of the screen and it docks just above the taskbar; near the top, it docks at the top. While docked, maximized windows stop at the bar instead of going under it (the strip is reserved the way the taskbar's is, with no taskbar buttons). Dropped near the left, right or middle it lines up there. Dragging the bar away gives the space back, and a docked bar docks again at the next start. While a full-screen program is on that screen the bar steps behind it.
- **Message files.** Gear menu > *Load message file (.mc)...* switches to any `.mc` file at once, *Open message file in editor* opens the current one, and *Use Voice Keyer's own message file* goes back to the built-in one. The first F1 to F12 block is the RUN set (calling CQ) and the second is the Search & Pounce set (hunting). The MODE button on the bar switches between them and remembers the choice.
- **CALL box** on the bar: the callsign of the station you are working. `{ICAO !}` and `{CALL}` in a message use it, and `{WIPE}` clears it. Type it, or let Voice Keyer take it from Ham Radio Deluxe (next item).
- **Callsign from HRD Logbook (optional).** If you log with Ham Radio Deluxe, Voice Keyer copies the callsign from HRD Logbook's open Add or Edit window into the CALL box (and the RST Sent value for `{SENTRST}`), within about a second of it changing. It reads the window through Windows UI Automation (the same interface screen readers use), so HRD needs no add-ons or settings, and it only reads: nothing in HRD is ever changed. A call you type by hand stays until HRD's changes again, and when HRD's box goes blank for a new QSO the CALL box clears. Untick *Take callsign from HRD Logbook* in the gear menu to turn it off. Voice Keyer works the same without HRD installed.
- **Right-click a button** to edit its label and script, preview it without keying the radio, or open the whole message file in your editor. Edits made in an external editor are picked up automatically.
- **Voice**: any Piper `.onnx` voice. Change it in *Settings > Voice*. *Import voice...* copies a newly trained voice into your model folder.
- **VOL button** sets the sending volume (click for a slider, or use the mouse wheel over it). It turns down what Voice Keyer plays to the radio and takes effect immediately, even mid-message; the Windows sound card setting is not touched. 100% is full level, 50% is about 12 dB down.
- **Esc** or the red square stops a message immediately (and unkeys the radio).
- **Keys work in any program.** The F1 to F12 keys reach Voice Keyer even when another window has the focus, and Esc stops a message from anywhere while one is playing. The catch: while Voice Keyer runs, other programs no longer receive F1 to F12 (a browser's F5, for example). Untick *F-keys work in any program* in the gear menu to limit them to when Voice Keyer is focused. If another program has already claimed a key, the status line names it.
- **Voice trainer** (gear menu): a guided recorder that builds a training dataset from your own voice, plus a Google Colab notebook that trains the voice. See below.

### Supported macros

Message syntax:

| Macro | Meaning |
| --- | --- |
| `{TTS text}` | Speak `text` in the selected voice |
| `{TTSParms 0.7}` | Speed for the speech that follows (Piper length scale; lower = faster). Optional 2nd/3rd numbers: noise scale, noise w |
| `{TTSTrim}` | Accepted; silence is always trimmed from the ends of spoken clips |
| `{ICAO !}` | NATO phonetic spelling of the callsign in the CALL box |
| `{ICAO ABC}` | NATO phonetic spelling of the literal text `ABC` |
| `{MYCALL}` `{OPERATOR}` `{ROVERQTH}` `{CALL}` | Station call, operator and Rover QTH (all in Settings), and the call in the CALL box |
| `Folder\file.wav` | Play a recording, relative to the *Wav folder* in Settings (`{OPERATOR}\Cq.wav`) |
| `{PARK}` `{PARK2}` `{PARKS}` | POTA park reference(s) from the PARK box on the bar (or Settings): the first, the second, or all. Spoken digit by digit, so `US-2038` is "U S two zero three eight". `{TTS into park {ICAO {PARK}}}` spells it phonetically instead |
| `{SENTRST}` (or `{RST}`) | The report you are sending, from the RST Sent box of HRD Logbook's open Add or Edit window, spoken digit by digit ("five seven"). A report over S9 drops the dB: `59+` is "five nine plus" and `59+10dB` is "five nine plus ten". Says "five nine" when HRD is not open or the box is blank |
| `{WIPE}` | Clear the CALL box |

Anything else in braces is not supported and shows a warning in the status line.

## Install

Run `VoiceKeyer-vX.Y.Z-setup.exe`. It needs administrator rights to install (it writes to Program Files; the program itself runs without them). No .NET runtime or other software is needed: Piper (the speech engine) and two stock voices, one male and one female, are inside.

The installer puts the program in `C:\Program Files\K5JSG\VoiceKeyer`, adds it to Add/Remove Programs, and offers an optional desktop shortcut. Running a newer installer upgrades in place: it keeps the one Installed Apps entry and your shortcuts, removes any other installed copy, and clears out any file an older version installed that the new one no longer ships.

Your settings, message file, voices and phrase cache live outside the program folder, in `%AppData%\VoiceKeyer` and `%LocalAppData%\VoiceKeyer`, so installs, upgrades and silent uninstalls never touch them. A normal uninstall asks whether to remove them too (default No, because they include any voice of your own). Only the two stock voices are ever packaged; a personal voice is never part of the installer. To use your own voice on another PC, see [Moving your voice to another PC](#moving-your-voice-to-another-pc).

## Setup

1. Install it (above), or run it from source (needs the .NET 10 SDK): `dotnet run --project src/VoiceKeyer`
2. Open the gear menu > **Settings**. Nothing is required: Voice Keyer uses its own `messages.mc` in `%AppData%\VoiceKeyer` until you pick another message file, and a bundled stock voice (`en_US-joe-medium`, male, is chosen first; `en_US-ljspeech-medium` is the female one) until you import your own. Pick either in *Settings > Voice*.
3. Choose the **output sound card** that goes to your radio (for example `FTDX10 (OUTPUT) (USB AUDIO CODEC)`). Leave it blank to play through the Windows default device, which is handy for testing with *Preview*.
4. Choose a **PTT method**:
   - *None*: the radio's VOX keys the transmitter.
   - *Serial RTS / DTR*: a serial port wired to the radio's PTT line. A port can only be open in one program, so pick one nothing else is using.
   - *CAT command*: sends an on/off string (default `TX1;` and `TX0;`) to the port. Check your radio's manual, and note the same port-sharing limit.
5. Set **My callsign** (`{MYCALL}`), **Operator** (`{OPERATOR}`) and **Rover QTH** (`{ROVERQTH}`) and **POTA park(s)** (`{PARK}`) if your messages use them.

> Always test with **Preview** (right-click a button) first. Preview plays the message without keying the radio.

## Training a voice of your own

Anyone can do this; it needs a microphone and a free Google account (Colab provides the GPU).

1. **Record.** Gear menu > *Voice trainer*. Give the voice a name, pick your microphone, and read each prompt as it appears
   (press Space to start and stop). Use a quiet room and keep the microphone at the same distance. Each clip is checked for clipping, low level, noise and length. Record at least 150 prompts (about 15 minutes); 250 or more is better. You can stop and continue later.
2. **Export.** Click *Export for training*. This creates `<name>_dataset.zip` (22.05 kHz mono clips plus `metadata.csv`).
3. **Train.** Open `trainer/Piper_Voice_Trainer.ipynb` in [Google Colab](https://colab.research.google.com), set the runtime to a GPU, and run the cells in order. It fine-tunes a Piper base checkpoint (downloaded by the notebook itself from Piper's checkpoint repository, not part of this repository) on your clips (about 1 to 3 hours) and downloads `<name>_voice.zip`.
4. **Use it.** *Settings > Import voice...* and pick the downloaded zip (or the `.onnx`, with its `.onnx.json` beside it). Pick it in the *Voice* box.

### Moving your voice to another PC

A trained voice is personal, so it is never part of this repository or the installer: only the two stock voices ship with Voice Keyer. Your voices live in `%AppData%\VoiceKeyer\voices`.

To use a voice on another PC with Voice Keyer installed: *Settings > Export voice...* writes the selected voice to one zip, copy that zip over, then *Settings > Import voice...* on the other PC. (Copying the voice's `.onnx` and `.onnx.json` into that PC's voice folder by hand works too.)

Changing voices later is just picking a different one in Settings. The synthesized-phrase cache is keyed on the voice, so switching never plays stale audio.

The notebook uses the `OHF-Voice/piper1-gpl` training code. It follows that project's documented commands, but it has not been run end to end yet; if a cell fails after an upstream change, check that project's `docs/TRAINING.md`.

## Notes

- **Speed.** The first time a phrase is spoken it takes about a second to render. Phrases in the macro file are rendered in the background when the file loads, so those are instant. A callsign typed in the entry window is rendered when you press the key, so allow about a second.
- Settings live in `%AppData%\VoiceKeyer\settings.json`, voices in `%AppData%\VoiceKeyer\voices`, recordings under `%AppData%\VoiceKeyer\wav` by default; rendered phrases are cached in `%LocalAppData%\VoiceKeyer\cache` (safe to delete). If you ran this program under its earlier name (Keyer), the old folders are moved to these names automatically the first time Voice Keyer starts.
- Tests: `dotnet test`. One test renders with the bundled Piper and a stock voice.
- **Building the installer:** `.\build.ps1` runs the tests, publishes a self-contained single-file exe with Piper and the stock voices beside it, and compiles `installer\VoiceKeyer.iss` with Inno Setup into `dist\VoiceKeyer-v<version>-setup.exe`. The version comes from `<Version>` in `src\VoiceKeyer\VoiceKeyer.csproj` (or `-Version`). The build stops if a loose DLL would be left out of the install, or if anything other than the two stock voices is in `voices\`. The installer's AppId must never change: it is how Windows recognises an upgrade.
- **Icon:** the program, installer and shortcuts use `src\VoiceKeyer\Assets\logo.ico`, made from `Assets\Logo.png`.

## Layout

```
src/VoiceKeyer/         WPF app (net10.0-windows)
  Macros/               .mc parser and macro engine
  Tts/                  Piper wrapper with on-disk cache
  Audio/                playback, PTT, silence trimming
  Services/             settings, message sending, voice packages, HRD callsign link
  Bundled/              Piper engine (rhasspy/piper, MIT) and the two stock voices, copied beside VoiceKeyer.exe
  Trainer/              prompt list and dataset recorder
  UI/                   bar, settings, macro editor, voice trainer
tests/VoiceKeyer.Tests  unit tests
trainer/                Colab training notebook
```

## License

Voice Keyer is free software, released under the GNU General Public License v3.0 (see `License.txt`). It ships with third-party components under their own licenses: see `THIRD-PARTY-NOTICES.md`.
