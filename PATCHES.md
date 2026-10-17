# Exact changes

Every change is gated on the SHA-256 of the file being changed, and verified again after writing.
If a file doesn't match the known version, that step is skipped and the file is left alone.

## 1. Old CPU fix (`ScriptsAssDef.dll` + `ggml-cpu.dll`), game build 0.1.71

**Problem.** Since 0.1.71 the game ships an AVX-only speech backend (`Plugins\x86_64\avx\ggml-cpu.dll`) for CPUs without AVX2.
But Unity preloads every native plugin in `Plugins\x86_64` at startup, including the AVX2 build `ggml-cpu.dll`, before any game code runs.
`WindowsSpeechCompatibility.EnsureAvxBackend()` then finds the wrong module already loaded and throws
*"An incompatible Whisper CPU backend was already loaded"*, so speech never starts.

**Fix.**
- Copy the developers' own `avx\ggml-cpu.dll` over `ggml-cpu.dll`, so the preloaded module is the AVX build.
- In `ScriptsAssDef.dll` (SHA-256 `b73088146d09e75d00b074ac05ce822f9911d181eb0d184f7dbaa5fc8406c2f9`),
  at file offset `0x117E97` inside `WindowsSpeechCompatibility.EnsureCpuSupported()`:
  replace `28 5B 38 00 06` (`call EnsureAvxBackend()`) with `00 00 00 00 00` (5 × `nop`).
  The CPU feature check before it stays in place.
- Result: `ScriptsAssDef.dll` SHA-256 `76d6f0d311de3bd7dcf6d16f2025848e43b8c07b7c533a80df441d29f81f6bb4`,
  `ggml-cpu.dll` SHA-256 `52e1ce23beb706a45b9fe8cbf9cb3860aeb63dca082bb908ec6347fae7e3ad53` (identical to the game's `avx\ggml-cpu.dll`).

Only applied when Windows reports AVX but no AVX2 (`IsProcessorFeaturePresent` 39 / 40).

## 2a. 8-bit speech model (`ggml-base.en.bin`, `ggml-base.bin`)

**Problem.** The game's Whisper `base` models are stored as 16-bit floats (f16).
CPUs without the F16C instruction set (e.g. Intel 2nd gen Core) convert every weight in software.
In our benchmark the encoder took 20.8 s for one 30-second window.

**Fix.** Convert the weights to whisper.cpp's `q8_0` format, the same algorithm as whisper.cpp's `examples/quantize`
(`quantize_row_q8_0_ref`, same tensors skipped, same header and version).
- The CI job `verify-model-conversion` checks the output is byte-identical to the official tool on every build.
- Size drops from 148 MB to 78 MB, and the encoder from 20.8 s to 8.5 s in the same benchmark.
- On modern CPUs the gain is about 10%.

## 2b. Listening window (`com.whisper.unity.dll`)

**Problem.** For every phrase, even a 1-second "hello", Whisper's encoder processes a full 30-second window (`audio_ctx = 0`).

**Fix.** Rewrite the 119-byte IL body of `Whisper.WhisperWrapper.InferenceWhisper(float[] samples, WhisperNativeParams param)`
in `com.whisper.unity.dll` (SHA-256 `611ca87d34dbb017ca0bac5a4a493c92d122cd5af211d78ab58bd46ec653e07b`, identical in 0.1.69 and 0.1.71),
at file offset `0x19F4`. The new body does the following:

```csharp
int ctx = samples.Length / 320 + 75;   // encoder frames of 20 ms, plus a 1.5 s margin
if (ctx < 512) ctx = 512;              // minimum window of about 10 s
if (ctx >= 1500) ctx = 0;              // long clips: whisper's normal full 30 s window
param.audio_ctx = ctx;
// ...then the original code: pin samples, call whisper_full(...), return false on error
```

- Two log calls that only fed the plugin's internal (disabled) logger were removed to make room.
- The `whisper_full` call and the error handling are the original bytes, unchanged.
- Unused space is padded with `nop`. Result SHA-256 `fb2ebfbe63008e3953776d11ce24a6bb0a99d5d1c73862fb4a3f4ccd8352fdc7`.
- Measured: encoder for a 1.1 s clip went from 8.8 s to 1.4–1.6 s on an AVX-only build.
  In-game on an i5-2500, a 2.1 s phrase was transcribed in 4.1 s, against about 15 s before.

## 3. Caller voice fix (`PocketTTS.dll`), game builds 0.1.71–0.1.74

**Problem.** In 0.1.74 every Portuguese caller line is cut off after about 0.3 s (players hear "DAL-" from "DALE!").
The game logs `provider=Pocket language=pt stage=generation_done ... queuedAudioMs=290` for almost every line, whatever its length.
The Pocket TTS loop in `PocketTTS.PocketTTSModel.RunPrompt` generates one 80 ms frame per step and stops 3 frames after the
first frame whose `eos_logit` is above -4. The Portuguese model's `eos_logit` is above -4 for its first 3–5 frames
(e.g. -2.5, -3.1, -3.7, -4.9, ...) and only rises again at the real end of the sentence, so generation stops after 4 frames.
English never does this: its first frames are around -7 to -10.

**Fix.** In `PocketTTS.dll` (SHA-256 `b47b39cd9be3417efd2c1c0e61a124f762685b4caf116e20e08165b706bf11bf`, identical in 0.1.71 and 0.1.74),
in `RunPrompt`, whose IL code starts at file offset `0x3280`:
- IL `0x06C6`–`0x06D7` (file `0x3946`, 18 bytes): `if (num13 < 0 && num15 > -4f) num13 = num14;` → 18 × `nop`.
- IL `0x0793`–`0x07C8` (file `0x3A13`, 54 bytes), directly before the loop's break check, previously
  `if (EnableProfiling) frameBirthTimes[num14] = lifeStopwatch.ElapsedMilliseconds;` (only feeds the `[TTS RT]` debug log),
  now holds the same check with one extra condition, padded with `nop`:

```csharp
if (num13 < 0 && num14 >= 8 && num15 > -4f) num13 = num14;   // ignore "end of speech" in the first 8 frames (~0.64 s)
```

- Result SHA-256 `6cf88903554073d028f07fca9a8a8fd8274fa46f32b37e4518e226b6754bd109`.
- Tested by running the game's Portuguese and English voice models outside the game with the same logic:
  Portuguese lines go from 0.32 s to their full length (2.6–4.5 s) and still end on their own; the shortest real
  sentence end seen was frame 13 (Portuguese) / 9 (English). English sentences are unchanged.
  One-word lines ("Yes.", "Sim.") get up to ~0.5 s of near-silence added at the end.

## 4. Graphics card test (no files changed)

The tool starts itself again as a child process (`--vulkan-probe`).
- The child loads the game's own `ggml-base.dll` and `ggml.dll` from `Plugins\x86_64`, with `KOLKATA_WHISPER_CPU_ONLY` removed from its environment.
- It calls `ggml_backend_dev_count()`. In the developers' build this is what loads `ggml-vulkan.dll`.
- It then calls `ggml_backend_dev_init()` and `ggml_backend_free()` for each GPU device.
- If the child crashes, or doesn't finish within 30 s, the tool tells the player to add the Steam launch option `-whisper-cpu`.
  The game itself already supports that option.

It reads `Steam\userdata\*\config\localconfig.vdf` to see whether `-whisper-cpu` is already set. It never writes to it.

## 5. Watermark tip (no files changed)

Reads the Unity PlayerPrefs value `KolkataOS.BanditCam.Watermark` from
`HKEY_CURRENT_USER\Software\Jatater Worldwide\Scam With Your Friends`, only to say whether the watermark is already off.
