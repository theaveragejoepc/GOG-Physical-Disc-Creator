# GOG Physical Disc Creator — user guide

Based on east35/GOG-Disc-Packager main, downloaded September 19, 2026.
The upstream GPL-3.0 license remains in place. TargetFill attribution is in
THIRD-PARTY-NOTICES.md.

## One game, multiple patches and DLC

Choose the base installer on **Game 1**. Use the separate **Patches** and **DLCs**
Browse buttons to select additional `patch_*.exe` and `setup_*.exe` files.
Expand **Installation order and game details** to edit the order. The list runs
from top to bottom after the base game. Move lines to change dependency order;
delete a line to omit an installer. Matching numbered BIN files are automatic.
Review the installation order in the scan log before building.

Names do not reliably reveal patch prerequisites, language, version or DLC
ownership. Choose compatible installers and the order required by the game.
The launcher runs the original installers individually, waits for each to finish,
and stops/offers retry on failure. Before each optional patch/DLC it offers
**Install**, **Skip**, and **Cancel**. After an optional installer reports an error,
it offers **Retry**, **Skip**, and **Cancel**. The base-game installer cannot be
skipped. Use Skip when a patch is already installed or is not required; the app
does not infer patch prerequisites from a filename or an installer exit code.
Skipped steps are logged and recorded as processed, not verified as installed.
Completed and skipped steps are recorded locally; retry
skips them while the base game is still detected. Cancellation takes effect
between installers: finish or cancel an already-running installer in its window.

## Artwork from local files or SteamGridDB

Choose **Choose artwork…** beside Background, Cover art, or Disc icon. The new
window always offers **Choose local file…**, without requiring internet access.
For online choices, enter your SteamGridDB API key, search by title, select the
matching game, select a thumbnail, then choose **Use selected artwork**. Previous
and Next page browse additional results. Covers use portrait grids, backgrounds
use heroes, and disc icons use icons. Covers and backgrounds support static
PNG/JPEG; online icons use PNG, as required by the icon endpoint.

Use **Get API key** to open https://www.steamgriddb.com/profile/preferences/api.
Choose **Save key** once; a successful online search also saves it automatically.
The key is encrypted with Windows DPAPI for your Windows account and stored in
`%LocalAppData%\GOG Disc Tool\steamgriddb-key.dat`. It loads automatically after
restarting the app. **Forget key** removes the local saved copy; it does not revoke
the key on SteamGridDB. It is never included in the application build, disc,
manifest, or artwork cache. Other users receive the app without your key. Selected images are normalized to PNG and
cached in `%LocalAppData%\GOG Disc Tool\Artwork` for offline packaging.

For collections, use **Individual Cover Art** on each game tab. It opens the same
local/online chooser and keeps the selected path with that game; it does not
replace files in your source game folder. Main Setup artwork applies to the
collection as a whole. The encrypted API key can also be saved or forgotten
from **Main Setup**.

The API client follows SteamGridDB's official API v2 and official wrapper routes:
https://www.steamgriddb.com/api/v2 and https://github.com/SteamGridDB/node-steamgriddb.
Regression checks cover both SteamGridDB CDN hosts and the icon-specific PNG
filter. Live verification on September 20, 2026 found Dungeons 3 artwork using
the saved local key. The key is never placed in source, logs, or builds. Normal
application use downloads artwork only when you use the online UI.

## Collections

Use **Main Setup** for disc type, title (typed manually), version, media and artwork.
**Add Game** creates and selects the next game tab and switches to collection mode.
Each game keeps separate setup, extras, patch/DLC order and cover inputs. Game title
and version can be edited under **Installation order and game details**.
**Remove Game** removes only that tab's inputs; it never deletes source files.
Switching disc type retains your tabs; Standard Game Disc uses Game 1.

**Finish** holds output, launcher, TargetFill and build controls. Scan results and
progress expand when scanning/building. **Clear All** and the after-build reset clear
inputs, retaining output, launcher, saved API key and disc inventory. They do not
delete source files. Game key card settings remain under Main Setup when that disc
type is selected; navigation skips the offline game tab in that mode.

Existing folder collections can be imported from the expander on a collection game
tab. Import adds games to existing populated tabs and uses blank tabs first.
Use this folder layout when importing:

```text
Collection/
  Game One/
    Base Game/setup_game.exe
    Base Game/setup_game-1.bin
    Patches/01/patch_game_update.exe
    Patches/02/patch_game_update.exe
    DLC/01/setup_expansion_one.exe
    DLC/02/setup_expansion_two.exe
    Extras/manual.pdf
    cover.jpg
  Game Two/
    setup_game_two.exe
    cover.png
```

The scanner selects one base setup outside `Extras`, `Patches`, and `DLC`.
It then queues Patches followed by DLC, naturally sorted by path within each
folder. Numbered subfolders make this order explicit and allow repeated filenames.

For a different order, add `install-order.txt` to a game's folder. List every
desired add-on installer relative to that folder, one per line, without the base
setup. This overrides automatic add-on discovery. Blank lines and lines beginning
with `#` are ignored. For example:

```text
Patches/01/patch_game_update.exe
DLC/01/setup_expansion_one.exe
DLC/02/setup_expansion_two.exe
Patches/02/patch_game_update.exe
```

Each game's `cover.jpg`, `cover.png`, or `cover.jpeg` is copied onto the disc and
cached by the launcher. The collection displays cover cards with Play and
Uninstall below installed games, and Install / details below available games.
Extras remain accessible. Missing artwork uses the collection cover or built-in
fallback. Collections retain the upstream single-disc capacity limit.

## Play without GOG Galaxy

Play first looks for a matching direct-game shortcut and preserves its arguments
and working directory, then considers an executable inside the installed game folder. Known library clients such as GalaxyClient.exe are excluded. Previously
saved Galaxy targets are rediscovered and repaired when the new launcher opens.
The same path is used for single games and collection Play buttons.

Existing packages need the new `LauncherPayload/Launch.exe` (or a rebuilt package)
to receive this fix; replacing only the packager application does not update an
old package's launcher. The app does not change Galaxy, desktop shortcuts, or your
game installation.

## AutoRun nostalgia

Every generated disc folder already includes `autorun.inf` next to `Launch.exe`
and `game.ico`, for standard games (every disc), collections, and game key cards:

```ini
[AutoRun]
label=Your Game Title
open=Launch.exe
icon=game.ico
```

The label uses the package title, or the individual product title on a game key
card. The file also includes an action description. Burn the contents of the disc
folder so these files remain at the disc root. Windows may show an AutoPlay prompt
or ignore automatic launching depending on its settings; `Launch.exe` still opens
manually. The app does not change Windows AutoRun settings.

## TargetFill

Enable **TargetFill: pad remaining disc space** for offline packages or collections.
The integrated engine adapts TargetFill 2.5.0's sparse-file method, creating two
zero-filled DAT files after payloads, manifests, launcher, and artwork are written.
It accounts for 2048-byte sector rounding and keeps the selected media's safety
reserve. Padding is not copied during installation or treated as a game file.

Use an output filesystem supporting sparse files, such as NTFS. Unsupported
filesystems fail with a clear message; disable padding or use another output drive.
This integrates the padding engine, not TargetFill's ISO builder or separate UI.
Build the ISO with your burning software, check its final capacity, and verify the
burn. DAT names do not guarantee inner/outer physical disc placement or additional
protection against disc damage. No optical burn was performed during automated tests.

## Existing features and validation

GOG Key Media remains available with its existing sign-in/download behavior.
The launcher now uses a dark palette, including installation and key-media panels.
Original GOG installer windows and Windows system dialogs keep their own theme.

Build: `dotnet build GOGDiscTool.sln -c Release`

Tests: `dotnet run --project tests/GogDisc.SelfTests/GogDisc.SelfTests.csproj -c Release`

Tests use synthetic installer bytes; no commercial game installer is executed.
Real GOG installation, account download, and optical-media testing remain necessary.

## Wizard verification

From the solution directory on Windows:

```powershell
dotnet run --project tests/GogDisc.WizardChecks/GogDisc.WizardChecks.csproj -c Release
```

This exercises tab navigation, independent game inputs, interleaved add-on order,
a synthetic two-game package build, duplicate names, mode switching, removal and
reset. Pass a directory after `--` to save UI previews. Fixtures are never executed.

## App identity and startup fixes

Run **GOG Physical Disc Creator.exe**. Its purple case, silver disc and green Play
icon is original artwork generated from the chosen Disc + Play concept. The source
PNG and multi-size Windows icon are in `assets/PhysicalDiscCreator.*`.

Launcher cache copies are now atomic and retry temporary access/sharing failures.
Windows argument handling preserves disc-root paths such as `G:\`. Startup errors
are also recorded in `%LocalAppData%\GOG Disc Tool\Logs\launcher-startup.log`.
A persistent permission problem is still reported after the bounded retries.

Image prompt (built-in image generation, selected concept edited): isolate concept
A, change its orange case to GOG-inspired purple, preserve the silver disc and green
Play symbol, remove labels and other concepts, and produce a transparent square icon.


## DOSBox, collection grid, and controllers

The launcher preserves game shortcut arguments and working directories, including DOSBox configuration files. Old saved bare-executable launch entries are rediscovered automatically. Game shortcuts must launch a game directly, not Galaxy or another library client. The manual game picker also accepts configured `.lnk` shortcuts.

Collections use a single cover grid with per-game Install or Play/Uninstall buttons. The window grows to the available screen area; larger collections scroll. Background artwork fills its header with a centered crop.

XInput controllers work in the launcher and patch/DLC choice dialogs: D-pad or left stick moves focus, A selects, B returns to the collection or cancels a patch/DLC prompt. Focused controls are highlighted. Controllers only affect the active launcher window. Windows file/confirmation dialogs and third-party installers still use their own input support.

Existing packages need their `Launch.exe` replaced with this build's `LauncherPayload/Launch.exe`, or must be rebuilt, to receive launcher fixes.


## ISO eject fix

The launcher now requests Windows Explorer's Eject action for both mounted ISOs and physical optical media. This replaces a raw device-eject command that could leave the ISO backing file attached. The launcher waits for the media to disappear and stays open with an error if ejection does not complete. Eject is unavailable during installation.

Validation: `tests/Test-IsoEject.ps1` creates its own disposable ISO and performs three mount/read/eject cycles using the launcher's eject implementation. It verifies that the image is detached and its file can be opened exclusively after each cycle. Run it with Windows PowerShell after building `GogDisc.SelfTests` in Release. Physical-disc tray behavior needs hardware verification.
