using AmbientVenGen.Core.Composition;
using AmbientVenGen.Desktop.Services;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace AmbientVenGen.Desktop.Views;

/// <summary>
/// A table of the repeats of a layer: a row for every property, a column for every play. A faint cell uses the value of
/// the layer; typing another value gives that play its own value (and the cell gets bright).
/// </summary>
public partial class RepeatPropertiesWindow : FluentWindow
{
    private const double RowHeight = 32;
    private const double CellWidth = 78;

    private static RepeatPropertiesWindow? _open;

    private readonly Layer _layer;
    private readonly Dictionary<(int Play, RepeatParam Param), System.Windows.Controls.TextBox> _cells = [];
    private List<RepeatParamInfo> _rows = [];
    private int _builtPlays = -1;

    private RepeatPropertiesWindow(Layer layer)
    {
        InitializeComponent();
        _layer = layer;
        _layer.PropertyChanged += OnLayerChanged;
        Closed += (_, _) =>
        {
            _layer.PropertyChanged -= OnLayerChanged;
            if (ReferenceEquals(_open, this))
                _open = null;
        };

        Build();
    }

    /// <summary>Opens the window for the layer (or brings the open one forward); the column of the play is shown.</summary>
    public static void ShowFor(Layer layer, int play)
    {
        if (_open is { } open && !ReferenceEquals(open._layer, layer))
        {
            open.Close();
            _open = null;
        }

        if (_open is null)
        {
            _open = new RepeatPropertiesWindow(layer);

            if (Application.Current.MainWindow is { IsLoaded: true } owner)
                _open.Owner = owner;

            _open.Show();
        }
        else
        {
            _open.Activate();
        }

        _open.ScrollTo(play);
    }

    private void ScrollTo(int play) =>
        CellsScroller.ScrollToHorizontalOffset(Math.Max(0, (play - 1.5) * CellWidth));

    private void OnLayerChanged(object? sender, PropertyChangedEventArgs e)
    {
        // a play was added or removed, or the layer became another kind: a new table; anything else only changes the faint values
        if (e.PropertyName is nameof(Layer.Repeats) or nameof(Layer.Type))
            Dispatcher.BeginInvoke(new Action(Build));
        else
            Dispatcher.BeginInvoke(new Action(Refresh));
    }

    private void Build()
    {
        int plays = Math.Max(1, _layer.Repeats);
        _rows = RepeatParamInfo.All.Where(r => r.AppliesTo(_layer)).ToList();
        _cells.Clear();

        TitleText.Text = plays > 1 ? $"{_layer.Name}  -  {plays} plays" : $"{_layer.Name}  -  the layer does not repeat (set Repeats first)";

        LabelsGrid.Children.Clear();
        LabelsGrid.RowDefinitions.Clear();
        LabelsGrid.ColumnDefinitions.Clear();
        CellsGrid.Children.Clear();
        CellsGrid.RowDefinitions.Clear();
        CellsGrid.ColumnDefinitions.Clear();

        // row 0 holds the numbers of the plays
        for (int r = 0; r <= _rows.Count; r++)
        {
            LabelsGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(RowHeight) });
            CellsGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(RowHeight) });
        }

        for (int p = 0; p < plays; p++)
        {
            CellsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CellWidth) });
            AddText(CellsGrid, p == 0 ? "1 (first)" : (p + 1).ToString(), 0, p, center: true, bold: true);
        }

        for (int r = 0; r < _rows.Count; r++)
        {
            RepeatParamInfo info = _rows[r];
            AddText(LabelsGrid, info.Unit.Length > 0 ? $"{info.Label}, {info.Unit}" : info.Label, r + 1, 0, center: false, bold: false);

            for (int p = 0; p < plays; p++)
            {
                var box = MakeCell(info, p);
                Grid.SetRow(box, r + 1);
                Grid.SetColumn(box, p);
                CellsGrid.Children.Add(box);
                _cells[(p, info.Param)] = box;
            }
        }

        _builtPlays = plays;
        Refresh();
    }

    private static void AddText(Grid grid, string text, int row, int column, bool center, bool bold)
    {
        var block = new TextBlock
        {
            Text = text,
            Margin = new Thickness(0, 0, 14, 0),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = center ? HorizontalAlignment.Center : HorizontalAlignment.Left,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = new SolidColorBrush(Color.FromRgb(0xCF, 0xCD, 0xE6))
        };

        Grid.SetRow(block, row);
        Grid.SetColumn(block, column);
        grid.Children.Add(block);
    }

    private System.Windows.Controls.TextBox MakeCell(RepeatParamInfo info, int play)
    {
        var box = new System.Windows.Controls.TextBox
        {
            Width = CellWidth - 8,
            Height = RowHeight - 4,
            Padding = new Thickness(2, 0, 2, 0),
            TextAlignment = TextAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Tag = (play, info),
            ContextMenu = new ContextMenu()
        };

        var reset = new MenuItem { Header = "Back to the value of the layer" };
        reset.Click += (_, _) => Commit(box, clear: true);
        box.ContextMenu.Items.Add(reset);

        box.GotKeyboardFocus += (_, _) => box.SelectAll();
        box.LostKeyboardFocus += (_, _) => Commit(box, clear: false);
        box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Commit(box, clear: false);
                e.Handled = true;
            }
            else if (e.Key == Key.Delete && box.SelectionLength == box.Text.Length)
            {
                Commit(box, clear: true);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                Refresh();
                e.Handled = true;
            }
        };

        return box;
    }

    private void Commit(System.Windows.Controls.TextBox box, bool clear)
    {
        if (box.Tag is not ValueTuple<int, RepeatParamInfo> tag)
            return;

        (int play, RepeatParamInfo info) = tag;

        double baseValue = info.Base(_layer);
        double? own = _layer.RepeatValue(play, info.Param);

        if (clear)
        {
            if (own is not null)
                _layer.SetRepeatValue(play, info.Param, null);
        }
        else if (info.Parse(box.Text) is { } typed)
        {
            // the value of the layer itself (as shown) is no change at all
            bool sameAsLayer = Math.Abs(typed * info.Scale - baseValue * info.Scale) < Math.Pow(10, -info.Decimals) / 2.0;

            if (sameAsLayer)
            {
                if (own is not null)
                    _layer.SetRepeatValue(play, info.Param, null);
            }
            else if (own is null || Math.Abs(own.Value - typed) > 1e-9)
            {
                _layer.SetRepeatValue(play, info.Param, typed);
            }
        }

        Refresh();
    }

    // The own values are bright, the values of the layer are faint.
    private void Refresh()
    {
        if (_builtPlays != Math.Max(1, _layer.Repeats))
            return;

        foreach (((int play, RepeatParam param), System.Windows.Controls.TextBox box) in _cells)
        {
            RepeatParamInfo info = _rows.First(r => r.Param == param);
            double? own = _layer.RepeatValue(play, param);

            if (!box.IsKeyboardFocused)
                box.Text = info.Format(own ?? info.Base(_layer));

            box.Opacity = own is null ? 0.45 : 1.0;
            box.FontWeight = own is null ? FontWeights.Normal : FontWeights.SemiBold;
        }
    }

    private void OnResetAll(object sender, RoutedEventArgs e)
    {
        _layer.RepeatOverrides.Clear();
        Refresh();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && Keyboard.FocusedElement is not System.Windows.Controls.TextBox)
            Close();
    }
}
