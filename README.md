# SWYF Speech Fixer

**Unofficial** community fix for speech-to-text and caller voices in *Scam With Your Friends*.
Not made, endorsed or supported by the game's developers (Jatater Worldwide).

> **Use it when:** your speech never turns into text, the game says *"An incompatible Whisper CPU backend was already loaded"*, the game crashes when speech starts, transcription is very slow on an older PC, or callers in **Portuguese** get cut off after a fraction of a second.

## Download

Get **`SWYF-Speech-Fixer.exe`** from the [**Releases**](../../releases) page.
Every release file is built automatically by GitHub from the code in this repository (see [Is it safe?](#is-it-safe)).

## How to use

1. Close the game completely.
2. Double-click `SWYF-Speech-Fixer.exe`. A window opens and finds the game by itself (or lets you pick the game folder).
   Windows may warn about an unknown program: **More info → Run anyway**.
3. The window lists every fix with a label: **Recommended**, **Installed**, **Optional** or **Not needed**.
   The recommended ones are already ticked. Click a fix to tick or untick it. Unticking an installed fix removes it.
4. Press **Apply**. When it's done, the window tells you if there's **one more step** (adding the Steam launch option
   `-whisper-cpu`, with a **Copy** button) and gives tips for your PC.

To **undo everything**, open it again and press **Undo everything**.
Fixes marked **UNTESTED** haven't been confirmed in the game yet: if one causes trouble, untick it and tell Tiger (Discord: `tigerlundberg`).
A game update or Steam's *Verify integrity of game files* also restores the original files. Just run the tool again afterwards.

**Text mode:** `SWYF-Speech-Fixer.exe --console` gives the old text menu (type numbers, Enter to apply, `U` to undo, `L` to switch language).
`--uninstall` undoes everything without asking.

## Em português

1. Feche o jogo completamente.
2. Abra o `SWYF-Speech-Fixer.exe` com dois cliques (baixe na página [**Releases**](../../releases)). Se o Windows avisar sobre um programa desconhecido: **Mais informações → Executar assim mesmo**.
3. A janela mostra cada correção. As recomendadas já vêm marcadas: clique para marcar/desmarcar e aperte **Aplicar**.
4. Para **desfazer tudo**, abra de novo e aperte **Desfazer tudo**.

Corrige, entre outras coisas, as vozes em português sendo cortadas depois de uma fração de segundo (versão 0.1.74 do jogo).
Ferramenta **não oficial**, não feita pelos desenvolvedores do jogo.

## Languages

The tool picks its language by itself: the game's call language first, then your Windows language, otherwise English.
Change it with the language button in the top-right corner of the window.

Available now: **English** and **Português (Brasil)**. Translations are made with AI and checked by players when possible,
so a line may sound a bit off. Tell Tiger (Discord: `tigerlundberg`) or open an issue.

**Add or fix a translation:** every text is in [`lang/`](lang), one plain text file per language (`en.txt`, `pt.txt`, ...).
Each line is `key = text`. To add a language, copy `en.txt` to `<code>.txt` (two-letter code, e.g. `fr.txt`), change `_name`
to the language's own name, and translate the text after each `=`. Keep `{0}`, `{1}` and `\n` as they are; they are filled in by
the program. Lines you leave out show in English. `build.cmd` includes every file in `lang/` automatically.

## What it does

| Step | Who it's for | What happens |
|---|---|---|
| **1. Processor check** | CPUs without AVX2 (e.g. Intel i5-2500, i7-3770) on game build 0.1.71 | Fixes *"An incompatible Whisper CPU backend was already loaded"* by using the developers' own AVX speech file |
| **2. Speech speed-up** | Everyone, biggest gain on older PCs | Converts the speech model to 8-bit (same model, half the size), and sizes the listening window to what you said instead of always 30 s |
| **3. Caller voices** | Everyone, needed for Portuguese (game 0.1.74) | Stops Portuguese callers being cut off after ~0.3 s ("DAL-"). The Portuguese voice model briefly signals "done" before it starts talking; the fix ignores that signal for the first ~0.6 s of each line |
| **4. EXPERIMENTAL: Qwen3 TTS on 8 GB cards** | Off by default. NVIDIA/AMD cards with 8 GB that the game reports as e.g. 7957 MB, and 16 GB PCs reported as ~15.8 GB | The game only offers the Qwen3 voices with at least 8192 MB graphics memory and 16000 MB RAM, so most 8 GB cards and 16 GB PCs just miss it. This lowers the limits to 7680 MB / 15000 MB. **The voice model may not fit next to the game on an 8 GB card: it can stutter, run out of memory or crash.** Untick it to remove it |
| **5. Better English speech recognition** | English players whose speech runs on the processor: RTX 4060 Ti and a few other cards the game won't use for speech, or anyone with `-whisper-cpu`. Ticked automatically on those PCs if the processor is fast enough (AVX2, 8+ threads) | Since 0.1.74 the game switches English speech on those PCs to the small Moonshine model, which is much less accurate ("give me your pig, Cornwallard"). This keeps the normal Whisper model on the processor instead, the same one other languages already use there. Each sentence takes a bit longer to recognize |
| **6. Graphics card test** | Everyone | Runs the game's own speech engine on your graphics card in a separate test process. If it crashes or freezes, tells you to add the launch option `-whisper-cpu` |
| **Tips** | Slower PCs | Reminds you to turn off the BanditCam watermark and cap FPS |

On an Intel i5-2500 with a 2010 graphics card, these steps took speech-to-text from *crashing / never finishing* to **about 4 seconds per sentence**.

### Files it changes

Only these, and only if they are the exact known versions (checked by SHA-256). Originals are kept as backups next to them.

| File | Change | Backup name |
|---|---|---|
| `Scam With Your Friends_Data\Managed\ScriptsAssDef.dll` | 5 bytes (step 1) | `.oldcpufix171-backup` |
| `Scam With Your Friends_Data\Plugins\x86_64\ggml-cpu.dll` | replaced with the game's own `avx\ggml-cpu.dll` (step 1) | `.oldcpufix171-backup` |
| `Scam With Your Friends_Data\Managed\com.whisper.unity.dll` | one 119-byte method body (step 2) | `.speedup-backup` |
| `Scam With Your Friends_Data\StreamingAssets\Whisper\ggml-base.en.bin`, `ggml-base.bin` | converted to 8-bit (step 2) | `.speedup-backup` |
| `Scam With Your Friends_Data\Managed\PocketTTS.dll` | 72 bytes in the voice generation loop (step 3) | `.voicefix-backup` |
| `Scam With Your Friends_Data\Managed\ScriptsAssDef.dll` | 2 bytes in the speech model choice (step 5) | `.whisperkeep-backup` |
| `Scam With Your Friends_Data\Managed\Assembly-CSharp.dll` | 8 bytes, two numbers in the Qwen3 hardware check (step 4, only if you tick it) | `.qwen8gb-backup` |

It **reads** (never writes) your Steam launch options for this game and the game's BanditCam watermark setting, only to show the right advice.
It downloads nothing and sends nothing anywhere. Exact byte-level details are in [PATCHES.md](PATCHES.md).

## Is it safe?

You shouldn't have to take our word for it, so here is how to check:

- **Read the code.** It's plain C# in [`src/`](src) (about 2,500 lines including the window). No obfuscation, no network code.
- **Built by GitHub, not on someone's PC.** The [Actions](../../actions) tab shows every build: the exact commands, and the SHA-256 of the .exe it produced. Releases are attached automatically by that build.
- **Check your download.** In Command Prompt: `certutil -hashfile SWYF-Speech-Fixer.exe SHA256` and compare with `SHA256SUMS.txt` on the release.
- **Scan it yourself** with your antivirus or [VirusTotal](https://www.virustotal.com) if you like.
- **Model conversion proven identical.** Every build checks that the model conversion is byte-for-byte the same as [whisper.cpp](https://github.com/ggml-org/whisper.cpp)'s own official `quantize` tool.
- **Build it yourself.** On any Windows 10/11 PC, run `build.cmd`. It uses the C# compiler that ships with Windows.

**Why might an antivirus flag it?** It's a new, unsigned program that modifies another program's files and starts a test copy of itself, which looks a lot like game patchers and cheats to automatic scanners.
Some machine-learning engines (generic names like `Malicious (score: 100)`, `susgen`, `Malware.<random>`) flag brand-new unsigned programs like this. The major antiviruses, including Microsoft Defender, should show it as clean. If you're unsure, read the code or build it yourself.

## FAQ

**Does it change the game for other players?** No. It only changes speech recognition and caller voice generation on your own PC.

**Can I get banned?** It doesn't touch gameplay, networking or anti-cheat. Still, it's unofficial; use it at your own risk.

**Will the developers fix this officially?** We've reported everything to them. Once an official update fixes these issues, you won't need this tool.

## For developers

- `build.cmd`: builds `dist\SWYF-Speech-Fixer.exe` (a window app; `src\*.cs` plus every `lang\*.txt` embedded as a resource).
- `src/Core.cs` has the shared logic (what's installed, applying, tips), `src/Gui.cs` the window, `src/Fixer.cs` the text mode,
  `src/Lang.cs` the translation lookup (`T("key")`, falls back to English per line).
- `SWYF-Speech-Fixer.exe --convert in.bin out.bin`: runs only the model conversion (used by the CI check).
- `tools/genmodel.py`: generates full-size Whisper test models with random weights for that check.
- Test-only options (`--assume-cpu`, `--assume-gpu`, `--assume-threads`, `--simulate-probe`, `--test-*` for driving the window, Linux support for the GPU test) only exist when compiled with `/define:SWYF_TEST`. Release builds don't contain them.

## License

MIT, see [LICENSE](LICENSE). *Scam With Your Friends* and its files belong to their owners; this repository contains none of the game's files.
