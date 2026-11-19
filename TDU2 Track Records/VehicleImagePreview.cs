using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace TDU2_Track_Records
{
    internal static class VehicleImagePreview
    {
        internal static Button CreateButton(ImageSource source, string vehicleName)
        {
            var button = new Button { Content = CreateImage(source), ToolTip = "Enlarge car photo" };
            button.SetResourceReference(FrameworkElement.StyleProperty, "VehiclePhotoButtonStyle");
            AutomationProperties.SetName(button, "Enlarge photo of " + vehicleName);
            button.Click += (sender, args) => Show(Window.GetWindow(button), source, vehicleName);
            return button;
        }

        internal static void Show(Window owner, ImageSource source, string vehicleName)
        {
            if (source != null)
                CreateDialog(owner, source, vehicleName).ShowDialog();
        }

        internal static Window CreateDialog(Window owner, ImageSource source, string vehicleName)
        {
            Rect workArea = SystemParameters.WorkArea;
            var dialog = new Window
            {
                Title = vehicleName,
                WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
                Width = Math.Min(1060, workArea.Width - 32),
                Height = Math.Min(720, workArea.Height - 48),
                ShowInTaskbar = false
            };
            // WPF requires a displayed owner before assigning it to a dialog.
            if (owner?.IsVisible == true)
                dialog.Owner = owner;
            dialog.SetResourceReference(Control.BackgroundProperty, "HudSurfaceBrush");
            ThemeManager.ApplyScopedControlStyles(dialog);

            var close = new Button { Content = "Close", IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right };
            close.Click += (sender, args) => dialog.Close();
            DockPanel.SetDock(close, Dock.Bottom);
            var layout = new DockPanel { Margin = new Thickness(12) };
            layout.Children.Add(close);
            layout.Children.Add(CreateImage(source));
            dialog.Content = layout;
            return dialog;
        }

        private static Image CreateImage(ImageSource source)
        {
            var image = new Image { Source = source, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            return image;
        }
    }
}
