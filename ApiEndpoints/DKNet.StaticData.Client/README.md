# DKNet.StaticData.Client

A typed .NET client for the DKNet StaticData file routes: upload, list, read, download and delete a file
under an owner.

- Every call takes the owner. The upload also takes the idempotency key.
- The client never gets, keeps or logs a credential. The consuming application attaches its own bearer
  token through a `DelegatingHandler` it registers.
- An error answer becomes a `StaticDataApiException` carrying the status code and the problem details.

The package lives in GitHub Packages of `baoduy/DKNet.StaticData.Api`. Restoring it needs a GitHub token
with `read:packages` scope.

## Usage

Register the client with the service's address and your own token handler. The client adds no handler, header or
token of its own; the handler you name is resolved from the container and chained onto every request.

```csharp
services.AddTransient<StaticDataTokenHandler>();
services.AddStaticDataClient(new Uri("https://staticdata.example.com"), typeof(StaticDataTokenHandler));

// Your handler attaches the bearer token your app gets for the StaticData API.
// Leave its InnerHandler unset: the client factory chains it.
public sealed class StaticDataTokenHandler(ITokenSource tokens) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetTokenAsync(ct));
        return await base.SendAsync(request, ct);
    }
}
```

Then call the 5 file routes with the owner:

```csharp
var file = await client.UploadAsync("customer-1", idempotencyKey: Guid.NewGuid().ToString("N"), "passport.pdf", stream);
var page = await client.ListAsync("customer-1", pageNumber: 1, pageSize: 20);
var details = await client.GetAsync("customer-1", file.FileId);
await using (var download = await client.DownloadAsync("customer-1", file.FileId))
{
    // download.Content, download.FileName, download.ContentType
}
await client.DeleteAsync("customer-1", file.FileId);
```

- The upload reads `stream` to its end and disposes it.
- A field the answer leaves out, such as `UpdatedBy` before the first update, reads as `null`.
- A non-success answer throws `StaticDataApiException` with `StatusCode` and, when the body holds them,
  `ProblemDetails`.

## Restore

Add the GitHub Packages source of `baoduy` to your `NuGet.Config`, authenticated with a GitHub token that has
`read:packages` scope (for example from the `NUGET_GITHUB_TOKEN` environment variable). Never commit the token.
