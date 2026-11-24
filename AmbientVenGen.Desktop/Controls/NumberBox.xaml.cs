using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AmbientVenGen.Desktop.Controls;

/// <summary>
/// A number field with up/down arrows, looking like <see cref="TimeBox"/>: a plain label until clicked, then a text box.
/// Scale turns the value into what is shown (a share 0..1 shown as 0..100 with the unit "%"). Typing is committed on Enter
/// or when the field loses focus; text that cannot be read is replaced by the last good value.
/// </summary>
public partial class NumberBox : UserControl
{
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header), typeof(string), typeof(NumberBox), new PropertyMetadata(string.Empty, OnPropertyChanged));

    public static readonly DependencyProperty HintProperty = DependencyProperty.Register(
        nameof(Hint), typeof(string), typeof(NumberBox), new PropertyMetadata(string.Empty, OnPropertyChanged));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(NumberBox),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnPropertyChanged));

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(NumberBox), new PropertyMetadata(0.0, OnPropertyChanged));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(NumberBox), new PropertyMetadata(100.0, OnPropertyChanged));

    /// <summary>One click of an arrow, in the units of the value.</summary>
    public static readonly DependencyProperty StepProperty = DependencyProperty.Register(
        nameof(Step), typeof(double), typeof(NumberBox), new PropertyMetadata(1.0));

    /// <summary>Digits after the decimal point (of the shown number).</summary>
    public static readonly DependencyProperty DecimalsProperty = DependencyProperty.Register(
        nameof(Decimals), typeof(int), typeof(NumberBox), new PropertyMetadata(2, OnPropertyChanged));

    /// <summary>The value is multiplied by this to be shown (100 for a share shown in percent).</summary>
    public static readonly DependencyProperty ScaleProperty = DependencyProperty.Register(
        nameof(Scale), typeof(double), typeof(NumberBox), new PropertyMetadata(1.0, OnPropertyChanged));

    /// <summary>Shown after the number ("s", "%").</summary>
    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
        nameof(Unit), typeof(string), typeof(NumberBox), new PropertyMetadata(string.Empty, OnPropertyChanged));

    /// <summary>The caption stands to the left of the field (one row) instead of above it.</summary>
    public static readonly DependencyProperty IsInlineProperty = DependencyProperty.Register(
        nameof(IsInline), typeof(bool), typeof(NumberBox), new PropertyMetadata(false, OnPropertyChanged));

    /// <summary>Width of the caption column of an inline box (so the fields of several rows line up).</summary>
    public static readonly DependencyProperty HeaderWidthProperty = DependencyProperty.Register(
        nameof(HeaderWidth), typeof(double), typeof(NumberBox), new PropertyMetadata(104.0, OnPropertyChanged));

    public bool IsInline { get => (bool)GetValue(IsInlineProperty); set => SetValue(IsInlineProperty, value); }

    public double HeaderWidth { get => (double)GetValue(HeaderWidthProperty); set => SetValue(HeaderWidthProperty, value); }

    public string Header { get => (string)GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }

    public string Hint { get => (string)GetValue(HintProperty); set => SetValue(HintProperty, value); }

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }

    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }

    public double Step { get => (double)GetValue(StepProperty); set => SetValue(StepProperty, value); }

    public int Decimals { get => (int)GetValue(DecimalsProperty); set => SetValue(DecimalsProperty, value); }

    public double Scale { get => (double)GetValue(ScaleProperty); set => SetValue(ScaleProperty, value); }

    public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }

    public NumberBox()
    {
        InitializeComponent();
        Sync();

        Loaded += (_, _) => Sync();
        DataContextChanged += (_, _) => Dispatcher.BeginInvoke(new Action(Sync), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private static void OnPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((NumberBox)d).Sync();

    private string Number(double value) =>
        (value * (Scale == 0 ? 1 : Scale)).ToString("F" + Math.Clamp(Decimals, 0, 6), CultureInfo.InvariantCulture);

    private void Sync()
    {
        if (Input is null)
            return;

        ApplyLayout();

        HeaderText.Text = Header;
        HeaderHint.Text = Hint;
        HeaderRow.Visibility = string.IsNullOrEmpty(Header) ? Visibility.Collapsed : Visibility.Visible;
        Input.Text = Number(Value);
        DisplayText.Text = string.IsNullOrEmpty(Unit) ? Number(Value) : Number(Value) + " " + Unit;
    }

    // Inline: "Hold   [ 0.50 s ]" on one low row; the arrows are smaller so the row does not grow while editing.
    private void ApplyLayout()
    {
        bool inline = IsInline;

        RootPanel.Orientation = inline ? Orientation.Horizontal : Orientation.Vertical;
        HeaderRow.VerticalAlignment = inline ? VerticalAlignment.Center : VerticalAlignment.Top;
        HeaderRow.Width = inline ? HeaderWidth : double.NaN;

        Thickness boxMargin = inline ? new Thickness(0) : new Thickness(0, 3, 0, 0);
        DisplayBox.Margin = boxMargin;
        EditGrid.Margin = boxMargin;
        DisplayBox.Width = inline ? 112 : double.NaN;
        EditGrid.Width = inline ? 112 : double.NaN;

        double height = inline ? 26 : 32;
        DisplayBox.Height = height;
        Input.Height = height;
        UpButton.Height = inline ? 12 : 15;
        DownButton.Height = inline ? 12 : 15;
        UpButton.Margin = new Thickness(0, 0, 0, inline ? 1 : 2);
    }

    private void OnDisplayClick(object sender, MouseButtonEventArgs e)
    {
        DisplayBox.Visibility = Visibility.Collapsed;
        EditGrid.Visibility = Visibility.Visible;
        Input.Focus();
        Input.SelectAll();
        e.Handled = true;
    }

    private void EndEditing()
    {
        EditGrid.Visibility = Visibility.Collapsed;
        DisplayBox.Visibility = Visibility.Visible;
    }

    private bool TryRead(string? text, out double value)
    {
        value = 0;

        if (!double.TryParse(text?.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double shown)
            || !double.IsFinite(shown))
            return false;

        value = shown / (Scale == 0 ? 1 : Scale);
        return true;
    }

    private void SetValue2(double value)
    {
        // SetCurrentValue keeps (and pushes through) a two-way binding.
        SetCurrentValue(ValueProperty, Math.Clamp(value, Minimum, Math.Max(Maximum, Minimum)));
        Sync();
    }

    private void Commit()
    {
        if (TryRead(Input.Text, out double value))
            SetValue2(value);
        else
            Sync();
    }

    private void Adjust(double delta)
    {
        double current = TryRead(Input.Text, out double typed) ? typed : Value;
        SetValue2(current + delta);
    }

    private double StepFor(ModifierKeys modifiers) =>
        Step * (modifiers.HasFlag(ModifierKeys.Shift) ? 10 : modifiers.HasFlag(ModifierKeys.Alt) ? 0.1 : 1);

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
}
