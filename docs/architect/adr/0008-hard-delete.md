# ADR-0008: Deleting a file removes its metadata and its bytes

- **Status:** Accepted — proposed answer to open question 2; the owner approves it with the design PR.
- **Context:**
  - The requester left open: soft delete (recoverable) or hard delete.
  - Files may hold customer documents. A caller that deletes one expects it gone.
  - No requirement asks to recover a file. Versioning is out of scope.
  - A file group delete is refused while a file is linked, so the link check must see real rows.
- **Decision:**
  - A delete removes the metadata row, commits, then deletes the bytes.
  - If the byte delete fails, the answer is still 204, and a warning names the storage key for an operator.
  - Settings and groups are deleted for good too.
- **Alternatives:**
  - *Soft delete.* Rejected: deleted documents would stay, with personal data, until some purge job runs. Every query and the group delete check would need to skip deleted rows.
  - *Bytes first, then the row.* Rejected: if the row delete then fails, the file still shows but cannot be downloaded.
- **Consequences:**
  - Easier: a delete means gone; erasure requests are simple.
  - Harder: no undo. A failed byte delete leaves orphaned bytes that no caller can reach; an operator removes them.
