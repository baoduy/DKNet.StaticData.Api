using DKNet.StaticData.App.Tests.FileGroups.Support;
using DKNet.StaticData.App.Tests.Files.Support;
using DKNet.StaticData.App.TestSupport;
using Microsoft.Extensions.Logging;

namespace DKNet.StaticData.App.Tests.Integration.TextRules;

/// <summary>
/// Brief DRK-2247 §3 rows 2, 5 and 6 and §6a D1, D8 and D9 on the file and file group routes alone, so they hold without
/// the UI settings routes: the owner is trimmed in the stored record and the <c>Location</c> header, a group update
/// stores its fields trimmed and under the display text rule, the group list filters are trimmed and refused when only
/// spaces or holding a control character, and the owner is checked before the body.
/// </summary>
public sealed class FileTextRuleTests(FilesHost hosts) : IClassFixture<FilesHost>
{
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task AFileUploadedWithOuterSpacesInTheOwnerLandsUnderTheTrimmedOwner(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);

        using var response = await api.UploadAsync(Callers.OnboardingSvc, " customer-1 ", "passport.pdf", Bytes.Of(1024), FilesApi.NewKey());

        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
        var file = await response.JsonAsync();
        file.GetProperty("owner").GetString().ShouldBe("customer-1");
        var location = response.Headers.Location.ShouldNotBeNull();
        (location.IsAbsoluteUri ? location.PathAndQuery : location.OriginalString)
            .ShouldBe($"/v1/files/{file.GetProperty("fileId").GetGuid()}?owner=customer-1");
        (await api.FileNamesAsync("customer-1")).ShouldBe(["passport.pdf"]);
    }

    /// <summary>§3 row 7 and §6 R5: a refused name keeps its <c>UploadRefused("name")</c> entry, with no owner or name.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task AnUploadNameWithAControlCharacterAsSentIsRefusedAndLoggedByItsReasonOnly(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);

        using var response = await api.UploadAsync(Callers.OnboardingSvc, "customer-1", "passport.pdf\t", Bytes.Of(1024), FilesApi.NewKey());

        await response.ShouldBeProblemDetailsAsync(HttpStatusCode.BadRequest);
        (await response.JsonAsync()).GetProperty("detail").GetString().ShouldBe(
            "The file name must be 1 to 255 characters, not counting the spaces at both ends, with no control character.");
        var entries = api.Host.LogCapture.Entries;
        var entry = entries
            .Where(e => e.Level == LogLevel.Information && e.Message.StartsWith("Upload refused", StringComparison.Ordinal))
            .ShouldHaveSingleItem();
        entry.Names("name").ShouldBeTrue($"\"{entry.Message}\" does not name the reason \"name\"");
        foreach (var personal in new[] { "customer-1", "passport.pdf" })
        {
            entries.ShouldNotContain(e => e.Holds(personal), $"a log entry holds \"{personal}\"");
        }

        (await api.FileNamesAsync("customer-1")).ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task AGroupCreatedWithOuterSpacesInTheOwnerLandsUnderTheTrimmedOwner(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);

        using var response = await api.CreateGroupAsync(
            Callers.OnboardingSvc, "customer-1 ", FileGroupsApi.GroupBody("Onboarding pack"), FilesApi.NewKey());

        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
        var group = await response.JsonAsync();
        group.GetProperty("owner").GetString().ShouldBe("customer-1");
        var location = response.Headers.Location.ShouldNotBeNull();
        (location.IsAbsoluteUri ? location.PathAndQuery : location.OriginalString)
            .ShouldBe($"/v1/file-groups/{group.GetProperty("groupId").GetGuid()}?owner=customer-1");
        (await api.GroupNamesAsync("customer-1")).ShouldBe(["Onboarding pack"]);
    }

    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task AGroupUpdateStoresItsFieldsTrimmed(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var groupId = await api.HasGroupAsync("customer-1", "Onboarding pack");

        using var response = await api.UpdateGroupAsync(Callers.OnboardingSvc, "customer-1", groupId,
            FileGroupsApi.GroupBody(" Visa pack ", " visa-application ", " CUS-0043 "), "\"1\"");

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        var group = await api.GroupAsync("customer-1", groupId);
        group.GetProperty("name").GetString().ShouldBe("Visa pack");
        group.GetProperty("purpose").GetString().ShouldBe("visa-application");
        group.GetProperty("externalRef").GetString().ShouldBe("CUS-0043");
    }

    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task AGroupUpdateRefusesANameOutsideTheDisplayText(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var groupId = await api.HasGroupAsync("customer-1", "Onboarding pack");

        using var response = await api.UpdateGroupAsync(
            Callers.OnboardingSvc, "customer-1", groupId, FileGroupsApi.GroupBody("Café"), "\"1\"");

        await response.ShouldBeProblemDetailsAsync(HttpStatusCode.BadRequest);
        var group = await api.GroupAsync("customer-1", groupId);
        group.GetProperty("name").GetString().ShouldBe("Onboarding pack");
        group.GetProperty("version").GetInt32().ShouldBe(1);
    }

    public static IEnumerable<object[]> TrimmedFilters => Databases.With(
        ["purpose", "customer-onboarding "],
        ["externalRef", " CUS-0042"]);

    [Theory]
    [MemberData(nameof(TrimmedFilters))]
    public async Task AGroupListFilterIsTrimmed(TestDatabase database, string filter, string value)
    {
        var api = await hosts.OnAsync(database);
        await api.HasGroupAsync("customer-1", "Onboarding pack", "customer-onboarding", "CUS-0042");
        await api.HasGroupAsync("customer-1", "Visa pack", "visa-application", "CUS-0043");

        using var response = await api.ListGroupsAsync(
            Callers.ReportSvc, "customer-1", $"&{filter}={Uri.EscapeDataString(value)}");

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        FileGroupsApi.Names(await response.JsonAsync(), "name").ShouldBe(["Onboarding pack"]);
    }

    public static IEnumerable<object[]> RefusedFilters => Databases.With(
        ["purpose", "  "],
        ["externalRef", "  "],
        ["purpose", "customer-onboarding\t"],
        ["externalRef", new string('e', 256)]);

    [Theory]
    [MemberData(nameof(RefusedFilters))]
    public async Task AGroupListFilterOfOnlySpacesOrWithAControlCharacterOrTooLongIsRefused(
        TestDatabase database, string filter, string value)
    {
        var api = await hosts.OnAsync(database);

        using var response = await api.ListGroupsAsync(
            Callers.ReportSvc, "customer-1", $"&{filter}={Uri.EscapeDataString(value)}");

        await response.ShouldBeProblemDetailsAsync(HttpStatusCode.BadRequest);
        ErrorFields(await response.JsonAsync()).ShouldBe([Field(filter)]);
    }

    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task TheOwnerIsCheckedBeforeTheBody(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);

        using var response = await api.CreateGroupAsync(
            Callers.OnboardingSvc, "customer-1\t", FileGroupsApi.GroupBody("Café"), FilesApi.NewKey());

        await response.ShouldBeProblemDetailsAsync(HttpStatusCode.BadRequest);
        ErrorFields(await response.JsonAsync()).ShouldBe(["owner"]);
        (await api.GroupNamesAsync("customer-1")).ShouldBeEmpty();
    }

    /// <summary>The fields the problem's <c>errors</c> name, each once.</summary>
    private static string?[] ErrorFields(JsonElement problem) =>
        [.. problem.GetProperty("errors").EnumerateArray().Select(e => e.GetProperty("field").GetString()).Distinct()];

    /// <summary>The field name a refused list filter carries in the problem's <c>errors</c>.</summary>
    private static string Field(string filter) => filter == "purpose" ? "Purpose" : "ExternalRef";
}
