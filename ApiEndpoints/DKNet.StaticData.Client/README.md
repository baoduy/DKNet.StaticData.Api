# DKNet.StaticData.Client

A typed .NET client for the DKNet StaticData file routes: upload, list, read, download and delete a file
under an owner, keep an owner's files together in file groups, link a file to a group or unlink it, and keep an
owner's UI settings in setting groups.

- Every call takes the owner. The upload and the group create also take the idempotency key.
- The group, setting and setting group updates and the link take the version you last read and send it as `If-Match: "<version>"`. Each
  returns the record with its new version.
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
- The upload and the list take an optional `groupId`: the upload links the new file to that group, the list
  returns only that group's files.
- A field the answer leaves out, such as `UpdatedBy` before the first update, reads as `null`.
- A non-success answer throws `StaticDataApiException` with `StatusCode` and, when the body holds them,
  `ProblemDetails`.

Then the 5 file group routes and the link:

```csharp
var group = await client.CreateGroupAsync("customer-1", idempotencyKey: Guid.NewGuid().ToString("N"), "Onboarding pack",
    purpose: "customer-onboarding", externalRef: "cust-42");
var passport = await client.UploadAsync("customer-1", Guid.NewGuid().ToString("N"), "passport.pdf", stream, groupId: group.GroupId);
var groupFiles = await client.ListAsync("customer-1", pageNumber: 1, pageSize: 20, groupId: group.GroupId);
var groups = await client.ListGroupsAsync("customer-1", pageNumber: 1, pageSize: 20, purpose: "customer-onboarding");
var read = await client.GetGroupAsync("customer-1", group.GroupId);
var renamed = await client.UpdateGroupAsync("customer-1", group.GroupId, read.Version, "Onboarding pack 2");
var unlinked = await client.LinkAsync("customer-1", passport.FileId, groupId: null, passport.Version);
await client.DeleteGroupAsync("customer-1", group.GroupId);
```

- The group update replaces the name, purpose and external reference: a `purpose` or `externalRef` you leave
  `null` is left out of the body, so the group has none after the update.
- `LinkAsync` with a `groupId` links the file to that group of the same owner; with `groupId: null` it unlinks it.
- A version that is no longer the record's answers 412 Precondition Failed, a group delete while a file is linked
  answers 409 Conflict, and an unknown group answers 404 Not Found. Each throws `StaticDataApiException`: read the
  record again and retry with its new version, or unlink its files first.

Then the 11 UI settings routes, which keep one display setting, such as a screen layout, per owner and setting key:

```csharp
var screens = await client.CreateSettingGroupAsync("backoffice-web", "Customer screens");
var layout = await client.CreateSettingAsync("backoffice-web", "customers.list.table", "TableLayout",
    """{"columns":["name","email"],"sort":"name","pageSize":25}""", groupId: screens.GroupId);
var saved = await client.GetSettingAsync("backoffice-web", layout.SettingId);
var updated = await client.UpdateSettingAsync("backoffice-web", saved.SettingId, saved.Version, "TableLayout",
    """{"columns":["name"],"sort":"name","pageSize":50}""", groupId: screens.GroupId);
await client.EraseSettingsAsync("backoffice-web");
```

- The value is raw JSON text. It goes out exactly as you pass it, and `Value` reads back the exact text the service
  answered. Text that is not one JSON value throws `JsonException` before any request goes out.
- The setting update replaces the type, group, value and schema version: a `groupId` you leave `null` unlinks the
  setting, and a `schemaVersion` you leave `null` becomes 1. The group update replaces the name and description.
- `ListSettingsAsync`, `ListSettingGroupsAsync`, `DeleteSettingAsync`, `GetSettingGroupAsync` and
  `DeleteSettingGroupAsync` work like their file group counterparts. A group delete while a setting is in it answers
  409 Conflict.
- `EraseSettingsAsync` deletes every setting, then every setting group, of the owner.

## Restore

Add the GitHub Packages source of `baoduy` to your `NuGet.Config`, authenticated with a GitHub token that has
`read:packages` scope (for example from the `NUGET_GITHUB_TOKEN` environment variable). Never commit the token.
