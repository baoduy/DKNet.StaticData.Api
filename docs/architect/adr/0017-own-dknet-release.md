# ADR-0017: One DKNet release for this service, not tied to DKNet.Accounts.Api's

- **Status:** Accepted
- **Context:**
  - Design revision 3 said DKNet packages use the release DKNet.Accounts.Api pins (13.2.5), and move with it.
  - On 13.2.5, the Postgres and SQL Server idempotency stores keep only 150 characters of a key. Keys of 151 to 255 characters answer 500 on this service (DRK-2214).
  - DKNet 13.2.6 stores keys of every accepted length. DKNet 13.2.7 caps keys at 500 characters in every store. The packages migrate their own key column.
  - The default key limit stays 255, and the key pattern does not change. ADR-0012's key rule (1 to 255 letters, digits, `-`, `_`) stays as it is.
  - DKNet.Accounts.Api uses the Redis idempotency store, so the bug does not affect it. It still pins 13.2.5.
  - The requester chose option A on 2026-10-12 (DRK-2189, answer 3) and asked to upgrade to the latest DKNet.
- **Decision:**
  - All DKNet packages of this service use one and the same DKNet release.
  - That release may be a later patch release than the one DKNet.Accounts.Api pins. This service does not wait for DKNet.Accounts.Api to move.
  - Slice 3 moves every DKNet package to 13.2.7, the latest release on 2026-10-12.
- **Alternatives:**
  - *Move DKNet.Accounts.Api first, then this service with it.* Rejected: it needs its own cycle on another repo, for a fix DKNet.Accounts.Api does not need.
  - *Stay on 13.2.5 until DKNet.Accounts.Api moves.* Rejected: keys of 151 to 255 characters keep answering 500 until then.
- **Consequences:**
  - Easier: this service takes a DKNet fix when it needs one.
  - Harder: the 2 services can run different DKNet patch releases. A behaviour seen in one may not hold in the other.
