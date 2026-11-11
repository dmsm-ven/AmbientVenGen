using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace AmbientVenGen.Desktop.Panels;

public partial class SelectedLayerPanel : UserControl
{
    public SelectedLayerPanel()
    {
        InitializeComponent();

        // The interval panel opens under its button but is pushed left so that at least 60 px stay free on the right.
        IntervalPopup.CustomPopupPlacementCallback = (popupSize, targetSize, _) =>
        {
            Window? window = Window.GetWindow(this);
            if (window is null)
                return [new CustomPopupPlacement(new Point(0, targetSize.Height + 4), PopupPrimaryAxis.Horizontal)];

            double buttonX = IntervalButton.TransformToAncestor(window).Transform(new Point(0, 0)).X;
            double roomRight = window.ActualWidth - buttonX - 60;
            double x = Math.Min(0, roomRight - popupSize.Width);
            return [new CustomPopupPlacement(new Point(x, targetSize.Height + 4), PopupPrimaryAxis.Horizontal)];
        };
    }
}
