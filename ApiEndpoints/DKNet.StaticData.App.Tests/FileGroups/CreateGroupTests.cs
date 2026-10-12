using DKNet.StaticData.App.Tests.FileGroups.Support;
using DKNet.StaticData.App.Tests.Files.Support;
using DKNet.StaticData.App.TestSupport;
using Microsoft.EntityFrameworkCore.Storage;

namespace DKNet.StaticData.App.Tests.FileGroups;

/// <summary>
/// Spec DRK-2235 §5 "Create a group": the group fields, kept exactly as sent, and the idempotency key, with the
/// upload's rules scoped by caller, owner, route and key.
/// </summary>
public sealed class CreateGroupTests(FilesHost hosts) : IClassFixture<FilesHost>
{
    /// <summary>Scenario Outline: A caller creates a file group.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task ACallerCreatesAFileGroup(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);

        using var response = await api.CreateGroupAsync(Callers.OnboardingSvc, "customer-1",
            FileGroupsApi.GroupBody("Onboarding pack", "customer-onboarding", "CUS-0042"), "group-1");

        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
        var group = await response.JsonAsync();
        var groupId = group.GetProperty("groupId").GetGuid();
        var location = response.Headers.Location.ShouldNotBeNull();
        (location.IsAbsoluteUri ? location.PathAndQuery : location.OriginalString)
            .ShouldBe($"/v1/file-groups/{groupId}?owner=customer-1");
        response.Headers.ETag?.Tag.ShouldBe("\"1\"");
        group.GetProperty("name").GetString().ShouldBe("Onboarding pack");
        group.GetProperty("purpose").GetString().ShouldBe("customer-onboarding");
        group.GetProperty("externalRef").GetString().ShouldBe("CUS-0042");
        group.GetProperty("owner").GetString().ShouldBe("customer-1");
        group.GetProperty("version").GetInt32().ShouldBe(1);
        group.GetProperty("createdBy").GetString().ShouldBe("onboarding-svc");
        group.Has("updatedBy").ShouldBeFalse();
        group.Has("updatedOn").ShouldBeFalse();
    }

    public static IEnumerable<object[]> EmptyFields => Databases.With(
        ["no purpose and no external reference"],
        ["the purpose null and the external reference null"]);

    /// <summary>Scenario Outline: A group with only a name leaves out the empty fields.</summary>
    [Theory]
    [MemberData(nameof(EmptyFields))]
    public async Task AGroupWithOnlyANameLeavesOutTheEmptyFields(TestDatabase database, string emptyFields)
    {
        var api = await hosts.OnAsync(database);
        var body = emptyFields == "no purpose and no external reference"
            ? """{"name":"Onboarding pack"}"""
            : """{"name":"Onboarding pack","purpose":null,"externalRef":null}""";

        using var response = await api.CreateGroupAsync(Callers.OnboardingSvc, "customer-1", body, FilesApi.NewKey());

        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
        var group = await response.JsonAsync();
        group.GetProperty("name").GetString().ShouldBe("Onboarding pack");
        group.Has("purpose").ShouldBeFalse();
        group.Has("externalRef").ShouldBeFalse();
    }

    public static IEnumerable<object[]> BadFields => Databases.With(
        ["no name"],
        ["the name \"   \""],
        ["a name of 101 characters"],
        ["a name with a tab character"],
        ["the name \"Pack\" and the purpose \"\""],
        ["the name \"Pack\" and a purpose of 101 characters"],
        ["the name \"Pack\" and the external reference \"   \""],
        ["the name \"Pack\" and an external reference of 256 characters"],
        ["the name \"Pack\" and a purpose with a tab character"],
        ["the name \"Pack\" and an external reference with a tab character"]);

    /// <summary>Scenario Outline: Group fields are checked.</summary>
    [Theory]
    [MemberData(nameof(BadFields))]
    public async Task GroupFieldsAreChecked(TestDatabase database, string fields)
    {
        var api = await hosts.OnAsync(database);
        var body = fields switch
        {
            "no name" => FileGroupsApi.GroupBody(null),
            "the name \"   \"" => FileGroupsApi.GroupBody("   "),
            "a name of 101 characters" => FileGroupsApi.GroupBody(new string('n', 101)),
            "a name with a tab character" => FileGroupsApi.GroupBody("Onboarding\tpack"),
            "the name \"Pack\" and the purpose \"\"" => FileGroupsApi.GroupBody("Pack", purpose: ""),
            "the name \"Pack\" and a purpose of 101 characters" => FileGroupsApi.GroupBody("Pack", purpose: new string('p', 101)),
            "the name \"Pack\" and the external reference \"   \"" => FileGroupsApi.GroupBody("Pack", externalRef: "   "),
            "the name \"Pack\" and an external reference of 256 characters" =>
                FileGroupsApi.GroupBody("Pack", externalRef: new string('e', 256)),
            "the name \"Pack\" and a purpose with a tab character" => FileGroupsApi.GroupBody("Pack", purpose: "customer\tonboarding"),
            "the name \"Pack\" and an external reference with a tab character" =>
                FileGroupsApi.GroupBody("Pack", externalRef: "CUS\t0042"),
            _ => throw new ArgumentOutOfRangeException(nameof(fields), fields, null)
        };

        using var response = await api.CreateGroupAsync(Callers.OnboardingSvc, "customer-1", body, FilesApi.NewKey());

        await response.ShouldBeProblemDetailsAsync(HttpStatusCode.BadRequest);
        (await api.GroupNamesAsync("customer-1")).ShouldBeEmpty();
    }

    /// <summary>Scenario: The group name is kept exactly as sent.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task TheGroupNameIsKeptExactlyAsSent(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);

        using var response = await api.CreateGroupAsync(
            Callers.OnboardingSvc, "customer-1", FileGroupsApi.GroupBody(" Onboarding Pack"), FilesApi.NewKey());

        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
        var created = await response.JsonAsync();
        created.GetProperty("name").GetString().ShouldBe(" Onboarding Pack");
        (await api.GroupAsync("customer-1", created.GetProperty("groupId").GetGuid()))
            .GetProperty("name").GetString().ShouldBe(" Onboarding Pack");
    }

    /// <summary>Scenario: Two groups of one owner may have the same name.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task TwoGroupsOfOneOwnerMayHaveTheSameName(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var first = await api.HasGroupAsync("customer-1", "Onboarding pack");

        using var response = await api.CreateGroupAsync(
            Callers.OnboardingSvc, "customer-1", FileGroupsApi.GroupBody("Onboarding pack"), FilesApi.NewKey());

        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
        (await response.JsonAsync()).GetProperty("groupId").GetGuid().ShouldNotBe(first);
        (await api.GroupNamesAsync("customer-1")).ShouldBe(["Onboarding pack", "Onboarding pack"]);
    }

    public static IEnumerable<object[]> BadKeys => Databases.With(
        ["no idempotency key"],
        ["the key \"group 1\""],
        ["a key of 256 characters"]);

    /// <summary>Scenario Outline: A group create needs a well-formed idempotency key.</summary>
    [Theory]
    [MemberData(nameof(BadKeys))]
    public async Task AGroupCreateNeedsAWellFormedIdempotencyKey(TestDatabase database, string key)
    {
        var api = await hosts.OnAsync(database);
        var sent = key switch
        {
            "no idempotency key" => null,
            "the key \"group 1\"" => "group 1",
            "a key of 256 characters" => new string('k', 256),
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, null)
        };

        using var response = await api.CreateGroupAsync(
            Callers.OnboardingSvc, "customer-1", FileGroupsApi.GroupBody("Onboarding pack"), sent);

        await response.ShouldBeProblemDetailsAsync(HttpStatusCode.BadRequest);
        (await api.GroupNamesAsync("customer-1")).ShouldBeEmpty();
    }

    /// <summary>Scenario Outline: A repeated group create after success replays the first answer.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task ARepeatedGroupCreateAfterSuccessReplaysTheFirstAnswer(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var body = FileGroupsApi.GroupBody("Onboarding pack");
        using var first = await api.CreateGroupAsync(Callers.OnboardingSvc, "customer-1", body, "group-1");
        await first.ShouldHaveStatusAsync(HttpStatusCode.Created);

        using var again = await api.CreateGroupAsync(Callers.OnboardingSvc, "customer-1", body, "group-1");

        await again.ShouldHaveStatusAsync(HttpStatusCode.Created);
        (await again.Content.ReadAsStringAsync()).ShouldBe(await first.Content.ReadAsStringAsync());
        again.Headers.Location.ShouldBeNull();
        again.Headers.ETag.ShouldBeNull();
        (await api.GroupNamesAsync("customer-1")).ShouldBe(["Onboarding pack"]);
    }

    /// <summary>Scenario: A repeated group create while the first still runs is refused.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task ARepeatedGroupCreateWhileTheFirstStillRunsIsRefused(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var body = FileGroupsApi.GroupBody("Onboarding pack");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // "is creating": the first create has passed its checks and is held just before its save
        api.Host.BeforeSave.Run(async () =>
        {
            entered.SetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(30));
        });
        var first = api.CreateGroupAsync(Callers.OnboardingSvc, "customer-1", body, "group-2");
        if (await Task.WhenAny(entered.Task, first, Task.Delay(TimeSpan.FromSeconds(30))) != entered.Task)
        {
            release.TrySetResult();
            using var unheld = first.IsCompleted ? await first : null;
            throw new ShouldAssertException(unheld is null
                ? "the first create never reached its save"
                : $"the first create never reached its save: {await AnswerAssertions.DescribeAsync(unheld)}");
        }

        using (var second = await api.CreateGroupAsync(Callers.OnboardingSvc, "customer-1", body, "group-2"))
        {
            release.SetResult();
            await second.ShouldHaveStatusAsync(HttpStatusCode.Conflict);
        }

        using (var firstAnswer = await first)
        {
            await firstAnswer.ShouldHaveStatusAsync(HttpStatusCode.Created);
        }

        (await api.GroupNamesAsync("customer-1")).ShouldBe(["Onboarding pack"]);
    }

    /// <summary>Scenario: The same key for another owner is a new group.</summary>
    [Theory]
    [MemberData(nameof(Databases.Both), MemberType = typeof(Databases))]
    public async Task TheSameKeyForAnotherOwnerIsANewGroup(TestDatabase database)
    {
        var api = await hosts.OnAsync(database);
        var body = FileGroupsApi.GroupBody("Onboarding pack");
        using var first = await api.CreateGroupAsync(Callers.OnboardingSvc, "customer-1", body, "group-3");
        await first.ShouldHaveStatusAsync(HttpStatusCode.Created);

        using var response = await api.CreateGroupAsync(Callers.OnboardingSvc, "customer-2", body, "group-3");

        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
        (await response.JsonAsync()).GetProperty("groupId").GetGuid()
            .ShouldNotBe((await first.JsonAsync()).GetProperty("groupId").GetGuid());
        (await api.GroupNamesAsync("customer-2")).ShouldBe(["Onboarding pack"]);
    }

    public static IEnumerable<object[]> RepeatsAfterAnError => Databases.With(
        [10, "409"],
        [331, "201, the create runs again"]);

    /// <summary>Scenario Outline: A repeat after an error answer waits for the reservation to end.</summary>
    /// <remarks>
    /// "The database could not be reached" is the save failing with <see cref="RetryLimitExceededException"/>, what EF
    /// Core surfaces once its retries on an unreachable server run out; the database itself keeps working, so the repeat
    /// finds it again. The store stamps its records with the wall clock, so the repeat moves them back, never waits.
    /// </remarks>
    [Theory]
    [MemberData(nameof(RepeatsAfterAnError))]
    public async Task ARepeatAfterAnErrorAnswerWaitsForTheReservationToEnd(TestDatabase database, int seconds, string status)
    {
        var api = await hosts.OnAsync(database);
        var body = FileGroupsApi.GroupBody("Onboarding pack");
        api.Host.BeforeSave.Run(() =>
            throw new RetryLimitExceededException("simulated: the database cannot be reached"));
        using (var failed = await api.CreateGroupAsync(Callers.OnboardingSvc, "customer-1", body, "group-4"))
        {
            await failed.ShouldBeProblemDetailsAsync(HttpStatusCode.ServiceUnavailable);
        }

        api.Host.BeforeSave.Reset(); // the database works again
        await IdempotencyRecords.AgeAsync(api, seconds);

        using var repeat = await api.CreateGroupAsync(Callers.OnboardingSvc, "customer-1", body, "group-4");

        if (status == "409")
        {
            await repeat.ShouldHaveStatusAsync(HttpStatusCode.Conflict);
            (await api.GroupNamesAsync("customer-1")).ShouldBeEmpty();
        }
        else
        {
            await repeat.ShouldHaveStatusAsync(HttpStatusCode.Created);
            (await api.GroupNamesAsync("customer-1")).ShouldBe(["Onboarding pack"]);
        }
    }
}
