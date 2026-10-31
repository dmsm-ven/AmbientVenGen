namespace AmbientVenGen.Desktop.ViewModels;

/// <summary>Sent by the preset library when the user wants a recording as a layer of the project.</summary>
public sealed record UseFieldPresetMessage(string Path, string Name);
