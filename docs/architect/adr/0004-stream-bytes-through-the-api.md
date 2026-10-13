# ADR-0004: Upload and download stream through the API; no download links

- **Status:** Accepted
- **Context:**
  - The requester decided that the API always streams file bytes back, for every provider, with no pre-signed or SAS links (requester decision 7).
  - The local provider cannot make a link at all.
  - Callers are services; a link would leave the owner check behind.
- **Decision:** Every upload and download goes through the API as a stream. The service opens the provider's read stream and copies it to the answer.
- **Alternatives:**
  - *Pre-signed or SAS links.* Rejected by the requester. A link also works for anyone who holds it, with no owner check and no log entry.
- **Consequences:**
  - Easier: the same owner check, logging and behaviour for every provider.
  - Harder: every byte passes through the service; large downloads hold a connection for their whole length.
