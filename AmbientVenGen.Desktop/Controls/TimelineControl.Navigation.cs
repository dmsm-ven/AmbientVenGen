using AmbientVenGen.Core.Composition;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace AmbientVenGen.Desktop.Controls;

/// <summary>Wheel zoom / scroll and the keyboard.</summary>
public sealed partial class TimelineControl
{
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        if (Project is null)
            return;

        ModifierKeys modifiers = Keyboard.Modifiers;

        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            // Zoom around the mouse position.
            double total = Total;
            double minSpan = Math.Min(MinViewSeconds, total);
            double mouseX = Math.Max(e.GetPosition(this).X, HeaderWidth);
            double anchorTime = XToTime(mouseX);
            double fraction = (mouseX - HeaderWidth) / TrackWidth;
            double newSpan = Math.Clamp(_viewSpan * (e.Delta > 0 ? 0.8 : 1.25), minSpan, total);

            _fit = newSpan >= total - 1e-6;
            _viewSpan = newSpan;
            _viewStart = anchorTime - fraction * newSpan;

            ClampView();
            InvalidateContent();
            RenderPlayhead();
            e.Handled = true;
        }
        else if (modifiers.HasFlag(ModifierKeys.Shift) && !_fit)
        {
            _viewStart += (e.Delta > 0 ? -1 : 1) * _viewSpan * 0.1;

            ClampView();
            InvalidateContent();
            RenderPlayhead();
            e.Handled = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (Keyboard.Modifiers == ModifierKeys.Alt
            && (key == Key.Up || key == Key.Down)
            && Project is { } project
            && SelectedLayer is { } selected)
        {
            project.MoveLayer(selected, key == Key.Up ? -1 : 1);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete && DeleteCommand is { } command && command.CanExecute(null))
        {
            command.Execute(null);
            e.Handled = true;
        }
    }
}
