using AmbientVenGen.Core.Composition;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AmbientVenGen.Core.Projects;

/// <summary>The content of a layer file (.aflay): the same kind of readable JSON as a project, with one layer in it.</summary>
public sealed record LayerFileSpec(int Version, LayerSpec Layer);

/// <summary>Saves a single layer to its own file and reads it back (the file is never changed by loading it).</summary>
public sealed class LayerFileService
{
    public const string Extension = ".aflay";

    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public void Save(string path, LayerSpec layer) =>
        File.WriteAllText(path, JsonSerializer.Serialize(new LayerFileSpec(CurrentVersion, layer), Options));

    public LayerSpec Load(string path)
    {
        LayerFileSpec? file = JsonSerializer.Deserialize<LayerFileSpec>(File.ReadAllText(path), Options);

        if (file?.Layer is null)
            throw new InvalidDataException("The file does not contain a valid layer.");

        if (file.Version > CurrentVersion)
            throw new InvalidDataException("The layer was saved by a newer version of the program.");

        return file.Layer;
    }
}
