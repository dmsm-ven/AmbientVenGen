# Ambient Forge

Desktop generator for **dark / space ambient** music. You build a track from layers on a timeline,
shape how each layer changes over time, listen to it live and export it to WAV or MP3.

<table>
  <tr>
    <th width="50%">MainWindow</th>
    <th width="50%">SettingsWindow</th>
  </tr>
  <tr>
    <td align="center">
      <img src="docs/screenshots/main-window.png" alt="Main window" style="max-height: 600px; width: auto; max-width: 100%; object-fit: contain;">
    </td>
    <td align="center">
      <img src="docs/screenshots/preset-window.png" alt="Preset library" style="max-height: 600px; width: auto; max-width: 100%; object-fit: contain;">
    </td>
  </tr>
</table>

## What it does

- **Layers on a timeline** - drag, resize, fade in/out, reorder, mute. Types:
  - *Drone* - low sustained tones,
  - *Noise* - filtered noise textures (wind, dust),
  - *Pad* - a soft harmonic "choir",
  - *Field record* - your own recording (wav, mp3, flac...), looped and slowed down or sped up.
- **Parameters that move** - Level, Brightness and Modulation go *from -> to* over the length of a layer,
  along a chosen curve (Linear, Sine, Ease in/out, Exponential).
- **Master section** - reverb, soft limiter, spectrum and level meters, clip indicator.
- **Preset library** - a catalogue of your recordings with waveform preview, play/loop, tags and tag filter.
- **Templates and projects** - ready-made compositions, projects saved as readable `.afproj` (JSON).
- **Export** - WAV (16/32-bit) or MP3
- **Controls** - sliders (default) or gauge knobs, optional "?" hints (see *Settings*).

## Run

Requires Windows and the .NET 10 SDK.

```
dotnet run --project AmbientVenGen.Desktop
```

Keys: `Space` play/stop, `Ctrl+N/O/S` new/open/save, `Ctrl+E` export, `Del` remove layer,
`Alt+Up/Down` move layer.

## Structure

| Project | Contents |
| --- | --- |
| `AmbientVenGen.Core` | Layers, project model, audio engine (generators, mixer, reverb), export, project files. No UI. |
| `AmbientVenGen.Desktop` | WPF app (WPF-UI, MVVM): timeline, inspector, preset library (SQLite), settings. |
| `AmbientVenGen.Core.Tests` | xUnit tests of the engine and the model (curves, fades, repeats, project files, mixer, pitch relations). Run: `dotnet test`. |

Built with .NET 10, WPF-UI, NAudio and CommunityToolkit.Mvvm.
