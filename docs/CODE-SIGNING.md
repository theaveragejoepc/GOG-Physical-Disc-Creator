# Code signing

This derivative's current release workflow produces **unsigned** Windows executables. No signing certificate is bundled or configured. Windows may display Unknown publisher or reputation warnings; a GitHub release is not a security certification.

Maintainers who acquire a code-signing identity can sign both the creator and `LauncherPayload/Launch.exe` before creating release ZIPs and checksums. Keep private keys and passwords outside source control. Do not claim a build is signed without verifying its Authenticode signature. Signing does not guarantee the absence of antivirus false positives.

For suspected false positives, report the exact release checksum and the detection to the relevant vendor. Do not instruct users to disable antivirus.
