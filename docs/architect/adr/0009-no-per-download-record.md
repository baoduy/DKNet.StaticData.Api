# ADR-0009: No per-download audit record; one log entry per download

- **Status:** Accepted — proposed answer to open question 3; the owner approves it with the design PR.
- **Context:**
  - The requester left open: a per-download audit record, or only the standard audit fields.
  - Callers are trusted services, not end users. The service cannot tell which end user asked.
  - The standard audit fields record who created and who last changed each record.
- **Decision:**
  - No download record in the database.
  - Each download writes one structured log entry: file id, size, caller id and trace id. No owner, no file name.
- **Alternatives:**
  - *A database row per download.* Rejected: a write on every read, a table that only grows, and it would still only name the calling service, not the person.
- **Consequences:**
  - Easier: downloads stay read-only; no retention rule for a new table.
  - Harder: who-downloaded-what lives in logs, kept only as long as the log store keeps them. A caller that needs a per-person record keeps it itself.
