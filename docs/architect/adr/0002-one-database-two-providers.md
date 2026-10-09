# ADR-0002: One relational database, Postgres or SQL Server, with exact comparison

- **Status:** Accepted
- **Context:**
  - The requester asked for the same stack as DKNet.Accounts.Api, with both Postgres and SQL Server supported.
  - DKNet.Accounts.Api picks its database with `Database:Provider`: `Postgres` (default) or `SqlServer`, each with its own migrations.
  - The DKNet.Templates scaffold supports Postgres only.
  - SQL Server's default collation ignores case. Postgres compares exactly. A unique key and the owner filter would behave differently on the 2 databases.
- **Decision:**
  - Keep all records in one relational database per deployment, picked by `Database:Provider`, as DKNet.Accounts.Api does. Postgres is the default.
  - Each database has its own migrations with the same tables, keys and indexes.
  - These columns compare exactly on both databases: `OwnedBy` on all 4 tables, `UiSetting.AppKey`, `UiSetting.SettingKey`, `SettingGroup.AppKey`, `SettingGroup.Name`, `FileGroup.Purpose` and `FileGroup.ExternalRef`. On SQL Server they use the binary collation `Latin1_General_100_BIN2`.
- **Alternatives:**
  - *Postgres only, as the template ships.* Rejected: the requester asked for both.
  - *Case-insensitive comparison everywhere.* Rejected: the owner is the caller's free text; `Customer-1` and `customer-1` may be 2 owners. Folding case would merge their data.
  - *A document store for UI settings.* Rejected: a second store to run, for data that fits a table.
- **Consequences:**
  - Easier: the same behaviour on both databases; one schema to reason about.
  - Harder: 2 sets of migrations; every integration test runs on both databases.
