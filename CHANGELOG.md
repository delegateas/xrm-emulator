# Changelog

## 1.5.0 (2026-02-08)

Initial open-source release of XRM Emulator.

### Features

* Dataverse emulator using XrmMockup365 with SOAP endpoint
* Aspire hosting extension (`AddXrmEmulatorContainer`, `WithMetadataFolder`, `WithSnapshotPersistence`)
* MetadataSync CLI tool for syncing Dataverse metadata
* Debug UI: data browser (`/debug/data`) and setup page (`/debug/setup`)
* Snapshot persistence (save/restore emulator state)
* Licensing system using Ed25519 cryptography
* Docker container image (`ghcr.io/delegateas/xrm-emulator`)
* Aspire E2E integration tests
