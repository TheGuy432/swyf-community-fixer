# Exact changes

Every change is gated on the SHA-256 of the file being changed, and verified again after writing.
If a file doesn't match the known version, that step is skipped and the file is left alone.

## Game build 26020526 (18 Oct 2026 update)

This update includes the developers' own fixes for 3a–3d (caller voices), 4 (Qwen3 limits: 7680 MB / 15000 MB) and 5
(English keeps Whisper on CPU speech). On this build those options show as *fixed in the game* and the tool doesn't touch
`PocketTTS.dll`, `Assembly-CSharp.dll` or `ScriptsAssDef.dll` at all. It recognises the build by the Steam build id in
`steamapps\appmanifest_4954910.acf` (26020526 or newer) or by these SHA-256 hashes:
`PocketTTS.dll` `37b76e8b372460e5eb6372022ea4277a459289b38f2ef04fe97573f9c35d2e95`,
`Assembly-CSharp.dll` `0edee3f51411f069a876be2f24e4b4acc41e1a08fe6d01c3a0a00f872f6007a7`,
`ScriptsAssDef.dll` `75cc108ace20b54d29579e739fbc20ee6a3381a61f2de10afe5c85f772308d22`.
A file that is still a 0.1.74 file (patched or not) always wins over the build id.

The listening window (2b) is not in the update, so the tool still offers it (new hashes below). Leftover backups of the
0.1.74 files (`.whisperkeep-backup`, the old `com.whisper.unity.dll.speedup-backup`) are deleted, since nothing can restore
them any more. The 8-bit models (2a) are unchanged by the update and stay installed.

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
- Build 26020526 ships a rebuilt `com.whisper.unity.dll` (SHA-256 `3912ce1585fe33097c1a597c951bd9d28d89e35aee96b21bc80f94e97f26cab4`)
  with the same method body at the same offset; the same patch gives `6e815e529e3a8f9580982ad8cceb66674695b00dd115c9ea8b066042849c2be1`.
- Measured: encoder for a 1.1 s clip went from 8.8 s to 1.4–1.6 s on an AVX-only build.
  In-game on an i5-2500, a 2.1 s phrase was transcribed in 4.1 s, against about 15 s before.

## 3. Caller voice fix (`PocketTTS.dll` + `Assembly-CSharp.dll`), game build 0.1.74

Tested outside the game on all 12 Pocket TTS languages and all 11 MOSS TTS languages (re-implementations of the game's
own pipelines, see the v4.4 notes); not yet confirmed in the game.

All changes below are *regions*: the tool only accepts a file if every region holds either the game's original bytes or
our patched bytes, and putting all regions back gives exactly the original file (SHA-256). That's how fixes in the same
file can be switched on and off independently, and why no backup files are needed any more.

**3a. End-of-line rule** (`PocketTTS.dll`, SHA-256 `b47b39cd9be3417efd2c1c0e61a124f762685b4caf116e20e08165b706bf11bf`, same in
0.1.71 and 0.1.74; `PocketTTSModel.RunPrompt`, IL code at file offset `0x3280`). The game stops 3 frames after the first frame
whose end-of-speech score is above -4. Portuguese crosses -4 in its first frames (every line ~0.3 s), and French, German,
Italian and Korean cross it briefly mid-sentence. New rule: from frame 8, stop when the score is above 0, or above -3 for 10
frames in a row. Across 10 languages it got 118 of 120 test lines right (game: 97), at ~0.4 s extra silence per line.
- IL `0x06C6` (18 bytes, the old check) → `nop`s.
- IL `0x0793` (54 bytes, the optional profiling block that only fed a debug log) →
  `V_55 = (num14 != 0 && eos > -3f) ? V_55 + 1 : 0; if (num13 < 0 && num14 >= 8 && (eos > 0f || V_55 >= 10)) num13 = num14;`
  (`V_55` is only used after the frame loop). v4.2's simpler rule (ignore frames 0–7) is recognised and upgraded.

**3b. Text preparation** (`TextProcessor.PrepareText`, IL code at file `0x844`). `Normalize(FormD)` followed by dropping every
non-spacing mark broke Korean (Hangul → Jamo the tokenizer doesn't know, ~half of each line `<unk>`) and Hindi (vowel signs,
virama, nasal marks deleted). File `0x8AA`: `ldc.i4.2` → `ldc.i4.1` (FormC); file `0x8CF`: `ldc.i4.5` → `ldc.i4.m1` (drop no
mark). Latin accents are still mapped by the game's own table first, so other languages get the same text as before.
Result: `PocketTTS.dll` SHA-256 `894ff1195b093819239821d45e90c0320b6e4fd09ab70b8a5cc6e9d2d58b715b`.

**3c. Card numbers** (`Assembly-CSharp.dll`, SHA-256 `99791cf93ec829d4784ee514ab78eba19e5e4a7ea841b291d979432a5c911d8f`;
`SentinelValueFactory.BuildGroupsSpoken`). A `[50ms]` pause tag after each `-` makes the voice engine generate every 4-digit
group separately; with MOSS 16 of 24 test readouts ran away (up to 63 s). File `0x40AFF`: `ldc.i4.s 45` → `ldc.i4.s 0`, so no
pause tags are added (3 of 24).

**3d. Filler lines** (`CallerResponseFillers.Get`, 66 string literals). "Um, okay..." / "嗯，好的……": MOSS often keeps going
for 5–30 s after an ellipsis. The ellipsis becomes `.  ` / `。 ` (same length; the trailing spaces are ignored). Over 30 runs:
13 runaways with "...", 5 with ".".

## 4. EXPERIMENTAL: Qwen3 TTS on 8 GB graphics cards (`Assembly-CSharp.dll`), game build 0.1.74

Off unless the player ticks it.

**Problem.** `Kolkata.QwenTts.QwenWorkerClient.HardwareEligible(vramMb, ramMb, availableMb, threads, vendor, device, ...)` returns true only if
`vramMb >= 8192` and `ramMb >= 16000` (plus NVIDIA/AMD, not a blocked device, `availableMb >= 6144`, `threads >= 4`, 64-bit Windows).
8 GB cards report less than 8192 MB (an RTX 4060 shows `VRAM: 7957 MB`), and PCs with 16 GB report about 15700–15900 MB,
so they never get the Qwen3 voice option.

**Patch.** In `Assembly-CSharp.dll` (SHA-256 `99791cf93ec829d4784ee514ab78eba19e5e4a7ea841b291d979432a5c911d8f`), in `HardwareEligible`
(method RVA `0x11D6E8`, IL code at file offset `0x11B8F4`):
- IL `0x0002` `ldc.i4 8192` → `ldc.i4 7680` (operand at file `0x11B8F7`: `00 20 00 00` → `00 1E 00 00`)
- IL `0x0073` `ldc.i4 16000` → `ldc.i4 15000` (operand at file `0x11B968`: `80 3E 00 00` → `98 3A 00 00`)
- With only this fix on, the file's SHA-256 is `3914ec19783fb0bf29bb470636ec1d14e911fa7041d4067425af81c651476bc6` (same as v4.3); it combines freely with 3c/3d.

All other checks are unchanged. Whether the Qwen3 model actually runs well next to the game on an 8 GB card has not been tested,
which is why this is opt-in and marked experimental.

## 5. Better English speech recognition (`ScriptsAssDef.dll`), game build 0.1.74

**Problem.** `WindowsSpeechCompatibility.NeedsCpuRecognition` keeps speech recognition off some graphics cards
(NVIDIA `1F82`, `2803` = RTX 4060 Ti, `2B85`; AMD `15D8`, `67DF` = RX 470/570/580, `699F`, `744C` = RX 7900 XTX) and off any
card when the game is started with `-whisper-cpu`. Since 0.1.74, `ShouldPreferMoonshine(whisperSupported, windowsArm64,
cpuCompatibility, cpuEnvironment, arguments)` then returns true, so English speech uses Moonshine tiny instead of Whisper.
Players on those PCs went from mostly correct transcripts on 0.1.69 to unusable ones on 0.1.74.

**Patch.** In `ScriptsAssDef.dll` (SHA-256 `4cf9f54e3760c2392952ef17f981e01c06fb483c655f9953ba9f6386889f0b2e`), in `ShouldPreferMoonshine`
(method RVA `0x11BCC3`, tiny header, IL code at file offset `0x119EC4`):
- IL `0x0009` `ldarg.2` (cpuCompatibility) → `ldc.i4.0` (file `0x119ECD`: `04` → `16`)
- IL `0x000B` `ldarg.3` (cpuEnvironment) → `ldc.i4.0` (file `0x119ECF`: `05` → `16`)
- Result SHA-256 `ede26084d0551f287d3d9c0f76a15d5f9c674b1b6d088d8952672a1bec0fc736`.

Moonshine is still used when Whisper can't run at all (no AVX) or with the `-moonshine` launch option, and the player's own
"Moonshine" choice in the game's settings still wins. `KOLKATA_WHISPER_CPU_ONLY` stays set, so Whisper runs on the CPU, as it
already does for all other languages on those PCs.

## 6. Graphics card test (no files changed)

The tool starts itself again as a child process (`--vulkan-probe`).
- The child loads the game's own `ggml-base.dll` and `ggml.dll` from `Plugins\x86_64`, with `KOLKATA_WHISPER_CPU_ONLY` removed from its environment.
- It calls `ggml_backend_dev_count()`. In the developers' build this is what loads `ggml-vulkan.dll`.
- It then calls `ggml_backend_dev_init()` and `ggml_backend_free()` for each GPU device.
- If the child crashes, or doesn't finish within 30 s, the tool tells the player to add the Steam launch option `-whisper-cpu`.
  The game itself already supports that option.
- After a crash or freeze it runs the child once more with `VK_LOADER_LAYERS_DISABLE=~implicit~` (Vulkan loader 1.3.234+) and
  `DISABLE_LAYER_AMD_SWITCHABLE_GRAPHICS_1=1`, set only for that child process, and lists the Vulkan implicit layers registered
  on the PC (`HKLM`/`HKCU\SOFTWARE\Khronos\Vulkan\ImplicitLayers` and the display drivers' `VulkanImplicitLayers`). If the
  second run passes, an overlay, recorder or AMD's switchable-graphics layer is a likely cause. This is only reported; the
  advice stays `-whisper-cpu`.

**More `-whisper-cpu` advice** (also no files changed). Even when the test passes, the tool asks for `-whisper-cpu` when:
- the PC has AMD processor graphics (vendor `1002`) plus an NVIDIA card (`10de`): on every such PC we've seen, the game's
  speech engine starts loading on the graphics card and never finishes;
- the game's call language (PlayerPrefs `Kolkata.CallLanguage`) isn't English: the multilingual model took 14–35 s per sentence
  on the graphics card on several PCs, against a few seconds on the processor. Not when the player picked Deepgram, or Moonshine
  for a language it covers (en, de, es, ar, ja, zh; PlayerPrefs `SpeechRecognition.Provider`).

It doesn't suggest removing `-whisper-cpu` on those PCs.

It reads `Steam\userdata\*\config\localconfig.vdf` to see whether `-whisper-cpu` is already set. It never writes to it.

## 7. Watermark tip (no files changed)

Reads the Unity PlayerPrefs value `KolkataOS.BanditCam.Watermark` from
`HKEY_CURRENT_USER\Software\Jatater Worldwide\Scam With Your Friends`, only to say whether the watermark is already off.

## 8. EXPERIMENTAL: bigger speech model (`StreamingAssets\Whisper\ggml-base.bin`)

Off unless the player ticks it. The game recognises every language except English with Whisper *base* (`ggml-base.bin`, 148 MB).
This puts Whisper *small* in its place: whisper.cpp's official `ggml-small.bin` (SHA-1 `55356645c2b361a969dfd0ef2c5a50d530afd8d5`)
converted to `q8_0` by the converter in 2a. GitHub Actions builds it, checks it against whisper.cpp's own `quantize` output,
and attaches it to each release as `SWYF-Speech-Model-small-q8.bin` (~250 MB) with its SHA-256. The tool looks for that file next
to itself and in the user's Downloads folder; it downloads nothing.

Only offered when all of these hold: the call language (`Kolkata.CallLanguage`) isn't English; speech runs on the processor
(`-whisper-cpu`, or a graphics card on the game's CPU-speech list); AVX2, at least 8 threads and 12 GB of memory. Before
installing it, the tool runs the speech speed test (section 9) on the processor with the model, capped to 64 tokens and no
temperature fallback (the cost of a normal sentence), and installs only if a 4-second sentence takes at most 2.5 s. It also
installs the listening window (2b), which the bigger model needs to be usable. The previous `ggml-base.bin` is kept as
`ggml-base.bin.bigmodel-backup`; undo moves it back. The speed-up's own undo leaves the bigger model alone.

## 9. Speech speed test (no files changed)

The tool starts itself again as a child process (`--speech-bench`). The child loads the game's own `ggml-base.dll`, `ggml.dll`
and `libwhisper.dll` from `Plugins\x86_64`, loads the speech model from memory with `whisper_init_from_buffer_with_params`
(like the game), and runs `whisper_full` twice on 4 seconds of one of the game's own caller voice clips
(`StreamingAssets\Callers\Defaults\*\voice.wav`, resampled to 16 kHz), with the game's settings: greedy decoding, no context,
the player's call language, whisper.cpp's default thread count, and the game's `audio_ctx`. For the graphics card run
`KOLKATA_WHISPER_CPU_ONLY` is removed and `use_gpu` is on; for the processor run it is set, as with `-whisper-cpu`. The child
prints the load time, the time of each run and whisper.cpp's encoder time (`whisper_get_timings`); the faster run counts.
A graphics card run that freezes is stopped after 60 s.

If the graphics card is more than twice as slow (and over 3 s), freezes or crashes, the tool asks for `-whisper-cpu`. If it is
clearly faster, the language-based `-whisper-cpu` advice is dropped, and players who have the option are told they may try
without it.
