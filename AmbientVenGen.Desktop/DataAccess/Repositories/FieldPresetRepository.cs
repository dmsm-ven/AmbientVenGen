using AmbientVenGen.Desktop.DataAccess.Models;
using Dapper;
using Microsoft.Data.Sqlite;
using System.IO;

namespace AmbientVenGen.Desktop.DataAccess.Repositories;

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
            "SELECT Id, FilePath, AddedAtUnix, DurationSeconds, SizeBytes, ViewsCount " +
            "FROM FieldPresets ORDER BY Id DESC");

        return presets.ToList();
    }

    /// <summary>Adds the preset and returns its new id.</summary>
    public async Task<long> AddAsync(FieldPreset preset)
    {
        await using SqliteConnection connection = await OpenAsync();

        return await connection.ExecuteScalarAsync<long>(
            """
            INSERT INTO FieldPresets (FilePath, AddedAtUnix, DurationSeconds, SizeBytes)
            VALUES (@FilePath, @AddedAtUnix, @DurationSeconds, @SizeBytes);
            SELECT last_insert_rowid();
            """,
            preset);
    }

    /// <summary>Counts one more use of the preset.</summary>
    public async Task IncrementViewsAsync(long id)
    {
        await using SqliteConnection connection = await OpenAsync();

        await connection.ExecuteAsync("UPDATE FieldPresets SET ViewsCount = ViewsCount + 1 WHERE Id = @Id", new { Id = id });
    }

    public async Task DeleteAsync(long id)
    {
        await using SqliteConnection connection = await OpenAsync();

        await connection.ExecuteAsync("DELETE FROM PresetTags WHERE PresetId = @Id", new { Id = id });
        await connection.ExecuteAsync("DELETE FROM FieldPresets WHERE Id = @Id", new { Id = id });
        await RemoveUnusedTagsAsync(connection);
    }

    /// <summary>The unique list of tags (for the filter), straight from the Tags table.</summary>
    public async Task<IReadOnlyList<string>> GetUniqueTagsAsync()
    {
        await using SqliteConnection connection = await OpenAsync();

        IEnumerable<string> names = await connection.QueryAsync<string>("SELECT Name FROM Tags ORDER BY Name");
        return names.ToList();
    }

    // A tag that no preset has any more disappears from the dictionary (and from the filter).
    private static Task RemoveUnusedTagsAsync(SqliteConnection connection) =>
        connection.ExecuteAsync("DELETE FROM Tags WHERE Id NOT IN (SELECT TagId FROM PresetTags)");

    /// <summary>Every tag of every preset.</summary>
    public async Task<IReadOnlyList<PresetTagRow>> GetAllTagsAsync()
    {
        await using SqliteConnection connection = await OpenAsync();

        IEnumerable<PresetTagRow> rows = await connection.QueryAsync<PresetTagRow>(
            """
            SELECT pt.PresetId, t.Name AS Tag
            FROM PresetTags pt
            JOIN Tags t ON t.Id = pt.TagId
            ORDER BY t.Name
            """);

        return rows.ToList();
    }

    /// <summary>Puts the tag on the preset (nothing happens if it is already there). The tag is created if it is new.</summary>
    public async Task AddTagAsync(long presetId, string tag)
    {
        await using SqliteConnection connection = await OpenAsync();

        await connection.ExecuteAsync(
            """
            INSERT OR IGNORE INTO Tags (Name) VALUES (@Tag);
            INSERT OR IGNORE INTO PresetTags (PresetId, TagId)
            SELECT @PresetId, Id FROM Tags WHERE Name = @Tag;
            """,
            new { PresetId = presetId, Tag = tag });
    }

    public async Task RemoveTagAsync(long presetId, string tag)
    {
        await using SqliteConnection connection = await OpenAsync();

        await connection.ExecuteAsync(
            "DELETE FROM PresetTags WHERE PresetId = @PresetId AND TagId = (SELECT Id FROM Tags WHERE Name = @Tag)",
            new { PresetId = presetId, Tag = tag });

        await RemoveUnusedTagsAsync(connection);
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

        // The schema of the current version. A database made by an older version must be converted first
        // with Database/Migrations/001_normalize_presets.sql (Name and Extension columns, tags in their own table).
        await connection.ExecuteAsync(
            """
            CREATE TABLE IF NOT EXISTS FieldPresets (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                FilePath        TEXT    NOT NULL,
                AddedAtUnix     INTEGER NOT NULL,
                DurationSeconds REAL    NOT NULL,
                SizeBytes       INTEGER NOT NULL,
                ViewsCount      INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS Tags (
                Id   INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT    NOT NULL UNIQUE COLLATE NOCASE
            );

            CREATE TABLE IF NOT EXISTS PresetTags (
                PresetId INTEGER NOT NULL REFERENCES FieldPresets (Id) ON DELETE CASCADE,
                TagId    INTEGER NOT NULL REFERENCES Tags (Id) ON DELETE CASCADE,
                PRIMARY KEY (PresetId, TagId)
            );

            CREATE INDEX IF NOT EXISTS IX_PresetTags_TagId ON PresetTags (TagId);
            """);
    }
}
