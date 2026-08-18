# Changelog

## [1.7.0](https://github.com/delegateas/xrm-emulator/compare/v1.6.0...v1.7.0) (2026-08-18)


### Features

* Add AppModuleRoleDefinition and EnvironmentVariableDefinition models ([3bb0bea](https://github.com/delegateas/xrm-emulator/commit/3bb0bea6c815453de7a47802684ff19b85694cd2))
* Add checkout command for existing Custom APIs and update usage instructions ([4a79ab5](https://github.com/delegateas/xrm-emulator/commit/4a79ab5f8c7d782dc40dfa0dd43360f2fe110bc6))
* Add configuration for solution exports, styles, and Xrm API shim ([4220eb8](https://github.com/delegateas/xrm-emulator/commit/4220eb814b07979617410e36692f3bc24c30ca49))
* Add environment variable management and solution component copy functionality ([e36b9e1](https://github.com/delegateas/xrm-emulator/commit/e36b9e19e14b180b78ea14d8136de9299d92a04a))
* Add execution history persistence to data directory and update .gitignore for runtime logs ([40f1475](https://github.com/delegateas/xrm-emulator/commit/40f1475cf0ef398bf4dad2f6939d43bcad0cf936))
* Add functionality to remove components from solution and introduce related model ([4f146eb](https://github.com/delegateas/xrm-emulator/commit/4f146ebe4065e3185aa5bb1c80089d2cfceb151a))
* Add ImportOverride method to RibbonImportWriter for full ribbon customization ([3bb0bea](https://github.com/delegateas/xrm-emulator/commit/3bb0bea6c815453de7a47802684ff19b85694cd2))
* Add plugin content update functionality and related models ([0421b93](https://github.com/delegateas/xrm-emulator/commit/0421b93b7a90cc4f1681d6b00a1f5b35cfc6455d))
* Add role association functionality to AppModuleWriter ([3bb0bea](https://github.com/delegateas/xrm-emulator/commit/3bb0bea6c815453de7a47802684ff19b85694cd2))
* add RolePrivilegeGrantReader for reading role privileges ([6875222](https://github.com/delegateas/xrm-emulator/commit/687522203a05472f3133842ea754e0260f4de4f7))
* Add seed hook, org structure reader, security role and plugin improvements; fix MockupServiceSettings constructor call ([680b918](https://github.com/delegateas/xrm-emulator/commit/680b9182588cb4d16d9d577f3feb710b5bb9f2c9))
* Add support for creating new entities and related commands ([0558a9a](https://github.com/delegateas/xrm-emulator/commit/0558a9a0e641b3c167105c9af0cecf3e3345e378))
* Add support for entity and relationship metadata deletion ([313fbff](https://github.com/delegateas/xrm-emulator/commit/313fbff6e959acfc235414b9b92ea60760a51b8a))
* Add support for plugin managed identity binding and related functionality ([2939f0e](https://github.com/delegateas/xrm-emulator/commit/2939f0e9a64eba52cb5fd7cbf984febfe1a05a3b))
* Add support for workflow activation and association management ([54c3cd7](https://github.com/delegateas/xrm-emulator/commit/54c3cd708d61cfa0cb06eb1a8c60f86ce4ecb6c2))
* add xrmmockup-patches script for managing XrmMockup local patches ([6875222](https://github.com/delegateas/xrm-emulator/commit/687522203a05472f3133842ea754e0260f4de4f7))
* create SecurityRoleAuditDocument for privilege audit management ([6875222](https://github.com/delegateas/xrm-emulator/commit/687522203a05472f3133842ea754e0260f4de4f7))
* Enhance Business Rule and BPF handling in Metadata Sync ([5563be5](https://github.com/delegateas/xrm-emulator/commit/5563be55aa191e646da530e896572e261f8ad791))
* enhance data import functionality with improved error handling and optional impersonation support ([345f669](https://github.com/delegateas/xrm-emulator/commit/345f669b93c0b6aa06bdde5c511f6675fc291ca5))
* Enhance DataImportWriter with lookup caching and improved error handling ([3bb0bea](https://github.com/delegateas/xrm-emulator/commit/3bb0bea6c815453de7a47802684ff19b85694cd2))
* Enhance MetadataFolderBuilder to support role privilege management and improve plugin collection logic ([a2d6755](https://github.com/delegateas/xrm-emulator/commit/a2d675577797a2890a4239e67ac1b4c35f5b3172))
* Enhance OAuth authorization URL with prompt parameter and update command-line flag handling ([d17039e](https://github.com/delegateas/xrm-emulator/commit/d17039e035d36110137c63d313b855c61cf6eeed))
* enhance OptionSetValueDefinition with DisplayName and update related commands for better user experience ([a435a51](https://github.com/delegateas/xrm-emulator/commit/a435a512eb34941aad239fc3f393537c46486fa6))
* Enhance OptionSetWriter to support dynamic language codes for labels ([3bb0bea](https://github.com/delegateas/xrm-emulator/commit/3bb0bea6c815453de7a47802684ff19b85694cd2))
* Enhance security role management with sync functionality and improve serialization logic ([de356f0](https://github.com/delegateas/xrm-emulator/commit/de356f04c4a6b0018c2be7a88eafa807eedfba61))
* Enhance SecurityRole handling with task-based privileges and improve FetchXML pagination; update MetadataFolderBuilder for workflow deduplication ([5bb7a06](https://github.com/delegateas/xrm-emulator/commit/5bb7a06f6223de5a49b124bcabc31ced7a5add96))
* Implement cascade deletion for entity-specific child records in CommitPipeline ([a59b4d3](https://github.com/delegateas/xrm-emulator/commit/a59b4d351a0ab0006fc625abed2e8a328aac78b9))
* Implement impersonation support in data import process and enhance MetadataFolderBuilder for activitypointer handling ([56f2c73](https://github.com/delegateas/xrm-emulator/commit/56f2c73a4dd0b3d1ee9d3d04c9dfed093e173536))
* implement JustificationCatalogue for audit justification resolution ([6875222](https://github.com/delegateas/xrm-emulator/commit/687522203a05472f3133842ea754e0260f4de4f7))
* Implement security role deletion functionality ([4e729c0](https://github.com/delegateas/xrm-emulator/commit/4e729c053bc1476b3cc50361c00bd86529e90e50))
* Implement solution component addition functionality and related model ([9ab9d2e](https://github.com/delegateas/xrm-emulator/commit/9ab9d2e6f41addcf4d6268f218d87040a5be78ac))
* implement SolutionSecurityRoleReader for solution role resolution ([6875222](https://github.com/delegateas/xrm-emulator/commit/687522203a05472f3133842ea754e0260f4de4f7))
* Introduce EnvironmentVariableWriter for managing environment variables ([3bb0bea](https://github.com/delegateas/xrm-emulator/commit/3bb0bea6c815453de7a47802684ff19b85694cd2))
* Modify IconWriter to handle virtual entities gracefully ([3bb0bea](https://github.com/delegateas/xrm-emulator/commit/3bb0bea6c815453de7a47802684ff19b85694cd2))
* rename methods for clarity in MetadataFolderBuilder and update related comments ([adbf0a3](https://github.com/delegateas/xrm-emulator/commit/adbf0a3602dbef5b42dacd259d25fb9f1a9f06a8))
* Update EntityPicker to include solution unique name and enhance entity retrieval; improve error message in SecurityRoleWriter ([6989cef](https://github.com/delegateas/xrm-emulator/commit/6989cefacc3f007fe75a6090889088e21a2fe860))
* Update EntityWriter to handle organization base language for labels ([3bb0bea](https://github.com/delegateas/xrm-emulator/commit/3bb0bea6c815453de7a47802684ff19b85694cd2))


### Bug Fixes

* added no-cache support and agentic model generator ([dcb4648](https://github.com/delegateas/xrm-emulator/commit/dcb46480140d7e28e907aa95ab2dbb319a7403ff))
* added some commands for agentic work ([857a6f0](https://github.com/delegateas/xrm-emulator/commit/857a6f05a72f5638394f307173698cf3801e3a89))
* Delete existing step images before re-registering to prevent duplicate accumulation ([9c2a3de](https://github.com/delegateas/xrm-emulator/commit/9c2a3dee10d10b56ea95326eeecc7dc485553e37))

## [1.6.0](https://github.com/delegateas/xrm-emulator/compare/v1.5.0...v1.6.0) (2026-02-09)


### Features

* add project metadata and README for XrmEmulator.Aspire.Hosting.Dataverse ([353761d](https://github.com/delegateas/xrm-emulator/commit/353761d5939d148aa8c43a355733297eed525193))

## [1.5.0](https://github.com/delegateas/ContextAnd.Aspire.Hosting.Dataverse/compare/v1.4.0...v1.5.0) (2026-02-08)


### Features

* add setup controller and E2E test infrastructure ([d6890c1](https://github.com/delegateas/ContextAnd.Aspire.Hosting.Dataverse/commit/d6890c1ed485d67e93641a1ccf2d1bcdeb9426d8))

## Changelog
