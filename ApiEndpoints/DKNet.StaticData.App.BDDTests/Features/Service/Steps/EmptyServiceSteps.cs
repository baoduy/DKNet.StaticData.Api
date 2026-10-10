using System.Text.RegularExpressions;

namespace DKNet.StaticData.App.BDDTests.Features.Service.Steps;

/// <summary>
/// Step bindings for EmptyService.feature (DRK-2198 slice 1). Every scenario drives the API's own start-up code
/// (<see cref="DatabaseSettingApiFactory"/>), never the shared InMemory host: @integration scenarios point it at a
/// real Postgres or SQL Server container (<see cref="TestDatabaseServer"/>), @unit scenarios at a server that does
/// not exist.
/// </summary>
[Binding]
public sealed class EmptyServiceSteps
{
    private const string PostgresProviderName = "Npgsql.EntityFrameworkCore.PostgreSQL";
    private const string SqlServerProviderName = "Microsoft.EntityFrameworkCore.SqlServer";
    private const string DatabaseCheckName = "CoreDbContext";

    // Nothing listens on port 1: a host pointed here can be composed, but any real database call fails fast.
    private const string UnreachablePostgres =
        "Host=127.0.0.1;Port=1;Database=AppDb;Username=postgres;Password=unused;Timeout=2";

    // D9: a stopped database must turn the status route Unhealthy within 30 s.
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(30);

    private string? _choice;
    private TestDatabase? _database;
    private TestDatabaseServer? _server;
    private TestDatabaseServer? _ownedServer;
    private string? _databaseName;
    private string? _connectionString;
    private bool _runDbMigrationWhenAppStart;
    private DatabaseSettingApiFactory? _host;
    private Exception? _startupError;
    private string[] _appliedBeforeStart = [];
    private HttpResponseMessage? _response;
    private string? _body;

    [AfterScenario]
    public async Task DisposeAsync()
    {
        _response?.Dispose();
        if (_host is not null)
        {
            await _host.DisposeAsync();
        }

        if (_ownedServer is not null)
        {
            await _ownedServer.DisposeAsync();
        }
    }

    #region Given

    [Given(@"^platform-ops (leaves out|leaves blank) the database choice$")]
    public void GivenPlatformOpsLeavesTheDatabaseChoice(string how)
    {
        // An empty environment variable cannot be set in-process (.NET deletes it), so blank is whitespace;
        // the empty string is pinned by DatabaseProvidersTests (D2).
        _choice = how == "leaves out" ? null : "   ";
        _database = TestDatabase.Postgres;
    }

    [Given(@"^platform-ops sets the database choice to ""([^""]*)""$")]
    public void GivenPlatformOpsSetsTheDatabaseChoiceTo(string choice)
    {
        _choice = choice;

        // Which server the test provides — "MySql" gets none: the service must stop before it needs one.
        _database = choice.ToUpperInvariant() switch
        {
            "POSTGRES" => TestDatabase.Postgres,
            "SQLSERVER" => TestDatabase.SqlServer,
            _ => null
        };
    }

    [Given(@"^an empty (Postgres|SQL Server) database$")]
    public async Task GivenAnEmptyDatabase(string database) => await UseNewDatabaseAsync(database);

    [Given(@"^migration at start is turned on$")]
    public void GivenMigrationAtStartIsTurnedOn() => _runDbMigrationWhenAppStart = true;

    [Given(@"^a (Postgres|SQL Server) database that the service already brought up to date$")]
    public async Task GivenADatabaseThatTheServiceAlreadyBroughtUpToDate(string database)
    {
        await UseNewDatabaseAsync(database);
        _runDbMigrationWhenAppStart = true;
        await StartHostAsync();
        _appliedBeforeStart = await ReadAppliedMigrationsAsync();
        _appliedBeforeStart.ShouldNotBeEmpty("the first start applied no migration");

        await _host!.DisposeAsync();
        _host = null;
    }

    [Given(@"^the service runs on a healthy (Postgres|SQL Server) database with token checking on$")]
    [Given(@"^the service runs on (Postgres|SQL Server) with token checking on$")]
    public async Task GivenTheServiceRunsOnWithTokenCheckingOn(string database)
    {
        await UseNewDatabaseAsync(database);
        _runDbMigrationWhenAppStart = true;
        await StartHostAsync();
        RequireHost();
    }

    /// <summary>The server is the scenario's own: the next step stops it, which must not touch the shared one.</summary>
    [Given(@"^the service runs on (Postgres|SQL Server)$")]
    public async Task GivenTheServiceRunsOn(string database)
    {
        _ownedServer = await TestDatabaseServer.StartAsync(ToTestDatabase(database));
        _server = _ownedServer;
        _choice ??= _server.Database == TestDatabase.SqlServer ? "SqlServer" : null;
        (_databaseName, _connectionString) = await _server.CreateEmptyDatabaseAsync();
        _runDbMigrationWhenAppStart = true;
        await StartHostAsync();
        RequireHost();
    }

    [Given(@"^the database server is stopped$")]
    public async Task GivenTheDatabaseServerIsStopped() => await _ownedServer!.StopAsync();

    /// <summary>
    /// Nothing provides Redis or a message bus: <see cref="DatabaseSettingApiFactory"/> clears both connection
    /// strings and keeps every hosted service, so a start-up dependency on either would show.
    /// </summary>
    [Given(@"^a (Postgres|SQL Server) database and no Redis or message bus$")]
    public async Task GivenADatabaseAndNoRedisOrMessageBus(string database)
    {
        await UseNewDatabaseAsync(database);
        _runDbMigrationWhenAppStart = true;
    }

    #endregion

    #region When

    [When(@"^the service starts$")]
    [When(@"^the service starts again$")]
    public async Task WhenTheServiceStarts()
    {
        if (_connectionString is null)
        {
            if (_database is { } database)
            {
                await UseNewDatabaseAsync(database);
            }
            else
            {
                _connectionString = UnreachablePostgres;
            }
        }

        await StartHostAsync();
    }

    [When(@"^the cluster readiness probe calls the health status route without a token$")]
    [When(@"^the cluster readiness probe calls the health status route$")]
    public async Task WhenTheClusterReadinessProbeCallsTheHealthStatusRoute() => await GetAsync("/healthz");

    [When(@"^platform-ops calls the health detail route without a token$")]
    public async Task WhenPlatformOpsCallsTheHealthDetailRouteWithoutAToken() => await GetAsync("/healthz/detail");

    [When(@"^onboarding-service calls the root address without a token$")]
    public async Task WhenOnboardingServiceCallsTheRootAddressWithoutAToken() => await GetAsync("/");

    #endregion

    #region Then

    [Then(@"^the service keeps its data in (Postgres|SQL Server)$")]
    public async Task ThenTheServiceKeepsItsDataIn(string database)
    {
        using var scope = RequireHost().Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>().Database;

        db.ProviderName.ShouldBe(database == "Postgres" ? PostgresProviderName : SqlServerProviderName);
        db.GetDbConnection().Database.ShouldBe(_databaseName);
        (await db.CanConnectAsync()).ShouldBeTrue($"the service cannot reach its {database} database");
    }

    [Then(@"^the service stops before it serves any request$")]
    public void ThenTheServiceStopsBeforeItServesAnyRequest()
    {
        _host.ShouldBeNull("the host was built and would have served requests");
        StartupInvalidOperation().ShouldNotBeNull($"start-up failed with something else: {_startupError}");
    }

    /// <summary>The exact text of spec DRK-2198 §5, unknown-value error.</summary>
    [Then(@"^the message names ""([^""]+)"" and ""([^""]+)"" as the allowed choices$")]
    public void ThenTheMessageNamesAsTheAllowedChoices(string first, string second) =>
        StartupInvalidOperation()!.Message.ShouldBe(
            $"'{_choice}' is not a supported value for Database:Provider. Use one of: {first}, {second}.");

    [Then(@"^the database is up to date$")]
    public async Task ThenTheDatabaseIsUpToDate()
    {
        using var scope = RequireHost().Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>().Database;

        (await db.GetAppliedMigrationsAsync()).ShouldNotBeEmpty("the start applied no migration");
        (await db.GetPendingMigrationsAsync()).ShouldBeEmpty();
    }

    [Then(@"^the database holds no product, purchase order or membership number sequence$")]
    public async Task ThenTheDatabaseHoldsNoProductPurchaseOrderOrMembershipNumberSequence()
    {
        var tables = await _server!.ListTablesAsync(_connectionString!);
        var sequences = await _server.ListSequencesAsync(_connectionString!);

        tables.ShouldNotBeEmpty("the migration created no table, not even its history");
        tables.Where(t => Regex.IsMatch(t, "product|purchase_?order", RegexOptions.IgnoreCase)).ShouldBeEmpty();
        sequences.Where(s => Regex.IsMatch(s, "membership", RegexOptions.IgnoreCase)).ShouldBeEmpty();
    }

    [Then(@"^no migration is applied$")]
    public async Task ThenNoMigrationIsApplied()
    {
        (await ReadAppliedMigrationsAsync()).ShouldBe(_appliedBeforeStart);

        using var scope = RequireHost().Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<CoreDbContext>().Database.GetPendingMigrationsAsync())
            .ShouldBeEmpty();
    }

    [Then(@"^the answer is ""(\w+)"" with HTTP (\d+)$")]
    public void ThenTheAnswerIsWithHttp(string status, int statusCode)
    {
        ((int)_response!.StatusCode).ShouldBe(statusCode);
        ReadBody().GetProperty("status").GetString().ShouldBe(status);
    }

    /// <summary>R3: the status route's whole answer is the status — no check name, duration or failure text.</summary>
    [Then(@"^the answer names no check$")]
    [Then(@"^the answer names no check and shows no failure text$")]
    public void ThenTheAnswerNamesNoCheck() =>
        ReadBody().EnumerateObject().Select(p => p.Name).ShouldBe(["status"]);

    [Then(@"^the report lists exactly 1 check, the database, as ""(\w+)""$")]
    public void ThenTheReportListsExactly1CheckTheDatabaseAs(string status)
    {
        _response!.StatusCode.ShouldBe(HttpStatusCode.OK);
        DatabaseCheck().GetProperty("status").GetString().ShouldBe(status);
    }

    [Then(@"^the report shows the check's duration$")]
    public void ThenTheReportShowsTheChecksDuration() =>
        TimeSpan.TryParse(DatabaseCheck().GetProperty("duration").GetString(), out _).ShouldBeTrue();

    [Then(@"^the report shows the database check as ""(\w+)""$")]
    public void ThenTheReportShowsTheDatabaseCheckAs(string status) =>
        DatabaseCheck().GetProperty("status").GetString().ShouldBe(status);

    [Then(@"^the report shows the check's failure message$")]
    public void ThenTheReportShowsTheChecksFailureMessage()
    {
        DatabaseCheck().TryGetProperty("exception", out var exception).ShouldBeTrue($"no failure message in {_body}");
        exception.GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Then(@"^the health status route answers ""(\w+)""$")]
    public async Task ThenTheHealthStatusRouteAnswers(string status)
    {
        await GetAsync("/healthz");
        ThenTheAnswerIsWithHttp(status, 200);
    }

    [Then(@"^the service refuses the call as unauthenticated$")]
    public void ThenTheServiceRefusesTheCallAsUnauthenticated() =>
        _response!.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

    [Then(@"^the answer carries no health status$")]
    public void ThenTheAnswerCarriesNoHealthStatus() =>
        Regex.IsMatch(_body!, "Healthy|Degraded|Unhealthy", RegexOptions.IgnoreCase)
            .ShouldBeFalse($"the answer carries a health status: {_body}");

    #endregion

    #region Helpers

    private static TestDatabase ToTestDatabase(string database) =>
        database == "Postgres" ? TestDatabase.Postgres : TestDatabase.SqlServer;

    private Task UseNewDatabaseAsync(string database) => UseNewDatabaseAsync(ToTestDatabase(database));

    private async Task UseNewDatabaseAsync(TestDatabase database)
    {
        _database = database;
        _choice ??= database == TestDatabase.SqlServer ? "SqlServer" : null;
        _server = await TestDatabaseServer.SharedAsync(database);
        (_databaseName, _connectionString) = await _server.CreateEmptyDatabaseAsync();
    }

    private async Task StartHostAsync() =>
        (_host, _startupError) = await DatabaseSettingApiFactory.StartAsync(
            _choice, _connectionString!, _runDbMigrationWhenAppStart);

    private DatabaseSettingApiFactory RequireHost() =>
        _host ?? throw new InvalidOperationException($"The service did not start: {_startupError}");

    private async Task GetAsync(string path)
    {
        using var client = RequireHost().CreateClient();
        client.Timeout = ProbeTimeout;
        _response = await client.GetAsync(path);
        _body = await _response.Content.ReadAsStringAsync();
    }

    private JsonElement ReadBody()
    {
        _body.ShouldNotBeNullOrWhiteSpace($"HTTP {(int)_response!.StatusCode} with an empty body");
        return JsonDocument.Parse(_body).RootElement;
    }

    /// <summary>The report's only entry, which must be the database check.</summary>
    private JsonElement DatabaseCheck()
    {
        var entries = ReadBody().GetProperty("entries");
        entries.EnumerateObject().Select(p => p.Name).ShouldBe([DatabaseCheckName], $"report: {_body}");
        return entries.GetProperty(DatabaseCheckName);
    }

    private async Task<string[]> ReadAppliedMigrationsAsync()
    {
        using var scope = RequireHost().Services.CreateScope();
        return [.. await scope.ServiceProvider.GetRequiredService<CoreDbContext>().Database.GetAppliedMigrationsAsync()];
    }

    private InvalidOperationException? StartupInvalidOperation() =>
        Chain(_startupError).OfType<InvalidOperationException>().FirstOrDefault();

    private static IEnumerable<Exception> Chain(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            yield return current;
        }
    }

    #endregion
}
