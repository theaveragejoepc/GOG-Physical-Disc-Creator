# GOG Physical Disc Creator

<img src="assets/PhysicalDiscCreator.png" width="112" alt="Purple disc-case application icon">

A Windows application for turning your own GOG offline installers into personal physical game editions, with a dark, controller-friendly launcher.

This is an independent modified version of [east35's GOG Disc Packager](https://github.com/east35/GOG-Disc-Packager), with padding adapted from [ArchivSect1986's TargetFill](https://github.com/ArchivSect1986/TargetFill). It is not an official GOG product and is not affiliated with GOG, CD Projekt, SteamGridDB, or those upstream maintainers.

**[Releases](https://github.com/theaveragejoepc/GOG-Physical-Disc-Creator/releases)** Â· [User guide](docs/USER-GUIDE.md) Â· [Build instructions](docs/BUILDING.md) Â· [Changelog](CHANGELOG.md) Â· [Credits and licenses](THIRD-PARTY-NOTICES.md)

## Features

- Package a base game with multiple patches and DLC installers, in an editable order. Optional installers can be skipped.
- Create single-game disc sets or a single-disc collection with individual cover art and Play/Uninstall controls.
- Retain GOG Key Media: a small physical token that downloads a game owned by the person signing in. It is not a transferable activation key.
- Choose local artwork or browse SteamGridDB with your own API key, saved locally using Windows account encryption.
- Optional TargetFill-style sparse DAT padding, with sector rounding and a capacity reserve.
- Dark launcher, centered background artwork, an expanding collection grid, and XInput navigation in launcher and patch/DLC windows.
- Direct game launching, including DOSBox shortcut arguments and working folders, without deliberately launching Galaxy.
- AutoRun metadata, per-disc verification, resumable staging, and Windows Explorer-based ISO/disc ejection.
- Keep launcher open enabled by default; your saved choice is remembered.

## Get started

1. Download a Windows x64 ZIP from this repository's Releases page, if available, and extract the entire ZIP. Keep `LauncherPayload` beside `GOG Physical Disc Creator.exe`.
2. Run **GOG Physical Disc Creator.exe**. On **Main Setup**, select the disc type, title, media capacity and artwork.
3. On **Game 1**, select your original GOG `setup_*.exe`. Matching BIN files are included automatically. Add patches, DLC and extras as needed.
4. Use **Add Game** for another collection tab. Review each game's installation order in the expandable settings.
5. On **Finish**, scan and review the layout, then build disc folders. Use separate burning software to create an ISO or burn the contents of each folder at the disc root.
6. Test the ISO before burning. Open `Launch.exe` manually if Windows does not offer AutoPlay.

Provide your own legally obtained installers and artwork. No commercial games, account credentials, or shared SteamGridDB key are included. Owning a game does not automatically grant permission to redistribute its installers or artwork.

## Requirements and limits

Windows 10/11 x64. Published builds are self-contained; building from source requires the SDK described in [BUILDING](docs/BUILDING.md). Offline installer discs do not need a GOG account or internet at install time, subject to the original game's own requirements. GOG Key Media and online artwork lookup require internet access.

**Preview status:** automated checks cover packaging, installer ordering, shortcut handling, and ISO eject/remount. Physical-disc burning/ejection and physical-controller behavior still need broader hardware testing. Collections currently fit on one disc; multi-disc support applies to individual games. The application creates disc folders, not ISOs, and does not burn discs. Third-party installers and Windows dialogs retain their own appearance and input support.

## Documentation

- [User guide](docs/USER-GUIDE.md): installer order, collections, artwork, padding and controls.
- [Troubleshooting](docs/TROUBLESHOOTING.md): game discovery, ISO ejection, storage and startup issues.
- [Privacy](docs/PRIVACY.md): local settings, API keys, account tokens and network use.
- [Build and test](docs/BUILDING.md), [release checklist](docs/RELEASING.md), [contributing](CONTRIBUTING.md), [security reports](SECURITY.md).
- [Upstream provenance and modification notice](docs/PROVENANCE.md).

## Credits and license

The application is distributed under the **GNU General Public License, version 3**; see [LICENSE](LICENSE). This derivative preserves upstream licensing rather than claiming the original work as our own. Modifications made September 19â€“26, 2026 are described in [CHANGELOG](CHANGELOG.md).

- **east35 and contributors â€” GOG Disc Packager:** the foundation, packaging engine, launcher, Key Media and original assets; GPL-3.0.
- **ArchivSect1986 â€” TargetFill 2.5.0:** sparse-file padding method adapted to C#; its MIT notice is preserved in [THIRD-PARTY-NOTICES](THIRD-PARTY-NOTICES.md).
- **Heroic Games Launcher contributors â€” heroic-gogdl:** optional separately downloaded GOG download helper; GPL-3.0, not bundled here.
- **SteamGridDB and its community:** artwork service accessed through its API. Artwork has its own rights and is not relicensed by this repository.
- **Microsoft and .NET contributors:** .NET/WPF runtime. Distributed runtime license notices accompany published builds.

Source for this derivative is in this repository. Release source must correspond to each binary release. The GPL does not grant rights to game content, third-party artwork, or trademarks. There is no warranty; see the license.
