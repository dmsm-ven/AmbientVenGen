using System.Globalization;

namespace AmbientVenGen.Desktop.Controls;

/// <summary>
/// Showing and typing numbers for the knobs and sliders, in one place: a format string such as
/// "{0:0.0} Hz" or "{0:P0}" (a percent format means that the value is stored as 0..1 and typed as 0..100).
/// </summary>
internal static class NumberText
{
    public static bool IsPercent(string format) => format.Contains(":P", StringComparison.OrdinalIgnoreCase);

    /// <summary>The value as shown on the control; a broken format falls back to a plain number.</summary>
    public static string Format(string format, double value)
    {
        try
        {
            return string.Format(CultureInfo.CurrentCulture, format, value);
        }
        catch (FormatException)
        {
            return value.ToString("0.##", CultureInfo.CurrentCulture);
        }
    }

    /// <summary>The value as it is offered for editing (percent as 0..100).</summary>
    public static string EditText(string format, double value) =>
        (IsPercent(format) ? value * 100.0 : value).ToString("0.##", CultureInfo.CurrentCulture);

    /// <summary>
    /// Reads what was typed: "82,4", "82.4", a trailing "Hz", "x", "%" and "s" are accepted;
    /// for a percent format the number is typed as percent.
    /// </summary>
    public static bool TryParse(string? text, string format, out double value)
    {
        value = 0;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        string cleaned = text.Trim();
        foreach (string suffix in new[] { "Hz", "x", "X", "×", "%", "s", "S" })
        {
            if (cleaned.EndsWith(suffix, StringComparison.Ordinal))
            {
                cleaned = cleaned[..^suffix.Length].Trim();
                break;
            }
        }

        bool parsed = double.TryParse(cleaned, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
            || double.TryParse(cleaned.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

        if (parsed && IsPercent(format))
            value /= 100.0;

        return parsed;
    }

    /// <summary>Rounds to the step (when there is one) and keeps the value inside the range.</summary>
    public static double Snap(double value, double step, double minimum, double maximum)
    {
        if (step > 0)
            value = Math.Round(value / step) * step;

        return Math.Clamp(Math.Round(value, 4), minimum, Math.Max(maximum, minimum));
    }
}
