namespace AmbientVenGen.Desktop.Models;

public sealed record CompositionSettings(
    float DroneLevel,
    float AtmosphereLevel,
    float Brightness,
    float Modulation,
    int Seed);