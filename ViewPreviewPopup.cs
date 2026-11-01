using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace MEP_Review_Tab_View_Ver1
{
    internal sealed class ViewPreviewPopup : IDisposable
    {
        private readonly Popup popup;
        private readonly TextBlock title;
        private readonly TextBlock message;
        private readonly Image image;
        private readonly Border border;
        internal ViewPreviewPopup(FrameworkElement anchor)
        {
            title = new TextBlock { Margin = new Thickness(10,6,10,6), FontWeight = FontWeights.SemiBold,
                Foreground = MepPalette.Brush(MepPalette.Ink), TextTrimming = TextTrimming.CharacterEllipsis };
            image = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(8), IsHitTestVisible = false };
            message = new TextBlock { Foreground = MepPalette.Brush(MepPalette.Muted), TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(20) };
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.Children.Add(title);
            grid.Children.Add(image); Grid.SetRow(image, 1);
            grid.Children.Add(message); Grid.SetRow(message, 1);
            border = new Border { Child = grid, Background = MepPalette.Brush(MepPalette.Surface), BorderBrush = MepPalette.Brush(MepPalette.Stroke),
                BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(4), Width = 570, Height = 430 };
            popup = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Right, HorizontalOffset = 8,
                AllowsTransparency = true, StaysOpen = true, Focusable = false, IsHitTestVisible = false, Child = border };
        }
        internal void Loading(string name, double availableWidth)
        {
            border.Width = Math.Min(570, Math.Max(300, availableWidth));
            title.Text = name;
            image.Source = null;
            message.Text = "Đang tạo ảnh xem trước…";
            message.Visibility = Visibility.Visible;
            popup.IsOpen = true;
        }
        internal void Show(ImageSource source)
        {
            image.Source = source;
            message.Visibility = Visibility.Collapsed;
        }
        internal void Error(string text)
        {
            message.Text = text;
            message.Visibility = Visibility.Visible;
        }
        internal void Hide() { popup.IsOpen = false; image.Source = null; }
        public void Dispose() { Hide(); popup.Child = null; popup.PlacementTarget = null; }
    }
}


