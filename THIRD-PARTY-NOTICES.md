# Third-party notices

## GOG Disc Packager — east35 and contributors

This application is a modified version of [east35/GOG-Disc-Packager](https://github.com/east35/GOG-Disc-Packager), based on commit `9d319500d3ed81ddc95af4a7b82e76a0d1fdf745`. Its GPL version 3 license is retained verbatim in `LICENSE`. Original fallback artwork, packaging, launcher, Key Media and documentation originate there. See `docs/PROVENANCE.md` and `CHANGELOG.md` for dated modification notices.

## .NET and Windows Desktop runtime

Self-contained Windows builds include Microsoft's .NET and Windows Desktop runtimes. Their upstream license and third-party notice files are copied from the restored runtime packages into `licenses/` during publishing. Sources: https://github.com/dotnet/runtime and https://github.com/dotnet/wpf. See those component notices for their terms; they are not replaced by the application's GPL license.

## Optional external services and helper

[heroic-gogdl](https://github.com/Heroic-Games-Launcher/heroic-gogdl), by Heroic Games Launcher contributors, is a GPL-3.0 helper downloaded separately when GOG Key Media needs it. It is not included in this source or binary ZIP. Its source and license are available in its upstream repository.

SteamGridDB is an API service used for optional artwork searches. No SteamGridDB SDK is bundled. Artwork and GOG/game trademarks retain their respective rights and are not relicensed by this application. Users provide their own credentials and content.

## TargetFill — ArchivSect1986

### Integration

Sparse-file padding is adapted from TargetFill v2.5.0 by ArchivSect1986.
Source: https://github.com/ArchivSect1986/TargetFill/releases/tag/v2.5.0

The integrated C# implementation checks native errors, uses exclusive creation,
accounts for sector rounding, and retains a filesystem reserve. It does not
claim to control file placement on optical media.

MIT License

Copyright (c) 2026 ArchivSect1986

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
