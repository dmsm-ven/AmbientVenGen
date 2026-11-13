using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AmbientVenGen.Desktop.Controls;

/// <summary>
/// A time field "hh:mm:ss" (value in seconds) with up/down arrows. Typing is committed on Enter or when
/// the field loses focus; text that cannot be read is replaced by the last good value.
/// </summary>
public partial class TimeBox : UserControl
{
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header), typeof(string), typeof(TimeBox),
        new PropertyMetadata(string.Empty, OnPropertyChanged));

    public static readonly DependencyProperty HintProperty = DependencyProperty.Register(
        nameof(Hint), typeof(string), typeof(TimeBox),
        new PropertyMetadata(string.Empty, OnPropertyChanged));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(TimeBox),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnPropertyChanged));

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(TimeBox),
        new PropertyMetadata(0.0, OnPropertyChanged));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(TimeBox),
        new PropertyMetadata(3600.0, OnPropertyChanged));

    /// <summary>The caption stands to the left of the time (one line) instead of above it.</summary>
    public static readonly DependencyProperty IsInlineProperty = DependencyProperty.Register(
        nameof(IsInline), typeof(bool), typeof(TimeBox),
        new PropertyMetadata(false, OnPropertyChanged));

    /// <summary>A whole number (for example the number of repeats) instead of hh:mm:ss.</summary>
    public static readonly DependencyProperty IsIntegerProperty = DependencyProperty.Register(
        nameof(IsInteger), typeof(bool), typeof(TimeBox),
        new PropertyMetadata(false, OnPropertyChanged));

    public bool IsInteger
    {
        get => (bool)GetValue(IsIntegerProperty);
        set => SetValue(IsIntegerProperty, value);
    }

    public bool IsInline
    {
        get => (bool)GetValue(IsInlineProperty);
        set => SetValue(IsInlineProperty, value);
    }

    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public string Hint
    {
        get => (string)GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public TimeBox()
    {
        InitializeComponent();
        Sync();

        // A box created for a new layer must show the value it was bound to at once.
        Loaded += (_, _) => Sync();
        DataContextChanged += (_, _) => Dispatcher.BeginInvoke(new Action(Sync), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private static void OnPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((TimeBox)d).Sync();

    private void Sync()
    {
        if (Input is null)
            return;

        ApplyLayout();

        HeaderText.Text = Header;
        HeaderHint.Text = Hint;
        Input.Text = Text(Value);
        DisplayText.Text = Text(Value);
    }

    // Inline: "Start [00:00:00]" on one line. The label and the editor have the same width, so nothing moves when editing starts.
    private void ApplyLayout()
    {
        bool inline = IsInline;

        RootPanel.Orientation = inline ? Orientation.Horizontal : Orientation.Vertical;
        HeaderRow.VerticalAlignment = inline ? VerticalAlignment.Center : VerticalAlignment.Top;
        HeaderRow.Margin = inline ? new Thickness(0, 0, 8, 0) : new Thickness(0);

        Thickness boxMargin = inline ? new Thickness(0) : new Thickness(0, 3, 0, 0);
        DisplayBox.Margin = boxMargin;
        EditGrid.Margin = boxMargin;

        double width = IsInteger ? 68 : 116;
        DisplayBox.Width = inline ? width : double.NaN;
        EditGrid.Width = inline ? width : double.NaN;
        Input.MaxLength = IsInteger ? 3 : 12;
    }

    private void OnDisplayClick(object sender, MouseButtonEventArgs e)
    {
        DisplayBox.Visibility = Visibility.Collapsed;
        EditGrid.Visibility = Visibility.Visible;
        Input.Focus();
        Input.SelectAll();
        e.Handled = true;
    }

    // Back to the plain label.
    private void EndEditing()
    {
        EditGrid.Visibility = Visibility.Collapsed;
        DisplayBox.Visibility = Visibility.Visible;
    }

    private string Text(double value) => IsInteger
        ? ((long)Math.Round(value)).ToString(CultureInfo.InvariantCulture)
        : Format(value);

    private bool TryRead(string? text, out double value)
    {
        if (!IsInteger)
            return TryParse(text, out value);

        value = 0;
        if (!int.TryParse(text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int number))
            return false;

        value = number;
        return true;
    }

    private double Clamp(double seconds) => Math.Clamp(seconds, Minimum, Math.Max(Maximum, Minimum));

    private void SetSeconds(double seconds)
    {
        // SetCurrentValue keeps (and pushes through) a two-way binding.
        SetCurrentValue(ValueProperty, Clamp(IsInteger ? Math.Round(seconds) : Math.Round(seconds, 3)));
        Sync();
    }

    /// <summary>Reads the text; when it is not a time the last good value comes back.</summary>
    private void Commit()
    {
        if (TryRead(Input.Text, out double seconds))
            SetSeconds(seconds);
        else
            Sync();
    }

    private void Adjust(double deltaSeconds)
    {
        double current = TryRead(Input.Text, out double typed) ? typed : Value;
        SetSeconds(current + deltaSeconds);
    }

    private double StepFor(ModifierKeys modifiers) => IsInteger
        ? (modifiers.HasFlag(ModifierKeys.Shift) ? 5 : 1)
        : modifiers.HasFlag(ModifierKeys.Alt) ? 0.1
        : modifiers.HasFlag(ModifierKeys.Control) ? 60
        : modifiers.HasFlag(ModifierKeys.Shift) ? 10
        : 1;

    private void OnUpClick(object sender, RoutedEventArgs e) => Adjust(StepFor(Keyboard.Modifiers));

    private void OnDownClick(object sender, RoutedEventArgs e) => Adjust(-StepFor(Keyboard.Modifiers));

    private void OnInputGotFocus(object sender, KeyboardFocusChangedEventArgs e) => Input.SelectAll();

    private void OnInputLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        Commit();
        EndEditing();
    }

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        // With Alt held the key arrives as Key.System and the real key is in SystemKey.
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        switch (key)
        {
            case Key.Enter:
                Commit();
                EndEditing();
                e.Handled = true;
                break;

            case Key.Escape:
                Sync();
                EndEditing();
                e.Handled = true;
                break;

            case Key.Up:
                Adjust(StepFor(Keyboard.Modifiers));
                Input.SelectAll();
                e.Handled = true;
                break;

            case Key.Down:
                Adjust(-StepFor(Keyboard.Modifiers));
                Input.SelectAll();
                e.Handled = true;
                break;
        }
    }

    private void OnInputMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Input.IsKeyboardFocused)
            return;

        Adjust(e.Delta > 0 ? StepFor(Keyboard.Modifiers) : -StepFor(Keyboard.Modifiers));
        e.Handled = true;
    }

    /// <summary>"mm:ss.fff": minutes (may exceed 59), seconds and milliseconds.</summary>
    private static string Format(double seconds)
    {
        long ms = (long)Math.Max(0, Math.Round(seconds * 1000));

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{ms / 60000:00}:{ms / 1000 % 60:00}.{ms % 1000:000}");
    }

    /// <summary>Accepts "ss", "ss.fff", "mm:ss", "mm:ss.fff" and "hh:mm:ss.fff" (minutes and seconds may exceed 59).</summary>
    private static bool TryParse(string? text, out double seconds)
    {
        seconds = 0;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        string[] parts = text.Trim().Replace(',', '.').Split(':');
        if (parts.Length > 3)
            return false;

        double total = 0;

        for (int i = 0; i < parts.Length; i++)
        {
            bool last = i == parts.Length - 1;

            if (last)
            {
                // the last part may have milliseconds after a dot
                if (!double.TryParse(parts[i], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double part) || part < 0)
                    return false;

                total = total * 60 + part;
            }
            else
            {
                if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out int number))
                    return false;

                total = total * 60 + number;
            }
        }

        seconds = total;
        return true;
    }
}
