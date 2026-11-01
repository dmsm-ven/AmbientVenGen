using AmbientVenGen.Desktop.ViewModels;
using System.ComponentModel;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace AmbientVenGen.Desktop;

public partial class MainWindow : FluentWindow
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // The interval panel opens under its button but is pushed left so that at least 60 px stay free on the right.
        IntervalPopup.CustomPopupPlacementCallback = (popupSize, targetSize, _) =>
        {
            double buttonX = IntervalButton.TransformToAncestor(this).Transform(new System.Windows.Point(0, 0)).X;
            double roomRight = ActualWidth - buttonX - 60;
            double x = Math.Min(0, roomRight - popupSize.Width);
            return [new CustomPopupPlacement(new System.Windows.Point(x, targetSize.Height + 4), PopupPrimaryAxis.Horizontal)];
        };

        PreviewKeyDown += OnPreviewKeyDown;
        Closing += OnClosing;
    }

    // Keyboard shortcuts. Plain keys are ignored while the user types or picks from a list.
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
            return;

        ModifierKeys modifiers = Keyboard.Modifiers;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (modifiers == ModifierKeys.Control)
        {
            switch (key)
            {
                case Key.E: Run(viewModel.ExportCommand, e); return;
                case Key.S: Run(viewModel.SaveProjectCommand, e); return;
                case Key.O: Run(viewModel.OpenProjectCommand, e); return;
                case Key.N: Run(viewModel.NewProjectCommand, e); return;
                case Key.D: Run(viewModel.DuplicateSelectedLayerCommand, e); return;
            }

            return;
        }

        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.S)
        {
            Run(viewModel.SaveProjectAsCommand, e);
            return;
        }

        if (modifiers != ModifierKeys.None)
            return;

        object? focused = Keyboard.FocusedElement;
        bool isTyping = focused is TextBoxBase
            || focused is System.Windows.Controls.ComboBox
            || focused is System.Windows.Controls.ComboBoxItem;

        if (isTyping)
            return;

        switch (key)
        {
            case Key.Space:
                Run(viewModel.TogglePlayCommand, e);
                break;

            case Key.Home:
                viewModel.SeekCommand.Execute(0.0);
                e.Handled = true;
                break;
        }
    }

    private static void Run(ICommand command, KeyEventArgs e)
    {
        if (command.CanExecute(null))
            command.Execute(null);

        e.Handled = true;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && !viewModel.ConfirmDiscardChanges())
            e.Cancel = true;
    }
}
