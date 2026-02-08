# Changelog

## 1.0.0 (2026-02-08)


### Features

* XRM Emulator v1.5.0 ([ba7adf0](https://github.com/delegateas/ContextAnd.Aspire.Hosting.Dataverse/commit/ba7adf08bf2c40c6579c9152e40617a0e1909291))

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
