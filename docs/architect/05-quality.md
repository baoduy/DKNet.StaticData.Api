# 05 — Quality attributes

## Security

### Trust boundaries

| Boundary | What crosses it | Control |
|---|---|---|
| Caller → service edge | HTTPS calls from backend services | Bearer token validated against the OIDC issuer; permission checked per route |
| Service → database | SQL | Credentials from the deployment's secrets; TLS to the server |
| Service → blob storage | File bytes | Provider credentials from the deployment's secrets; TLS for Azure Storage and S3 |
| Browser → this service | Nothing | Browsers never reach the service. The Helm chart exposes it in the cluster only; its HTTP route is off by default, as in DKNet.Accounts.Api's chart |

### Authentication

- Every `/v1` route needs a bearer token. `RequireAuthorization` is on, the scaffold default. It is never turned off in a deployment.
- The scaffold's demonstration sign-in (`EnableDemoAuthentication`) stays off in every deployment.
- Tokens come from client credentials only. A user sign-in token is not a supported caller.
- The issuer, audience and metadata address are settings (`Authentication:Schemes:Bearer:*`). Entra ID in a deployment; the local Keycloak under the AppHost.
- The caller id is the first of the token's `client_id`, `azp` or `appid` claims. A valid token with none of them answers 401 (ADR-0006).

### Authorization

- 4 permissions, each accepted as a scope (`scp` or `scope`) or an app role (`roles`) (ADR-0006):
  - `files.read` — file and file group reads, and download.
  - `files.write` — upload, link, delete; file group create, update, delete.
  - `settings.read` — UI setting and setting group reads.
  - `settings.write` — UI setting and setting group create, update, delete.
- A missing permission answers 403.
- `/healthz` is anonymous and reports status only. `/healthz/detail` needs any valid token.
- **The owner is a partition, not a permission.** A caller with a permission can reach any owner's data by naming that owner. The service trusts its callers to name only owners they serve. This follows the requester's decision that every owner comes from the caller (ADR-0005).
- No route uses cookies, so no route needs an antiforgery token. `EnableAntiforgery` stays off, and the upload route turns off the antiforgery check ASP.NET Core adds to form-file routes.
- Within one call, the owner filter is strict. It is not ignorable and denies all rows when no owner is set.

### Content safety

- The content type comes from the file extension, never from the caller.
- Downloads always answer with `Content-Disposition: attachment` and `X-Content-Type-Options: nosniff`. A browser never renders a stored file inline from this service.
- `.html` and `.htm` are not on the default allow-list.
- File names are cut to their last path segment. The storage key never holds the file name or the owner, so no caller text reaches a blob path.
- Malware scanning and encryption at rest are the storage provider's (requester decision 11).

### Personal data

| Data | Where it flows | Rule |
|---|---|---|
| Owner | Query string, database, idempotency records, API answers | Never logged. Request logs and traces drop the query string. |
| File name | Upload form, database, `Content-Disposition`, API answers | Never logged. |
| File bytes | Blob storage, upload and download streams | Never logged, never cached in memory as a whole by the service. |
| External reference | Database, API answers | Never logged. |
| UI setting value | Database, API answers | Not expected to hold personal data. Never logged. |

### Secrets

| Secret | Used for | Where it lives |
|---|---|---|
| Database connection string | Postgres or SQL Server | The deployment's secret store, passed to the pod through the Helm chart's secret settings. The Key Vault mount used by DKNet.Accounts.Api's chart has a known gap with workload identity, so slice 1 picks and proves the mount |
| Azure Storage connection string | Provider `AzureStorage` | The same secret store |
| S3 access key and secret | Provider `AwsS3` | The same secret store |

No secret is in `appsettings.json`, an image or a log.

## Observability

### Health

- `/healthz` answers healthy, degraded or unhealthy, with no detail. It is anonymous, for the container probes.
- `/healthz/detail` gives the per-check report to a caller with a valid token.
- Checks: the database. No blob storage check in version 1: a storage outage shows as 503 on file routes and in error logs.

### Logs

Structured logs. Every entry carries the trace id and the caller id. None carries the owner, a file name, an external reference or a setting value.

| Event | Level | Fields |
|---|---|---|
| File uploaded | Information | file id, size, content type, caller id |
| File downloaded | Information | file id, size, caller id |
| File deleted | Information | file id, caller id |
| Upload refused (extension, size, form) | Information | reason, caller id |
| Bytes missing for an existing file | Error | file id, storage key |
| Orphaned bytes after a failed delete | Warning | file id, storage key |
| Blob storage or database unreachable | Error | the dependency, the exception type |

An operator finds orphaned bytes by the warning's storage key and removes them in the provider.

### Metrics

- The scaffold's OpenTelemetry setup, behind its `EnableOpenTelemetry` flag: ASP.NET Core request metrics and HTTP client metrics.
- Upload and download sizes and durations come from the request metrics of the 2 file routes. No custom metric in version 1.

### Correlation

- W3C `traceparent` is read on every call and the trace id goes into every log entry.
- The service starts no outbound call to another DKNet service, so no correlation id travels further.

## Performance and scale

| Number | Value | Source |
|---|---|---|
| Max file size | 50,000,000 bytes | Requester decision 8 (50 MB), counted as DKNet.Svc.BlobStorage counts a megabyte |
| Max UI setting value | 65,536 bytes as UTF-8 | Brief, proposed fields (64 KB) |
| Max owner length | 255 characters | Requester decision 5 |
| Upload request body limit | 51,000,000 bytes | This design: the file limit plus room for the form |
| Upload and download request timeout | 300 seconds | This design: 50,000,000 bytes at a slow 2 Mbit/s takes about 200 seconds |
| Requests per second, latency target, number of files, total bytes | Unknown | Open question. No caller has given a number yet. |

- The service is stateless. Replicas scale out behind the cluster service.
- Each upload is buffered by ASP.NET Core to a temporary file once it passes 64 KB, so memory stays flat (ADR-0011). The container needs temporary disk for the uploads in flight.
- Downloads stream from the provider's read stream; the service never holds a whole file in memory.

## Packaging and deployment

- **Container image:** `ghcr.io/baoduy/dknet.staticdata-api`, for `linux-x64` and `linux-arm64`. Built by the .NET SDK container publish, as DKNet.Accounts.Api builds its image. Tagged with the release version and `latest`.
- **CI:** a build and test workflow on pushes and pull requests to `dev`. A publish workflow on pushes to `main` computes the release version from the tags and pushes the image.
- **Helm chart:** `dknet-staticdata`, built on the drunk-app chart like DKNet.Accounts.Api's. It sets `Database:Provider`, `BlobStorage:Provider`, the provider settings, the allow-list and `RequireAuthorization`. Its HTTP route is off by default.
- **Local folder provider:** for development and single-replica use only. With more than 1 replica, use Azure Storage or S3, or a volume every replica shares.
- **Migrations:** each database has its own migrations. They run as the scaffold runs them: at start when `RunDbMigrationWhenAppStart` is on.
- **No NuGet package** ships in version 1.

## Testing approach

Integration tests run against real infrastructure (Policy 02):

| Behaviour | Infrastructure | Why real |
|---|---|---|
| Every route, owner isolation, unique keys, foreign key refusals, `If-Match` and `Version` | Postgres and SQL Server in containers; every test runs on both | Collation, unique keys and concurrency differ between the 2 databases |
| Owners and keys compare exactly (`A` and `a` are different) | Both databases | The SQL Server default collation ignores case |
| A list with no `fromDate` or `toDate` returns a record created and last updated more than 3 months ago; Flow 5 finds and updates that setting | Both databases, audit times set in the past | DKNet's default 3-month window must stay off |
| Upload, download, delete and the size and extension checks, at 50,000,000 bytes and 1 byte more | Local blob provider on a temporary folder | Real streaming and hashing |
| Idempotent upload: replay after 201 (same body, no `Location` or `ETag`), 409 while the first runs, 1 file stored | The database's idempotency store, both databases | Real reservation and replay |
| File name with non-ASCII characters in `Content-Disposition` | Local provider | Real headers |
| Failed insert after stored bytes deletes the bytes | Local provider, database made to fail | The cleanup path |

- The OIDC issuer is faked: a test signing key and test tokens with a scope or an app role, and with and without a caller claim.
- The Azure Storage and S3 adapters are not tested again here. DKNet's own Svc.BlobStorage tests cover them.

## Runtime architecture

The planned runtime shape. The first docs ticket after the scaffold draws the code-derived diagram at `docs/diagrams/` and reports any difference from this one as a design question.

![A calling service gets a token from the OIDC issuer with client credentials and calls the API edge over HTTPS; the edge checks the bearer token, the scope or app role and the owner, then hands the call to the files, file group or UI settings endpoints inside the per-replica container; the files endpoints stream bytes through the DKNet blob adapter to the one blob storage provider of the deployment, and every endpoint group reads and writes metadata through EF Core with the owner filter to the Postgres or SQL Server database.](diagrams/runtime.svg)

Supporting detail:

- **Ports:** the container listens on 8080 over HTTP. TLS ends before the pod.
- **Auth:** bearer token from client credentials; scope or app role `files.read`, `files.write`, `settings.read`, `settings.write`; caller id from `client_id`, `azp` or `appid`.
- **Owner:** the `owner` query parameter on every `/v1` route; the DKNet owner filter fails closed.
- **Configuration:** `Database:Provider` = `Postgres` (default) or `SqlServer`; `BlobStorage:Provider` = `Local`, `AzureStorage` or `AwsS3`; `Files:AllowedExtensions`.
- **Limits:** 50,000,000 bytes per file; 51,000,000-byte body and 300-second timeout on upload; 65,536 bytes per setting value.
- **Idempotency:** `X-Idempotency-Key` on `POST /v1/files` and `POST /v1/file-groups`, scoped by caller and owner, `ConflictHandling` = `CachedResult`, 330-second in-flight reservation, kept 4 hours in the service's database.
