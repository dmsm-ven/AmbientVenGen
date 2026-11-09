using CommunityToolkit.Mvvm.Messaging.Messages;

namespace AmbientVenGen.Desktop.ViewModels;

/// <summary>
/// Sent by the preset library when the user wants a recording in the project: as a new Field record layer,
/// or (<paramref name="ReplaceCurrent"/>) as the file of the selected Field record layer.
/// </summary>
public sealed record UseFieldPresetMessage(string Path, string Name, bool ReplaceCurrent = false, double DurationSeconds = 0);

/// <summary>Sent by the main window when the selected layer changes: the name of the selected Field record layer, or null.</summary>
public sealed record FieldRecordTargetMessage(string? LayerName);

/// <summary>Asked by the preset library when it opens: which Field record layer would get the file (the reply is its name, or null).</summary>
public sealed class FieldRecordTargetRequest : RequestMessage<string?>;
