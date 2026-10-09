using DKNet.StaticData.Share;

namespace DKNet.StaticData.App.Tests.Unit.Share;

/// <summary>
/// DRK-2198 §6a rows D1-D7: the operator's database choice. Expected values and the error text are the literals of
/// spec DRK-2198 §3 and §5.
/// </summary>
public class DatabaseProvidersTests
{
    [Theory]
    [InlineData(null)] // D1: key absent
    [InlineData("")] // D2
    [InlineData("   ")] // D2
    public void Parse_NoChoice_IsPostgres(string? value) =>
        DatabaseProviders.Parse(value).ShouldBe(DatabaseProvider.Postgres);

    [Theory]
    [InlineData("Postgres", DatabaseProvider.Postgres)] // D3
    [InlineData("postgres", DatabaseProvider.Postgres)] // D3
    [InlineData("SqlServer", DatabaseProvider.SqlServer)] // D4
    [InlineData("sqlserver", DatabaseProvider.SqlServer)] // D4
    public void Parse_SupportedChoiceInAnyCase_IsThatDatabase(string value, DatabaseProvider expected) =>
        DatabaseProviders.Parse(value).ShouldBe(expected);

    [Theory]
    [InlineData("MySql", "'MySql' is not a supported value for Database:Provider. Use one of: Postgres, SqlServer.")] // D5
    [InlineData("0", "'0' is not a supported value for Database:Provider. Use one of: Postgres, SqlServer.")] // D6
    [InlineData("1", "'1' is not a supported value for Database:Provider. Use one of: Postgres, SqlServer.")] // D6
    [InlineData(" SqlServer ", "' SqlServer ' is not a supported value for Database:Provider. Use one of: Postgres, SqlServer.")] // D7
    public void Parse_AnyOtherValue_ThrowsNamingTheAllowedChoices(string value, string message) =>
        Should.Throw<InvalidOperationException>(() => DatabaseProviders.Parse(value)).Message.ShouldBe(message);

    [Fact]
    public void DatabaseProviderKey_IsTheAccountsApiSettingName() =>
        SharedConsts.DatabaseProviderKey.ShouldBe("Database:Provider");
}
