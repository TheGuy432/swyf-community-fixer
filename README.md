# SWYF Speech Fixer

**Unofficial** community fix for speech-to-text in *Scam With Your Friends*.
Not made, endorsed or supported by the game's developers (Jatater Worldwide).

> **Use it when:** your speech never turns into text, the game says *"An incompatible Whisper CPU backend was already loaded"*, the game crashes when speech starts, or transcription is very slow on an older PC.

## Download

Get **`SWYF-Speech-Fixer.exe`** from the [**Releases**](../../releases) page.
Every release file is built automatically by GitHub from the code in this repository (see [Is it safe?](#is-it-safe)).

## How to use

1. Close the game completely.
2. Run `SWYF-Speech-Fixer.exe`. It finds the game automatically, or asks for the folder.
   Windows may warn about an unknown program: **More info → Run anyway**.
3. Read what it says. If you see **ACTION NEEDED**, follow the steps (adding a Steam launch option).

To **undo everything**, run it again and type `U`.
A game update or Steam's *Verify integrity of game files* also restores the original files. Just run the tool again afterwards.

## What it does

| Step | Who it's for | What happens |
|---|---|---|
| **1. Processor check** | CPUs without AVX2 (e.g. Intel i5-2500, i7-3770) on game build 0.1.71 | Fixes *"An incompatible Whisper CPU backend was already loaded"* by using the developers' own AVX speech file |
| **2. Speech speed-up** | Everyone, biggest gain on older PCs | Converts the speech model to 8-bit (same model, half the size), and sizes the listening window to what you said instead of always 30 s |
| **3. Graphics card test** | Everyone | Runs the game's own speech engine on your graphics card in a separate test process. If it crashes or freezes, tells you to add the launch option `-whisper-cpu` |
| **4. Tips** | Slower PCs | Reminds you to turn off the BanditCam watermark and cap FPS |

On an Intel i5-2500 with a 2010 graphics card, these steps took speech-to-text from *crashing / never finishing* to **about 4 seconds per sentence**.

### Files it changes

Only these, and only if they are the exact known versions (checked by SHA-256). Originals are kept as backups next to them.

| File | Change | Backup name |
|---|---|---|
| `Scam With Your Friends_Data\Managed\ScriptsAssDef.dll` | 5 bytes (step 1) | `.oldcpufix171-backup` |
| `Scam With Your Friends_Data\Plugins\x86_64\ggml-cpu.dll` | replaced with the game's own `avx\ggml-cpu.dll` (step 1) | `.oldcpufix171-backup` |
| `Scam With Your Friends_Data\Managed\com.whisper.unity.dll` | one 119-byte method body (step 2) | `.speedup-backup` |
| `Scam With Your Friends_Data\StreamingAssets\Whisper\ggml-base.en.bin`, `ggml-base.bin` | converted to 8-bit (step 2) | `.speedup-backup` |

It **reads** (never writes) your Steam launch options for this game and the game's BanditCam watermark setting, only to show the right advice.
It downloads nothing and sends nothing anywhere. Exact byte-level details are in [PATCHES.md](PATCHES.md).

## Is it safe?

You shouldn't have to take our word for it, so here is how to check:

- **Read the code.** It's about 1,000 lines of plain C# in [`src/`](src). No obfuscation, no network code.
- **Built by GitHub, not on someone's PC.** The [Actions](../../actions) tab shows every build: the exact commands, and the SHA-256 of the .exe it produced. Releases are attached automatically by that build.
- **Check your download.** In Command Prompt: `certutil -hashfile SWYF-Speech-Fixer.exe SHA256` and compare with `SHA256SUMS.txt` on the release.
- **Scan it yourself** with your antivirus or [VirusTotal](https://www.virustotal.com) if you like.
- **Model conversion proven identical.** Every build checks that the model conversion is byte-for-byte the same as [whisper.cpp](https://github.com/ggml-org/whisper.cpp)'s own official `quantize` tool.
- **Build it yourself.** On any Windows 10/11 PC, run `build.cmd`. It uses the C# compiler that ships with Windows.

**Why might an antivirus flag it?** It's a new, unsigned program that modifies another program's files and starts a test copy of itself, which looks a lot like game patchers and cheats to automatic scanners.
Some machine-learning engines (generic names like `Malicious (score: 100)`, `susgen`, `Malware.<random>`) flag brand-new unsigned programs like this. The major antiviruses, including Microsoft Defender, should show it as clean. If you're unsure, read the code or build it yourself.

## FAQ

**Does it change the game for other players?** No. It only changes speech recognition on your own PC.

**Can I get banned?** It doesn't touch gameplay, networking or anti-cheat. Still, it's unofficial; use it at your own risk.

**Will the developers fix this officially?** We've reported everything to them. Once an official update fixes these issues, you won't need this tool.

## For developers

- `build.cmd`: builds `dist\SWYF-Speech-Fixer.exe`.
- `SWYF-Speech-Fixer.exe --convert in.bin out.bin`: runs only the model conversion (used by the CI check).
- `tools/genmodel.py`: generates full-size Whisper test models with random weights for that check.
- Test-only options (`--assume-cpu`, `--simulate-probe`, Linux support for the GPU test) only exist when compiled with `/define:SWYF_TEST`. Release builds don't contain them.

## License

MIT, see [LICENSE](LICENSE). *Scam With Your Friends* and its files belong to their owners; this repository contains none of the game's files.
