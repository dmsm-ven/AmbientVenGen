using AmbientVenGen.Desktop.Models;
using Dapper;
using Microsoft.Data.Sqlite;
using System.IO;

namespace AmbientVenGen.Desktop.DataAccess;

/// <summary>SQLite storage (through Dapper) for the field-preset library.</summary>
public sealed class FieldPresetRepository
{
    private readonly string _connectionString;
    private readonly Lazy<Task> _initialization;

    public FieldPresetRepository()
    {
        // An absolute path in the user profile. A relative "Data Source=presets.db" depends on the
        // current directory, which a file dialog changes, so records could end up in another file.
        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AmbientForge");

        Directory.CreateDirectory(directory);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(directory, "presets.db")
        }.ToString();

        // Created on first use, so a database problem surfaces where it can be reported to the user.
        _initialization = new Lazy<Task>(InitializeAsync);
    }

    public async Task<IReadOnlyList<FieldPreset>> GetAllAsync()
    {
        await using SqliteConnection connection = await OpenAsync();

        IEnumerable<FieldPreset> presets = await connection.QueryAsync<FieldPreset>(
            "SELECT Id, Name, FilePath, AddedAtUnix, DurationSeconds, SizeBytes, Extension " +
            "FROM FieldPresets ORDER BY Id");

        return presets.ToList();
    }

    /// <summary>Adds the preset and returns its new id.</summary>
    public async Task<long> AddAsync(FieldPreset preset)
    {
        await using SqliteConnection connection = await OpenAsync();

        return await connection.ExecuteScalarAsync<long>(
            """
            INSERT INTO FieldPresets (Name, FilePath, AddedAtUnix, DurationSeconds, SizeBytes, Extension)
            VALUES (@Name, @FilePath, @AddedAtUnix, @DurationSeconds, @SizeBytes, @Extension);
            SELECT last_insert_rowid();
            """,
            preset);
    }

    public async Task DeleteAsync(long id)
    {
        await using SqliteConnection connection = await OpenAsync();

        await connection.ExecuteAsync("DELETE FROM FieldPresets WHERE Id = @Id", new { Id = id });
    }

    private async Task<SqliteConnection> OpenAsync()
    {
        await _initialization.Value;

        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        return connection;
    }

    private async Task InitializeAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await connection.ExecuteAsync(
            """
            CREATE TABLE IF NOT EXISTS FieldPresets (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                Name            TEXT    NOT NULL,
                FilePath        TEXT    NOT NULL,
                AddedAtUnix     INTEGER NOT NULL,
                DurationSeconds REAL    NOT NULL,
                SizeBytes       INTEGER NOT NULL,
                Extension       TEXT    NOT NULL
            );
            """);
    }
}
