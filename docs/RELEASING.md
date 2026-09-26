# Release checklist

1. Review attribution, license notices, dependency changes and the staged file list. No credentials, game content, source archives from unrelated projects, build caches or personal test screenshots belong in Git.
2. Run the regression suite, relevant UI checks, and the optional ISO eject integration test on Windows. Record remaining hardware limitations.
3. Add release notes at `docs/releases/<tag>.md`. This derivative starts at `v1.0.0-beta.1`; do not reuse upstream release claims.
4. Tag the exact tested commit. The tag workflow publishes a Windows ZIP, matching source ZIP and SHA-256 checksums. Keep source accessible beside binaries. Preview tags containing a hyphen are marked prerelease.
5. Check the downloaded ZIP, notices and source, and test extraction and launching. Builds are unsigned unless separately signed; do not describe them as signed or antivirus-approved.

If distributing a modified launcher to others on physical media, comply with GPL source-conveyance requirements as well as the licenses of all other contents. A repository link alone is not a blanket substitute for the GPL's physical-distribution conditions. Game ownership is not redistribution permission.
