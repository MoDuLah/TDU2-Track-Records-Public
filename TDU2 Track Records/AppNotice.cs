using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace TDU2_Track_Records
{
    internal sealed class AppNotice
    {
        private static readonly ConditionalWeakTable<Window, AppNotice> Hosts =
            new ConditionalWeakTable<Window, AppNotice>();
        private static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(229, 37, 33));
        private readonly Grid root;
        private readonly FrameworkElement content;
        private readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(7) };
        private Border notification;
        private TaskCompletionSource<bool> confirmation;

        private AppNotice(Window window, Grid host = null, FrameworkElement hostContent = null)
        {
            root = host ?? new Grid();
            content = hostContent ?? (FrameworkElement)window.Content;
            if (host == null)
            {
                window.Content = null;
                root.Children.Add(content);
                window.Content = root;
            }
            timer.Tick += (sender, e) => ClearNotification();
            window.Closed += (sender, e) =>
            {
                ClearNotification();
                confirmation?.TrySetResult(false);
            };
            window.PreviewKeyDown += (sender, e) =>
            {
                if (e.Key == Key.Escape && confirmation != null)
                {
                    confirmation.TrySetResult(false);
                    e.Handled = true;
                }
            };
        }

        internal static void Attach(Window window, Grid host, FrameworkElement content)
        {
            Hosts.GetValue(window, owner => new AppNotice(owner, host, content));
        }

        public static void Show(Window source, string message, string title = null, bool persistent = false)
        {
            var host = ForWindow(source);
            host?.ShowNotification(message, title, persistent);
        }

        public static Task<bool> ConfirmAsync(Window source, string title, string message, string action)
        {
            var host = ForWindow(source);
            return host == null ? Task.FromResult(false) : host.Confirm(title, message, action);
        }

        private static AppNotice ForWindow(Window source)
        {
            Window window = source?.IsVisible == true ? source : Application.Current?.MainWindow ?? source;
            return window?.Content is FrameworkElement ? Hosts.GetValue(window, owner => new AppNotice(owner)) : null;
        }

        private void ShowNotification(string message, string title, bool persistent)
        {
            ClearNotification();
            var dismiss = CreateButton("Dismiss");
            dismiss.Click += (sender, e) => ClearNotification();
            notification = CreateCard(title, message, dismiss);
            notification.HorizontalAlignment = HorizontalAlignment.Right;
            notification.VerticalAlignment = VerticalAlignment.Bottom;
            notification.Margin = new Thickness(12);
            root.Children.Add(notification);
            if (!persistent)
                timer.Start();
        }

        private void ClearNotification()
        {
            timer.Stop();
            if (notification == null)
                return;
            root.Children.Remove(notification);
            notification = null;
        }

        private async Task<bool> Confirm(string title, string message, string action)
        {
            if (confirmation != null)
                return false;

            ClearNotification();
            var previousFocus = Keyboard.FocusedElement;
            var cancel = CreateButton("Cancel");
            var accept = CreateButton(action);
            confirmation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            cancel.Click += (sender, e) => confirmation.TrySetResult(false);
            accept.Click += (sender, e) => confirmation.TrySetResult(true);
            var card = CreateCard(title, message, cancel, accept);
            card.HorizontalAlignment = HorizontalAlignment.Center;
            card.VerticalAlignment = VerticalAlignment.Center;
            var overlay = new Grid { Background = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)) };
            Panel.SetZIndex(overlay, 1);
            overlay.Children.Add(card);
            // Keep keyboard navigation inside the confirmation.
            KeyboardNavigation.SetTabNavigation(overlay, KeyboardNavigationMode.Cycle);
            bool wasEnabled = content.IsEnabled;
            content.SetCurrentValue(UIElement.IsEnabledProperty, false);
            root.Children.Add(overlay);
            cancel.Focus();
            try
            {
                return await confirmation.Task;
            }
            finally
            {
                root.Children.Remove(overlay);
                content.SetCurrentValue(UIElement.IsEnabledProperty, wasEnabled);
                confirmation = null;
                if (previousFocus is UIElement element && element.IsVisible)
                    element.Focus();
            }
        }

        private static Button CreateButton(string text)
        {
            var button = new Button { Content = text, Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(8, 0, 0, 0) };
            button.SetResourceReference(FrameworkElement.StyleProperty, "AppScopedButtonStyle");
            return button;
        }

        private static Border CreateCard(string title, string message, params Button[] buttons)
        {
            var body = new StackPanel();
            if (!string.IsNullOrEmpty(title))
                body.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 5) });
            body.Children.Add(new ScrollViewer
            {
                MaxHeight = 300,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }
            });
            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
            };
            foreach (var button in buttons)
                actions.Children.Add(button);
            body.Children.Add(actions);
            var card = new Border
            {
                Child = body, MaxWidth = 490, MinWidth = 280,
                Background = new SolidColorBrush(Color.FromRgb(27, 29, 33)),
                BorderBrush = Accent, BorderThickness = new Thickness(3, 1, 1, 1),
                Padding = new Thickness(12)
            };
            card.SetValue(System.Windows.Documents.TextElement.ForegroundProperty, Brushes.White);
            card.SetValue(System.Windows.Documents.TextElement.FontSizeProperty, 16.0);
            return card;
        }
    }
}
