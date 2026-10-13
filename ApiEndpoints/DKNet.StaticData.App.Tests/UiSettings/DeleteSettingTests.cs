using DKNet.StaticData.App.Tests.Files.Support;
using DKNet.StaticData.App.Tests.UiSettings.Support;
using DKNet.StaticData.App.TestSupport;

namespace DKNet.StaticData.App.Tests.UiSettings;

/// <summary>
/// Spec DRK-2245 §5 "Delete a setting": a delete answers 204 and ignores a version tag; 2 deletes of one setting at the
/// same moment both answer 204.
/// </summary>
public sealed class DeleteSettingTests(FilesHost hosts) : IClassFixture<FilesHost>
{
    /// <summary>Scenario: A caller deletes a setting.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task ACallerDeletesASetting(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var settingId = await api.HasSettingAsync("backoffice-web", "customers.list.table");
        (await api.SettingKeysAsync("backoffice-web")).ShouldBe(["customers.list.table"]);

        using var response = await api.DeleteSettingAsync(Callers.LayoutSvc, "backoffice-web", settingId, ifMatch: "\"7\"");

        await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        using var read = await api.ReadSettingAsync(Callers.LayoutSvc, "backoffice-web", settingId);
        await read.ShouldBeProblemDetailsAsync(HttpStatusCode.NotFound);
        (await api.SettingKeysAsync("backoffice-web")).ShouldBeEmpty();
    }

    /// <summary>Scenario Outline: Two deletes of one setting at the same moment both succeed.</summary>
    /// <remarks>
    /// "At the same moment": each delete is held just before its save until both have reached it, so both have read the
    /// setting before either deletes it.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task TwoDeletesOfOneSettingAtTheSameMomentBothSucceed(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var settingId = await api.HasSettingAsync("backoffice-web", "customers.list.table");
        SaveRaces.HoldBothSavesTogether(api);

        var answers = await Task.WhenAll(
            api.DeleteSettingAsync(Callers.LayoutSvc, "backoffice-web", settingId),
            api.DeleteSettingAsync(Callers.LayoutSvc, "backoffice-web", settingId));

        try
        {
            await SaveRaces.ShouldBePairAsync(answers, HttpStatusCode.NoContent, HttpStatusCode.NoContent);
        }
        finally
        {
            foreach (var answer in answers)
            {
                answer.Dispose();
            }
        }

        using var read = await api.ReadSettingAsync(Callers.LayoutSvc, "backoffice-web", settingId);
        await read.ShouldBeProblemDetailsAsync(HttpStatusCode.NotFound);
    }
}
