using AmbientVenGen.Core.Composition;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace AmbientVenGen.Desktop.Controls;

/// <summary>The time ruler and its tick steps.</summary>
public sealed partial class TimelineControl
{
    private void DrawRuler(DrawingContext dc, double width)
    {
        dc.DrawRectangle(BrushOf(Color.FromArgb(0x58, 0x0C, 0x0C, 0x12)), null, new Rect(0, 0, width, RulerHeight));

        dc.DrawLine(
            new Pen(BrushOf(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), 1),
            new Point(0, RulerHeight - 0.5),
            new Point(width, RulerHeight - 0.5));

        FormattedText caption = MakeText("LAYERS", 10, Color.FromArgb(0x80, 0xC9, 0xC8, 0xD6), bold: true);
        dc.DrawText(caption, new Point(20, (RulerHeight - caption.Height) / 2.0));

        GetTicks(out double major, out double minor, out int divisions);

        var majorPen = new Pen(BrushOf(Color.FromArgb(0x90, 0xC9, 0xC8, 0xD6)), 1);
        var minorPen = new Pen(BrushOf(Color.FromArgb(0x48, 0xC9, 0xC8, 0xD6)), 1);

        dc.PushClip(new RectangleGeometry(new Rect(HeaderWidth, 0, width - HeaderWidth, RulerHeight)));

        long first = (long)Math.Ceiling(_viewStart / minor);
        long last = (long)Math.Floor((_viewStart + _viewSpan) / minor);

        for (long i = first; i <= last; i++)
        {
            double time = i * minor;
            double x = Math.Round(TimeToX(time)) + 0.5;
            bool isMajor = i % divisions == 0;

            if (isMajor)
            {
                dc.DrawLine(majorPen, new Point(x, RulerHeight - 11), new Point(x, RulerHeight - 1));

                FormattedText label = MakeText(FormatTime(time), 10.5, Color.FromArgb(0xB8, 0xC9, 0xC8, 0xD6));
                dc.DrawText(label, new Point(x + 4, 5));
            }
            else if (minor * PixelsPerSecond >= 8)
            {
                dc.DrawLine(minorPen, new Point(x, RulerHeight - 6), new Point(x, RulerHeight - 1));
            }
        }

        dc.Pop();
    }

    // ---------------------------------------------------------------- ruler helpers

    private void GetTicks(out double major, out double minor, out int divisions)
    {
        int index = TickSteps.Length - 1;

        for (int i = 0; i < TickSteps.Length; i++)
        {
            if (TickSteps[i] * PixelsPerSecond >= 90)
            {
                index = i;
                break;
            }
        }

        major = TickSteps[index];
        divisions = TickDivisions[index];
        minor = major / divisions;
    }

    private static string FormatTime(double seconds)
    {
        int total = (int)Math.Round(seconds);

        return total >= 3600
            ? $"{total / 3600}:{total % 3600 / 60:00}:{total % 60:00}"
            : $"{total / 60}:{total % 60:00}";
    }
}
