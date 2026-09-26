# Privacy and local data

The application has no added analytics service. It stores working data under `%LocalAppData%\GOG Disc Tool`; disc inventory may also use the creator's existing per-user storage. Logs can contain game titles and local paths.

- SteamGridDB API key: `steamgriddb-key.dat`, encrypted with Windows DPAPI for the current user. Forget key removes the saved copy, not the remote key. The key is sent to SteamGridDB for authenticated API requests and is not included in packages.
- Artwork: downloaded on request from SteamGridDB's approved CDN hosts and cached locally. Local artwork requires no API key.
- GOG Key Media: uses GOG browser authentication and the separately downloaded heroic-gogdl helper. Its auth file is in `GOG Runtime/auth.json`. Treat it as a credential; do not share it. Do not assume it has the SteamGridDB key's DPAPI protection.
- Game detection: reads installation records and shortcut targets locally. Package state, queue progress and launcher settings stay in the user's local profile.
- Online features contact GOG, SteamGridDB and GitHub as needed. The optional download helper checks its GitHub releases for updates. Those services have their own policies.

Offline installer packages contain selected installers/extras/artwork and launcher metadata, not your account tokens or API key. Inspect generated media before sharing; selected files may contain personal or copyrighted content. Redact account data, keys, full local paths and private download URLs from public issue reports.
