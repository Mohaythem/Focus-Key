using Microsoft.Data.Sqlite;

namespace FocusKey.Foundation.Data;

/// <summary>
/// Creates configured connections to the local Focus Key database.
/// Connection tuning lives here so no caller has to remember the pragmas.
/// </summary>
public sealed class SqliteConnectionFactory
{
    /// <summary>
    /// WAL: readers never block the writer, and the database survives an abrupt exit.
    /// NORMAL synchronous is the usual desktop trade-off alongside WAL.
    /// busy_timeout keeps a briefly locked database from failing instantly.
    /// </summary>
    private static readonly string[] Pragmas =
    [
        "PRAGMA journal_mode = WAL;",
        "PRAGMA synchronous = NORMAL;",
        "PRAGMA busy_timeout = 5000;",
    ];

    private readonly string _connectionString;

    /// <param name="databaseFile">Full path of the SQLite database file. Created on first use.</param>
    public SqliteConnectionFactory(string databaseFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseFile);

        DatabaseFile = Path.GetFullPath(databaseFile);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabaseFile,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            Pooling = true,
        }.ToString();
    }

    public string DatabaseFile { get; }

    /// <summary>Opens a connection and applies the standard Focus Key pragmas.</summary>
    public SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);

        try
        {
            connection.Open();

            foreach (string pragma in Pragmas)
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = pragma;
                command.ExecuteNonQuery();
            }

            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    /// <inheritdoc cref="OpenConnection"/>
    public async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection(_connectionString);

        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            foreach (string pragma in Pragmas)
            {
                await using SqliteCommand command = connection.CreateCommand();
                command.CommandText = pragma;
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
