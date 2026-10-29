namespace AmbientVenGen.Desktop.Presets;

public sealed record AmbientPreset(
    string Name,
    double DroneLevel,
    double AtmosphereLevel,
    double Brightness,
    double Modulation);
