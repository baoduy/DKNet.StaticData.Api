using System.Diagnostics;
using System.Text.RegularExpressions;
using DKNet.StaticData.App.Tests.Files.Support;
using DKNet.StaticData.App.Tests.SettingGroups.Support;
using DKNet.StaticData.App.Tests.UiSettings.Support;
using DKNet.StaticData.App.TestSupport;
using Microsoft.Extensions.Logging;

namespace DKNet.StaticData.App.Tests.UiSettings;

/// <summary>
/// Spec DRK-2245 §5 "Answers, limits and logs": an unreachable database answers 503 within the time limit and writes
/// the file routes' Error entry; setting and setting group actions write no Information entry, and no log entry and no
/// trace holds the owner, a group name or a setting value.
/// </summary>
/// <remarks>
/// "Writes no Information log entry" is read for the service's own code, the categories under <c>DKNet.StaticData</c>:
/// the framework's and the DKNet libraries' entries are theirs, not the action's.
/// </remarks>
public sealed class SettingLogTests(FilesHost hosts) : IClassFixture<FilesHost>
{
    public static IEnumerable<object[]> UnreachableDatabaseActions => Databases.With(
        ["lists the settings"],
        ["creates the setting \"orders.list.table\""],
        ["creates the group \"Order screens\""],
        ["erases all settings"]);

    /// <summary>Scenario Outline: A database that cannot be reached answers 503.</summary>
    [Theory]
    [MemberData(nameof(UnreachableDatabaseActions))]
    public async Task ADatabaseThatCannotBeReachedAnswers503(TestDatabase database, string action)
    {
        var api = await hosts.OnOwnServerAsync(database);
        await api.HasSettingAsync("backoffice-web", "customers.list.table");
        await api.Server.StopAsync();
        api.Host.LogCapture.Clear();
        var traceId = ActivityTraceId.CreateRandom().ToHexString();
        var traceParent = $"00-{traceId}-{ActivitySpanId.CreateRandom().ToHexString()}-01";
        var clock = Stopwatch.StartNew();

        using var response = action switch
        {
            "lists the settings" => await api.ListSettingsAsync(Callers.LayoutSvc, "backoffice-web", traceParent: traceParent),
            "creates the setting \"orders.list.table\"" => await api.CreateSettingAsync(
                Callers.LayoutSvc, "backoffice-web", UiSettingsApi.SettingBody("orders.list.table"), traceParent),
            "creates the group \"Order screens\"" => await api.CreateSettingGroupAsync(
                Callers.LayoutSvc, "backoffice-web", SettingGroupsApi.GroupBody("Order screens"), traceParent),
            "erases all settings" => await api.EraseSettingsAsync(Callers.LayoutSvc, "backoffice-web", traceParent),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };

        clock.Stop();
        await response.ShouldBeProblemDetailsAsync(HttpStatusCode.ServiceUnavailable);
        clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(30));
        var error = api.Host.LogCapture.Entries
            .Where(e => e.Level == LogLevel.Error && DatabaseFailed().IsMatch(e.Message))
            .ShouldHaveSingleItem("no single Error log entry \"The database failed: <exception type>\"");
        error.Names(traceId).ShouldBeTrue($"\"{error.Message}\" does not carry the call's trace id {traceId}");
        error.Names("layout-svc").ShouldBeTrue($"\"{error.Message}\" does not name the caller id");
    }

    public static IEnumerable<object[]> SettingActions => Databases.With(
        ["creates the setting \"orders.list.table\" with the value { \"density\": \"compact\" }"],
        ["updates \"customers.list.table\" with the value { \"density\": \"compact\" }"],
        ["deletes \"customers.list.table\""],
        ["creates the group \"Order screens\""],
        ["renames \"Customer screens\" to \"Main screens\""],
        ["deletes the group \"Old screens\""],
        ["erases all settings"]);

    /// <summary>Scenario Outline: A setting action writes no log entry and no personal data.</summary>
    [Theory]
    [MemberData(nameof(SettingActions))]
    public async Task ASettingActionWritesNoLogEntryAndNoPersonalData(TestDatabase database, string action)
    {
        const string compact = """{ "density": "compact" }""";
        var api = await hosts.OnAsync(database);
        var oldScreens = await api.HasSettingGroupAsync("backoffice-web", "Old screens");
        var customerScreens = await api.HasSettingGroupAsync("backoffice-web", "Customer screens");
        var settingId = await api.HasSettingAsync("backoffice-web", "customers.list.table", customerScreens, compact);
        api.Host.LogCapture.Clear();
        using var traces = new TestTraceCapture();

        var (call, status) = action switch
        {
            "creates the setting \"orders.list.table\" with the value { \"density\": \"compact\" }" => (api.CreateSettingAsync(
                Callers.LayoutSvc, "backoffice-web", UiSettingsApi.SettingBody("orders.list.table", value: compact)), HttpStatusCode.Created),
            "updates \"customers.list.table\" with the value { \"density\": \"compact\" }" => (api.UpdateSettingAsync(
                Callers.LayoutSvc, "backoffice-web", settingId,
                UiSettingsApi.SettingBody(null, value: compact, groupId: UiSettingsApi.Id(customerScreens)), "\"1\""), HttpStatusCode.OK),
            "deletes \"customers.list.table\"" =>
                (api.DeleteSettingAsync(Callers.LayoutSvc, "backoffice-web", settingId), HttpStatusCode.NoContent),
            "creates the group \"Order screens\"" => (api.CreateSettingGroupAsync(
                Callers.LayoutSvc, "backoffice-web", SettingGroupsApi.GroupBody("Order screens")), HttpStatusCode.Created),
            "renames \"Customer screens\" to \"Main screens\"" => (api.UpdateSettingGroupAsync(
                Callers.LayoutSvc, "backoffice-web", customerScreens, SettingGroupsApi.GroupBody("Main screens"), "\"1\""), HttpStatusCode.OK),
            "deletes the group \"Old screens\"" =>
                (api.DeleteSettingGroupAsync(Callers.LayoutSvc, "backoffice-web", oldScreens), HttpStatusCode.NoContent),
            "erases all settings" => (api.EraseSettingsAsync(Callers.LayoutSvc, "backoffice-web"), HttpStatusCode.NoContent),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };

        using (var response = await call)
        {
            await response.ShouldHaveStatusAsync(status);
        }

        var entries = api.Host.LogCapture.Entries;
        entries.ShouldNotContain(
            e => e.Level == LogLevel.Information && e.Category.StartsWith("DKNet.StaticData", StringComparison.Ordinal),
            "a setting action wrote an Information log entry");
        foreach (var personal in new[] { "backoffice-web", "Customer screens", "compact" })
        {
            entries.ShouldNotContain(e => e.Holds(personal), $"a log entry holds \"{personal}\"");
            traces.Holds(personal).ShouldBeFalse($"a trace holds \"{personal}\"");
        }
    }

    /// <summary>The file routes' Error entry for the database: it names the database and the exception type.</summary>
    private static Regex DatabaseFailed() => new(@"^The database failed: \w+Exception$", RegexOptions.None, TimeSpan.FromSeconds(1));
}
