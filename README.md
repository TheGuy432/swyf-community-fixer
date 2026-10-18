# SWYF Community Fixer

**Unofficial** community fixes for *Scam With Your Friends*: speech recognition, caller voices and more.
Formerly *SWYF Speech Fixer*.

> **Disclaimer:** this tool is made by **Tiger** (Discord: `tigerlundberg`), a player in the Scam With Your Friends community.
> It is **not made, endorsed or supported by Jatater Worldwide**, the game's developers, so please don't ask them about it:
> ask in the [fixer thread](https://discord.com/channels/1406416808700940399/1560834677529514055) instead. It changes some of the
> game's files on your PC, so use it at your own risk. *Undo everything*, or Steam's *Verify integrity of game files*, puts the
> original files back.

> **Use it when:** your speech never turns into text, the game says *"An incompatible Whisper CPU backend was already loaded"*, the game crashes when speech starts, or transcription is very slow on an older PC.

> **Game update of 18 Oct 2026 (Steam build 26020526):** the game now has its own fixes for the caller voices (steps 3), the Qwen3 limits (4) and English speech on the processor (5), so on that build the tool shows those as *fixed in the game* and leaves those files alone. The speed-up (2), the graphics card test (6) and the launch option advice still apply. If you used an older version of this tool, just run v4.5 once: it reinstalls the speed-up's listening window, which the update replaced, and removes backups of the old game files.

## Help

This tool is **not supported by the game's developers**, so please don't ask about it in the game's bug or support threads.
Ask in Tiger's **[fixer thread](https://discord.com/channels/1406416808700940399/1560834677529514055)** on the
Scam With Your Friends Discord instead. After running the tool, press **Copy report for Discord** and paste the report there:
it tells us your game version, PC, launch options and test results in one go.

## Download

Get **`SWYF-Community-Fixer.exe`** from the [**Releases**](../../releases) page.
Every release file is built automatically by GitHub from the code in this repository (see [Is it safe?](#is-it-safe)).

## How to use

1. Close the game completely.
2. Double-click `SWYF-Community-Fixer.exe`. A window opens and finds the game by itself (or lets you pick the game folder).
   Windows may warn about an unknown program: **More info → Run anyway**.
3. The window lists every fix with a label: **Recommended**, **Installed**, **Optional** or **Not needed**.
   The recommended ones are already ticked. Click a fix to tick or untick it. Unticking an installed fix removes it.
4. Not sure what a fix does? Click **What does this do?** under it.
5. Press **Apply**. When it's done, the window tells you if there's **one more step** (adding the Steam launch option
   `-whisper-cpu`, with a **Copy** button) and gives tips for your PC.

To **undo everything**, open it again and press **Undo everything**.
Fixes marked **UNTESTED** haven't been confirmed in the game yet: if one causes trouble, untick it and tell Tiger in the [fixer thread](https://discord.com/channels/1406416808700940399/1560834677529514055) on the game's Discord.
A game update or Steam's *Verify integrity of game files* also restores the original files. Just run the tool again afterwards.

**Text mode:** `SWYF-Community-Fixer.exe --console` gives the old text menu (type numbers, Enter to apply, `U` to undo, `L` to switch language).
`--uninstall` undoes everything without asking.

### Bigger speech model (optional, experimental)

For players whose game language isn't English and whose speech recognition makes lots of mistakes. It needs a second
download, because the model is about 250 MB:

1. Add the launch option `-whisper-cpu` in Steam (right-click the game > Properties > Launch Options). The tool's
   **Copy -whisper-cpu** and **Open Steam** buttons help with that.
2. On the [Releases](../../releases) page, download **`SWYF-Speech-Model-small-q8.bin`** as well as the .exe. Leave it in your
   **Downloads** folder or put it next to the .exe.
3. Run the tool (or press **Check again** if it's still open), tick **Bigger speech model for your language** and press **Apply**.
   It first times the model on your processor and only installs it if your PC is fast enough.

The first reply in each game session takes a few extra seconds while the game loads its models (about 3 s for the bigger
model on an i7-8700K); that's normal, later replies are faster.

The option only shows up when the tool sees your game language isn't English. If your PC doesn't meet the requirements
(AVX2, 8 threads, 12 GB memory), it says so instead. To remove it, untick it or press **Undo everything**.

## Em português

1. Feche o jogo completamente.
2. Abra o `SWYF-Community-Fixer.exe` com dois cliques (baixe na página [**Releases**](../../releases)). Se o Windows avisar sobre um programa desconhecido: **Mais informações → Executar assim mesmo**.
3. A janela mostra cada correção. As recomendadas já vêm marcadas: clique para marcar/desmarcar e aperte **Aplicar**.
4. Para **desfazer tudo**, abra de novo e aperte **Desfazer tudo**.

Na versão 0.1.74 do jogo corrige, entre outras coisas, as vozes em português sendo cortadas depois de uma fração de segundo. Desde a atualização de 18 de outubro (build 26020526) o próprio jogo corrige isso; a ferramenta continua deixando o reconhecimento de voz mais rápido e diz quando usar a opção `-whisper-cpu`.
Ferramenta **não oficial**, não feita pelos desenvolvedores do jogo.

## Languages

The tool picks its language by itself: the game's call language first, then your Windows language, otherwise English.
Change it with the language button in the top-right corner of the window.

Available now: **English**, **Português (Brasil)**, **Deutsch**, **Español**, **Français**, **Polski** and **Русский**. Translations are made with AI and checked by players when possible,
so a line may sound a bit off. Tell Tiger in the [fixer thread](https://discord.com/channels/1406416808700940399/1560834677529514055) or open an issue.

**Add or fix a translation:** every text is in [`lang/`](lang), one plain text file per language (`en.txt`, `pt.txt`, ...).
Each line is `key = text`. To add a language, copy `en.txt` to `<code>.txt` (two-letter code, e.g. `fr.txt`), change `_name`
to the language's own name, and translate the text after each `=`. Keep `{0}`, `{1}` and `\n` as they are; they are filled in by
the program. Lines you leave out show in English. `build.cmd` includes every file in `lang/` automatically.

## What it does

| Step | Who it's for | What happens |
|---|---|---|
| **1. Processor check** | CPUs without AVX2 (e.g. Intel i5-2500, i7-3770) on game build 0.1.71 | Fixes *"An incompatible Whisper CPU backend was already loaded"* by using the developers' own AVX speech file |
| **2. Speech speed-up** | Everyone, biggest gain on older PCs | Converts the speech model to 8-bit (same model, half the size), and sizes the listening window to what you said instead of always 30 s |
| **3. Caller voices** (game 0.1.74 only; built into the game since build 26020526) | Everyone, especially Portuguese, French, German, Italian, Korean, Hindi and the MOSS languages (Swedish, Danish, Polish, Turkish, Hungarian, Greek, Arabic, Persian, Chinese, Japanese, Russian) | A stricter end-of-line rule so callers stop being cut off mid-sentence (Portuguese after ~0.3 s), correct Korean and Hindi text for the voice model, card numbers read in one go instead of 4-digit bits (up to 60 s → ~8 s with MOSS), and filler lines ending in "." instead of "..." (MOSS rambles on after "...") |
| **4. EXPERIMENTAL: Qwen3 TTS on 8 GB cards** (0.1.74 only; built into the game since build 26020526) | Off by default. NVIDIA/AMD cards with 8 GB that the game reports as e.g. 7957 MB, and 16 GB PCs reported as ~15.8 GB | The game only offers the Qwen3 voices with at least 8192 MB graphics memory and 16000 MB RAM, so most 8 GB cards and 16 GB PCs just miss it. This lowers the limits to 7680 MB / 15000 MB. **The voice model may not fit next to the game on an 8 GB card: it can stutter, run out of memory or crash.** Untick it to remove it |
| **5. Better English speech recognition** (0.1.74 only; built into the game since build 26020526) | English players whose speech runs on the processor: RTX 4060 Ti and a few other cards the game won't use for speech, or anyone with `-whisper-cpu`. Ticked automatically on those PCs if the processor is fast enough (AVX2, 8+ threads) | Since 0.1.74 the game switches English speech on those PCs to the small Moonshine model, which is much less accurate ("give me your pig, Cornwallard"). This keeps the normal Whisper model on the processor instead, the same one other languages already use there. Each sentence takes a bit longer to recognize |
| **6. Graphics card test** | Everyone | Runs the game's own speech engine on your graphics card in a separate test process. If it crashes or freezes, tells you to add the launch option `-whisper-cpu`, and tests once more with Vulkan add-on layers (overlays, recorders, AMD switchable graphics) turned off to show whether one of them is the cause |
| **Launch option advice** | PCs with AMD processor graphics + an NVIDIA card; players whose game language isn't English | Asks you to add `-whisper-cpu` even when the test passes: on AMD + NVIDIA PCs the game's speech often never finishes loading on the graphics card, and speech for languages other than English is very slow there (15-30 s per sentence). For German, Spanish, Arabic, Japanese and Chinese it also points to the game's own *Moonshine (6 languages)* setting |
| **EXPERIMENTAL: Bigger speech model** | Players whose game language isn't English, with `-whisper-cpu`, a processor with AVX2 and 8+ threads, and 12 GB+ memory. Off unless you tick it | Replaces the game's small multilingual speech model (Whisper *base*) with Whisper *small*, which makes far fewer mistakes outside English but needs about 2-3× the work per sentence. Needs the extra download `SWYF-Speech-Model-small-q8.bin` from the [Releases](../../releases) page, next to the .exe or in your Downloads folder. Before installing it, it times the model on your processor with the game's own speech engine and only installs it if a 4-second sentence takes 2.5 s or less. English keeps the game's English model |
| **EXPERIMENTAL: Speech speed test** | Anyone with slow speech recognition. Off unless you tick it | Times the game's own speech engine on a 4-second piece of one of the game's caller voice clips, in your game language, on your graphics card and on your processor, and tells you which is faster. Changes no files. A screenshot of the numbers helps us find out why speech is slow on some PCs |
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
| `Scam With Your Friends_Data\Managed\PocketTTS.dll` | 74 bytes: end-of-line rule and text preparation (step 3) | none needed: every change is reversible byte for byte |
| `Scam With Your Friends_Data\Managed\ScriptsAssDef.dll` | 2 bytes in the speech model choice (step 5) | `.whisperkeep-backup` |
| `Scam With Your Friends_Data\Managed\Assembly-CSharp.dll` | card-number pauses (1 byte) and 66 filler lines (step 3); Qwen3 limits (8 bytes, step 4, only if you tick it) | none needed: every change is reversible byte for byte |
| `Scam With Your Friends_Data\StreamingAssets\Whisper\ggml-base.bin` | replaced with the bigger speech model (only if you tick it) | `.bigmodel-backup` |

The first time it runs it shows the disclaimer above, and remembers that you accepted it in `HKEY_CURRENT_USER\Software\SWYF Community Fixer` (`DisclaimerAccepted`); that is the only thing it writes outside the game folder.
It **reads** (never writes) your Steam launch options and installed build number for this game, and the game's BanditCam watermark, call language and speech recognition settings, only to show the right advice.
It downloads nothing and sends nothing anywhere (the bigger speech model is a separate download you make yourself). Exact byte-level details are in [PATCHES.md](PATCHES.md).

## Is it safe?

You shouldn't have to take our word for it, so here is how to check:

- **Read the code.** It's plain C# in [`src/`](src) (about 3,400 lines including the window). No obfuscation, no network code.
- **Built by GitHub, not on someone's PC.** The [Actions](../../actions) tab shows every build: the exact commands, and the SHA-256 of the .exe it produced. Releases are attached automatically by that build.
- **Check your download.** In Command Prompt: `certutil -hashfile SWYF-Community-Fixer.exe SHA256` and compare with `SHA256SUMS.txt` on the release.
- **Scan it yourself** with your antivirus or [VirusTotal](https://www.virustotal.com) if you like.
- **Model conversion proven identical.** Every build checks that the model conversion is byte-for-byte the same as [whisper.cpp](https://github.com/ggml-org/whisper.cpp)'s own official `quantize` tool.
- **The bigger speech model is built by GitHub too.** The same build downloads OpenAI's Whisper *small* model (`ggml-small.bin`) from whisper.cpp's official Hugging Face page, checks its SHA-1, converts it, checks the result against the official tool and attaches it to the release with its SHA-256. The model is MIT-licensed, see [WHISPER-MODEL-LICENSE.txt](WHISPER-MODEL-LICENSE.txt).
- **Build it yourself.** On any Windows 10/11 PC, run `build.cmd`. It uses the C# compiler that ships with Windows.

**Why might an antivirus flag it?** It's a new, unsigned program that modifies another program's files and starts a test copy of itself, which looks a lot like game patchers and cheats to automatic scanners.
Some machine-learning engines (generic names like `Malicious (score: 100)`, `susgen`, `Malware.<random>`) flag brand-new unsigned programs like this. The major antiviruses, including Microsoft Defender, should show it as clean. If you're unsure, read the code or build it yourself.

## FAQ

**Does it change the game for other players?** No. It only changes speech recognition and caller voice generation on your own PC.

**Can I get banned?** It doesn't touch gameplay, networking or anti-cheat. Still, it's unofficial; use it at your own risk.

**Will the developers fix this officially?** We've reported everything to them. Once an official update fixes these issues, you won't need this tool.

## For developers

- `build.cmd`: builds `dist\SWYF-Community-Fixer.exe` (a window app; `src\*.cs` plus every `lang\*.txt` embedded as a resource).
- `src/Core.cs` has the shared logic (what's installed, applying, tips), `src/Gui.cs` the window, `src/Fixer.cs` the text mode,
  `src/Lang.cs` the translation lookup (`T("key")`, falls back to English per line).
- `SWYF-Community-Fixer.exe --convert in.bin out.bin`: runs only the model conversion (used by the CI check and to build the bigger speech model).
- `--speech-bench <plugin dir> <model> <wav> <language> <gpu 0/1> <audio_ctx> <capped 0/1>`: the speech speed test's child process.
- `tools/genmodel.py`: generates full-size Whisper test models with random weights for that check.
- Test-only options (`--assume-cpu`, `--assume-gpu`, `--assume-threads`, `--assume-build`, `--assume-lang`, `--assume-provider`, `--assume-layers`, `--assume-ram`, `--assume-model-dir`, `--simulate-probe`, `--simulate-bench`, `--test-*` for driving the window, Linux support for the GPU test) only exist when compiled with `/define:SWYF_TEST`. Release builds don't contain them.

## License

MIT, see [LICENSE](LICENSE). *Scam With Your Friends* and its files belong to their owners; this repository contains none of the game's files.
