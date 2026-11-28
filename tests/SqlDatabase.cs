using Clc.LinkShortener.Data;
using Dapper;
using Microsoft.Data.SqlClient;

namespace LinkShortener.Tests;

public sealed class SqlFactAttribute : FactAttribute
{
    internal const string ConnectionVariable = "LINK_SHORTENER_TEST_SQL";

    public SqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionVariable)))
        {
            Skip = $"Set {ConnectionVariable} to run against a disposable SQL Server test database.";
        }
    }
}

public sealed class SqlDatabase : IAsyncLifetime
{
    private readonly string databaseName = $"LinkShortenerNotesTests_{Guid.NewGuid():N}";
    private SqlConnectionStringBuilder master;
    private SqlConnectionStringBuilder testDatabase;
    private bool created;

    public LinkShortenerDb Helper => new(testDatabase);

    public async Task InitializeAsync()
    {
        var connection = Environment.GetEnvironmentVariable(SqlFactAttribute.ConnectionVariable);
        if (string.IsNullOrWhiteSpace(connection))
        {
            return;
        }

        master = new SqlConnectionStringBuilder(connection) { InitialCatalog = "master" };
        testDatabase = new SqlConnectionStringBuilder(connection) { InitialCatalog = databaseName, Pooling = false };

        try
        {
            await using var sql = new SqlConnection(master.ConnectionString);
            await sql.ExecuteAsync($"CREATE DATABASE [{databaseName}];");
            created = true;
            await ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "fixtures.sql")));
            await ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "verify-note-schema.sql")));
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        if (created)
        {
            await using var sql = new SqlConnection(master.ConnectionString);
            await sql.ExecuteAsync($"DROP DATABASE [{databaseName}];");
            created = false;
        }
    }

    public async Task ExecuteAsync(string command, object parameters = null)
    {
        await using var sql = new SqlConnection(testDatabase.ConnectionString);
        await sql.ExecuteAsync(command, parameters);
    }

    public async Task<T> ReadAsync<T>(string command, object parameters = null)
    {
        await using var sql = new SqlConnection(testDatabase.ConnectionString);
        return await sql.QuerySingleAsync<T>(command, parameters);
    }

    public Task SeedNoteAsync(string id, string text, int daysOld = 0) => ExecuteAsync("""
        INSERT INTO dbo.Notes (ID, Creator, CreationDate, NoteText)
        VALUES (@id, 'test.user', DATEADD(DAY, -@daysOld, GETDATE()), @text);
        """, new { id, text, daysOld });
}
