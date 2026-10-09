# 01 — Scope

## Purpose

- Services in the DKNet banking platform need to keep files, such as the documents of a customer onboarding pack.
- UI apps need to remember how each user set up a screen, such as the column order and visible columns of a table.
- No DKNet service keeps either today. Each service would need its own file store, its own blob settings and its own settings tables.
- DKNet StaticData is the one place that keeps both.
- A caller names an owner on every call. The service keeps each record under that owner and returns it only to calls that name the same owner.

## Users and consumers

| Who | Kind | What they do |
|---|---|---|
| Backend services | Calling system | Call the API with a machine-to-machine token (client credentials). Upload, read and delete files; create and delete file groups; save and read UI settings. Every call names the owner. |
| React app backends | Calling system | Call the API for their browser app, with their own machine-to-machine token. The browser never calls this service. |
| Operators | Human | Deploy the service, pick the database and the blob storage provider, set the file type allow-list, and read logs and metrics. |
| End users | Human | Use the calling apps. They never call this service and never hold a token for it. |

## Responsibilities

- Keep file metadata in its database and file bytes in blob storage.
- Accept a file only when its extension is on the allow-list and its size is at most 50,000,000 bytes.
- Compute and keep the SHA-256 checksum and the size of each file.
- Stream file bytes back on request, for every storage provider.
- Keep file groups, and link each file to at most one group of the same owner.
- Refuse to delete a file group while a file is linked to it.
- Keep one UI setting per owner, app and screen, with an opaque JSON value of at most 65,536 bytes.
- Keep setting groups, and refuse to delete one while a setting is linked to it.
- Keep every record under the owner the caller named at create. The owner never changes.
- Answer a call only with records of the owner that call names.
- Guard every update against a lost update from a concurrent caller.
- Delete a file's metadata and bytes together when the caller deletes the file.
- Log every file upload, download and delete, without personal data.

## Non-goals

| The service never… | Who does it instead |
|---|---|
| Decides who the owner is, or derives an owner from the token | The calling service, which names the owner on every call |
| Checks whether an end user may see an owner's data | The calling service. This service trusts every caller that holds the permission (05-quality, Authorization) |
| Accepts calls from a browser or a user sign-in token | The React app's own backend calls on the browser's behalf |
| Hands out download links (pre-signed or SAS URLs) | No one; bytes always stream through the API (ADR-0004) |
| Keeps file versions or replaces a file's bytes | No one in version 1; the caller uploads a new file and deletes the old one |
| Scans files for malware | The storage provider, when the operator turns it on there |
| Encrypts file bytes itself | The storage provider's encryption at rest |
| Keeps named views or app-wide default layouts | No one in version 1 |
| Reads, checks or upgrades the content of a UI setting value | The UI app, using `SettingType` and `SchemaVersion` |
| Keeps a record of each download | No one; a log entry only (ADR-0009) |
| Renders, converts or previews files | No one in version 1 |
| Implements blob storage access | DKNet.Svc.BlobStorage packages in the DKNet repo |
| Implements the owner filter | DKNet.EfCore.DataAuthorization in the DKNet repo |
| Implements idempotency | DKNet.AspCore.Idempotency and its Postgres and SQL Server stores in the DKNet repo |
| Issues tokens or manages identities | The OIDC issuer: Microsoft Entra ID, or the local Keycloak under the AppHost |
| Runs the blob store or the database server | The storage provider and the database server of the deployment |
| Ships a typed client package | No one in version 1; a later design revision may add one, as DKNet.Notification.Api did |
| Owns customer, account or ledger data | DKNet.Accounts.Api |

## Boundaries

| Neighbour | Where the work splits |
|---|---|
| DKNet.Templates | It gives the starting solution shape. This service owns everything after the scaffold. |
| DKNet (packages) | The packages give blob access, the owner filter, idempotency, list paging and audit fields. This service owns the file, group and setting rules. |
| DKNet.Accounts.Api | The reference service for stack, layout and deployment. It owns accounts and ledger data. No runtime call in either direction in version 1. |
| Calling services | A caller decides the owner and who may see it. This service keeps the data under that owner. |
| OIDC issuer | The issuer signs caller tokens. This service validates them and checks the permission. |
| Blob storage provider | The provider stores the bytes, encrypts them at rest and may scan them. This service decides the storage key and streams the bytes. |
| Database server | The server stores the rows. This service owns its schema and migrations. |
