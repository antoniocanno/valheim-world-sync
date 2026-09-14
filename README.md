# Valheim World Sync

Windows app (.NET 10/WPF) that rotates the host of a local Valheim world among friends using Cloudflare R2. Before opening the game, the launcher acquires a lease and downloads the current version; when Valheim closes, it snapshots and publishes progress via CAS.

## Create the bucket and get the R2 details

One-time setup by the world owner. Anyone joining via invite can skip to **First run**.

Prerequisite: a Cloudflare account with R2 enabled.

1. Create the bucket: in the Cloudflare dashboard go to **Storage & databases → R2 → Overview → Create bucket**. Enter a name, location, and storage class, and note the exact bucket name (case-sensitive).
2. Note the S3 endpoint: on **R2 → Overview** copy the account endpoint, in the form `https://<ACCOUNT_ID>.r2.cloudflarestorage.com` (the Account ID is shown in the dashboard). Jurisdictional buckets use the matching endpoint, e.g. `https://<ACCOUNT_ID>.eu.r2.cloudflarestorage.com`. The app only accepts an HTTPS URL ending in `.r2.cloudflarestorage.com`, with no path, query, or embedded credentials.
3. Generate the S3 credentials: under **R2 → Overview → Account Details → Manage** next to **API Tokens**, choose **Create Account API token** (valid until manually revoked) or **Create User API token** (inherits your permissions and is disabled if you leave the account). Under **Permissions** choose **Object Read & Write** with **Apply to specific buckets only** and select only the bucket created above. Avoid **Admin Read & Write**, which can create, list, and delete buckets and change the configuration of every bucket in the account.
4. Save the **Access Key ID** and **Secret Access Key** immediately. The secret is not shown again.
5. In the app, fill in:

   | App field | R2 value |
   | --- | --- |
   | Endpoint | URL from step 2 |
   | Bucket | Name from step 1 |
   | Access Key ID | Key from step 4 |
   | Secret Access Key | Secret from step 4 |

6. Click **Test R2**. The test writes, reads, and deletes a temporary file under `worlds/<worldId>/diagnostics/`; a read-only token fails by design. The expected result reports read, write, and delete access.

If it fails, check: `https://` endpoint with the correct suffix, bucket name typed exactly as created, correct key pair (a lost secret requires a new token), and **Object Read & Write** permission on the right bucket.

Official references: [S3 API and credentials](https://developers.cloudflare.com/r2/get-started/s3), [token authentication and permissions](https://developers.cloudflare.com/r2/api/tokens), and [storage and operations pricing](https://developers.cloudflare.com/r2/platform/pricing).

## First run

1. Run `ValheimWorldSync.exe`. The app is self-contained; Steam and Valheim remain external dependencies.
2. Enter your local player name and choose **Create shared world** or **Join with invite**.
3. When creating, use the details from **Create the bucket and get the R2 details**, click **Test R2**, and select the full world folder. Defaults to `%USERPROFILE%\AppData\LocalLow\IronGate\Valheim\worlds_local`.
4. Publish with **Import local world** and export a `.vwsinvite`. The invite is already password-encrypted; send the file and the password through separate channels.

> ⚠️ Share worlds and invites only with trusted friends. The invite contains R2 keys with read and write access to the bucket: anyone with the file and password can read, delete, and publish objects and generate storage and operations charges on your Cloudflare account. The app does not isolate players by prefix.

5. To join, import the invite and enter its password. Paths, player name, and installation identity never come from the owner's machine.
6. Click **Play** and pick the exact world name shown by the launcher in Valheim. Wait for **Synced** after closing the game.

The app works with local worlds only. If it detects Steam Cloud signs, it shows **Manage Saves → Worlds → Move to Local**. It never touches Steam Cloud files directly.

## Where data lives

Local state is under `%LOCALAPPDATA%\ValheimWorldSync`; non-secret connection data lives in `profiles/<id>/`, verified recovery ZIPs in `recovery/<id>`. `Access Key ID` and `Secret Access Key` stay in Windows Credential Manager under `ValheimWorldSync/profile/<id>/r2`.

Multiple profiles may share a bucket (`worlds/<worldId>/` per world), but R2 isolates by bucket, not by prefix: anyone with the credential sees every world in that bucket.

## Safety and limits

- No world merging. Any member with write credentials can reset the remote; true owner isolation would require an authorization service outside R2.
- Replaced worlds are kept as verified recovery ZIPs; reset requires Valheim closed, an exclusive lease, and typing the exact name.
- Current caps: 4 GiB ZIP, 32 GiB expanded content, 500k entries. Characters, mods, save conversion, and dedicated servers are out of scope.

See `docs/architecture.md` for protocol internals (lease, CAS, manifest schema, staging, retries).

## Development

Requires .NET SDK 10.0.302 or a later feature band:

```powershell
./scripts/verify.ps1
./scripts/publish.ps1
```

The single-file executable lands in `artifacts/publish/win-x64/ValheimWorldSync.exe`. R2 tests that mutate the manifest require `VWS_R2_TEST_CONFIG` pointing to an exclusive bucket or prefix.
