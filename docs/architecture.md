# Architecture

The Core contains lease, CAS, states, and contracts. Infrastructure implements R2, ZIP, journals, and recovery. Platform.Windows contains Credential Manager, save discovery, and the Valheim process. App composes the services and presents WPF with NotifyIcon.

Each installation has a global identity and a local player. Profiles isolate connection, optional path, session, logs, downloads, and recovery. Credentials live in the Windows Credential Manager. Each world uses `worlds/<worldId>/` as its remote prefix.

`lock.json` is the authoritative manifest in schema v2, with display name, canonical folder, retention, and author. Every mutation uses ETag and compare-and-swap. The ZIP is immutable and uploaded before changing `Current`; the previous version goes into `History`.

Heartbeat occurs every 60 seconds and the lease expires after 180 seconds. Download, game, snapshot, upload, reset, and backoff remain covered. A session only publishes if it still holds the lease and the base version has not advanced.

Installations extract to `.vws-work-*` outside `worlds_local`, register `install.json`, rename within the same volume, and preserve the previous world as a verified ZIP. The local catalog does not remove copies automatically.

Invitations use AES-256-GCM and PBKDF2-HMAC-SHA256 with versioned parameters. The payload includes connection and credentials, but excludes player, paths, and local identity.

The group shares write credentials. Any member can publish or reset following the protocol. An exclusive owner role would require an external coordinator.
