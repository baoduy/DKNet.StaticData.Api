# 02 — Domain

## Bounded context

**Static Data** — keeping files and UI display settings for calling services, each record under the owner the caller names.

## Ubiquitous language

| Term | Meaning | Not to be confused with |
|---|---|---|
| Owner | Free text of 1 to 255 characters that the caller names on every call. Every record belongs to exactly one owner. It is often a user id or a customer id. | The caller. One caller works for many owners. |
| Caller | The backend system that sent the request, known by its token's `client_id`, `azp` or `appid` claim (ADR-0006). | The owner, or the end user of the caller's app. |
| Permission | One of `files.read`, `files.write`, `settings.read`, `settings.write`, carried in the token as a scope or an app role. | The owner. A permission says what a caller may do, never whose data. |
| File | One stored document: its metadata in the database and its bytes in blob storage. The aggregate is `StoredFile`. | A file group. |
| Stored bytes | The content of a file in blob storage. They never change after upload. | The file's metadata. |
| Storage key | The name of the bytes in blob storage: `files/<file id><extension>`. Internal; never returned to a caller. | The file name. |
| File name | The name the caller gave the file at upload, for example `passport.pdf`. It is returned on download. | The storage key. |
| Allow-list | The file extensions this deployment accepts, set by the operator (ADR-0007). | The content type, which the service derives from the extension. |
| Checksum | The SHA-256 hash of the stored bytes, as 64 lowercase hex characters. | The `Version` of the record. |
| File group | A named set of files of one owner, for example one customer onboarding pack. | A setting group. |
| Purpose | Free text that says what a file group is for, for example `customer-onboarding`. | The group's name. |
| External reference | Free text that ties a file group to a record elsewhere, for example a customer id. | The owner. |
| UI setting | One saved layout of one screen of one app, for one owner. | A named view. Version 1 has none. |
| App key | The UI app a setting belongs to, for example `backoffice-web`. | A permission or a client id. |
| Setting key | The screen or component a setting belongs to, for example `customers.list.table`. | The setting's id. |
| Setting type | Tells the UI how to read the value, for example `TableLayout`, `Filter` or `Theme`. The service does not check it against a list. | The content type of a file. |
| Value | The setting's content: one JSON document of at most 65,536 UTF-8 bytes. The service keeps it exactly as sent and never reads inside it. | The setting's metadata. |
| Schema version | A whole number the UI uses to upgrade an old value. Default 1. | `Version`. |
| Setting group | A named set of UI settings of one owner and one app. | A file group. |
| Version | A whole number on every record, 1 at create and 1 higher after each update. A caller sends it back in `If-Match` to update (ADR-0010). | The schema version, or a release version of the service. |
| Linked | A file whose `GroupId` names a file group, or a setting whose `GroupId` names a setting group. | Owned. Ownership comes from the owner, not the group. |
| Provider | The one blob storage backend of a deployment: `Local`, `AzureStorage` or `AwsS3` (ADR-0003). | The database provider, `Postgres` or `SqlServer` (ADR-0002). |

## Aggregates

All 4 aggregates are audited entities with a GUID key from `Guid.CreateVersion7()`. Each carries `CreatedBy`, `CreatedOn`, `UpdatedBy` and `UpdatedOn`, stamped with the caller id. Each is owned: it carries `OwnedBy`, the DKNet.EfCore.DataAuthorization owner field, which holds the owner (ADR-0005).

These invariants hold for every aggregate:

- The owner is set at create and never changes.
- A record is visible only to a call that names the same owner. A record of another owner does not exist for that call.
- `Version` is 1 at create and goes up by exactly 1 with each update.
- An update with a stale `Version` changes nothing.

### StoredFile

- **Root:** `StoredFile`, identified by `FileId`.
- **Value objects inside:**
  - `FileName` — 1 to 255 characters, the last path segment only, no control characters. Its extension is on the allow-list.
  - `ContentType` — derived from the file name's extension with DKNet's extension map. Never taken from the caller.
  - `Checksum` — SHA-256 of the stored bytes, 64 lowercase hex characters.
  - `StorageKey` — `files/<FileId><extension>`, the extension in lowercase.
- **State:** `SizeBytes`, `GroupId` (optional), `Version`.
- **Invariants:**
  - `SizeBytes` is 1 to 50,000,000.
  - `SizeBytes` and `Checksum` describe the stored bytes exactly.
  - The stored bytes, the file name, the content type and the storage key never change after create.
  - The storage key always ends in an allowed extension and never holds the owner or the file name.
  - A `StoredFile` exists only after its bytes are stored.
  - When `GroupId` is set, it names a `FileGroup` of the same owner.
  - Only `GroupId` can change after create.
- **References:** `FileGroup` by id only.

### FileGroup

- **Root:** `FileGroup`, identified by `GroupId`.
- **Fields:** `Name` (1 to 100 characters), `Purpose` (optional, up to 100 characters), `ExternalRef` (optional, up to 255 characters), `Version`.
- **Invariants:**
  - A group is deleted only when no `StoredFile` is linked to it. Otherwise the delete is refused and nothing changes.
  - `Name`, `Purpose` and `ExternalRef` may change. The owner may not.
- **References:** none. Files point to the group, never the other way.

### UiSetting

- **Root:** `UiSetting`, identified by `SettingId`.
- **Fields:** `AppKey` (1 to 100 characters), `SettingKey` (1 to 200 characters), `SettingType` (1 to 50 characters), `GroupId` (optional), `Value`, `SchemaVersion`, `Version`.
- **Invariants:**
  - At most 1 setting exists per owner, app key and setting key.
  - `AppKey` and `SettingKey` never change after create.
  - `Value` is valid JSON of 1 to 65,536 UTF-8 bytes. It is kept exactly as sent.
  - `SchemaVersion` is 1 or more.
  - When `GroupId` is set, it names a `SettingGroup` of the same owner and the same app key.
- **References:** `SettingGroup` by id only.

### SettingGroup

- **Root:** `SettingGroup`, identified by `GroupId`.
- **Fields:** `AppKey` (1 to 100 characters), `Name` (1 to 100 characters), `Description` (optional, up to 500 characters), `Version`.
- **Invariants:**
  - At most 1 group exists per owner, app key and name.
  - `AppKey` never changes after create.
  - A group is deleted only when no `UiSetting` is linked to it. Otherwise the delete is refused and nothing changes.
- **References:** none. Settings point to the group, never the other way.

These columns compare exactly, case and accents included, on both databases (ADR-0002): `OwnedBy` on all 4 tables, `UiSetting.AppKey`, `UiSetting.SettingKey`, `SettingGroup.AppKey`, `SettingGroup.Name`, `FileGroup.Purpose` and `FileGroup.ExternalRef`. `Customer-1` and `customer-1` are 2 different owners.

![Four owned aggregates sit inside one owner partition: StoredFile references FileGroup by id and keeps its bytes in blob storage under its storage key, and UiSetting references SettingGroup by id with the same owner and app key.](diagrams/domain-model.svg)

## Domain events

None in version 1. No other service consumes a change. Each file upload, download and delete is one structured log entry instead (05-quality, Logs).

## Lifecycles

None. No aggregate has states. Each record exists from its create until its delete, and a delete removes it for good (ADR-0008). A file group or setting group is either empty or has linked children; that is a count, not a state.
