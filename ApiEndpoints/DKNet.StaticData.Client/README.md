# DKNet.StaticData.Client

A typed .NET client for the DKNet StaticData file routes: upload, list, read, download and delete a file
under an owner.

- Every call takes the owner. The upload also takes the idempotency key.
- The client never gets, keeps or logs a credential. The consuming application attaches its own bearer
  token through a `DelegatingHandler` it registers.
- An error answer becomes a `StaticDataApiException` carrying the status code and the problem details.

The package lives in GitHub Packages of `baoduy/DKNet.StaticData.Api`. Restoring it needs a GitHub token
with `read:packages` scope.
