using FocusKey.Foundation.Data;
using FocusKey.Foundation.Sessions;
using Microsoft.Data.Sqlite;

namespace FocusKey.Foundation.Tests.Sessions;

/// <summary>
/// An initialized session database in a throwaway directory, plus helpers for reaching the raw
/// storage that the repository sits on top of.
/// </summary>
internal sealed class SessionStore : IDisposable
{
    private readonly TempDirectory _temp = new();

    internal SessionStore()
    {
        DatabaseFile = Path.Combine(_temp.Path, "focus_key.db");
        Connections = new SqliteConnectionFactory(DatabaseFile);
        Initialization = new DatabaseBootstrapper(Connections).Initialize();
        Repository = new SqliteSessionRepository(Connections);
    }

    internal string DatabaseFile { get; }

    internal SqliteConnectionFactory Connections { get; }

    internal DatabaseInitializationResult Initialization { get; }

    internal SqliteSessionRepository Repository { get; }

    /// <summary>A repository on a brand-new factory, as a restarted application would have.</summary>
    internal SqliteSessionRepository ReopenRepository() =>
        new(new SqliteConnectionFactory(DatabaseFile));

    internal DatabaseInitializationResult Reinitialize() =>
        new DatabaseBootstrapper(new SqliteConnectionFactory(DatabaseFile)).Initialize();

    internal SqliteConnection OpenRawConnection() => Connections.OpenConnection();

    internal int ExecuteRaw(string sql, params (string Name, object Value)[] parameters)
    {
        using SqliteConnection connection = OpenRawConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;

        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return command.ExecuteNonQuery();
    }

    internal T? ScalarRaw<T>(string sql)
    {
        using SqliteConnection connection = OpenRawConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        object? value = command.ExecuteScalar();

        return value is null or DBNull ? default : (T)Convert.ChangeType(value, typeof(T));
    }

    public void Dispose()
    {
        // Release pooled file handles so the temporary directory can be removed.
        SqliteConnection.ClearAllPools();
        _temp.Dispose();
    }
}
