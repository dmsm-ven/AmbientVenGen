namespace AmbientVenGen.Desktop.DataAccess.Models;

/// <summary>One tag put on one preset (a row of the PresetTags table).</summary>
public sealed class PresetTagRow
{
    public long PresetId { get; set; }
    public string Tag { get; set; } = string.Empty;
}
