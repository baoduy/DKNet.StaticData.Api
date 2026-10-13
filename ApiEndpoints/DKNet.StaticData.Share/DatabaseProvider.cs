namespace DKNet.StaticData.Share;

/// <summary>
///     The database the service keeps its data in, chosen by the operator through
///     <see cref="SharedConsts.DatabaseProviderKey" />.
/// </summary>
public enum DatabaseProvider
{
    /// <summary>PostgreSQL, the default when no choice is set.</summary>
    Postgres,

    /// <summary>Microsoft SQL Server.</summary>
    SqlServer
}

/// <summary>
///     Reads the operator's database choice.
/// </summary>
public static class DatabaseProviders
{
    /// <summary>
    ///     Parses the value of <see cref="SharedConsts.DatabaseProviderKey" />.
    /// </summary>
    /// <param name="value">The configured value; null or blank means <see cref="DatabaseProvider.Postgres" />.</param>
    /// <returns>The chosen database.</returns>
    /// <exception cref="InvalidOperationException">The value names no supported database.</exception>
    public static DatabaseProvider Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DatabaseProvider.Postgres;
        }

        // Matched by name only: Enum.TryParse would also take "0" or "1", and would trim a padded value.
        foreach (var provider in Enum.GetValues<DatabaseProvider>())
        {
            if (string.Equals(provider.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                return provider;
            }
        }

        throw new InvalidOperationException(
            $"'{value}' is not a supported value for {SharedConsts.DatabaseProviderKey}. Use one of: " +
            $"{string.Join(", ", Enum.GetNames<DatabaseProvider>())}.");
    }
}
