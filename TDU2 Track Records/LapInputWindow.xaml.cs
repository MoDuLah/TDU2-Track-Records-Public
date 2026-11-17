using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TDU2_Track_Records
{
    public partial class LapInputWindow : Window
    {
        public int TotalHundredths { get; private set; }

        public LapInputWindow()
        {
            InitializeComponent();
        }

        private void Txt_Laps_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Clear previous fields
            lapFieldsPanel.Children.Clear();

            // Check if the input is a valid integer between 1 and 20
            if (int.TryParse(txt_Laps.Text, out int laps) && laps >= 1 && laps <= 20)
            {
                for (int i = 1; i <= laps; i++)
                {
                    var lapPanel = new StackPanel { Orientation = Orientation.Horizontal };

                    lapPanel.Children.Add(new TextBlock { Text = $"Lap {i} (MM:SS.MM) ", VerticalAlignment = VerticalAlignment.Center });
                    var txtMinutes = new TextBox { MinWidth = 60, MaxLength = 2, Name = $"txt_Minutes_{i}" };
                    var txtSeconds = new TextBox { MinWidth = 60, MaxLength = 2, Name = $"txt_Seconds_{i}" };
                    var txtHundredths = new TextBox { MinWidth = 60, MaxLength = 2, Name = $"txt_Hundredths_{i}" };

                    // Add event handlers for formatting
                    txtMinutes.PreviewTextInput += NumericOnly_PreviewTextInput;
                    txtMinutes.TextChanged += NavigateToNextTextbox;
                    txtMinutes.LostFocus += FormatTimeInput;
                    txtSeconds.PreviewTextInput += NumericOnly_PreviewTextInput;
                    txtSeconds.LostFocus += FormatTimeInput;
                    txtSeconds.TextChanged += NavigateToNextTextbox;
                    txtHundredths.PreviewTextInput += NumericOnly_PreviewTextInput;
                    txtHundredths.LostFocus += FormatTimeInput;
                    txtHundredths.TextChanged += NavigateToNextTextbox;

                    lapPanel.Children.Add(txtMinutes);
                    lapPanel.Children.Add(new TextBlock { Text = ":",Focusable = false, VerticalAlignment = VerticalAlignment.Center });
                    lapPanel.Children.Add(txtSeconds);
                    lapPanel.Children.Add(new TextBlock { Text = ".", Focusable = false, VerticalAlignment = VerticalAlignment.Center });
                    lapPanel.Children.Add(txtHundredths);

                    lapFieldsPanel.Children.Add(lapPanel);
                }
            }
        }

        private void Submit_Click(object sender, RoutedEventArgs e)
        {
            int total = 0;

            foreach (var child in lapFieldsPanel.Children)
            {
                if (child is StackPanel lapPanel)
                {
                    var minutesBox = lapPanel.Children[1] as TextBox;
                    var secondsBox = lapPanel.Children[3] as TextBox;
                    var hundredthsBox = lapPanel.Children[5] as TextBox;

                    // Validate and parse input from each TextBox
                    if (int.TryParse(minutesBox.Text, out int minutes) && minutes >= 0 &&
                        int.TryParse(secondsBox.Text, out int seconds) && seconds >= 0 && seconds < 60 &&
                        int.TryParse(hundredthsBox.Text, out int hundredths) && hundredths >= 0 && hundredths < 100)
                    {
                        total += minutes * 6000 + seconds * 100 + hundredths;
                    }
                    else
                    {
                        // Handle invalid input if necessary
                        AppNotice.Show(this, "Please enter valid lap time values.", "Input Error");
                        return; // Exit the method if there's an error
                    }
                }
            }

            if (total == 0)
            {
                AppNotice.Show(this, "Enter a lap time greater than zero.", "Input Error");
                return;
            }

            TotalHundredths = total;
            DialogResult = true;
        }


        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false; // Close the window without setting values
            this.Close();
        }

        private void NumericOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // Allow only numeric input
            e.Handled = !int.TryParse(e.Text, out _);
        }

        private void FormatTimeInput(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                int value; // Declare the value variable here

                // Ensure two digits for each time field.
                if (textBox.Name.StartsWith("txt_Minutes") ||
                    textBox.Name.StartsWith("txt_Seconds") ||
                    textBox.Name.StartsWith("txt_Hundredths"))
                {
                    textBox.Text = int.TryParse(textBox.Text, out value) ? value.ToString("D2") : "00";
                }
            }
        }

        private void NavigateToNextTextbox(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox textBox && textBox.Text.Length == textBox.MaxLength)
            {
                // Focus on the next TextBox in the same StackPanel
                var lapPanel = FindParent<StackPanel>(textBox);
                if (lapPanel != null)
                {
                    // Find the index of the next TextBox
                    for (int i = 0; i < lapPanel.Children.Count; i++)
                    {
                        if (lapPanel.Children[i] == textBox)
                        {
                            // Iterate to find the next TextBox
                            for (int j = i + 1; j < lapPanel.Children.Count; j++)
                            {
                                if (lapPanel.Children[j] is TextBox nextTextBox)
                                {
                                    FocusNextTextBox(nextTextBox);
                                    break; // Exit the loop once the next TextBox is found
                                }
                            }
                            break; // Exit the loop once the current TextBox is found
                        }
                    }
                }
            }
        }
        private async void FocusNextTextBox(TextBox nextTextBox)
        {
            await Task.Delay(10); // Small delay to ensure UI updates
            nextTextBox.Focus();
        }

        private T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject parentObject = VisualTreeHelper.GetParent(child);
            if (parentObject == null) return null;
            if (parentObject is T parent) return parent;
            return FindParent<T>(parentObject);
        }
    }
}
