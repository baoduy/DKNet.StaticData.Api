# ADR-0010: An app-managed `Version` number guards updates, through `ETag` and `If-Match`

- **Status:** Accepted
- **Context:**
  - The brief asks for a concurrency token so 2 callers cannot overwrite each other silently.
  - DKNet's concurrency marker maps `RowVersion` to a database-generated row version. SQL Server and Postgres generate row versions differently. DKNet.Accounts.Api has no concurrency token to follow.
  - The design must behave the same on both databases (ADR-0002).
- **Decision:**
  - Every aggregate has `Version`, a 32-bit whole number: 1 at create, 1 higher after each update. EF Core uses it as the concurrency token.
  - Every record answer has `version` in the body and `ETag: "<version>"`.
  - Every `PUT` needs `If-Match`. No `If-Match` answers 428. A stale one answers 412 and changes nothing.
  - Create is a separate `POST`. A second create of the same natural key answers 409. No route does create-or-update.
  - `DELETE` does not need `If-Match`.
- **Alternatives:**
  - *Database-generated row version.* Rejected: different on each database, and unproven on Postgres in this stack.
  - *Last write wins.* Rejected by the brief.
  - *One create-or-update `PUT` by natural key.* Rejected: its rule for a missing `If-Match` is unclear, and the natural key holds free text in the URL.
- **Consequences:**
  - Easier: the same rule on both databases; a caller sees a conflict and decides.
  - Harder: callers must read before they update, and handle 412.
