# Troubleshooting

- **DOSBox opens to a prompt:** use a configured direct-game shortcut, not bare DOSBox.exe. The launcher preserves arguments and working folders. A shortcut may be in Public Desktop or the installation folder even if absent from your personal Desktop folder. Name matching can still require manual selection.
- **Galaxy opens:** select the installed game's direct executable or configured shortcut. Library-client shortcuts are intentionally excluded.
- **Old ISO still behaves incorrectly:** each ISO contains its own `Launch.exe`. Rebuild it with the current creator/payload; updating the creator does not update already-built ISOs or discs.
- **ISO cannot remount after eject:** this preview uses Explorer's Eject operation and verifies removal. ISOs ejected with an older launcher may still need Windows-level cleanup/reboot. Use File Explorer's Eject if an operation fails; do not change file permissions just because Windows displays a generic permission error.
- **Patch already installed/unneeded:** choose Skip for that optional step. The base installer cannot be skipped. Verify patch prerequisites yourself.
- **Collection is too large:** collections currently occupy one disc. Choose higher-capacity media or split the collection manually.
- **Padding fails:** use an output filesystem supporting Windows sparse files, or disable TargetFill. Always check the final ISO size and verify burns.
- **Controller does not navigate a GOG installer:** XInput support covers this project's launcher and patch/DLC choice dialogs. Third-party installers and Windows dialogs have their own input support.
- **Startup/access error:** inspect `%LocalAppData%\GOG Disc Tool\Logs\launcher-startup.log`. Close older running launcher instances and report a redacted error. Do not disable antivirus as a workaround.

For bug reports, include app version, Windows version, disc type, reproduction steps, and a redacted screenshot/log. Never upload game installers or authentication files.
