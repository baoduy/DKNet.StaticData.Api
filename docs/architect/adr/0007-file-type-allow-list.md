# ADR-0007: Accept only file extensions on a configured allow-list

- **Status:** Accepted — proposed answer to open question 1; the owner approves it with the design PR.
- **Context:**
  - The requester left open: a configurable allow-list, or every type accepted.
  - The service does no malware scanning (requester decision 11).
  - The storage key needs an extension (ADR-0003).
  - DKNet's extension map gives a content type for common extensions.
- **Decision:**
  - Accept a file only when its extension is on `Files:AllowedExtensions`. The match ignores case.
  - Default list: `.pdf`, `.png`, `.jpg`, `.jpeg`, `.gif`, `.txt`, `.csv`, `.json`, `.xml`, `.doc`, `.docx`, `.xls`, `.xlsx`, `.zip`. Each has a content type in DKNet's map.
  - A file with no extension, or another extension, answers 400.
  - An empty list stops the host at start.
  - The content type comes from the extension, never from the caller.
- **Alternatives:**
  - *Accept every type.* Rejected: with no scanning, it would store executables and HTML pages for other services to hand on. It also leaves files with no extension, which the storage key cannot take.
  - *Check the file's magic bytes.* Rejected for version 1: more code for little gain while callers are trusted services. A later revision can add it.
- **Consequences:**
  - Easier: a known set of types; a safe content type on every download.
  - Harder: a new type needs a settings change in each deployment.
