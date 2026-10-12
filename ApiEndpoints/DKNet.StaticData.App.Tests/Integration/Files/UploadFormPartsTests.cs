using DKNet.StaticData.App.Tests.FileGroups.Support;
using DKNet.StaticData.App.Tests.Files.Support;
using DKNet.StaticData.App.TestSupport;

namespace DKNet.StaticData.App.Tests.Integration.Files;

/// <summary>
/// Brief DRK-2237 §3 row 3 and design <c>03-integration.md</c> check 6: besides the file, an upload form may hold only
/// 1 <c>groupId</c> part; any other part answers 400 and stores nothing, with or without a group id. The group is checked
/// before any byte is sent to blob storage.
/// </summary>
public sealed class UploadFormPartsTests(FilesHost hosts) : IClassFixture<FilesHost>
{
    public static IEnumerable<object[]> OtherParts => Databases.With([false], [true]);

    [Theory]
    [MemberData(nameof(OtherParts))]
    public async Task AnyOtherPartIsRefused(TestDatabase database, bool withGroupId)
    {
        var api = await hosts.OnAsync(database);
        var groupId = await api.HasGroupAsync("customer-1", "Onboarding pack");
        var form = FilesApi.Form(("passport.pdf", Bytes.Of(1024), "application/pdf"));
        form.Add(new StringContent("hello"), "note");
        if (withGroupId)
        {
            form.Add(new StringContent(groupId.ToString()), "groupId");
        }

        using var response = await api.SendAsync(
            HttpMethod.Post, FilesApi.Route + FilesApi.OwnerQuery("customer-1"), Callers.OnboardingSvc, form, FilesApi.NewKey());

        await response.ShouldBeProblemDetailsAsync(HttpStatusCode.BadRequest);
        (await api.FileNamesAsync("customer-1")).ShouldBeEmpty();
        api.StoredBlobs().ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task AnUnknownGroupAnswers404BeforeBlobStorageIsCalled(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        api.Host.Blob.SaveFailure = new IOException("simulated: blob storage cannot be reached");

        using var response = await api.UploadIntoAsync(
            Callers.OnboardingSvc, "customer-1", "passport.pdf", FilesApi.NewKey(), Guid.NewGuid().ToString());

        await response.ShouldBeProblemDetailsAsync(HttpStatusCode.NotFound);
        (await response.JsonAsync()).GetProperty("detail").GetString().ShouldBe("The file group does not exist.");
    }
}
