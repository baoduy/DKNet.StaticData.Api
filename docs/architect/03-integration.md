# 03 — Integration

## Context map

| Neighbour | Direction | How they talk |
|---|---|---|
| Backend services and React app backends | Caller → this service | HTTPS REST, bearer token from client credentials |
| Browsers | Never direct | A browser calls its own app backend, which calls this service |
| OIDC issuer (Microsoft Entra ID; Keycloak under the AppHost) | This service → issuer | OpenID Connect metadata and signing keys, read to validate tokens |
| Database (Postgres or SQL Server, one per deployment) | This service → database | SQL through EF Core, for metadata, settings and idempotency records |
| Blob storage (Local folder, Azure Storage or AWS S3, one per deployment) | This service → storage | Through the DKNet.Svc.BlobStorage packages, in process |
| DKNet packages (DKNet repo) | This service → packages | NuGet package references, in process |
| DKNet.Templates | One time, at scaffold | `dotnet new dknet-minimal`; no runtime link |
| DKNet.Accounts.Api | None at runtime | The reference for stack, layout, CI, Helm chart and client package; nothing is shared at build time |
| `DKNet.StaticData.Client` (GitHub Packages) | Caller → package | A caller may reference the typed client package this repo publishes (ADR-0014) |

This service calls no other DKNet service at runtime. No DKNet service is a build-time dependency. Other repos may reference only the published client package, never the service.

![Backend services and React app backends call DKNet StaticData over HTTPS with a client-credentials token from the OIDC issuer, browsers never call it directly, and StaticData keeps metadata in Postgres or SQL Server and bytes in one blob storage provider, with DKNet packages referenced at build time, DKNet.Templates used once at scaffold, and backend services free to reference the published DKNet.StaticData.Client package.](diagrams/context-map.svg)

## Exposed API

### Rules for every `/v1` route

- `Authorization: Bearer <token>` is required: a valid token for this API, with a caller id. A missing or invalid token answers 401. So does a token with none of `client_id`, `azp` or `appid`.
- Every `GET` needs the app role `staticdata.read`. Every `POST`, `PUT` and `DELETE` needs `staticdata.write`. The service reads the roles from the token's `roles` claim (ADR-0016).
- `staticdata.write` does not include reading. An app that reads and writes holds both roles.
- A valid token with a caller id but without the role the route needs answers 403, as problem details. Nothing changes.
- Order of answers: 401 for the token and the caller id, then 403 for the role, then every input check (`owner`, headers, body, `If-Match`). A 403 reserves no idempotency key.
- The `owner` query parameter is required: 1 to 255 characters, not only white space, no control characters. It is URL-encoded, so any other character is allowed (ADR-0005).
- The owner is never trimmed or case-folded. It compares exactly.
- A record of another owner answers 404, the same as a record that does not exist.
- Errors answer as problem details (`application/problem+json`).
- A JSON answer leaves out every field that has no value. It never writes such a field as `null`. This is the scaffold's JSON rule, and it holds on every route.
- A JSON reader and the typed client read a missing field as empty.
- The rule covers answer fields only. A request body may still send `null` where a route says so. A setting `value` is kept and returned exactly as sent, `null` values inside it included.
- Every record answer carries `version` in its body and `ETag: "<version>"` as a header. An idempotent replay is the one exception (see check 4 of the upload).
- Every `PUT` needs `If-Match: "<version>"`. No `If-Match` answers 428. A stale one answers 412 and changes nothing (ADR-0010).
- List routes page with `pageNumber` and `pageSize`, as DKNet's list paging does, newest record first.
- DKNet's default 3-month activity window is off: `DKNet:ListQuery:DefaultActivityWindowMonths` = 0. A list returns every matching record of the owner, however old.
- Optional `fromDate` and `toDate` narrow a list to records created or updated in that range. They are DKNet's list parameters; a caller that sends neither gets all records.
- JSON routes keep the scaffold's request body limit of 1,048,576 bytes and its 30-second request timeout.

### Routes

| Verb | Path | Purpose | Auth |
|---|---|---|---|
| POST | `/v1/files` | Upload one file. | Bearer token, `staticdata.write` |
| GET | `/v1/files` | List the owner's files; optional `groupId` filter. | Bearer token, `staticdata.read` |
| GET | `/v1/files/{fileId}` | Read one file's metadata. | Bearer token, `staticdata.read` |
| GET | `/v1/files/{fileId}/content` | Stream one file's bytes. | Bearer token, `staticdata.read` |
| PUT | `/v1/files/{fileId}/group` | Link the file to a group, or unlink it. | Bearer token, `staticdata.write` |
| DELETE | `/v1/files/{fileId}` | Delete the file's metadata and bytes. | Bearer token, `staticdata.write` |
| POST | `/v1/file-groups` | Create a file group. | Bearer token, `staticdata.write` |
| GET | `/v1/file-groups` | List the owner's file groups; optional `purpose` and `externalRef` filters, exact match. | Bearer token, `staticdata.read` |
| GET | `/v1/file-groups/{groupId}` | Read one file group. | Bearer token, `staticdata.read` |
| PUT | `/v1/file-groups/{groupId}` | Change name, purpose or external reference. | Bearer token, `staticdata.write` |
| DELETE | `/v1/file-groups/{groupId}` | Delete an empty file group. | Bearer token, `staticdata.write` |
| POST | `/v1/ui-settings` | Create one UI setting. | Bearer token, `staticdata.write` |
| GET | `/v1/ui-settings` | List the owner's settings; optional `appKey`, `settingKey` and `groupId` filters, exact match. | Bearer token, `staticdata.read` |
| GET | `/v1/ui-settings/{settingId}` | Read one UI setting. | Bearer token, `staticdata.read` |
| PUT | `/v1/ui-settings/{settingId}` | Replace a setting's type, group, value and schema version. | Bearer token, `staticdata.write` |
| DELETE | `/v1/ui-settings/{settingId}` | Delete one UI setting. | Bearer token, `staticdata.write` |
| POST | `/v1/ui-setting-groups` | Create a setting group. | Bearer token, `staticdata.write` |
| GET | `/v1/ui-setting-groups` | List the owner's setting groups; optional `appKey` filter. | Bearer token, `staticdata.read` |
| GET | `/v1/ui-setting-groups/{groupId}` | Read one setting group. | Bearer token, `staticdata.read` |
| PUT | `/v1/ui-setting-groups/{groupId}` | Change name or description. | Bearer token, `staticdata.write` |
| DELETE | `/v1/ui-setting-groups/{groupId}` | Delete an empty setting group. | Bearer token, `staticdata.write` |
| GET | `/healthz` | Liveness and readiness: status only. | Anonymous |
| GET | `/healthz/detail` | Per-check health report. | Anonymous (ADR-0015) |

The scaffold's OpenAPI and Scalar pages stay behind its `EnableSwagger` flag, which is off by default.

### Typed client: `DKNet.StaticData.Client`

- A .NET package, published to GitHub Packages by the publish workflow on `main` (ADR-0014).
- One typed call per `/v1` route. No call for `/healthz` or `/healthz/detail`.
- Every call takes the `owner`. Upload and file group create take the `X-Idempotency-Key`. Every update takes the `version` it sends as `If-Match`.
- An error answer becomes a typed error with the status code and the problem details.
- It reads a missing answer field as empty.
- It never gets, keeps or logs a credential. The consuming app attaches its own bearer token.
- It holds the public contract only, and no server code.
- Coverage grows by slice: file routes in slice 2, file group routes in slice 3, UI settings routes in slice 4. Each slice updates it with its routes.
- Restoring it needs a GitHub token with `read:packages`.

### `POST /v1/files?owner=<owner>`

Headers:

- `X-Idempotency-Key: <key>` — required. Send a new GUID per upload (ADR-0012).
- `Content-Type: multipart/form-data` — required (ADR-0011). Anything else answers 415.

Form parts:

| Part | Type | Required | Rules |
|---|---|---|---|
| `file` | file | Yes | Exactly 1. Its file name is the `FileName`. Its declared content type is ignored. |
| `groupId` | GUID | No | From slice 3. Names a file group of the same owner. |

Any other part answers 400.

The route needs no antiforgery token. Callers send a bearer token and never a cookie, so the route turns off the antiforgery check that ASP.NET Core adds to form-file routes. The scaffold's `EnableAntiforgery` flag stays off.

The request body limit on this route is 51,000,000 bytes: the 50,000,000-byte file plus room for the form. The request timeout on this route is 300 seconds.

Checks, in order. The first failure answers and nothing is stored:

1. Token and caller id — 401.
2. App role `staticdata.write` — 403.
3. `owner` — 400.
4. `X-Idempotency-Key` missing, or not 1 to 255 letters, digits, `-` and `_` — 400. A repeat of a key this caller used for this owner on this route (ADR-0012):
   - after the first upload answered 2xx, within 4 hours: replays the first answer — the same status (201) and the same JSON body, with no `Location` and no `ETag`. Nothing is stored. The caller takes `version` from the body.
   - while the first upload is still running: 409. Nothing is stored. The caller waits and repeats the same key.
   - after the first upload answered an error: 409 until that key's 330-second reservation ends, then the upload runs again. A caller that got an error answer sends a new key.
5. Content type — 415.
6. Exactly 1 `file` part and no unknown part — 400.
7. File name: 1 to 255 characters after the path is removed, no control characters, extension on the allow-list — 400.
8. Size: 0 bytes answers 400. Over 50,000,000 bytes answers 413.
9. `groupId`, when sent, names a file group of this owner — otherwise 404.

Answer: 201 with `Location: /v1/files/{fileId}?owner=<owner>`, `ETag` and the file metadata.

File metadata (every file answer):

| Field | Type | Notes |
|---|---|---|
| `fileId` | GUID | |
| `owner` | string | As sent at create. |
| `fileName` | string | |
| `contentType` | string | Derived from the extension. |
| `sizeBytes` | integer | |
| `checksum` | string | SHA-256, 64 lowercase hex characters. |
| `groupId` | GUID | Left out when the file has no group. |
| `version` | integer | |
| `createdBy`, `createdOn`, `updatedBy`, `updatedOn` | string, date-time | Caller ids and UTC times. `updatedBy` and `updatedOn` are left out until the first update. |

The storage key is never in an answer.

### `GET /v1/files/{fileId}/content?owner=<owner>`

- 200 streams the stored bytes.
- `Content-Type` is the stored content type. `Content-Length` is `sizeBytes`.
- `Content-Disposition: attachment` carries the file name twice: an ASCII fallback in `filename`, and the exact name, UTF-8 encoded, in `filename*`.
- `X-Content-Type-Options: nosniff` is set.
- 404 when the file does not exist for this owner.
- 500 when the metadata exists and the bytes are missing. It is logged as an error.
- 503 when blob storage is unreachable.
- The request timeout on this route is 300 seconds.

### `PUT /v1/files/{fileId}/group?owner=<owner>`

- Body: `{ "groupId": "<guid>" }` to link, `{ "groupId": null }` to unlink.
- Needs `If-Match`. 404 when the file, or the named group, does not exist for this owner.
- Answer: 200 with the file metadata.

### `POST /v1/file-groups?owner=<owner>`

- Needs `X-Idempotency-Key`, with the same rule as the upload.
- Body: `name` (string, 1 to 100, required), `purpose` (string, up to 100, optional), `externalRef` (string, up to 255, optional).
- Answer: 201 with the group: `groupId`, `owner`, `name`, `purpose`, `externalRef`, `version` and the 4 audit fields.
- `PUT /v1/file-groups/{groupId}` takes the same 3 fields and needs `If-Match`.
- `DELETE /v1/file-groups/{groupId}` answers 204, or 409 while a file is linked.

### `POST /v1/ui-settings?owner=<owner>`

Body fields:

| Field | Type | Required | Rules |
|---|---|---|---|
| `appKey` | string | Yes | 1 to 100 characters, no control characters. |
| `settingKey` | string | Yes | 1 to 200 characters, no control characters. |
| `settingType` | string | Yes | 1 to 50 characters, no control characters. |
| `groupId` | GUID | No | Names a setting group of the same owner and app key. |
| `value` | any JSON | Yes | Not `null`. At most 65,536 bytes as UTF-8. |
| `schemaVersion` | integer | No | 1 or more. Default 1. |

- Answer: 201 with the setting: `settingId`, `owner`, the 6 fields above, `version` and the 4 audit fields.
- 409 when a setting already exists for this owner, app key and setting key.
- 404 when `groupId` names no setting group of this owner. 409 when the group has another app key.
- No idempotency key: the unique key already stops a duplicate.

`PUT /v1/ui-settings/{settingId}` takes `settingType`, `groupId`, `value` and `schemaVersion`, with the same rules, and needs `If-Match`. `appKey` and `settingKey` never change.

Example value for a table layout, kept exactly as sent:

```json
{
  "columns": [
    { "key": "name", "order": 1, "visible": true, "width": 240, "pinned": "left" },
    { "key": "status", "order": 2, "visible": false, "width": 120, "pinned": null }
  ],
  "pageSize": 50,
  "sort": { "key": "name", "direction": "asc" },
  "density": "compact"
}
```

### `POST /v1/ui-setting-groups?owner=<owner>`

- Body: `appKey` (string, 1 to 100, required), `name` (string, 1 to 100, required), `description` (string, up to 500, optional).
- Answer: 201. 409 when a group with this owner, app key and name exists.
- `PUT /v1/ui-setting-groups/{groupId}` takes `name` and `description` and needs `If-Match`.
- `DELETE /v1/ui-setting-groups/{groupId}` answers 204, or 409 while a setting is linked.

## Published events

None. No other service consumes a change in version 1.

## Consumed APIs and events

| Source | What | Why | When it is down |
|---|---|---|---|
| OIDC issuer | OpenID Connect metadata and signing keys | Validate caller tokens | Tokens cannot be validated once the cached keys expire: calls answer 401. Health stays up. |
| Database | Tables of this service, through EF Core | Keep metadata, settings and idempotency records | Calls answer 503 or 500 and change nothing. `/healthz` reports unhealthy. |
| Blob storage, through DKNet.Svc.BlobStorage (`IBlobService`) | `SaveAsync` with a stream, `OpenReadAsync`, `DeleteAsync` | Store, stream and delete file bytes | Upload and download answer 503 and keep no metadata. A delete still removes the metadata; the bytes left behind are logged (Flow 3). Setting routes are not affected. |

This service consumes no events.

## Dependencies

| Dependency | Kind | Direction | Used for |
|---|---|---|---|
| DKNet.Templates (`dknet-minimal`) | Template | Once, at scaffold | Starting solution shape |
| DKNet.AspCore.Extensions | NuGet | This service → package | Endpoint registration and list paging |
| DKNet.AspCore.Idempotency, .NpgsqlStore, .MsSqlStore | NuGet | This service → package | Idempotency keys in the service's database (ADR-0012) |
| DKNet.EfCore.Abstractions, .AuditLogs, .Extensions | NuGet | This service → package | Audited entities and audit stamping from the caller id |
| DKNet.EfCore.DataAuthorization | NuGet | This service → package | The owner field, owner stamping and the owner filter (ADR-0005) |
| DKNet.Svc.BlobStorage.Abstractions, .Local, .AzureStorage, .AwsS3 | NuGet | This service → package | Blob access, one provider per deployment (ADR-0003) |
| Npgsql.EntityFrameworkCore.PostgreSQL, Microsoft.EntityFrameworkCore.SqlServer | NuGet | This service → package | The 2 database providers (ADR-0002) |
| Microsoft.AspNetCore.Authentication.JwtBearer | NuGet | This service → package | Bearer token validation |
| OIDC issuer, database, blob storage | Runtime services | This service → service | See Consumed APIs |

- DKNet packages use the release DKNet.Accounts.Api pins (13.2.5 today), and move with it.
- Every arrow points from this service to a library or an infrastructure service. No cycle exists.
- The template's own packages stay as the scaffold brings them.

## Main flows

Every flow starts with the token check (401) and the role check (403) from the rules for every `/v1` route. A failure there answers before any other step and changes nothing.

### Flow 1 — Upload a file

1. The caller posts the form to `POST /v1/files?owner=<owner>` with a token and an idempotency key.
2. The service runs checks 1 to 9 (Exposed API). A failure answers and stores nothing.
3. The service makes a new `FileId` and the storage key `files/<FileId><extension>`.
4. It reads the buffered upload once to compute the SHA-256 checksum and the size, then streams the bytes to blob storage.
5. It inserts the `StoredFile` row with the owner, the checksum, the size and `Version` 1.
6. It answers 201 with the metadata. The idempotency package keeps that status and body for 4 hours.

Failure paths:

- Blob storage fails in step 4: the service deletes the storage key, best effort, and answers 503. No metadata exists.
- The insert fails in step 5: the service deletes the stored bytes, best effort, and answers 503 or 500. If that delete fails too, it logs a warning with the file id and the storage key. Those bytes are orphaned and no caller can reach them.
- The caller retries with the same key: while the first upload runs, 409; once it answered 201, the first status and body again, with no `Location` or `ETag`. No second file is stored.

![The caller posts a file with a bearer token, an owner and an idempotency key; the service checks the token, the write role, the owner, the extension and the size, hashes the buffered bytes, streams them to blob storage under a new storage key, inserts the metadata row, and answers 201; when the insert fails it deletes the stored bytes and answers with an error.](diagrams/upload-file.svg)

### Flow 2 — Download a file

1. The caller calls `GET /v1/files/{fileId}/content?owner=<owner>` with a token.
2. The service reads the `StoredFile` row through the owner filter. No row answers 404.
3. It opens a read stream on the storage key and streams the bytes with the stored content type, size and file name.
4. It writes one log entry for the download.

Failure paths:

- The bytes are missing: 500 and an error log entry with the file id.
- Blob storage is unreachable: 503.

![The caller asks for a file's content with a bearer token and an owner; the service checks the token, the caller id, the read role and the owner, reads the metadata row through the owner filter, opens a read stream on blob storage under the storage key, streams the bytes back with the stored content type and file name, and answers 404 when the owner has no such file.](diagrams/download-file.svg)

### Flow 3 — Delete a file

1. The caller calls `DELETE /v1/files/{fileId}?owner=<owner>`.
2. The service reads the row through the owner filter. No row answers 404.
3. It deletes the row and commits.
4. It deletes the bytes by the storage key.
5. It answers 204 and writes one log entry.

Failure path: the byte delete in step 4 fails. The answer is still 204, as the file no longer exists for any caller. The service logs a warning with the file id and the storage key. Those bytes are orphaned until an operator removes them (05-quality, Logs).

### Flow 4 — Delete a file group

1. The caller calls `DELETE /v1/file-groups/{groupId}?owner=<owner>`.
2. The service reads the group through the owner filter. No group answers 404.
3. When any `StoredFile` is linked, it answers 409 and changes nothing.
4. Otherwise it deletes the group and answers 204.

The database's foreign key from `StoredFile.GroupId` refuses the delete too, so a file linked between steps 3 and 4 still ends in 409 (04-data). Setting groups follow the same flow with `UiSetting.GroupId`.

### Flow 5 — Save a UI layout

1. The app backend reads the setting: `GET /v1/ui-settings?owner=<owner>&appKey=<app>&settingKey=<screen>`, with no `fromDate` or `toDate`. The activity window is off, so a setting last saved years ago is still found.
2. No setting: it creates one with `POST /v1/ui-settings` and gets 201 with `ETag: "1"`.
3. A setting: it replaces the value with `PUT /v1/ui-settings/{settingId}` and `If-Match` set to the version it read. It gets 200 and the new `ETag`.
4. Two saves race on create: the second `POST` gets 409. The backend goes back to step 1.
5. Two saves race on update: the second `PUT` gets 412 and nothing changes. The backend reads again and decides.

![An app backend reads a setting by owner, app key and setting key; when none exists it creates one and gets version 1, otherwise it sends a PUT with If-Match set to the version it read and gets the next version, and a stale If-Match answers 412 without a change.](diagrams/save-ui-setting.svg)
