using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System;

namespace TDU2_Track_Records
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private const double WorkAreaMargin = 32.0;

        protected override void OnStartup(StartupEventArgs e)
        {
            // Styles and third-party templates can provide their own focus
            // adorners. Apply this locally so no control shows a dotted focus
            // rectangle, regardless of which theme or template created it.
            EventManager.RegisterClassHandler(
                typeof(FrameworkElement),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(RemoveFocusVisual),
                true);
            EventManager.RegisterClassHandler(
                typeof(FrameworkElement),
                Keyboard.PreviewGotKeyboardFocusEvent,
                new KeyboardFocusChangedEventHandler(RemoveIncomingFocusVisual),
                true);

            EventManager.RegisterClassHandler(
                typeof(Window),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(ConstrainWindowToWorkArea));

            try
            {
                SaveGameGarageImport.EnsureSchema(TDU2_Track_Records.Properties.Settings.Default.connectionString);
                EventCatalog.RepairLegacyData(TDU2_Track_Records.Properties.Settings.Default.connectionString);
            }
            catch (Exception ex)
            {
                StartupUri = null;
                var errorWindow = new Window
                {
                    Title = "TDU2 Track Records",
                    Width = 550,
                    Height = 300,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(14, 16, 19)),
                    Content = new Grid()
                };
                MainWindow = errorWindow;
                errorWindow.Loaded += async (sender, args) =>
                {
                    await AppNotice.ConfirmAsync(errorWindow, "Database error",
                        $"The database could not be prepared.\n\n{ex.Message}", "Close");
                    if (errorWindow.IsVisible)
                        errorWindow.Close();
                };
                errorWindow.Show();
                return;
            }

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            TDU2_Track_Records.Properties.Settings.Default.Save();
            base.OnExit(e);
        }

        private static void RemoveFocusVisual(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element)
                element.FocusVisualStyle = null;
        }

        private static void RemoveIncomingFocusVisual(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (ReferenceEquals(sender, e.NewFocus) && sender is FrameworkElement element)
                element.FocusVisualStyle = null;
        }

        private static void ConstrainWindowToWorkArea(object sender, RoutedEventArgs e)
        {
            if (!(sender is Window window) || !ReferenceEquals(e.OriginalSource, window))
            {
                return;
            }

            Rect workArea = SystemParameters.WorkArea;
            double availableWidth = Math.Max(320.0, workArea.Width - WorkAreaMargin);
            double availableHeight = Math.Max(240.0, workArea.Height - WorkAreaMargin);

            window.MinWidth = Math.Min(window.MinWidth, availableWidth);
            window.MinHeight = Math.Min(window.MinHeight, availableHeight);
            if (!(window is MainWindow))
            {
                // The main window needs to grow past the startup margin when maximized.
                window.MaxWidth = Math.Min(window.MaxWidth, availableWidth);
                window.MaxHeight = Math.Min(window.MaxHeight, availableHeight);
            }

            bool isTooWide = window.ActualWidth > availableWidth;
            bool isTooTall = window.ActualHeight > availableHeight;
            if (isTooWide || isTooTall)
            {
                double fittedWidth = Math.Min(window.ActualWidth, availableWidth);
                double fittedHeight = Math.Min(window.ActualHeight, availableHeight);

                window.SizeToContent = SizeToContent.Manual;
                window.Width = fittedWidth;
                window.Height = fittedHeight;
            }

            window.Left = Math.Max(workArea.Left, Math.Min(window.Left, workArea.Right - window.ActualWidth));
            window.Top = Math.Max(workArea.Top, Math.Min(window.Top, workArea.Bottom - window.ActualHeight));
        }
    }
}
