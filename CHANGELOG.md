# Changelog

## 1.0.0-beta.1 â€” 2026-09-26

First public preview of **GOG Physical Disc Creator**, a derivative of east35/GOG-Disc-Packager. This version series belongs to this derivative and does not replace upstream releases.

Modifications developed September 19â€“26, 2026:

- New tabbed Main Setup, per-game, and Finish interface; retained expandable advanced settings and GOG Key Media.
- Multiple ordered patch/DLC installers, optional Skip, and recorded queue progress.
- TargetFill-inspired C# sparse padding with sector rounding and reserve checks.
- Local and SteamGridDB artwork selection with an encrypted per-user API key; corrected CDN handling and icon filters.
- Dark cover-art collection grid, centered backgrounds, and screen-limited automatic window sizing.
- Direct game launch, DOSBox shortcut arguments and working-directory preservation, and cached-launch-target repair.
- XInput launcher navigation; Keep launcher open defaults to enabled.
- Renamed creator executable and purple disc-case app icon.
- Atomic runtime caching, transient file-access retries, disc-root argument handling, and startup logging.
- Explorer-based ISO/disc Eject replacing raw device ejection; repeated ISO remount verified.
- Public documentation, attribution, privacy guidance, source distribution and release automation.
- Publishing refuses to overwrite an existing output directory.

Retained upstream packaging, disc inventory, AutoRun, verification, staging, GOG Key Media, and original fallback assets. See [provenance](docs/PROVENANCE.md).

Known limits: collections are single-disc; padding does not guarantee physical sector placement; patch prerequisites are not inferred; physical optical hardware and physical-controller testing remain outstanding.
