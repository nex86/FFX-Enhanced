# FFX Enhanced 0.90 Beta

New launcher and DLL-free 60 fps patch for **FINAL FANTASY X/X-2 HD Remaster** (Steam, PC; app id 359870).
Unofficial fan modification. Works with the current Steam build only (see [Compatibility](#compatibility)).

> **Beta.** Most of this was tested on Windows 11 without long play sessions. Nothing was tested on Steam Deck / SteamOS yet.
> Back up your saves. Items marked **experimental** below may break the picture, crash the game or stop working after a game update.

## Features

**Launcher** (`FFX&X-2_LAUNCHER.exe`, replaces the original launcher)
- Stand-alone reimplementation in the look of the original: fullscreen at any resolution, keyboard, mouse and XInput controller
  (Steam Deck controls included), the original menu, credits and music, 8 languages.
- Settings page in the style of the in-game menu, tabs **Audio, Video, Controls, Language, Advanced**, buttons OK / Cancel / Apply / Defaults.
- **Video quality presets** as in the game: Low / Medium / High set and lock the eight detail settings; Custom unlocks them.
- **Framerate** 30 (original) / 60 / 90 / 120 / 240.
- **Controls**: key bindings of FFX, FFX-2 and Last Mission (Keyboard 1, Keyboard 2, controller), written only when they pass the
  game's own check (otherwise the game would reset all bindings).
- **Voice** language row (the games tie voices to the text language: Japanese voices = Japanese/Korean/Chinese text).
- Backs up `FFX.exe`, `FFX-2.exe`, `FFX&X-2_Will.exe` to `backup_original\` at first start and verifies them (CRC-32) at every start.
- Optional sharper menu graphics: if `data\metamenu_1440.vbf` / `metamenu_2160.vbf` exist they are used by monitor height
  (not distributed here, they contain game artwork).

**60 fps patch** (no DLL, no Wine settings)
- On Apply the launcher writes `FFX_enh.exe` / `FFX-2_enh.exe` (copies of *your* EXEs with the patch added) and `ffx_enh.ini`,
  and starts those. The original EXEs stay untouched. Only EXEs with the Steam release CRC are patched.
- Based on FFX-60fps by Peter Nguyen (Apache-2.0). Frame Rate row in the games' Esc menu. All options of the original mod are on the
  launcher's Advanced tab; settings in `ffx_enh.ini`, logs `FFX_enh.log` / `FFX-2_enh.log`.
- `FFX_enh_Patcher.exe` does the same without the launcher.

**Advanced tab**
- **Ultrawide** (experimental): full width on screens wider than 16:9, both games (SUWSF patches, ported).
- **Skip logos and autosave notice**, **Don't restart the launcher** after quitting a game.
- **External File Loader (EFL)**: ffgriever's loader (included) is loaded by the patch itself; reads unpacked files from `data\mods`.
  EFL's `dinput8.dll` is not needed.
- **Menu graphics** (Automatic / 1440p / 2160p / Original), **Use RevoFX** (only if RevoFX is installed), **Proton compatibility mode**.

## Experimental / may break

| Part | Status | Risk |
|---|---|---|
| 60 fps in FFX | title screens and short play tested | timing bugs in single scenes possible |
| 60 fps in FFX-2 | starts, patch installs; little play | as above |
| 90 / 120 / 240 fps | **not tested** | animation/physics/timing errors; FFX-2 above 60 is new |
| Ultrawide | **not tested in game** (patches verified offline) | stretched or misplaced UI, wrong FOV, crashes |
| External File Loader | **not tested in game** with the patched EXEs | EFL may refuse the modified EXE ("unsupported game version"); then nothing is loaded |
| Controls tab | not tested in game | punctuation keys (`- = [ ] \ ; ' , . /`) are mapped by US layout and may differ on other layouts; if the game rejects a list it resets all bindings |
| Skip logos / no launcher restart | not tested in game | intro or exit may hang |
| `optional\dbghelp.dll` | not tested in game | - |
| Proton / Steam Deck | **not tested** | everything above |
| Windows Defender | may flag `FFX-2_enh.exe` (false positive "Wacatac") | add the game folder as exclusion, then Apply again |

## Install (Windows)

1. Game folder (Steam > the game > Properties > Installed Files > Browse): rename `FFX&X-2_LAUNCHER.exe` to `FFX&X-2_LAUNCHER_original.exe`.
2. Copy the contents of `game\` from the release ZIP into the game folder.
3. Start the game from Steam. Config > Video > Framerate 60 > Apply.
4. EFL (optional): Config > Advanced > External File Loader = On; put unpacked game files into `data\mods`.

## Steam Deck / Proton

Same install (Desktop Mode, game folder `~/.local/share/Steam/steamapps/common/FINAL FANTASY FFX&FFX-2 HD Remaster/`).
No launch options needed. Three ways, pick one:

- **A (default)**: Framerate 60 > Apply; the launcher starts `FFX_enh.exe` / `FFX-2_enh.exe`.
- **B**: Advanced > Proton compatibility mode = On: the 60 fps builds replace `FFX.exe` / `FFX-2.exe` (originals in `backup_original\`). Off restores them.
- **C**: copy `optional\dbghelp.dll` next to `FFX.exe` and set launch options `WINEDLLOVERRIDES="dbghelp=n,b" %command%`.
  With this DLL present the launcher disables A and B.

If Proton's gamepad helper moves a mouse pointer over the launcher: launch option `PROTON_USE_XALIA=0 %command%`.
EFL needs the VC++ 2015-2022 x86 runtime (present in Proton prefixes).

## Compatibility

- Steam release EXEs only: `FFX.exe` CRC `FC9264CD`, `FFX-2.exe` `6C2274E0`, `FFX&X-2_Will.exe` `40E21BB6`. Other files are not patched
  (error message). After a Steam update the patch must be adapted.
- With RevoFX installed its own 60 fps / options are used; Advanced > Use RevoFX = Off renames its `d3d11.dll` and uses this patch.
- Game settings stay in `Documents\SQUARE ENIX\...\GameSetting.ini`; everything else in `ffx_enh.ini` in the game folder.

## Uninstall

Proton compatibility mode Off (if on). Delete `FFX_enh.exe`, `FFX-2_enh.exe`, `ffx_enh.ini`, `FFX_enh_Patcher.exe`, `EFL\`,
`dbghelp.dll` (if copied), the `*.log` files, and rename the original launcher back. Or: Steam > Verify integrity of game files.

## Build

Windows, Python 3 with `pip install ziglang`, .NET Framework 4 (ships with Windows). In the repository root:

```
python fps60exe\build.py
powershell -ExecutionPolicy Bypass -File launcher\fswrap\build.ps1
```

Output: `fps60exe\bin\FFX_enh_Patcher.exe`, `fps60exe\bin\dbghelp.dll`, `launcher\fswrap\bin\FFX&X-2_LAUNCHER.exe`.
Launcher texts: `python launcher\fswrap\gen_res\gen_res.py launcher\fswrap\gen_res launcher\fswrap\src\LauncherRes.cs`.
Menu graphics archives from your own files: `python tools\vbfbuild.py - <folder> <out.vbf> --prefix metamenu/`.

## Credits and licences

- FFX Enhanced: Apache-2.0 (`LICENSE`).
- 60 fps patch code: [FFX-60fps](https://github.com/peternguyenst/FFX-60fps) by Peter Nguyen, Apache-2.0, modified
  (`module\fps60\NOTICE.txt` lists the changes). MinHook by Tsuda Kageyu, BSD-2-Clause.
- Ultrawide patches: SUWSF by PhantomGamers, MIT.
- External File Loader and simpleLog: ffgriever ([source](https://gitlab.com/ffgriever/ffx-x-2-hd-external-file-loader)),
  BSD-2-Clause; simpleLog also (c) Philipp Kraehenbuehl. Included unchanged as released (ff10-file-loader 1.2.0.0, simpleLog 1.0.2.0).
- Licence texts: `licenses\`.

FINAL FANTASY is a registered trademark of Square Enix. Game files are not included; you need your own copy of the game.
