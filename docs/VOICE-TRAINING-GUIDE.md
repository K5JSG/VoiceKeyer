# Getting a good voice on the first try

Notes from training and tuning a real operator voice. Follow them in order and the first voice should be close to usable.

## Before you record
1. **Set your call sign.** Settings > *My call*. The reading list is built around it (your call alone, in CQ loops, in reports, with "stroke Papa"). The list is saved when you start, so set it first. If you forget, close the trainer, set it, and reopen it before recording anything.
2. **Add your own lines.** Create `my_phrases.txt` in your voice folder (`Documents\Voice Keyer Training\<voice name>`), one line per prompt, written the way you say it: a local contest ("CQ, en why queue pee."), county names, a hard-to-say town. These come before the general English.
3. **Use the microphone you use on air.** A headset you wear for phone operation gives a voice that matches your on-air sound. Set its Windows level so your peaks reach about -12 to -6 dBFS; the recorder warns about clipping, low level and noise. No EQ and no noise removal: your radio's own TX EQ shapes the sound later.
4. **Quiet room, same distance, steady pace.** Read each prompt as you would say it on the air, calls included. Redo a clip when you stumble; a few bad clips do more harm than a few missing ones. When you have recorded a good part of the list, press **Check recordings**: it compares your clips with each other and suggests the ones to record again (a possible pop or thump, background noise, much louder or quieter than the rest, or a length that does not fit the words). It is a measurement, not a listener, so play a flagged clip before you redo it. Say yes and the trainer steps through just those clips; your old takes are kept in `redo_backup`.

## Recording
- Record the whole list if you can (about 300 prompts, about 20 minutes). At least 150 (15 minutes) still works. The most useful lines come first.
- Say "Papa" naturally. The list has Papa at the start, middle and end of calls on purpose: with only "stroke Papa" in the data, a trained voice squashes a Papa inside a call to "pa".
- Spell acronyms as you say them ("en why queue pee", not "NYQP"), so the text matches the sound.
- Export (the trainer applies a 75 Hz high-pass and raises quiet clips to a healthy level in the zip copy only; your recordings are not changed).

## Training
- Run `trainer/Piper_Voice_Trainer.ipynb` in Google Colab with a GPU (see the README). More epochs is not better: the voice that sounded best was reached after only a few dozen epochs of fine-tuning. If you can export several checkpoints, listen to a few and pick by ear.
- Listen at normal speed (length scale 1.0) when comparing. Compare the same phrases each time: your call, "CQ DX" three times, a call with a P in the middle, a report.

## Tuning in the keyer (no retraining)
Use `{TTSParms length noise_scale noise_w}` before your `{TTS ...}` text; it stays in force until changed.
- **Speed:** a length of 0.8 is a good start; 1.0 can sound slow on air.
- **noise_w 0.4** (default 0.8) evens out repeated phrases. Without it, the second "CQ DX" in a row can come out faster or slower than the first.
- **noise_scale 0.4** (default 0.667) makes the voice steadier; raise it if consonants such as P sound smoothed away.
- **Pauses:** Piper pauses only at punctuation. A comma is short and a full stop is longer and lifts the pitch. Write "CQ NYQP. CQ NYQP. CQ NYQP." (or the spoken form) rather than relying on spaces.
- **Papa inside a call** is respelled "Pop-paw" by `{ICAO}` automatically; a Papa after a slash ("stroke Papa") is left alone. A voice trained on the new list may not need this.
- **Hard words** (a town, a county) sound robotic when the training data has only a few examples. Respell them for the vowel ("Munroe" for Monroe) or add them to `my_phrases.txt` and record several times.

## On the air (Yaesu FTDX10 example; other rigs are similar)
Set the data/mic source to the USB audio, keep the Windows volume at about 80 percent, and set the radio's USB gain so the ALC just touches. Keep processor/compressor light (10 dB or less) and avoid VOX in favour of CAT or RTS/DTR PTT with 150 to 250 ms lead and 200 to 300 ms tail. Test into a dummy load with the monitor on, then ask a friend or a KiwiSDR how you sound. If the voice sounds harsh, lower the TX EQ boost for the keyer message.
