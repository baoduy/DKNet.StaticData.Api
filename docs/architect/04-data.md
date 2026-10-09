# 04 — Data

## Ownership

This service owns and writes:

- File metadata (`StoredFile`) and file groups (`FileGroup`).
- File bytes in blob storage, under keys that start with `files/`.
- UI settings (`UiSetting`) and setting groups (`SettingGroup`).
- Idempotency records, in the shape DKNet.AspCore.Idempotency owns.

This service never writes:

- Owners as records. An owner is a value the caller names; no owner table exists.
- Customer, account or ledger data. DKNet.Accounts.Api owns it.
- Identities and tokens. The OIDC issuer owns them.

## Storage

| Data | Store | Why |
|---|---|---|
| `StoredFile`, `FileGroup`, `UiSetting`, `SettingGroup` | One relational database: Postgres (default) or SQL Server, picked by `Database:Provider` (ADR-0002) | Unique keys, foreign keys and the owner filter need a relational store. The requester asked for both databases. |
| File bytes | One blob storage provider per deployment: `Local`, `AzureStorage` or `AwsS3` (ADR-0003) | Bytes up to 50,000,000 per file do not belong in database rows. |
| Idempotency records | The same database, in the tables of the DKNet idempotency store for that database (ADR-0012) | No extra store to run. |

Each database has its own migrations. Both create the same tables, keys and indexes.

Owners and keys compare exactly on both databases (ADR-0002):

- The exact-compare columns are `OwnedBy` on all 4 tables, `UiSetting.AppKey`, `UiSetting.SettingKey`, `SettingGroup.AppKey`, `SettingGroup.Name`, `FileGroup.Purpose` and `FileGroup.ExternalRef`.
- On SQL Server, these columns use the binary collation `Latin1_General_100_BIN2`.
- On Postgres, the default comparison is already exact. No column uses a case-insensitive type.

## Entities

Every entity also has the 4 DKNet audit fields:

| Field | Type | Length or precision | Required | Unique or indexed | Default | Notes |
|---|---|---|---|---|---|---|
| `CreatedBy` | string | 255 | Yes | — | Caller id | Stamped at create. |
| `CreatedOn` | date-time with offset | — | Yes | — | UTC now | Stamped at create. |
| `UpdatedBy` | string | 255 | No | — | null | Stamped at each update. |
| `UpdatedOn` | date-time with offset | — | No | — | null | Stamped at each update. |

The caller id is the token's `client_id`, `azp` or `appid` claim (ADR-0006). It is never the owner.

### StoredFile

| Field | Type | Length or precision | Required | Unique or indexed | Default | Notes |
|---|---|---|---|---|---|---|
| `Id` | GUID | — | Yes | Primary key | `Guid.CreateVersion7()` | `fileId` in the API. |
| `OwnedBy` | string | 255 | Yes | Index (`OwnedBy`, `CreatedOn`) | — | The owner. Never changes. Personal data: may identify a person. |
| `FileName` | string | 255 | Yes | — | — | Personal data: may name a person. |
| `ContentType` | string | 100 | Yes | — | — | From the extension map. |
| `SizeBytes` | integer (64-bit) | — | Yes | — | — | 1 to 50,000,000 bytes. |
| `Checksum` | string | 64 | Yes | — | — | SHA-256, lowercase hex. |
| `StorageKey` | string | 100 | Yes | Unique | — | `files/<Id><extension>`. Never returned. |
| `GroupId` | GUID | — | No | Index (`GroupId`); foreign key to `FileGroup.Id`, no cascade | null | Link to a file group of the same owner. |
| `Version` | integer (32-bit) | — | Yes | — | 1 | Concurrency token (ADR-0010). |

### FileGroup

| Field | Type | Length or precision | Required | Unique or indexed | Default | Notes |
|---|---|---|---|---|---|---|
| `Id` | GUID | — | Yes | Primary key | `Guid.CreateVersion7()` | `groupId` in the API. |
| `OwnedBy` | string | 255 | Yes | Index (`OwnedBy`, `CreatedOn`) | — | The owner. Never changes. Personal data. |
| `Name` | string | 100 | Yes | — | — | |
| `Purpose` | string | 100 | No | Index (`OwnedBy`, `Purpose`) | null | For example `customer-onboarding`. |
| `ExternalRef` | string | 255 | No | Index (`OwnedBy`, `ExternalRef`) | null | For example a customer id. Personal data: may identify a person. |
| `Version` | integer (32-bit) | — | Yes | — | 1 | Concurrency token. |

The foreign key from `StoredFile.GroupId` refuses a group delete while a file is linked. That refusal answers 409.

### UiSetting

| Field | Type | Length or precision | Required | Unique or indexed | Default | Notes |
|---|---|---|---|---|---|---|
| `Id` | GUID | — | Yes | Primary key | `Guid.CreateVersion7()` | `settingId` in the API. |
| `OwnedBy` | string | 255 | Yes | Unique (`OwnedBy`, `AppKey`, `SettingKey`) | — | The owner. Never changes. Personal data. |
| `AppKey` | string | 100 | Yes | Part of the unique key | — | Never changes. |
| `SettingKey` | string | 200 | Yes | Part of the unique key | — | Never changes. |
| `SettingType` | string | 50 | Yes | — | — | |
| `GroupId` | GUID | — | No | Index (`GroupId`); foreign key to `SettingGroup.Id`, no cascade | null | Same owner and app key as the setting. |
| `Value` | text | Up to 65,536 UTF-8 bytes | Yes | — | — | JSON kept exactly as sent: `text` on Postgres, `nvarchar(max)` on SQL Server. Not expected to hold personal data. |
| `SchemaVersion` | integer (32-bit) | — | Yes | — | 1 | 1 or more. |
| `Version` | integer (32-bit) | — | Yes | — | 1 | Concurrency token. |

### SettingGroup

| Field | Type | Length or precision | Required | Unique or indexed | Default | Notes |
|---|---|---|---|---|---|---|
| `Id` | GUID | — | Yes | Primary key | `Guid.CreateVersion7()` | `groupId` in the API. |
| `OwnedBy` | string | 255 | Yes | Unique (`OwnedBy`, `AppKey`, `Name`) | — | The owner. Never changes. Personal data. |
| `AppKey` | string | 100 | Yes | Part of the unique key | — | Never changes. |
| `Name` | string | 100 | Yes | Part of the unique key | — | |
| `Description` | string | 500 | No | — | null | |
| `Version` | integer (32-bit) | — | Yes | — | 1 | Concurrency token. |

### FileSettings (configuration, one per deployment)

| Setting | Type | Required | Default | Notes |
|---|---|---|---|---|
| `Files:AllowedExtensions` | list of strings | Yes | `.pdf`, `.png`, `.jpg`, `.jpeg`, `.gif`, `.txt`, `.csv`, `.json`, `.xml`, `.doc`, `.docx`, `.xls`, `.xlsx`, `.zip` | Lowercase, each with its dot. An empty list stops the host at start (ADR-0007). |
| `BlobStorage:Provider` | string | Yes | `Local` | `Local`, `AzureStorage` or `AwsS3`. Any other value stops the host at start. |
| `BlobStorage:LocalFolder:RootFolder` | string | With `Local` | — | The package's local provider setting. |
| `BlobService:AzureStorage:ConnectionString`, `:ContainerName` | string | With `AzureStorage` | — | The package's Azure settings. The connection string is a secret. |
| `BlobService:S3:ConnectionString`, `:BucketName`, `:AccessKey`, `:Secret`, `:RegionEndpointName`, `:ForcePathStyle` | string, boolean | With `AwsS3` | Region `us-east-1` | The package's S3 settings. `AccessKey` and `Secret` are secrets. |
| `Database:Provider` | string | No | `Postgres` | `Postgres` or `SqlServer`, as in DKNet.Accounts.Api. |
| `DKNet:ListQuery:DefaultActivityWindowMonths` | integer | Yes | 0 | DKNet's own default is 3 months. This service sets 0, so a list with no `fromDate` or `toDate` returns all records, however old. Never set above 0. |

The file size limit is fixed at 50,000,000 bytes, the requester's 50 MB. It is not a setting.

The setting names under `BlobStorage:LocalFolder`, `BlobService:AzureStorage` and `BlobService:S3` belong to the DKNet packages. They are used as the packages define them.

### IdempotencyRecord (shape owned by DKNet.AspCore.Idempotency)

- One record per caller, owner, route and key. It is reserved for 330 seconds when the request starts, then kept for 4 hours once a 2xx answer is stored (ADR-0012).
- It holds the first answer's status, body and content type: file or group metadata. It holds no headers. The body holds the owner and the file name, which are personal data.
- The package creates and migrates its own tables.

## Retention

| Record | Lives until | Deleted by |
|---|---|---|
| `StoredFile` and its bytes | The caller deletes the file | `DELETE /v1/files/{fileId}`: the row, then the bytes (ADR-0008) |
| `FileGroup` | The caller deletes it, after unlinking or deleting its files | `DELETE /v1/file-groups/{groupId}` |
| `UiSetting` | The caller deletes it | `DELETE /v1/ui-settings/{settingId}` |
| `SettingGroup` | The caller deletes it, after unlinking or deleting its settings | `DELETE /v1/ui-setting-groups/{groupId}` |
| Idempotency record | 4 hours after the first answer | The idempotency package |
| Orphaned bytes (a failed best-effort delete) | An operator removes them | The operator, using the warning log entry that names the storage key |

No record expires on its own. Erasing all of an owner's data is the caller's job: it lists and deletes each record. The list routes have no activity window, so a list with no `fromDate` or `toDate` reaches every record of the owner, however old.
