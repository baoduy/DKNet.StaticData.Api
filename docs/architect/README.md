# DKNet StaticData

DKNet StaticData stores files and UI display settings for other backend services, each record kept under an owner the caller names.

| | |
|---|---|
| **Repo** | DKNet.StaticData.Api — https://github.com/baoduy/DKNet.StaticData.Api |
| **Service name** | DKNet.StaticData |
| **Bounded context** | Static Data |
| **Stack** | .NET 10 / ASP.NET Core minimal API with DKNet packages, scaffolded from DKNet.Templates (`dknet-minimal`), EF Core on Postgres or SQL Server, Aspire AppHost for local runs |
| **Status** | Active |
| **Design revision** | 1 |
| **Owner** | drunkcoding |
| **Root ticket** | DRK-2184 |

## Documents

- [01-scope.md](01-scope.md) — What problem does the service solve, for whom, and what does it never do?
- [02-domain.md](02-domain.md) — Which terms, aggregates and rules make up the model?
- [03-integration.md](03-integration.md) — Which API does it expose, what does it depend on, and how does a call flow?
- [04-data.md](04-data.md) — What does it store, where, and for how long?
- [05-quality.md](05-quality.md) — How is it secured, observed, tested, packaged and deployed?
- [adr/](adr/) — Why each major choice was made, and which options were rejected:
  - [ADR-0001](adr/0001-why-a-new-service.md) — Why a new service.
  - [ADR-0002](adr/0002-one-database-two-providers.md) — One relational database, Postgres or SQL Server, with exact comparison of owners and keys on both.
  - [ADR-0003](adr/0003-file-bytes-through-dknet-blob-storage.md) — File bytes in blob storage through DKNet.Svc.BlobStorage; one provider per deployment; the storage key is the file id plus its extension.
  - [ADR-0004](adr/0004-stream-bytes-through-the-api.md) — Upload and download stream through the API; no download links.
  - [ADR-0005](adr/0005-owner-query-parameter-and-owner-filter.md) — The owner travels as the `owner` query parameter and is enforced by DKNet.EfCore.DataAuthorization.
  - [ADR-0006](adr/0006-caller-authorization-scope-or-app-role.md) — Authorize callers by scope or app role, 4 permissions.
  - [ADR-0007](adr/0007-file-type-allow-list.md) — Accept only file extensions on a configured allow-list. Proposed answer to open question 1.
  - [ADR-0008](adr/0008-hard-delete.md) — Deleting a file removes its metadata and its bytes. Proposed answer to open question 2.
  - [ADR-0009](adr/0009-no-per-download-record.md) — No per-download audit record; one log entry per download. Proposed answer to open question 3.
  - [ADR-0010](adr/0010-app-managed-version-and-if-match.md) — An app-managed `Version` number guards updates, through `ETag` and `If-Match`.
  - [ADR-0011](adr/0011-upload-as-multipart-form-data.md) — Upload as `multipart/form-data`.
  - [ADR-0012](adr/0012-idempotency-keys-in-the-service-database.md) — Idempotency keys on the 2 create routes with no natural key, stored in the service's own database.
- [diagrams/](diagrams/) — archify IR (`.json`) and render (`.svg`) for every diagram.

## Runtime architecture

![A calling service gets a token from the OIDC issuer with client credentials and calls the API edge over HTTPS; the edge checks the bearer token, the scope or app role and the owner, then hands the call to the files, file group or UI settings endpoints inside the per-replica container; the files endpoints stream bytes through the DKNet blob adapter to the one blob storage provider of the deployment, and every endpoint group reads and writes metadata through EF Core with the owner filter to the Postgres or SQL Server database.](diagrams/runtime.svg)

## Delivery slices

Each slice is one future Workflow B ticket, delivered in this order. The requester fixed the order.

1. **Scaffold** — generate the solution with `dotnet new dknet-minimal`, remove the template's samples, and keep an empty host. Add the database choice (`Database:Provider` = `Postgres` or `SqlServer`) with migrations for both, the health routes, the CI build on `dev`, the multi-arch container publish on `main`, the Helm chart, and an AppHost with Postgres, SQL Server and a local blob folder. Realises: README, 04 Storage, 05 Observability (Health) and Packaging and deployment, ADR-0002 (database choice).
2. **Files** — caller authentication with the caller id claim, the 4 permissions (only `files.read` and `files.write` are used yet), the `owner` query parameter and the owner filter, the `StoredFile` aggregate with the exact-comparison rule for its owner column (ADR-0002; every later slice applies it to its own columns), upload, metadata, list, download and delete, the extension allow-list, the 50,000,000-byte limit and its per-route request bounds, the 3 blob providers and their settings, idempotency on upload with the hourly sweep of expired idempotency rows, and the file log entries. Realises: 02 StoredFile, 03 Files routes and Flows 1 to 3, 04 StoredFile and FileSettings, 05 Security, Observability (Logs), ADR-0003 to ADR-0009, ADR-0011, ADR-0012.
3. **File groups** — the `FileGroup` aggregate with create, read, list, update and delete, the delete refusal while a file is linked, `groupId` on upload, link and unlink of a file, listing files by group, `Version` with `ETag` and `If-Match`, and idempotency on group create. Realises: 02 FileGroup, 03 File group routes and Flow 4, 04 FileGroup, ADR-0010, ADR-0012.
4. **UI settings** — the `UiSetting` and `SettingGroup` aggregates with their routes, the one-setting-per-owner-app-screen rule, the 65,536-byte JSON value check, the setting group delete refusal, and the `settings.read` and `settings.write` permissions. Realises: 02 UiSetting and SettingGroup, 03 UI settings routes and Flow 5, 04 UiSetting and SettingGroup, ADR-0010.
