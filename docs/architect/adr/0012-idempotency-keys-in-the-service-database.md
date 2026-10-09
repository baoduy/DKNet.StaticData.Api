# ADR-0012: Idempotency keys on the 2 create routes with no natural key, in the service's database

- **Status:** Accepted
- **Context:**
  - A caller that times out on an upload retries. Without a guard, each retry stores another file.
  - `POST /v1/files` and `POST /v1/file-groups` have no natural unique key. The 2 settings create routes do, and answer 409 on a repeat.
  - DKNet.AspCore.Idempotency has stores for Postgres and SQL Server. Its header is `X-Idempotency-Key` by default, as in DKNet.Accounts.Api. It can scope keys with a resolver, and can replay the first answer.
  - The service runs no Redis.
- **Decision:**
  - `POST /v1/files` and `POST /v1/file-groups` require `X-Idempotency-Key`.
  - Keys are scoped by caller id, route and key.
  - A repeat within 4 hours, the package default, replays the first answer and changes nothing.
  - Records live in the service's own database, in the store for its `Database:Provider`.
- **Alternatives:**
  - *No idempotency.* Rejected: retried uploads would store duplicate 50 MB files.
  - *The Redis store.* Rejected: a third store to run for one feature.
  - *Idempotency on every POST.* Rejected: the settings routes are already guarded by their unique keys.
- **Consequences:**
  - Easier: safe retries on uploads, with no new infrastructure.
  - Harder: callers send a new key per upload; the replayed answer holds the owner and file name for 4 hours.
