# ADR-0011: Upload as `multipart/form-data`

- **Status:** Accepted
- **Context:**
  - An upload carries the bytes, the file name and, from slice 3, an optional group id.
  - The file name is free text and may hold non-ASCII characters.
  - ASP.NET Core reads a form file to a temporary file once it passes 64 KB, so memory stays flat.
- **Decision:** `POST /v1/files` takes `multipart/form-data` with exactly 1 `file` part and an optional `groupId` part. The file name comes from the `file` part. Its declared content type is ignored.
- **Alternatives:**
  - *Raw bytes as the body, with the file name in a header.* Rejected: a non-ASCII file name does not fit a header without a second encoding.
  - *Raw bytes with the file name in the query string.* Rejected: it puts more personal data in the URL.
  - *Base64 inside JSON.* Rejected: a third larger, and held in memory.
- **Consequences:**
  - Easier: every HTTP client can send it; the file name travels as the client gave it.
  - Harder: the upload is buffered to temporary disk before it streams to storage, so the container needs temporary space for the uploads in flight.
