# AGENTS.md

Windows-only .NET 10 WPF app that syncs a local Valheim world between friends via Cloudflare R2. No non-Windows build/test path.

## Commands

Run from repo root in PowerShell:

- `./scripts/verify.ps1` — full quality gate: restore (`--locked-mode`) → build Release → tests → WPF smoke render. This is what CI runs.
- `./scripts/publish.ps1` — self-contained single-file `artifacts/publish/win-x64/ValheimWorldSync.exe`.
- Single test project: `dotnet test tests/ValheimWorldSync.Tests -c Release --no-build`
- Single test: add `--filter "FullyQualifiedName~LocalizationTests"`.

## Before completing a change

- Run `dotnet format ValheimWorldSync.slnx`.
- Ensure `./scripts/verify.ps1` passes after the final changes.

## Toolchain gotchas

- The solution file is `ValheimWorldSync.slnx` (new XML format), not `.sln`. Always reference `.slnx`.
- Central package versions live in `Directory.Packages.props`; lock files are committed and restore uses `--locked-mode`. Any package version/add/remove requires regenerating lock files (`dotnet restore` without `--locked-mode`) and committing them, or verify fails.
- `global.json` pins SDK `10.0.302` (rollForward `latestFeature`); test runner is `Microsoft.Testing.Platform`.
- `Directory.Build.props` sets `ImplicitUsings=enable` globally, but the App project disables it and relies on its own `GlobalUsings.cs`.
- Test projects are xunit.v3 with `OutputType=Exe` (they run as executables, not VSTest libraries). They also carry the VSTest bridge (`Microsoft.NET.Test.Sdk` + `xunit.runner.visualstudio`), so plain `dotnet test` works alongside the `Microsoft.Testing.Platform` runner set in `global.json`.

## Project layout

Layered; App is the only entrypoint (`AssemblyName=ValheimWorldSync`, WPF + WinForms):

- `src/ValheimWorldSync.Core` — protocol: lease, CAS, manifest schema (`lock.json`), sync states, models. No I/O deps.
- `src/ValheimWorldSync.Infrastructure` — R2/S3 storage, ZIP archive, recovery, config.
- `src/ValheimWorldSync.Platform.Windows` — Credential Manager, save discovery, Valheim process handling.
- `src/ValheimWorldSync.App` — WPF UI + NotifyIcon; wires services together.
- `tests/` — `ValheimWorldSync.Tests` (in-memory fakes), `ValheimWorldSync.IntegrationTests` (real R2), `ValheimWorldSync.Windows.Tests` (net10.0-windows).
- `tools/ValheimWorldSync.UiSmoke` — renders real WPF windows to PNGs; part of verify.

`tools/ValheimWorldSync.Diagnostics` is not in the solution and has no source (bin/obj leftovers only); ignore it.

## Testing quirks

- Integration tests skip silently unless `VWS_R2_TEST_CONFIG` env var points to an exclusive R2 bucket/prefix; they mutate real R2. The world round-trip test also skips if a `valheim` process is running.
- Test suites that touch culture must use `TestCultureScope` (restores `Strings.Language` + thread cultures) to avoid leaking state between tests in the same session.

## Localization

- All user-facing strings go through `Strings.Get/Format` in `src/ValheimWorldSync.Core/Localization`. Neutral = `en-US`, satellite = `pt-BR`.
- Language is explicit static state (`Strings.SetLanguage`), never thread-ambient `CurrentUICulture`. XAML binds via `LocExtension`.
- New strings must be added to BOTH `Strings.resx` and `Strings.pt-BR.resx` — `LocalizationTests.PortugueseCatalogHasNoOrphansOrGaps` fails on any gap/orphan.

## Security

- R2 `Access Key ID` / `Secret Access Key` never touch disk or config; stored in Windows Credential Manager (`ValheimWorldSync/profile/<id>/r2`). `config.json` holds only non-secret connection data under `%LOCALAPPDATA%\ValheimWorldSync`.

## Docs

All docs: `README.md`, `docs/architecture.md`, `docs/acceptance.md`.