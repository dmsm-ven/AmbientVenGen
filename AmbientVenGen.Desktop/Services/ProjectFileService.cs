using AmbientVenGen.Desktop.Composition;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AmbientVenGen.Desktop.Services;

/// <summary>Saves and loads projects as readable JSON (.afproj).</summary>
public sealed class ProjectFileService
{
    public const string Extension = ".afproj";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public void Save(string path, ProjectSpec spec)
    {
        string json = JsonSerializer.Serialize(spec, Options);
        File.WriteAllText(path, json);
    }

    public ProjectSpec Load(string path)
    {
        string json = File.ReadAllText(path);
        ProjectSpec? spec = JsonSerializer.Deserialize<ProjectSpec>(json, Options);

        if (spec is null || spec.Layers is null)
            throw new InvalidDataException("The file does not contain a valid project.");

        if (spec.Version > ProjectSpec.CurrentVersion)
            throw new InvalidDataException("The project was created by a newer version of the program.");

        return spec;
    }
}
