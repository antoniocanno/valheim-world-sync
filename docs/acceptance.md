# Acceptance verification

## Automated

- CAS, stale ETag, expired lease, lost response, and concurrent base version.
- Manifest v2, per-world prefixes, and version author.
- Full snapshot, ZIP safety, staging outside `worlds_local`, and crash.
- Isolation between profiles and default save discovery.
- Encrypted invite, wrong password, tampering, and absence of secret in plain text.
- Upload, download, progress, retry, and disposable R2 test.
- Import, play, conflict, recovery, retention, and reset preserving the previous version.
- Release build and WPF bindings smoke.

## Pending external gates

1. Validate Credential Manager in an interactive Windows desktop session; the current automated host returns `ERROR_NO_SUCH_LOGON_SESSION`.
2. Provide an exclusive R2 bucket or prefix for the tests controlled by `VWS_R2_TEST_CONFIG`.
3. Open a restored copy of a real 1.0 save in Valheim.
4. Run the full flow with two Steam accounts and two installations.
5. Interrupt the network during large transfers and suspend/resume Windows.
6. Test the EXE on a clean Windows x64 machine, without the .NET runtime.
7. Measure CPU and memory during a real match.

Use disposable copies and namespaces until these gates are completed. Hash integrity does not replace validation of the save by the game.
